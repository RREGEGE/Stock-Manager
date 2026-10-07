using System.Net;
using System.Text.Json;
using System.Web;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Core;
using Portfolio.Data;
using Portfolio.Kis;
using Portfolio.Web.Components.Layout;
using Portfolio.Web.Components.Pages;
using Portfolio.Web.Services;

namespace Portfolio.Tests.Ui;

// 오늘 등락 (설계서 F-14)
public class DayChangeTests : UiTestBase
{
    private static HoldingView View(decimal price, decimal prevClose, long quantity = 10) =>
        new("X", "X", "주식", quantity, 1_000m, price, IsStale: price <= 0) { PrevClose = prevClose };

    [Fact]
    public void 오늘_등락은_전일_종가_대비로_계산한다()
    {
        var up = View(10_100m, 10_000m);
        var down = View(9_900m, 10_000m);

        Assert.Equal((100m, 0.01m, 1_000m), (up.DayChange, up.DayChangeRate, up.DayChangeAmount));
        Assert.Equal((-100m, -0.01m, -1_000m), (down.DayChange, down.DayChangeRate, down.DayChangeAmount));
    }

    [Fact]
    public void 현재가나_전일_종가가_없으면_오늘_등락을_계산하지_않는다()
    {
        Assert.Null(View(0m, 10_000m).DayChangeRate);        // 현재가를 못 받음
        Assert.Null(View(10_000m, 0m).DayChangeRate);        // 전일 종가를 못 받음
        Assert.Null(View(10_000m, 0m).DayChangeAmount);
    }

    [Fact]
    public async Task 계좌의_오늘_손익은_전일_종가를_아는_종목만_더한다()
    {
        var model = await LoadModelAsync();

        // 시드: +240,000 −240,000 +200,000 0 −50,000 +120,000 0 = +270,000원, 어제 평가금액 99,730,000원
        Assert.Equal(270_000m, model.DayChangeAmount);
        Assert.Equal(7, model.DayChangeCount);
        Assert.Equal(270_000m / 99_730_000m, model.DayChangeRate);
    }

    [Fact]
    public void 대시보드는_오늘_손익_카드와_표의_오늘_열을_보여_준다()
    {
        var cut = Render<Home>();
        cut.WaitForElement(".summary-grid");

        Assert.Equal("오늘 손익 +270,000원 +0.27% · 전일 종가 대비", Text(cut.FindAll(".summary-card")[3]));
        Assert.Contains("up", cut.FindAll(".summary-value")[3].ClassName);

        // 종목 순서: 주식(KOSPI200, S&P500, 반도체), 채권(국고채, 미국채), 배당(고배당, 리츠)
        var cells = cut.FindAll("td.day");
        Assert.Equal(["+1.01%", "-0.99%", "+1.69%", "+0.00%", "-0.50%", "+1.01%", "+0.00%"], cells.Select(Text));
        Assert.Equal(["up", "down", "up", "", "down", "up", ""], cells.Select(c => c.ClassList.Contains("up") ? "up" : c.ClassList.Contains("down") ? "down" : ""));
        Assert.Equal("전일 대비 +400원", cells[0].GetAttribute("title"));
    }

    [Fact]
    public void 오늘_열로_정렬할_수_있다()
    {
        var cut = Render<Home>();
        cut.WaitForElement(".summary-grid");

        cut.FindAll("th button.sort").Single(b => Text(b) == "오늘").Click();   // 많이 오른 순

        Assert.Equal(["+1.69%", "+1.01%", "+1.01%", "+0.00%", "+0.00%", "-0.50%", "-0.99%"], cut.FindAll("td.day").Select(Text));
    }

    [Fact]
    public void 휴대폰_헤더에도_오늘_등락을_보여_준다()
    {
        var cut = Render<TopNav>();
        cut.WaitForElement(".mobile-total");

        Assert.Equal("오늘 +270,000원 (+0.27%)", Text(cut.Find(".mobile-total-day")));
        Assert.Contains("up", cut.Find(".mobile-total-day").ClassName);
    }

    [Fact]
    public async Task 시세가_바뀌면_오늘_등락도_새로고침_없이_바뀐다()
    {
        var cut = Render<Home>();
        cut.WaitForElement(".summary-grid");

        var next = SeedData.Prices.ToDictionary();
        next["SEED01"] = 39_000m;   // 전일 39,600원보다 내림
        await RefreshPricesAsync(next);

        cut.WaitForAssertion(() =>
        {
            // 금액이 줄어 줄 순서가 바뀌므로 종목명으로 찾는다
            var day = cut.FindAll("tbody tr").Single(r => r.TextContent.Contains("KOSPI200 ETF")).QuerySelector("td.day")!;
            Assert.Equal("-1.52%", Text(day));
            Assert.Contains("down", day.ClassName);
            Assert.StartsWith("오늘 손익 -330,000원", Text(cut.FindAll(".summary-card")[3]));
        });
    }
}

