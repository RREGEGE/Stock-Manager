using System.Net;
using System.Text.Json;
using System.Web;
using Portfolio.Core;
using Portfolio.Kis;
using Portfolio.Web;

namespace Portfolio.Tests;

// 지수·환율 (설계서 F-09). 응답 예시는 KIS 공식 예제의 필드 이름으로 만든 가상 값이다.
public class MarketIndicatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 10, 0, 0, TimeSpan.FromHours(9));   // 월요일 10:00 KST

    private static string DomesticJson(string value, string change, string sign, string rate) =>
        $$$"""{"rt_cd":"0","msg_cd":"MCA00000","msg1":"정상처리 되었습니다.","output":{"bstp_nmix_prpr":"{{{value}}}","bstp_nmix_prdy_vrss":"{{{change}}}","prdy_vrss_sign":"{{{sign}}}","bstp_nmix_prdy_ctrt":"{{{rate}}}"}}""";

    private static string OverseasJson(string value, string change, string sign, string rate) =>
        $$$"""{"rt_cd":"0","msg_cd":"MCA00000","msg1":"정상처리 되었습니다.","output1":{"ovrs_nmix_prpr":"{{{value}}}","ovrs_nmix_prdy_vrss":"{{{change}}}","prdy_vrss_sign":"{{{sign}}}","prdy_ctrt":"{{{rate}}}","ovrs_nmix_prdy_clpr":"0","hts_kor_isnm":"가상"},"output2":[]}""";

    [Fact]
    public void 표시할_지표는_코스피_코스닥_SP500_나스닥_환율_다섯_가지다()
    {
        Assert.Equal(
            [("코스피", "0001"), ("코스닥", "1001"), ("S&P 500", "SPX"), ("나스닥", "COMP"), ("원/달러 환율", "FX@KRW")],
            MarketIndicators.All.Select(s => (s.Name, s.Code)));
        Assert.Equal(2, MarketIndicators.Domestic.Count());
        Assert.Equal(3, MarketIndicators.Overseas.Count());
    }

    [Fact]
    public void 국내_지수_응답을_읽는다()
    {
        using var doc = JsonDocument.Parse(DomesticJson("2650.12", "12.34", "2", "0.47"));

        var kospi = KisResponseParser.ParseDomesticIndex(doc.RootElement, "KOSPI", Now);

        Assert.Equal(new MarketIndicator("KOSPI", 2650.12m, 12.34m, 0.0047m, Now), kospi);
    }

    [Theory]
    [InlineData("3.21", "5", "0.37", -3.21, -0.0037)]     // 하락인데 값에 부호가 없는 경우
    [InlineData("-3.21", "5", "-0.37", -3.21, -0.0037)]   // 하락이고 값에도 부호가 있는 경우
    [InlineData("3.21", "2", "0.37", 3.21, 0.0037)]       // 상승
    [InlineData("0.00", "3", "0.00", 0, 0)]               // 보합
    [InlineData("-3.21", "", "-0.37", -3.21, -0.0037)]    // 부호 코드가 없으면 값의 부호를 따른다
    public void 전일_대비는_부호_코드와_값의_부호를_모두_처리한다(string change, string sign, string rate, decimal expectedChange, decimal expectedRate)
    {
        using var doc = JsonDocument.Parse(DomesticJson("870.45", change, sign, rate));

        var indicator = KisResponseParser.ParseDomesticIndex(doc.RootElement, "KOSDAQ", Now)!;

        Assert.Equal((expectedChange, expectedRate), (indicator.Change, indicator.ChangeRate));
    }

    [Fact]
    public void 해외_지수와_환율_응답을_읽는다()
    {
        using var doc = JsonDocument.Parse(OverseasJson("1385.50", "2.50", "2", "0.18"));

        var fx = KisResponseParser.ParseOverseasIndicator(doc.RootElement, "USDKRW", Now);

        Assert.Equal(new MarketIndicator("USDKRW", 1385.50m, 2.50m, 0.0018m, Now), fx);
    }

    [Fact]
    public void 값이_없거나_0이면_지표로_쓰지_않는다()
    {
        using var empty = JsonDocument.Parse("""{"rt_cd":"0","output":{}}""");
        using var zero = JsonDocument.Parse(OverseasJson("0", "0", "3", "0"));
        using var noOutput = JsonDocument.Parse("""{"rt_cd":"0"}""");

        Assert.Null(KisResponseParser.ParseDomesticIndex(empty.RootElement, "KOSPI", Now));
        Assert.Null(KisResponseParser.ParseOverseasIndicator(zero.RootElement, "SPX", Now));
        Assert.Null(KisResponseParser.ParseOverseasIndicator(noOutput.RootElement, "SPX", Now));
    }

    [Fact]
    public async Task 지표마다_맞는_API와_파라미터로_조회한다()
    {
        var handler = new FakeKisHandler();
        handler.OnApi = req => FakeKisHandler.Json(HttpStatusCode.OK,
            req.RequestUri!.AbsolutePath == KisClient.IndexPricePath
                ? DomesticJson("2650.12", "12.34", "2", "0.47")
                : OverseasJson("5750.80", "28.15", "2", "0.49"));
        var (_, client) = KisTestFactory.Create(handler, new InMemoryTokenStore(), new ManualClock(Now));
        var provider = new KisMarketIndicatorProvider(client);

        var results = new List<MarketIndicator?>();
        foreach (var spec in MarketIndicators.All)
            results.Add(await provider.GetAsync(spec));

        Assert.All(results, Assert.NotNull);
        var calls = handler.ApiRequests.Select(r => (r.Path, Query: HttpUtility.ParseQueryString(r.Query), TrId: r.Headers["tr_id"])).ToList();

        // 코스피·코스닥: 업종 지수 현재가, 시장 구분 U
        Assert.Equal([("U", "0001"), ("U", "1001")],
            calls.Take(2).Select(c => (c.Query["FID_COND_MRKT_DIV_CODE"]!, c.Query["FID_INPUT_ISCD"]!)));
        Assert.All(calls.Take(2), c => Assert.Equal((KisClient.IndexPricePath, "FHPUP02100000"), (c.Path, c.TrId)));

        // S&P 500·나스닥: 해외지수(N), 환율: X. 최근 일주일의 일별 시세를 요청한다.
        Assert.Equal([("N", "SPX"), ("N", "COMP"), ("X", "FX@KRW")],
            calls.Skip(2).Select(c => (c.Query["FID_COND_MRKT_DIV_CODE"]!, c.Query["FID_INPUT_ISCD"]!)));
        Assert.All(calls.Skip(2), c =>
        {
            Assert.Equal((KisClient.OverseasChartPath, "FHKST03030100"), (c.Path, c.TrId));
            Assert.Equal(("20260928", "20261005", "D"),
                (c.Query["FID_INPUT_DATE_1"], c.Query["FID_INPUT_DATE_2"], c.Query["FID_PERIOD_DIV_CODE"]));
        });
    }

    [Fact]
    public async Task 앱키가_없으면_지표를_조회하지_않는다()
    {
        var noKey = Microsoft.Extensions.Options.Options.Create(new KisOptions { AppKey = "", AppSecret = "" });
        var handler = new FakeKisHandler();
        var clock = new ManualClock(Now);
        var tokens = new KisTokenManager(KisTestFactory.Http(handler), noKey, new InMemoryTokenStore(), clock);
        var provider = new KisMarketIndicatorProvider(new KisClient(KisTestFactory.Http(handler), tokens, noKey, clock));

        Assert.Null(await provider.GetAsync(MarketIndicators.All[0]));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("GET", "/uapi/overseas-price/v1/quotations/inquire-daily-chartprice", true)]   // 해외 지수·환율 조회
    [InlineData("GET", "/uapi/domestic-stock/v1/quotations/inquire-index-price", true)]        // 국내 지수 조회
    [InlineData("POST", "/uapi/overseas-stock/v1/trading/order", false)]                       // 해외주식 주문
    [InlineData("GET", "/uapi/overseas-stock/v1/trading/inquire-balance", false)]              // 해외주식 잔고
    [InlineData("POST", "/uapi/overseas-price/v1/quotations/inquire-daily-chartprice", false)] // 조회 경로라도 POST는 불가
    public void 주문_차단_필터는_해외_시세_조회만_추가로_허용한다(string method, string path, bool expected)
    {
        Assert.Equal(expected, KisQuotationOnlyHandler.IsAllowed(new HttpMethod(method), new Uri("https://openapi.koreainvestment.com:9443" + path)));
    }

    private sealed class ScriptedProvider(Func<IndicatorSpec, MarketIndicator?> respond) : IMarketIndicatorProvider
    {
        public List<string> Asked { get; } = [];
        public Task<MarketIndicator?> GetAsync(IndicatorSpec spec, CancellationToken ct = default)
        {
            Asked.Add(spec.Key);
            return Task.FromResult(respond(spec));
        }
    }

    [Fact]
    public async Task 한_지표가_실패해도_나머지는_갱신하고_실패한_지표는_직전_값을_유지한다()
    {
        var store = new MarketIndicatorStore();
        store.Set(new MarketIndicator("KOSDAQ", 870m, 1m, 0.001m, Now.AddMinutes(-1)));
        var errors = new List<string>();
        int notified = 0;
        var notifier = new PortfolioNotifier();
        notifier.Changed += () => notified++;
        var provider = new ScriptedProvider(spec => spec.Key switch
        {
            "KOSPI" => new MarketIndicator("KOSPI", 2650m, 10m, 0.004m, Now),
            "KOSDAQ" => throw new HttpRequestException("통신 실패"),
            _ => null,
        });
        var updater = new MarketIndicatorUpdater(provider, store, notifier, (spec, _) => errors.Add(spec.Key));

        int updated = await updater.RefreshAsync(MarketIndicators.All);

        Assert.Equal(1, updated);
        Assert.Equal(5, provider.Asked.Count);                 // 실패 뒤에도 끝까지 조회
        Assert.Equal(["KOSDAQ"], errors);
        Assert.Equal(2650m, store.Get("KOSPI")!.Value);
        Assert.Equal(870m, store.Get("KOSDAQ")!.Value);        // 직전 값 유지
        Assert.Null(store.Get("SPX"));
        Assert.Equal(1, notified);                             // 화면에 한 번 알림
    }

    // 2026-10-05는 월요일
    private static DateTimeOffset Kst(int day, int hour, int minute) => new(2026, 10, day, hour, minute, 0, TimeSpan.FromHours(9));

    [Fact]
    public void 국내_지수는_처음_한_번_장중에는_매번_장_마감_후에는_종가를_한_번_조회한다()
    {
        Assert.True(MarketIndicatorPollingService.ShouldRefreshDomestic(Kst(4, 12, 0), null));              // 일요일이라도 처음 한 번
        Assert.True(MarketIndicatorPollingService.ShouldRefreshDomestic(Kst(5, 10, 0), Kst(5, 9, 59)));     // 장중
        Assert.False(MarketIndicatorPollingService.ShouldRefreshDomestic(Kst(5, 15, 35), Kst(5, 15, 30)));  // 마감 직후, 종가 조회 전
        Assert.True(MarketIndicatorPollingService.ShouldRefreshDomestic(Kst(5, 15, 40), Kst(5, 15, 30)));   // 종가 1회
        Assert.False(MarketIndicatorPollingService.ShouldRefreshDomestic(Kst(5, 15, 41), Kst(5, 15, 40)));  // 그 뒤에는 조회하지 않음
        Assert.False(MarketIndicatorPollingService.ShouldRefreshDomestic(Kst(4, 12, 0), Kst(3, 12, 0)));    // 주말
    }

    [Fact]
    public void 해외_지수와_환율은_10분마다_조회한다()
    {
        Assert.True(MarketIndicatorPollingService.ShouldRefreshOverseas(Kst(5, 10, 0), null));
        Assert.False(MarketIndicatorPollingService.ShouldRefreshOverseas(Kst(5, 10, 9), Kst(5, 10, 0)));
        Assert.True(MarketIndicatorPollingService.ShouldRefreshOverseas(Kst(5, 10, 10), Kst(5, 10, 0)));
        Assert.True(MarketIndicatorPollingService.ShouldRefreshOverseas(Kst(4, 3, 0), Kst(4, 2, 0)));        // 주말·밤에도
    }

    [Fact]
    public void 지수와_환율은_소수점_둘째_자리까지_표시한다()
    {
        Assert.Equal("2,650.12", DisplayFormat.Decimal2(2650.123m));
        Assert.Equal("18,920.33", DisplayFormat.Decimal2(18_920.33m));
        Assert.Equal("+12.34", DisplayFormat.SignedDecimal2(12.34m));
        Assert.Equal("-3.20", DisplayFormat.SignedDecimal2(-3.2m));
        Assert.Equal("0.00", DisplayFormat.SignedDecimal2(0m));
    }
}
