using System.Net;
using System.Text.Json;
using System.Web;
using Bunit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Core;
using Portfolio.Data;
using Portfolio.Kis;
using Portfolio.Web.Components.Pages;
using Portfolio.Web.Components.Shared;
using Portfolio.Web.Services;

namespace Portfolio.Tests.Ui;

// 예상 배당 (설계서 F-16)
public class DividendEstimatorTests
{
    private static readonly DateOnly Today = new(2026, 11, 4);

    private static HoldingView H(string code, long quantity, decimal price = 10_000m) =>
        new(code, code, "배당", quantity, price, price, false);

    private static DividendRecord D(string code, string record, string? pay, decimal perShare) =>
        new(code, DateOnly.Parse(record), pay is null ? null : DateOnly.Parse(pay), perShare);

    // 실제 응답(2026-11-04)의 삼성전자 배당: 결산 1회 + 분기 3회
    private static readonly IReadOnlyList<DividendRecord> Samsung =
    [
        D("005930", "2026-09-30", "2026-11-27", 4601m), D("005930", "2026-06-30", "2026-08-28", 374m),
        D("005930", "2026-03-31", "2026-05-29", 372m), D("005930", "2025-12-31", "2026-04-17", 566m),
        D("005930", "2025-09-30", "2025-11-20", 361m),   // 12개월보다 앞선 기준일: 세지 않는다
    ];

    [Fact]
    public void 최근_12개월_배당이_같은_달에_다시_나온다고_본다()
    {
        var f = DividendEstimator.Estimate([H("005930", 10)], new Dictionary<string, IReadOnlyList<DividendRecord>> { ["005930"] = Samsung },
            Today, AccountTaxType.Taxable);

        // 이번 달(11월)부터 12개월. 지급일이 아직 오지 않은 11월 배당은 그대로, 지난 지급은 1년 뒤로
        Assert.Equal((2026, 11), (f.Months[0].Year, f.Months[0].Month));
        Assert.Equal((2027, 10), (f.Months[11].Year, f.Months[11].Month));
        var byMonth = f.Months.Where(m => m.Amount > 0).Select(m => (m.Year, m.Month, m.Amount));
        Assert.Equal([(2026, 11, 46_010m), (2027, 4, 5_660m), (2027, 5, 3_720m), (2027, 8, 3_740m)], byMonth);

        Assert.Equal(59_130m, f.AnnualAmount);
        Assert.Equal(59_130m / 12m, f.MonthlyAverage);
        var h = Assert.Single(f.Holdings);
        Assert.Equal((5_913m, 59_130m), (h.AnnualPerShare, h.AnnualAmount));
        Assert.Equal([4, 5, 8, 11], h.PayMonths);
    }

    [Fact]
    public void 이번_달에_이미_받은_배당도_이번_달에_센다()
    {
        // 월배당 ETF: 10월 30일 기준, 11월 3일 지급 (오늘 11월 4일)
        IReadOnlyList<DividendRecord> monthly = [D("458730", "2026-10-30", "2026-11-03", 35m), D("458730", "2026-09-30", "2026-10-02", 39m)];

        var f = DividendEstimator.Estimate([H("458730", 100)], new Dictionary<string, IReadOnlyList<DividendRecord>> { ["458730"] = monthly },
            Today, AccountTaxType.Taxable);

        Assert.Equal(3_500m, f.Months[0].Amount);                 // 2026년 11월
        Assert.Equal(3_900m, f.Months[11].Amount);                // 2027년 10월 (10월 지급이 1년 뒤로)
    }

    [Theory]
    [InlineData(AccountTaxType.Taxable, 100_000, 84_600)]   // 15.4% 원천징수
    [InlineData(AccountTaxType.Isa, 100_000, 100_000)]      // 계좌 안에서는 원천징수 없음
    [InlineData(AccountTaxType.Pension, 100_000, 100_000)]
    public void 세후_금액은_계좌_종류에_따라_다르다(AccountTaxType taxType, int annual, int afterTax)
    {
        Assert.Equal(afterTax, DividendEstimator.AfterTax(annual, taxType));
    }

    [Fact]
    public void 배당이_없는_종목과_지급일이_없는_배당을_처리한다()
    {
        IReadOnlyList<DividendRecord> noPayDate = [D("X", "2026-06-30", null, 100m)];   // 지급일이 없으면 기준일 다음 달

        var f = DividendEstimator.Estimate([H("X", 10), H("Y", 10)], new Dictionary<string, IReadOnlyList<DividendRecord>> { ["X"] = noPayDate },
            Today, AccountTaxType.Taxable);

        Assert.Equal([7], Assert.Single(f.Holdings).PayMonths);
        Assert.Equal(1_000m, f.Months.Single(m => m.Month == 7).Amount);
        Assert.Equal(1_000m / 200_000m, f.Yield);                 // 배당이 없는 종목도 평가금액에는 들어간다
    }

