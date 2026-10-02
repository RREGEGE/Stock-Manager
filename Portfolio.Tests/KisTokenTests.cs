using Microsoft.AspNetCore.DataProtection;
using Portfolio.Core;
using Portfolio.Data;
using Portfolio.Kis;

namespace Portfolio.Tests;

public class KisTokenTests
{
    // 2026-10-02 10:00 KST
    private static ManualClock Clock() => new(new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.FromHours(9)));

    [Fact]
    public async Task AC05_토큰_발급_후_재시작해도_DB의_토큰을_재사용한다()
    {
        using var db = new TestDb();
        var protection = new EphemeralDataProtectionProvider();   // 재시작 후에도 같은 키 (운영에서는 키 파일 유지)
        var handler = new FakeKisHandler { OnApi = _ => FakeKisHandler.PriceOk() };
        var clock = Clock();

        // 1차 실행: 토큰 발급 후 시세 조회
        var (_, client1) = KisTestFactory.Create(handler, new DbAccessTokenStore(db, protection), clock);
        await client1.GetPriceAsync("005930");
        Assert.Equal(1, handler.TokenCalls);

        // 재시작: 새 인스턴스, 같은 DB
        clock.Now = clock.Now.AddHours(1);
        var (_, client2) = KisTestFactory.Create(handler, new DbAccessTokenStore(db, protection), clock);
        await client2.GetPriceAsync("005930");
        await client2.GetPriceAsync("000660");

        Assert.Equal(1, handler.TokenCalls);   // 발급 API 재호출 없음
        Assert.All(handler.ApiRequests, r => Assert.Equal("Bearer token-1", r.Headers["authorization"]));
    }

    [Fact]
    public async Task 같은_실행_중에는_메모리의_토큰을_쓴다()
    {
        var handler = new FakeKisHandler { OnApi = _ => FakeKisHandler.PriceOk() };
        var (_, client) = KisTestFactory.Create(handler, new InMemoryTokenStore(), Clock());

        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => client.GetPriceAsync("005930")));

        Assert.Equal(1, handler.TokenCalls);
    }

    [Fact]
    public async Task 만료_10분_전부터는_선제_재발급한다()
    {
        var handler = new FakeKisHandler { OnApi = _ => FakeKisHandler.PriceOk() };
        var clock = Clock();
        var store = new InMemoryTokenStore();
        var (tokens, _) = KisTestFactory.Create(handler, store, clock);
        // 발급 토큰 만료: 2026-10-03 09:00 KST

        Assert.Equal("token-1", await tokens.GetTokenAsync());

        clock.Now = new DateTimeOffset(2026, 10, 3, 8, 49, 0, TimeSpan.FromHours(9));   // 만료 11분 전
        Assert.Equal("token-1", await tokens.GetTokenAsync());

        clock.Now = new DateTimeOffset(2026, 10, 3, 8, 51, 0, TimeSpan.FromHours(9));   // 만료 9분 전
        Assert.Equal("token-2", await tokens.GetTokenAsync());
        Assert.Equal(2, handler.TokenCalls);
        Assert.Equal("token-2", store.Saved!.Value);
    }

    [Fact]
    public async Task 저장된_토큰이_만료됐으면_새로_발급한다()
    {
        var handler = new FakeKisHandler { OnApi = _ => FakeKisHandler.PriceOk() };
        var store = new InMemoryTokenStore
        {
            Saved = new AccessToken("old", new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.FromHours(9))),
        };
        var (tokens, _) = KisTestFactory.Create(handler, store, Clock());

        Assert.Equal("token-1", await tokens.GetTokenAsync());
        Assert.Equal(1, handler.TokenCalls);
    }

    [Fact]
    public async Task 토큰_만료_응답이면_1회_재발급_후_재시도한다()
    {
        var handler = new FakeKisHandler();
        handler.OnApi = req => req.Headers.Authorization?.Parameter == "token-1"
            ? FakeKisHandler.TokenExpired()
            : FakeKisHandler.PriceOk("71000");
        var (_, client) = KisTestFactory.Create(handler, new InMemoryTokenStore(), Clock());

        var quote = await client.GetPriceAsync("005930");

        Assert.Equal(71_000m, quote.Price);
        Assert.Equal(2, handler.TokenCalls);
        Assert.Equal(2, handler.ApiRequests.Count());
    }

    [Fact]
    public async Task 재발급_후에도_인증_오류면_더_재시도하지_않고_실패한다()
    {
        var handler = new FakeKisHandler { OnApi = _ => FakeKisHandler.TokenExpired() };
        var (_, client) = KisTestFactory.Create(handler, new InMemoryTokenStore(), Clock());

        var ex = await Assert.ThrowsAsync<KisApiException>(() => client.GetPriceAsync("005930"));

        Assert.Equal("EGW00123", ex.MsgCode);
        Assert.Equal(2, handler.TokenCalls);
        Assert.Equal(2, handler.ApiRequests.Count());
    }

    [Fact]
    public async Task 토큰_발급_실패는_예외로_알리고_비밀값을_메시지에_넣지_않는다()
    {
        var handler = new FakeKisHandler
        {
            OnToken = _ => FakeKisHandler.Json(System.Net.HttpStatusCode.Forbidden,
                """{"error_code":"EGW00133","error_description":"접근토큰 발급 잠시 후 다시 시도하세요(1분당 1회)"}"""),
        };
        var (tokens, _) = KisTestFactory.Create(handler, new InMemoryTokenStore(), Clock());

        var ex = await Assert.ThrowsAsync<KisApiException>(() => tokens.GetTokenAsync());

        Assert.Equal("EGW00133", ex.MsgCode);
        Assert.DoesNotContain("test-app-secret", ex.Message);
        Assert.DoesNotContain("test-app-key", ex.Message);
    }

    [Fact]
    public async Task 시세_요청에_필요한_헤더를_보낸다()
    {
        var handler = new FakeKisHandler { OnApi = _ => FakeKisHandler.PriceOk() };
        var (_, client) = KisTestFactory.Create(handler, new InMemoryTokenStore(), Clock());

        await client.GetPriceAsync("005930");

        var h = handler.ApiRequests.Single().Headers;
        Assert.Equal("FHKST01010100", h["tr_id"]);
        Assert.Equal("P", h["custtype"]);
        Assert.Equal("test-app-key", h["appkey"]);
        Assert.Equal("test-app-secret", h["appsecret"]);
    }
}
