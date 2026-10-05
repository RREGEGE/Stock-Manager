using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Portfolio.Core;
using Portfolio.Kis;

namespace Portfolio.Tests;

// KIS 서버 대역: 요청을 기록하고 경로별 응답을 돌려준다. 실제 네트워크 호출 없음.
public sealed class FakeKisHandler : HttpMessageHandler
{
    public List<(string Path, string Query, Dictionary<string, string> Headers)> Requests { get; } = [];
    public Func<HttpRequestMessage, HttpResponseMessage>? OnApi { get; set; }
    public Func<int, HttpResponseMessage>? OnToken { get; set; }

    public int TokenCalls => Requests.Count(r => r.Path == KisTokenManager.TokenPath);
    public IEnumerable<(string Path, string Query, Dictionary<string, string> Headers)> ApiRequests =>
        Requests.Where(r => r.Path != KisTokenManager.TokenPath);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value),
            StringComparer.OrdinalIgnoreCase);
        Requests.Add((request.RequestUri!.AbsolutePath, request.RequestUri.Query, headers));

        if (request.RequestUri.AbsolutePath == KisTokenManager.TokenPath)
            return Task.FromResult(OnToken?.Invoke(TokenCalls) ?? TokenResponse($"token-{TokenCalls}", "2026-10-03 09:00:00"));

        return Task.FromResult(OnApi?.Invoke(request) ?? Json(HttpStatusCode.NotFound, "{}"));
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage TokenResponse(string token, string expiredKst) => Json(HttpStatusCode.OK,
        $$"""{"access_token":"{{token}}","token_type":"Bearer","expires_in":86400,"access_token_token_expired":"{{expiredKst}}"}""");

    public static HttpResponseMessage PriceOk(string price = "70000") => Json(HttpStatusCode.OK,
        $$$"""{"rt_cd":"0","msg_cd":"MCA00000","msg1":"정상처리 되었습니다.","output":{"stck_prpr":"{{{price}}}","stck_sdpr":"69000"}}""");

    public static HttpResponseMessage TokenExpired() => Json(HttpStatusCode.InternalServerError,
        """{"rt_cd":"1","msg_cd":"EGW00123","msg1":"기간이 만료된 token 입니다."}""");
}

public sealed class InMemoryTokenStore : IAccessTokenStore
{
    public AccessToken? Saved { get; set; }
    public Task<AccessToken?> LoadAsync(CancellationToken ct = default) => Task.FromResult(Saved);
    public Task SaveAsync(AccessToken token, CancellationToken ct = default) { Saved = token; return Task.CompletedTask; }
}

public static class KisTestFactory
{
    public static readonly IOptions<KisOptions> Options = Microsoft.Extensions.Options.Options.Create(
        new KisOptions { Environment = KisEnvironment.Mock, AppKey = "test-app-key", AppSecret = "test-app-secret" });

    public static HttpClient Http(FakeKisHandler handler) =>
        new(handler, disposeHandler: false) { BaseAddress = Options.Value.BaseAddress };

    // 앱 1회 실행에 해당하는 토큰 관리자 + 클라이언트. 새로 만들면 '재시작'과 같다.
    public static (KisTokenManager Tokens, KisClient Client) Create(
        FakeKisHandler handler, IAccessTokenStore store, TimeProvider clock)
    {
        var tokens = new KisTokenManager(Http(handler), Options, store, clock);
        return (tokens, new KisClient(Http(handler), tokens, Options, clock));
    }
}