public class DayChangeWithoutPriceTests : UiTestBase
{
    protected override bool StartWithPrices => false;

    [Fact]
    public void 현재가가_없으면_오늘_등락을_0이_아니라_빈_값으로_보여_준다()
    {
        var cut = Render<Home>();
        cut.WaitForElement(".summary-grid");

        Assert.Equal("오늘 손익 - 현재가를 받으면 계산됩니다", Text(cut.FindAll(".summary-card")[3]));
        Assert.All(cut.FindAll("td.day"), c => Assert.Equal("-", Text(c)));

        var nav = Render<TopNav>();
        nav.WaitForElement(".mobile-total");
        Assert.Empty(nav.FindAll(".mobile-total-day"));
    }
}

// 종목 검색과 종목 정보 (설계서 F-15)
public class StockPageTests : UiTestBase
{
    // 실제 응답(2026-10-07)에서 쓰는 항목만 남긴 것. 값도 그때의 공개 시세다.
    private const string StockJson = """
        {"rt_cd":"0","msg_cd":"MCA00000","msg1":"정상처리 되었습니다.","output":{"rprs_mrkt_kor_name":"KOSPI200","bstp_kor_isnm":"전기·전자",
        "stck_prpr":"272250","prdy_vrss":"250","prdy_vrss_sign":"2","prdy_ctrt":"0.09","acml_vol":"8864988",
        "stck_oprc":"269500","stck_hgpr":"279500","stck_lwpr":"268500","stck_mxpr":"353500","stck_llam":"190500","stck_sdpr":"272000",
        "hts_avls":"15916494","per":"41.48","pbr":"4.25","eps":"6564.00",
        "w52_hgpr":"374500","w52_hgpr_date":"20260619","w52_lwpr":"90200","w52_lwpr_date":"20251014"}}
        """;
    private const string EtfJson = """
        {"rt_cd":"0","output":{"rprs_mrkt_kor_name":"ETF","bstp_kor_isnm":"ETF(실물복제/수익증권)",
        "stck_prpr":"109610","prdy_vrss":"-1135","prdy_vrss_sign":"5","prdy_ctrt":"-1.02","acml_vol":"11028149",
        "stck_oprc":"109420","stck_hgpr":"111600","stck_lwpr":"109085","stck_mxpr":"143965","stck_llam":"77525","stck_sdpr":"110745",
        "hts_avls":"254624","per":"0.00","pbr":"0.00","eps":"0.00",
        "w52_hgpr":"152455","w52_hgpr_date":"20260619","w52_lwpr":"49395","w52_lwpr_date":"20251013"}}
        """;

    [Fact]
    public void 종목_정보_응답을_읽는다()
    {
        using var doc = JsonDocument.Parse(StockJson);

        var d = KisResponseParser.ParseStockDetail(doc.RootElement, "005930", Clock.Now)!;

        Assert.Equal((272_250m, 250m, 0.0009m, 272_000m), (d.Price, d.Change, d.ChangeRate, d.PrevClose));
        Assert.Equal((269_500m, 279_500m, 268_500m, 8_864_988L), (d.Open, d.High, d.Low, d.Volume));
        Assert.Equal((353_500m, 190_500m), (d.UpperLimit, d.LowerLimit));
        Assert.Equal(1_591_649_400_000_000m, d.MarketCap);                       // 억 원 단위 → 원
        Assert.Equal((41.48m, 4.25m, 6_564m), (d.Per, d.Pbr, d.Eps));
        Assert.Equal((374_500m, new DateOnly(2026, 6, 19), 90_200m, new DateOnly(2025, 10, 14)),
            (d.Week52High, d.Week52HighDate, d.Week52Low, d.Week52LowDate));
        Assert.Equal(("KOSPI200", "전기·전자"), (d.Market, d.Sector));
    }

    [Fact]
    public void ETF는_PER_PBR_EPS가_없고_내린_날은_전일_대비가_음수다()
    {
        using var doc = JsonDocument.Parse(EtfJson);

        var d = KisResponseParser.ParseStockDetail(doc.RootElement, "069500", Clock.Now)!;

        Assert.Equal((-1_135m, -0.0102m), (d.Change, d.ChangeRate));
        Assert.Equal((null, null, null), (d.Per, d.Pbr, d.Eps));
        Assert.Equal(25_462_400_000_000m, d.MarketCap);
    }