    [Fact]
    public void 보유_종목에_배당이_없으면_빈_예상을_돌려준다()
    {
        var f = DividendEstimator.Estimate([H("Y", 10)], new Dictionary<string, IReadOnlyList<DividendRecord>>(), Today, AccountTaxType.Isa);

        Assert.False(f.HasDividends);
        Assert.Empty(f.Holdings);
        Assert.Equal(12, f.Months.Count);
        Assert.All(f.Months, m => Assert.Equal(0m, m.Amount));
    }
}

public class DividendKisTests
{
    // 실제 응답(2026-11-04)에서 쓰는 항목만 남긴 것
    private const string Json = """
        {"output1":[
          {"record_date":"20260930","sht_cd":"005930","isin_name":"삼성전자","divi_kind":"분기","per_sto_divi_amt":"4601","divi_pay_dt":"2026/11/27"},
          {"record_date":"20251231","sht_cd":"005930","isin_name":"삼성전자","divi_kind":"결산","per_sto_divi_amt":"566","divi_pay_dt":"2026/04/17"},
          {"record_date":"20251231","sht_cd":"005930","isin_name":"삼성전자","divi_kind":"결산","per_sto_divi_amt":"0","divi_pay_dt":""},
          {"record_date":"20260630","sht_cd":"999999","isin_name":"다른 종목","divi_kind":"","per_sto_divi_amt":"10","divi_pay_dt":"2026/07/02"}
        ],"rt_cd":"0","msg_cd":"MCA00000","msg1":"정상처리 되었습니다."}
        """;

    [Fact]
    public void 배당_이력_응답을_읽는다()
    {
        using var doc = JsonDocument.Parse(Json);

        var records = KisResponseParser.ParseDividends(doc.RootElement, "005930");

        // 현금 배당이 0원인 줄과 다른 종목의 줄은 뺀다. 기준일 최신 순.
        Assert.Equal(
            [(new DateOnly(2026, 9, 30), (DateOnly?)new DateOnly(2026, 11, 27), 4_601m, "분기"), (new DateOnly(2025, 12, 31), new DateOnly(2026, 4, 17), 566m, "결산")],
            records.Select(r => (r.RecordDate, r.PayDate, r.PerShare, r.Kind)));
    }

    [Fact]
    public async Task 배당_이력은_예탁원_배당일정_주소로_조회한다()
    {
        var handler = new FakeKisHandler { OnApi = _ => FakeKisHandler.Json(HttpStatusCode.OK, Json) };
        var (_, client) = KisTestFactory.Create(handler, new InMemoryTokenStore(), new ManualClock(DateTimeOffset.Now));

        var records = await new KisDividendProvider(client).GetAsync("005930", new DateOnly(2025, 8, 4), new DateOnly(2026, 11, 4));

        Assert.Equal(2, records.Count);
        var call = Assert.Single(handler.ApiRequests);
        var q = HttpUtility.ParseQueryString(call.Query);
        Assert.Equal((KisClient.DividendPath, "HHKDB669102C0"), (call.Path, call.Headers["tr_id"]));
        Assert.Equal(("0", "20250804", "20261104", "005930"), (q["GB1"], q["F_DT"], q["T_DT"], q["SHT_CD"]));
    }

    [Theory]
    [InlineData("GET", "/uapi/domestic-stock/v1/ksdinfo/dividend", true)]     // 배당일정 조회
    [InlineData("POST", "/uapi/domestic-stock/v1/ksdinfo/dividend", false)]    // 조회 경로라도 POST는 불가
    [InlineData("POST", "/uapi/domestic-stock/v1/trading/order-cash", false)]  // 주문은 계속 막는다
    [InlineData("GET", "/uapi/domestic-stock/v1/trading/inquire-balance", false)]
    public void 주문_차단_필터는_배당일정_조회만_추가로_허용한다(string method, string path, bool expected)
    {
        Assert.Equal(expected, KisQuotationOnlyHandler.IsAllowed(new HttpMethod(method), new Uri("https://openapi.koreainvestment.com:9443" + path)));
    }
}

public class DividendRepositoryTests
{
    private sealed class CountingProvider(Func<string, IReadOnlyList<DividendRecord>> respond) : IDividendProvider
    {
        public List<string> Asked { get; } = [];
        public Task<IReadOnlyList<DividendRecord>> GetAsync(string symbolCode, DateOnly from, DateOnly to, CancellationToken ct = default)
        {
            Asked.Add(symbolCode);
            return Task.FromResult(respond(symbolCode));
        }
    }

