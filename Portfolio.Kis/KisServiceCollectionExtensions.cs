using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Timeout;
using Portfolio.Core;

namespace Portfolio.Kis;

public static class KisServiceCollectionExtensions
{
    private const string AuthClient = "KisAuth";
    private const string ApiClient = "KisApi";

    // KIS 시세 서비스 등록. IAccessTokenStore는 호출하는 쪽(Data)에서 등록한다.
    public static IServiceCollection AddKisPriceProvider(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<KisOptions>(configuration.GetSection(KisOptions.SectionName));

        // 토큰 관리자·클라이언트는 싱글턴이 HttpClient를 계속 쓰므로 연결 수명으로 DNS 변경을 반영한다.
        static SocketsHttpHandler Primary() => new() { PooledConnectionLifetime = TimeSpan.FromMinutes(5) };

        // 토큰 발급: 1분 1회 제한이 있어 재시도하지 않는다.
        services.AddHttpClient(AuthClient, (sp, http) =>
            {
                http.BaseAddress = sp.GetRequiredService<IOptions<KisOptions>>().Value.BaseAddress;
                http.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(Primary)
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan);

        // 시세 조회: 통신 오류·일시적 상태 코드만 재시도한다. HTTP 500은 KIS가 업무 오류(토큰 만료 등)에도
        // 쓰므로 재시도하지 않고 KisClient의 인증 오류 처리에 맡긴다.
        services.AddHttpClient(ApiClient, (sp, http) =>
                http.BaseAddress = sp.GetRequiredService<IOptions<KisOptions>>().Value.BaseAddress)
            .ConfigurePrimaryHttpMessageHandler(Primary)
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
            .AddResilienceHandler("kis-api", pipeline =>
            {
                pipeline.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 2,
                    Delay = TimeSpan.FromSeconds(1),
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    ShouldHandle = args => ValueTask.FromResult(args.Outcome switch
                    {
                        { Exception: HttpRequestException or TimeoutRejectedException } => true,
                        { Result.StatusCode: HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
                            or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable
                            or HttpStatusCode.GatewayTimeout } => true,
                        _ => false,
                    }),
                });
                pipeline.AddTimeout(TimeSpan.FromSeconds(10));
            });

        services.AddSingleton(sp => new KisTokenManager(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(AuthClient),
            sp.GetRequiredService<IOptions<KisOptions>>(),
            sp.GetRequiredService<IAccessTokenStore>(),
            sp.GetService<TimeProvider>(),
            sp.GetService<ILogger<KisTokenManager>>()));
        services.AddSingleton(sp => new KisClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(ApiClient),
            sp.GetRequiredService<KisTokenManager>(),
            sp.GetRequiredService<IOptions<KisOptions>>(),
            sp.GetService<TimeProvider>(),
            sp.GetService<ILogger<KisClient>>()));
        services.AddSingleton<IPriceProvider>(sp => new KisPriceProvider(
            sp.GetRequiredService<KisClient>(),
            sp.GetService<ILogger<KisPriceProvider>>()));
        return services;
    }
}