    [Fact]
    public void 현재가가_0이거나_내용이_없으면_종목_정보로_쓰지_않는다()
    {
        using var zero = JsonDocument.Parse("""{"rt_cd":"0","output":{"stck_prpr":"0"}}""");
        using var empty = JsonDocument.Parse("""{"rt_cd":"0"}""");

        Assert.Null(KisResponseParser.ParseStockDetail(zero.RootElement, "000000", Clock.Now));
        Assert.Null(KisResponseParser.ParseStockDetail(empty.RootElement, "000000", Clock.Now));
    }

    [Fact]
    public async Task 종목_정보는_현재가_조회_주소로_한_번_조회한다()
    {
        var handler = new FakeKisHandler { OnApi = _ => FakeKisHandler.Json(HttpStatusCode.OK, StockJson) };
        var (_, client) = KisTestFactory.Create(handler, new InMemoryTokenStore(), Clock);

        var detail = await new KisStockDetailProvider(client).GetAsync("005930");

        Assert.Equal(272_250m, detail!.Price);
        var call = Assert.Single(handler.ApiRequests);
        var query = HttpUtility.ParseQueryString(call.Query);
        Assert.Equal((KisClient.PricePath, "FHKST01010100", "J", "005930"),
            (call.Path, call.Headers["tr_id"], query["FID_COND_MRKT_DIV_CODE"], query["FID_INPUT_ISCD"]));
    }