    [Fact]
    public async Task 종목마다_하루_한_번만_조회하고_배당이_없는_종목도_다시_묻지_않는다()
    {
        using var db = new TestDb();
        await SeedData.ApplyAsync(db.Context);
        var clock = new ManualClock(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.FromHours(9)));
        var seed = SeedData.Dividends(new DateOnly(2026, 10, 2));
        var provider = new CountingProvider(code => seed.GetValueOrDefault(code, []));
        var repo = new DividendRepository(db, provider, clock);

        Assert.Equal(7, await repo.RefreshDueAsync());
        Assert.Equal(7, provider.Asked.Count);
        Assert.Equal(0, await repo.RefreshDueAsync());          // 같은 날에는 다시 묻지 않는다
        Assert.Equal(7, provider.Asked.Count);

        clock.Now = clock.Now.AddDays(1);
        Assert.Equal(7, await repo.RefreshDueAsync());          // 하루가 지나면 다시

        var stored = await repo.GetAsync(["SEED07", "SEED04"]);
        Assert.Equal(12, stored["SEED07"].Count);
        Assert.False(stored.ContainsKey("SEED04"));            // 배당이 없는 종목
    }

    [Fact]
    public async Task 조회에_실패한_종목은_저장된_이력을_그대로_두고_다음에_다시_묻는다()
    {
        using var db = new TestDb();
        await SeedData.ApplyAsync(db.Context);
        var clock = new ManualClock(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.FromHours(9)));
        var seed = SeedData.Dividends(new DateOnly(2026, 10, 2));
        await new DividendRepository(db, new FakeDividendProvider(seed), clock).RefreshDueAsync();

        clock.Now = clock.Now.AddDays(1);
        var failing = new CountingProvider(code => code == "SEED07" ? throw new HttpRequestException("통신 실패") : seed.GetValueOrDefault(code, []));
        var repo = new DividendRepository(db, failing, clock);

        Assert.Equal(6, await repo.RefreshDueAsync());
        Assert.Equal(12, (await repo.GetAsync(["SEED07"]))["SEED07"].Count);   // 직전 이력 유지
        failing.Asked.Clear();
        await repo.RefreshDueAsync();
        Assert.Equal(["SEED07"], failing.Asked);                                // 실패한 종목만 다시 묻는다
    }

    [Fact]
    public async Task 이름이_ISA인_기존_계좌는_ISA_계좌로_시작한다()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<PortfolioDbContext>().UseSqlite(connection).Options;
        await using (var before = new PortfolioDbContext(options))
        {
            await before.GetService<IMigrator>().MigrateAsync("AddAccounts");   // 배당 기능 이전
            await before.Database.ExecuteSqlRawAsync("""
                UPDATE Accounts SET Name = 'ISA' WHERE Id = 1;
                INSERT INTO Accounts (Id, Name, SortOrder) VALUES (2, '일반', 2);
                """);
        }

        await using var db = new PortfolioDbContext(options);
        await db.Database.MigrateAsync();

        Assert.Equal([("ISA", AccountTaxType.Isa), ("일반", AccountTaxType.Taxable)],
            await db.Accounts.OrderBy(a => a.Id).Select(a => ValueTuple.Create(a.Name, a.TaxType)).ToListAsync());
    }
}

public class DividendPageTests : UiTestBase
{
    private PortfolioService Service => Services.GetRequiredService<PortfolioService>();

    [Fact]
    public void 대시보드는_예상_월_배당금과_달별_막대와_종목별_내역을_보여_준다()
    {
        var cut = Render<Home>();
        cut.WaitForElement(".dividend-card");

        // 시드: 리츠 1,600주 × 20원 × 12 + 고배당 800주 × 150원 × 4 + S&P500 1,200주 × 60원 × 4 = 1,152,000원
        Assert.Equal("예상 월 배당금 96,000원 세후 81,216원 · 일반 계좌", Text(cut.FindAll(".summary-card")[4]));
        Assert.Equal("일반 계좌", Text(cut.Find(".tax-badge")));
        Assert.Equal(
            ["월평균 (세전) 96,000원", "월평균 (세후) 81,216원", "연간 (세전) 1,152,000원", "배당 수익률 1.2%"],
            cut.FindAll(".dividend-totals > div").Select(Cells));

        // 오늘이 10월 2일이므로 10월부터 12개월
        Assert.Equal(["10월", "11월", "12월", "1월", "2월", "3월", "4월", "5월", "6월", "7월", "8월", "9월"],
            cut.FindAll(".bar-month").Select(Text));
        Assert.Contains("10월 152,000원, 11월 104,000원", cut.Find(".dividend-bars").GetAttribute("aria-label"));   // 9월 기준(리츠+고배당), 10월 기준(리츠+S&P500)
        Assert.Contains("1월 152,000원", cut.Find(".dividend-bars").GetAttribute("aria-label"));   // 리츠 + 고배당 12월분 + S&P

        Assert.Equal(
            ["고배당 ETF 1월, 4월, 7월, 10월 600원 480,000원", "리츠 ETF 매월 240원 384,000원", "미국 S&P500 ETF 2월, 5월, 8월, 11월 240원 288,000원"],
            cut.FindAll(".dividend-card tbody tr").Select(Cells));
        Assert.Equal("stocks/SEED06", cut.Find(".dividend-card a.stock-link").GetAttribute("href"));
        Assert.Contains("세후는 배당소득세 15.4% 원천징수를 뺀 금액입니다.", Text(cut.Find(".dividend-card .note")));
    }

