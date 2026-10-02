using System.Net;
using System.Text.Json;
using System.Web;
using Portfolio.Kis;

namespace Portfolio.Tests;

public class KisPriceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 10, 0, 0, TimeSpan.FromHours(9));

    // 요청한 종목 각각에 가격 1,000원 × 순번을 돌려주는 멀티시세 응답
    private static HttpResponseMessage MultiPriceEcho(HttpRequestMessage req)
    {
        var q = HttpUtility.ParseQueryString(req.RequestUri!.Query);
        var items = Enumerable.Range(1, 30)
            .Select(i => q[$"FID_INPUT_ISCD_{i}"])
            .Where(code => code is not null)
            .Select((code, i) => $$"""{"inter_shrn_iscd":"{{code}}","inter2_prpr":"{{(i + 1) * 1000}}","inter2_prdy_clpr":"900"}""");
        return FakeKisHandler.Json(HttpStatusCode.OK,
            $$"""{"rt_cd":"0","msg_cd":"MCA00000","msg1":"정상처리 되었습니다.","output":[{{string.Join(",", items)}}]}""");
    }

    private static List<string> Codes(int n) => Enumerable.Range(1, n).Select(i => $"{i:000000}").ToList();

    [Fact]
    public async Task AC06_보유_31종목이면_멀티시세가_30종목_단위로_나뉘어_호출된다()
    {
        var handler = new FakeKisHandler { OnApi = MultiPriceEcho };
        var (_, client) = KisTestFactory.Create(handler, new InMemoryTokenStore(), new ManualClock(Now));
        var provider = new KisPriceProvider(client);

        var prices = await provider.GetPricesAsync(Codes(31));

        var calls = handler.ApiRequests.ToList();
        Assert.Equal(2, calls.Count);
        Assert.All(calls, c => Assert.Equal(KisClient.MultiPricePath, c.Path));
        Assert.All(calls, c => Assert.Equal(KisClient.MultiPriceTrId, c.Headers["tr_id"]));
        Assert.Equal(30, CountSymbols(calls[0].Query));
        Assert.Equal(1, CountSymbols(calls[1].Query));
        Assert.Equal(31, prices.Count);
        Assert.Equal(1_000m, prices["000031"].Price);   // 두 번째 호출의 첫 종목
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(30, 1)]
    [InlineData(60, 2)]
    [InlineData(61, 3)]
    public async Task 호출_횟수는_종목수를_30으로_나눈_올림이다(int symbols, int expectedCalls)
    {
        var handler = new FakeKisHandler { OnApi = MultiPriceEcho };
        var (_, client) = KisTestFactory.Create(handler, new InMemoryTokenStore(), new ManualClock(Now));

        await new KisPriceProvider(client).GetPricesAsync(Codes(symbols));

        Assert.Equal(expectedCalls, handler.ApiRequests.Count());
    }

    [Fact]
    public async Task 멀티시세_요청_파라미터는_시장코드_J와_종목코드_쌍이다()
    {
        var handler = new FakeKisHandler { OnApi = MultiPriceEcho };
        var (_, client) = KisTestFactory.Create(handler, new InMemoryTokenStore(), new ManualClock(Now));

        await client.GetMultiPriceAsync(["005930", "0001A0"]);

        var q = HttpUtility.ParseQueryString(handler.ApiRequests.Single().Query);
        Assert.Equal("J", q["FID_COND_MRKT_DIV_CODE_1"]);
        Assert.Equal("005930", q["FID_INPUT_ISCD_1"]);
        Assert.Equal("J", q["FID_COND_MRKT_DIV_CODE_2"]);
        Assert.Equal("0001A0", q["FID_INPUT_ISCD_2"]);
    }

    [Fact]
    public async Task 멀티시세가_실패한_묶음은_단일_현재가로_대체_조회한다()
    {
        var handler = new FakeKisHandler();
        handler.OnApi = req => req.RequestUri!.AbsolutePath == KisClient.MultiPricePath
            ? FakeKisHandler.Json(HttpStatusCode.OK, """{"rt_cd":"1","msg_cd":"EGW00201","msg1":"초당 거래건수를 초과하였습니다."}""")
            : FakeKisHandler.PriceOk("55000");
        var (_, client) = KisTestFactory.Create(handler, new InMemoryTokenStore(), new ManualClock(Now));

        var prices = await new KisPriceProvider(client).GetPricesAsync(["005930", "000660"]);

        Assert.Equal(2, prices.Count);
        Assert.All(prices.Values, p => Assert.Equal(55_000m, p.Price));
        Assert.Equal(2, handler.ApiRequests.Count(r => r.Path == KisClient.PricePath));
    }

    [Fact]
    public async Task 대체_조회도_실패한_종목은_결과에서_빠진다()
    {
        var handler = new FakeKisHandler { OnApi = _ => FakeKisHandler.Json(HttpStatusCode.InternalServerError, "error") };
        var (_, client) = KisTestFactory.Create(handler, new InMemoryTokenStore(), new ManualClock(Now));

        var prices = await new KisPriceProvider(client).GetPricesAsync(["005930"]);

        Assert.Empty(prices);
    }

    [Fact]
    public void 멀티시세_샘플_JSON을_파싱한다()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", "intstock-multprice.json")));

        var quotes = KisResponseParser.ParseMultiPrice(doc.RootElement, Now);

        Assert.Equal(2, quotes.Count);
        Assert.Equal(("069500", 40_000m, 39_500m), (quotes[0].SymbolCode, quotes[0].Price, quotes[0].PrevClose));
        Assert.Equal(("0001A0", 0m, 12_345m), (quotes[1].SymbolCode, quotes[1].Price, quotes[1].PrevClose));
        Assert.All(quotes, q => Assert.Equal(Now, q.FetchedAt));
    }

    [Fact]
    public async Task 단일_현재가는_stck_prpr를_현재가로_stck_sdpr를_전일종가로_쓴다()
    {
        var handler = new FakeKisHandler { OnApi = _ => FakeKisHandler.PriceOk("70100") };
        var (_, client) = KisTestFactory.Create(handler, new InMemoryTokenStore(), new ManualClock(Now));

        var quote = await client.GetPriceAsync("005930");

        Assert.Equal(("005930", 70_100m, 69_000m), (quote.SymbolCode, quote.Price, quote.PrevClose));
        var q = HttpUtility.ParseQueryString(handler.ApiRequests.Single().Query);
        Assert.Equal("J", q["FID_COND_MRKT_DIV_CODE"]);
        Assert.Equal("005930", q["FID_INPUT_ISCD"]);
    }

    [Fact]
    public async Task 멀티시세는_31종목_이상을_한번에_받지_않는다()
    {
        var handler = new FakeKisHandler { OnApi = MultiPriceEcho };
        var (_, client) = KisTestFactory.Create(handler, new InMemoryTokenStore(), new ManualClock(Now));

        await Assert.ThrowsAsync<ArgumentException>(() => client.GetMultiPriceAsync(Codes(31)));
        Assert.Empty(handler.ApiRequests);
    }

    private static int CountSymbols(string query) =>
        HttpUtility.ParseQueryString(query).AllKeys.Count(k => k!.StartsWith("FID_INPUT_ISCD_"));
}
