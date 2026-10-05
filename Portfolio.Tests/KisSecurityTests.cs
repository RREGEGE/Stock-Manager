using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Portfolio.Core;
using Portfolio.Data;
using Portfolio.Kis;

namespace Portfolio.Tests;

// AC-13: DB·로그 어디에도 APP SECRET과 접근토큰이 평문으로 남지 않는다. + 주문 API 차단 (설계서 7.2)
public class KisSecurityTests
{
    private const string AppKey = "TESTAPPKEY-0123456789";
    private const string AppSecret = "TESTAPPSECRET-abcdefghijklmnopqrstuvwxyz";
    private const string Token1 = "TESTTOKEN-ONE-eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzUxMiJ9";
    private const string Token2 = "TESTTOKEN-TWO-eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzUxMiJ9";

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Lines { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Capture(this, categoryName);
        public void Dispose() { }

        private sealed class Capture(CapturingLoggerProvider owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            {
                owner.Lines.Enqueue($"[scope:{category}] {state}");
                return null;
            }

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                // 화면에 찍히는 문장, 구조화된 값(이름=값), 예외 전체를 모두 남긴다
                string values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                    ? string.Join("; ", pairs.Select(p => $"{p.Key}={p.Value}")) : "";
                owner.Lines.Enqueue($"[{logLevel}:{category}] {formatter(state, exception)} | {values} | {exception}");
            }
        }
    }

    // 실제 등록 코드(AddKisPriceProvider)를 그대로 쓰되, 서버만 가짜로 바꾼다
    private static (ServiceProvider Services, CapturingLoggerProvider Logs, FakeKisHandler Server) Build(TestDb db)
    {
        var logs = new CapturingLoggerProvider();
        var server = new FakeKisHandler
        {
            OnToken = n => FakeKisHandler.TokenResponse(n == 1 ? Token1 : Token2, "2026-10-03 09:00:00"),
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Kis:Environment"] = "Mock",
            ["Kis:AppKey"] = AppKey,
            ["Kis:AppSecret"] = AppSecret,
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        services.AddSingleton<TimeProvider>(new ManualClock(new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.FromHours(9))));
        services.AddSingleton<IDbContextFactory<PortfolioDbContext>>(db);
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddSingleton<IAccessTokenStore, DbAccessTokenStore>();
        services.AddKisPriceProvider(config);
        services.ConfigureAll<HttpClientFactoryOptions>(o =>
            o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = server));
        return (services.BuildServiceProvider(), logs, server);
    }

    private static HttpResponseMessage MultiPriceOk() => FakeKisHandler.Json(HttpStatusCode.OK,
        """{"rt_cd":"0","msg_cd":"MCA00000","msg1":"정상처리 되었습니다.","output":[{"inter_shrn_iscd":"005930","inter2_prpr":"70000","inter2_prdy_clpr":"69000"}]}""");

    [Fact]
    public async Task AC13_시세_조회의_모든_로그에_APP_SECRET과_토큰_평문이_없다()
    {
        using var db = new TestDb();
        var (services, logs, server) = Build(db);
        await using var _ = services;

        // 토큰 발급 → 정상 조회 → 토큰 만료 응답(재발급) → 조회 실패까지 모든 경로를 지난다
        int call = 0;
        server.OnApi = req => ++call switch
        {
            1 => MultiPriceOk(),
            2 => FakeKisHandler.TokenExpired(),
            3 => MultiPriceOk(),
            _ => FakeKisHandler.Json(HttpStatusCode.OK, """{"rt_cd":"1","msg_cd":"EGW00201","msg1":"초당 거래건수를 초과하였습니다."}"""),
        };
        var provider = services.GetRequiredService<IPriceProvider>();
        await provider.GetPricesAsync(["005930"]);
        await provider.GetPricesAsync(["005930"]);
        await provider.GetPricesAsync(["005930"]);

        Assert.Equal(2, server.TokenCalls);                 // 발급 1회 + 만료 후 재발급 1회
        Assert.True(logs.Lines.Count > 10);                 // 가장 상세한 수준(Trace)까지 실제로 수집됨
        Assert.Contains(logs.Lines, l => l.Contains("EGW00201"));   // 실패 사유(msg_cd)는 남는다
        // 요청 헤더가 로그에 찍히는 수준까지 켰고(헤더 이름은 보임), 값은 가려져 있다
        Assert.Contains(logs.Lines, l => l.Contains("appsecret", StringComparison.OrdinalIgnoreCase));

        string all = string.Join("\n", logs.Lines);
        Assert.DoesNotContain(AppSecret, all);
        Assert.DoesNotContain(Token1, all);
        Assert.DoesNotContain(Token2, all);
        Assert.DoesNotContain(AppKey, all);
    }

    [Fact]
    public async Task AC13_토큰_발급_실패_로그에도_APP_SECRET이_없다()
    {
        using var db = new TestDb();
        var (services, logs, server) = Build(db);
        await using var _ = services;
        server.OnToken = _ => FakeKisHandler.Json(HttpStatusCode.Forbidden,
            """{"error_code":"EGW00133","error_description":"접근토큰 발급 잠시 후 다시 시도하세요(1분당 1회)"}""");

        var prices = await services.GetRequiredService<IPriceProvider>().GetPricesAsync(["005930"]);

        Assert.Empty(prices);
        string all = string.Join("\n", logs.Lines);
        Assert.Contains("EGW00133", all);
        Assert.DoesNotContain(AppSecret, all);
        Assert.DoesNotContain(AppKey, all);
    }

    [Fact]
    public async Task AC13_DB의_어떤_칸에도_APP_SECRET과_토큰_평문이_없다()
    {
        using var db = new TestDb();
        await SeedData.ApplyAsync(db.Context);
        var (services, _, server) = Build(db);
        await using var __ = services;
        server.OnApi = _ => MultiPriceOk();

        // 토큰 발급·저장, 시세 저장(PriceCache)까지 실제 흐름대로 실행
        await new PriceUpdater(db, services.GetRequiredService<IPriceProvider>(), new PriceStore()).RefreshAsync(["005930"]);
        Assert.Equal(1, await db.Context.ApiTokens.CountAsync());
        Assert.Equal(1, await db.Context.PriceCaches.CountAsync());

        string dump = await DumpAllTextAsync(db);

        Assert.Contains("SEED01", dump);          // 실제로 모든 표를 읽었는지 확인
        Assert.DoesNotContain(Token1, dump);
        Assert.DoesNotContain(AppSecret, dump);
        Assert.DoesNotContain(AppKey, dump);
    }

    // 모든 표의 모든 칸을 글자로 읽어 한 덩어리로 만든다
    private static async Task<string> DumpAllTextAsync(TestDb db)
    {
        var connection = db.Context.Database.GetDbConnection();
        var tables = new List<string>();
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        }

        var sb = new System.Text.StringBuilder();
        foreach (string table in tables)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT * FROM \"{table}\"";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                for (int i = 0; i < reader.FieldCount; i++)
                    sb.Append(reader.IsDBNull(i) ? "" : reader.GetValue(i).ToString()).Append('\n');
        }
        return sb.ToString();
    }

    [Theory]
    [InlineData("POST", "/oauth2/tokenP", true)]
    [InlineData("GET", "/uapi/domestic-stock/v1/quotations/intstock-multprice", true)]
    [InlineData("GET", "/uapi/domestic-stock/v1/quotations/inquire-price", true)]
    [InlineData("POST", "/uapi/domestic-stock/v1/trading/order-cash", false)]          // 주식 주문
    [InlineData("GET", "/uapi/domestic-stock/v1/trading/inquire-balance", false)]      // 잔고 조회
    [InlineData("POST", "/uapi/domestic-stock/v1/quotations/inquire-price", false)]    // 시세 경로라도 POST는 불가
    [InlineData("GET", "/uapi/domestic-stock/v1/quotations/../trading/inquire-balance", false)]
    [InlineData("GET", "/oauth2/tokenP", false)]
    [InlineData("DELETE", "/oauth2/revokeP", false)]
    public void 토큰_발급과_시세_조회_경로만_허용한다(string method, string path, bool expected)
    {
        var uri = new Uri("https://openapi.koreainvestment.com:9443" + path, new UriCreationOptions { DangerousDisablePathAndQueryCanonicalization = true });

        Assert.Equal(expected, KisQuotationOnlyHandler.IsAllowed(new HttpMethod(method), uri));
    }

    [Fact]
    public async Task 주문_경로_요청은_KIS_서버로_나가기_전에_차단된다()
    {
        using var db = new TestDb();
        var (services, _, server) = Build(db);
        await using var __ = services;
        var http = services.GetRequiredService<IHttpClientFactory>().CreateClient("KisApi");

        var ex = await Assert.ThrowsAsync<KisApiException>(() =>
            http.PostAsync("/uapi/domestic-stock/v1/trading/order-cash", new StringContent("{}")));

        Assert.Contains("차단", ex.Message);
        Assert.Empty(server.Requests);   // 가짜 서버까지 도달한 요청이 없다
    }
}