    [Fact]
    public async Task ISA_계좌로_바꾸면_세후가_세전과_같아진다()
    {
        await Service.SetAccountTaxTypeAsync(TradingAccount.DefaultId, AccountTaxType.Isa);

        var cut = Render<Home>();
        cut.WaitForElement(".dividend-card");

        Assert.Equal("예상 월 배당금 96,000원 세후 96,000원 · ISA 계좌", Text(cut.FindAll(".summary-card")[4]));
        Assert.Equal("ISA 계좌", Text(cut.Find(".tax-badge")));
        Assert.Contains("이 계좌는 배당을 받을 때 세금이 떼어지지 않아", Text(cut.Find(".dividend-card .note")));
    }

    [Fact]
    public void 설정_화면에서_계좌_종류를_고를_수_있다()
    {
        var cut = Render<AccountManager>();
        var select = cut.WaitForElement(".tax-select");

        Assert.Equal("기본 계좌 계좌 종류", select.GetAttribute("aria-label"));
        Assert.Equal(["일반", "ISA", "연금(연금저축·IRP)"], cut.FindAll(".tax-select option").Select(Text));
        Assert.True(cut.FindAll(".tax-select option")[0].HasAttribute("selected"));

        select.Change(((int)AccountTaxType.Isa).ToString());

        cut.WaitForAssertion(() => Assert.Equal("계좌 종류를 'ISA'(으)로 바꿨습니다.", Text(cut.Find(".status"))));
        cut.WaitForAssertion(() => Assert.True(cut.FindAll(".tax-select option")[1].HasAttribute("selected")));
    }

    [Fact]
    public void 종목_정보_화면에_배당_이력과_배당_수익률을_보여_준다()
    {
        var cut = Render<Stocks>(p => p.Add(x => x.Code, "SEED07"));
        cut.WaitForElement(".facts");

        cut.WaitForAssertion(() => Assert.Equal("최근 12개월 주당 240원 · 현재가 기준 배당 수익률 4.8%", Text(cut.Find(".dividend-sum"))));
        var rows = cut.FindAll("section[aria-labelledby=h-dividends] tbody tr").Select(Cells).ToList();
        Assert.Equal(12, rows.Count);
        Assert.Equal("2026-09-30 2026-10-03 20원", rows[0]);
    }

    [Fact]
    public void 배당이_없는_종목은_없다고_알린다()
    {
        var cut = Render<Stocks>(p => p.Add(x => x.Code, "SEED04"));
        cut.WaitForElement(".facts");

        Assert.Equal("최근 배당 이력이 없습니다.", Text(cut.Find("section[aria-labelledby=h-dividends] .detail-empty")));
    }

    [Fact]
    public void 영어로는_배당_영역도_영어로_보여_준다()
    {
        T.Language = Loc.English;

        var cut = Render<Home>();
        cut.WaitForElement(".dividend-card");

        Assert.Equal("Expected dividends by month", Text(cut.Find("#h-dividend")));
        Assert.Equal(["Oct", "Nov", "Dec", "Jan"], cut.FindAll(".bar-month").Take(4).Select(Text));
        Assert.Equal("리츠 ETF Monthly ₩240 ₩384,000", Cells(cut.FindAll(".dividend-card tbody tr")[1]));
    }
}

public class DividendWithoutHistoryTests : UiTestBase
{
    public DividendWithoutHistoryTests() => DividendSource = new FakeDividendProvider(new Dictionary<string, IReadOnlyList<DividendRecord>>());

    protected override bool StartWithPrices => true;

    [Fact]
    public async Task 배당_이력이_없으면_0원이_아니라_없다고_알린다()
    {
        // 시작할 때 받아 둔 시드 배당을 지운다
        await Db.Context.Dividends.ExecuteDeleteAsync();

        var cut = Render<Home>();
        cut.WaitForElement(".dividend-card");

        Assert.Equal("예상 월 배당금 - 배당 이력이 없습니다", Text(cut.FindAll(".summary-card")[4]));
        Assert.Equal("최근 12개월에 배당을 준 보유 종목이 없습니다. 배당 이력은 하루 한 번 받아 옵니다.", Text(cut.Find(".dividend-empty")));
        Assert.Empty(cut.FindAll(".dividend-bars"));
    }
}