    [Fact]
    public async Task 앱키가_없으면_종목_정보를_조회하지_않는다()
    {
        var noKey = Microsoft.Extensions.Options.Options.Create(new KisOptions { AppKey = "", AppSecret = "" });
        var handler = new FakeKisHandler();
        var tokens = new KisTokenManager(KisTestFactory.Http(handler), noKey, new InMemoryTokenStore(), Clock);

        Assert.Null(await new KisStockDetailProvider(new KisClient(KisTestFactory.Http(handler), tokens, noKey, Clock)).GetAsync("005930"));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void 큰_금액은_조_단위로_줄여_쓴다()
    {
        var ko = new Loc();
        var en = new Loc { Language = Loc.English };

        Assert.Equal(("1,591.6조 원", "₩1,591.6T"), (ko.LargeWon(1_591_649_400_000_000m), en.LargeWon(1_591_649_400_000_000m)));
        Assert.Equal(("4,000억 원", "₩400B"), (ko.LargeWon(400_000_000_000m), en.LargeWon(400_000_000_000m)));
    }

    [Fact]
    public async Task 종목을_검색해_고르면_그_종목의_주소로_이동한다()
    {
        var cut = Render<Stocks>();
        cut.WaitForElement("#search");
        Assert.Empty(cut.FindAll(".detail-card"));          // 고르기 전에는 검색창만

        cut.Find("#search").Input("리츠");
        var result = cut.WaitForElement(".results .result");
        Assert.Equal("리츠 ETF SEED07 보유 중", Cells(result));
        await cut.InvokeAsync(() => result.Click());

        Assert.Equal("http://localhost/stocks/SEED07", Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact]
    public void 종목_정보_화면은_시세와_내_보유_현황을_보여_준다()
    {
        var cut = Render<Stocks>(p => p.Add(x => x.Code, "SEED01"));
        cut.WaitForElement(".facts");

        Assert.Equal("KOSPI200 ETF", Text(cut.Find("#h-stock")));
        Assert.Equal("SEED01 · 가상 · 가상 업종", Text(cut.Find(".stock-meta")));
        Assert.Equal("40,000원", Text(cut.Find(".price")));
        Assert.Equal("▲ 상승 400 (+1.01%)", Text(cut.Find(".change")));
        Assert.Contains("up", cut.Find(".change").ClassName);
        Assert.Equal("09:41 기준 · 전일 대비", Text(cut.Find(".as-of")));

        var facts = cut.FindAll(".facts > div").ToDictionary(d => Text(d.QuerySelector("dt")!), d => Text(d.QuerySelector("dd")!));
        Assert.Equal("39,600원", facts["전일 종가"]);
        Assert.Equal("1,234,567주", facts["거래량"]);
        Assert.Equal("4,000억 원", facts["시가총액"]);
        Assert.Equal("12.34", facts["PER"]);
        Assert.StartsWith("50,000원", facts["52주 최고"]);
        Assert.Equal(
            ["전일 종가", "시가", "고가", "저가", "거래량", "시가총액", "상한가", "하한가", "52주 최고", "52주 최저", "PER", "PBR", "EPS"],
            facts.Keys);

        // 내 보유 현황: 600주, 평균 36,000원 → 평가 2,400만 원, +240만 원(+11.11%)
        Assert.Equal("기본 계좌 600주 36,000원 24,000,000원 +2,400,000원 (+11.11%)", Cells(cut.Find("section[aria-labelledby=h-mine] tbody tr")));
    }

    [Fact]
    public async Task 여러_계좌에_있는_종목은_계좌마다_한_줄씩_보여_준다()
    {
        var service = Services.GetRequiredService<PortfolioService>();
        var second = await service.AddAccountAsync("연금저축");
        await service.SaveHoldingAsync(second.Id, "SEED01", "KOSPI200 ETF", 10, 50_000m, null);

        var cut = Render<Stocks>(p => p.Add(x => x.Code, "SEED01"));
        cut.WaitForElement(".facts");

        cut.WaitForAssertion(() => Assert.Equal(
            ["기본 계좌 600주 36,000원 24,000,000원 +2,400,000원 (+11.11%)", "연금저축 10주 50,000원 400,000원 -100,000원 (-20.00%)"],
            cut.FindAll("section[aria-labelledby=h-mine] tbody tr").Select(Cells)));
    }

    private sealed class ScriptedDetails(Func<string, StockDetail?> respond) : IStockDetailProvider
    {
        public Task<StockDetail?> GetAsync(string symbolCode, CancellationToken ct = default) => Task.FromResult(respond(symbolCode));
    }

    [Fact]
    public void 보유하지_않은_종목도_볼_수_있고_추가하는_길을_알려_준다()
    {
        using var doc = JsonDocument.Parse(EtfJson);
        var etf = KisResponseParser.ParseStockDetail(doc.RootElement, "069500", Clock.Now);
        StockDetails = new ScriptedDetails(_ => etf);

        var cut = Render<Stocks>(p => p.Add(x => x.Code, "069500"));
        cut.WaitForElement(".facts");

        Assert.Equal("069500", Text(cut.Find("#h-stock")));              // 종목 목록에 없으면 코드로 표시
        Assert.Equal("109,610원", Text(cut.Find(".price")));
        Assert.Contains("down", cut.Find(".change").ClassName);
        var facts = cut.FindAll(".facts > div").ToDictionary(d => Text(d.QuerySelector("dt")!), d => Text(d.QuerySelector("dd")!));
        Assert.Equal(("-", "-", "-"), (facts["PER"], facts["PBR"], facts["EPS"]));
        Assert.Equal("25.5조 원", facts["시가총액"]);
        Assert.Equal("보유하지 않은 종목입니다. 보유 종목에 추가하기", Text(cut.Find("section[aria-labelledby=h-mine] .detail-empty")));
    }

    [Fact]
    public void 종목_정보를_받아오지_못하면_이유와_다시_시도하는_방법을_알려_준다()
    {
        int calls = 0;
        StockDetails = new ScriptedDetails(_ => { calls++; return null; });

        var cut = Render<Stocks>(p => p.Add(x => x.Code, "SEED01"));
        var message = cut.WaitForElement(".detail-card .detail-empty");

        Assert.Equal("종목 정보를 받아오지 못했습니다. 종목코드를 확인하거나 잠시 뒤 '새로 고침'을 눌러 주세요.", Text(message));
        Assert.Empty(cut.FindAll(".facts"));

        cut.Find(".detail-head button").Click();          // 새로 고침
        cut.WaitForAssertion(() => Assert.Equal(2, calls));
    }

    [Fact]
    public void 표의_종목명을_누르면_종목_정보로_간다()
    {
        var cut = Render<Home>();
        cut.WaitForElement(".summary-grid");

        var link = cut.Find("tbody td.name a.stock-link");
        Assert.Equal(("KOSPI200 ETF", "stocks/SEED01"), (Text(link), link.GetAttribute("href")));
    }
}

public class StockPageWithoutKeyTests : UiTestBase
{
    public StockPageWithoutKeyTests()
    {
        PriceSource = new PriceSourceInfo(Ready: false, SettingsPath: null);
        StockDetails = new KeylessDetails();
    }

    private sealed class KeylessDetails : IStockDetailProvider
    {
        public Task<StockDetail?> GetAsync(string symbolCode, CancellationToken ct = default) => Task.FromResult<StockDetail?>(null);
    }

    [Fact]
    public void KIS_키가_없으면_종목_정보_대신_이유를_알려_준다()
    {
        var cut = Render<Stocks>(p => p.Add(x => x.Code, "SEED01"));
        var message = cut.WaitForElement(".detail-card .detail-empty");

        Assert.Equal("KIS 앱키가 설정되지 않아 종목 정보를 받아올 수 없습니다.", Text(message));
        // 보유 현황은 시세 없이도 수량과 단가를 보여 준다
        Assert.Equal("기본 계좌 600주 36,000원 - -", Cells(cut.Find("section[aria-labelledby=h-mine] tbody tr")));
    }
}
