using Bunit;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Web.Components.Layout;
using Portfolio.Web.Components.Pages;
using Portfolio.Web.Services;

namespace Portfolio.Tests.Ui;

// 현재가를 한 번도 받지 못한 상태 (KIS 키 발급 전): 틀린 숫자를 보여 주지 않고 이유를 안내한다
public class NoPriceStateTests : UiTestBase
{
    protected override bool StartWithPrices => false;

    public NoPriceStateTests() =>
        PriceSource = new PriceSourceInfo(Ready: false, SettingsPath: @"D:\Program\Portfolio\data\settings.json");

    [Fact]
    public void 대시보드는_전액_손실로_보이지_않고_매입금액_기준으로_보여_준다()
    {
        var cut = Render<Home>();
        cut.WaitForElement(".summary-grid");

        var cards = cut.FindAll(".summary-card").Select(Text).ToList();
        Assert.Equal("총 평가금액 95,500,000원 예수금 제외 · 7개 종목은 매입금액 기준", cards[0]);   // 0원이 아니다
        Assert.Equal("평가손익 - 현재가를 받으면 계산됩니다", cards[2]);                              // -100%가 아니다
        Assert.DoesNotContain("-100", cut.Markup);
        Assert.DoesNotContain("down", cut.FindAll(".summary-value")[2].ClassName ?? "");

        // 비중은 매입금액으로 추정한다: 주식 55,600,000 / 95,500,000 = 58.2%
        Assert.Contains("58.2%", Text(cut.FindAll(".legend")[0]));
        Assert.Equal("주식 58.2% / 목표 50.0% +8.2%p 초과", Text(cut.FindAll(".target-head")[0]));

        var row = cut.FindAll(".table tbody tr").First(r => r.TextContent.Contains("KOSPI200 ETF"));
        Assert.Contains("시세 없음", row.TextContent);
        Assert.Contains("21,600,000원", row.TextContent);   // 600주 × 36,000원 (매입금액)
        Assert.Contains("매입가", row.TextContent);
        Assert.Equal("-", Text(row.Children.Last()));       // 손익률은 계산하지 않는다
    }

    [Fact]
    public void 화면에서_원인과_해결_방법을_안내한다()
    {
        var cut = Render<Home>();
        cut.WaitForElement(".summary-grid");

        string notice = Text(cut.Find(".notice-info"));
        Assert.Contains("현재가를 받아오지 못하고 있습니다.", notice);
        Assert.Contains("KIS 앱키가 설정되지 않았습니다.", notice);
        Assert.Contains(@"D:\Program\Portfolio\data\settings.json", notice);
    }

    [Fact]
    public void 추가매수_계산은_종목_없음이_아니라_시세_없음으로_알린다()
    {
        var cut = Render<Rebalance>();
        cut.WaitForElement(".amount-card");

        var groups = cut.FindAll(".group-card tbody tr").Select(Text).ToList();
        Assert.All(groups, g => Assert.DoesNotContain("종목 없음", g));
        Assert.Contains(groups, g => g.Contains("시세 없음"));
        Assert.Equal("배정된 그룹 종목의 현재가가 없어 매수 주수를 계산할 수 없습니다. 그룹별 배정 금액까지만 계산했습니다.",
            cut.Find(".no-orders").TextContent);
        Assert.Contains("KIS 앱키가 설정되지 않았습니다.", cut.Find(".notice-info").TextContent);
    }

    [Fact]
    public void 휴대폰_헤더도_손실로_표시하지_않는다()
    {
        var cut = Render<TopNav>();
        cut.WaitForElement(".mobile-total");

        Assert.Equal("95,500,000원", Text(cut.Find(".mobile-total-amount")));
        Assert.Equal("현재가를 받으면 손익이 계산됩니다", Text(cut.Find(".mobile-total-pl")));
        Assert.Equal("장중 · 시세 없음", cut.Find(".market-text").TextContent);
    }

    [Fact]
    public async Task 시세를_받으면_안내가_사라지고_정상_계산으로_바뀐다()
    {
        var cut = Render<Home>();
        cut.WaitForElement(".notice-info");

        await RefreshPricesAsync(SeedData.Prices);

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll(".notice-info"));
            Assert.Equal("총 평가금액 100,000,000원 예수금 제외", Text(cut.FindAll(".summary-card")[0]));
            Assert.Equal("평가손익 +4,500,000원 +4.71%", Text(cut.FindAll(".summary-card")[2]));
        });
    }
}

// 처음 쓰는 상태 (보유 종목 0개): 0으로 가득 찬 화면 대신 시작 방법을 안내한다
public class EmptyStateTests : UiTestBase
{
    public EmptyStateTests()
    {
        Db.Context.Holdings.ExecuteDelete();
    }

    [Fact]
    public void 대시보드는_시작_안내와_보유_종목_추가_버튼을_보여_준다()
    {
        var cut = Render<Home>();
        var start = cut.WaitForElement(".empty-state");

        Assert.Contains("보유 종목을 추가하면 시작됩니다", start.TextContent);
        Assert.Equal("holdings", start.QuerySelector("a.btn-primary")!.GetAttribute("href"));
        Assert.Empty(cut.FindAll(".summary-card"));      // '0원', '+0.00%', '-50.0%p 부족'을 보여 주지 않는다
        Assert.Empty(cut.FindAll(".target-head"));
        Assert.DoesNotContain("부족", cut.Markup);
    }

    [Fact]
    public void 추가매수_계산은_보유_종목이_필요하다고_안내한다()
    {
        var cut = Render<Rebalance>();
        var start = cut.WaitForElement(".empty-state");

        Assert.Contains("보유 종목이 있어야 계산할 수 있습니다", start.TextContent);
        Assert.Empty(cut.FindAll(".amount-card"));
    }
}

// 조작 흐름: 줄에서 바로 삭제, 결과 알림, 정렬, 그룹 추가 전 이름 입력
public class InteractionFlowTests : UiTestBase
{
    private IRenderedComponent<Holdings> RenderHoldings()
    {
        var cut = Render<Holdings>();
        cut.WaitForElement(".list-card");
        return cut;
    }

    private static List<string> Names(IRenderedComponent<Holdings> cut) =>
        cut.FindAll(".list-card td.name").Select(Text).ToList();

    [Fact]
    public async Task 표의_각_줄에서_바로_삭제할_수_있고_한_번_더_확인한다()
    {
        var cut = RenderHoldings();

        cut.FindAll(".list-card button").Single(b => b.GetAttribute("aria-label") == "리츠 ETF 삭제").Click();

        // 아직 삭제 전: 그 줄에서 확인을 묻는다
        var confirm = cut.Find(".list-card .confirm");
        Assert.Contains("삭제할까요?", confirm.TextContent);
        Assert.Equal(7, await Db.CreateDbContext().Holdings.CountAsync());

        await confirm.QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "삭제").ClickAsync(new());

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("6개 종목", cut.Find("#h-list").TextContent);
            Assert.Equal("리츠 ETF을(를) 삭제했습니다.", cut.Find(".result-message").TextContent);
        });
        Assert.Equal(6, await Db.CreateDbContext().Holdings.CountAsync());
    }

    [Fact]
    public async Task 삭제_확인에서_취소하면_지우지_않는다()
    {
        var cut = RenderHoldings();
        cut.FindAll(".list-card button").Single(b => b.GetAttribute("aria-label") == "리츠 ETF 삭제").Click();

        cut.Find(".list-card .confirm").QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "취소").Click();

        Assert.Empty(cut.FindAll(".list-card .confirm"));
        Assert.Equal(7, await Db.CreateDbContext().Holdings.CountAsync());
    }

    [Fact]
    public async Task 저장하면_결과를_알려_준다()
    {
        var cut = RenderHoldings();
        await cut.FindAll(".list-card button").Single(b => b.GetAttribute("aria-label") == "리츠 ETF 수정").ClickAsync(new());
        cut.WaitForAssertion(() => Assert.Equal("종목 수정", cut.Find("#h-form").TextContent));
        cut.Find("#qty").Input("2000");

        await cut.FindAll(".editor button").Single(b => b.TextContent.Trim() == "저장").ClickAsync(new());

        cut.WaitForAssertion(() =>
        {
            var message = cut.Find(".result-message");
            Assert.Equal("리츠 ETF을(를) 수정했습니다.", message.TextContent);
            Assert.Equal("status", message.GetAttribute("role"));
        });
    }

    [Fact]
    public void 열_제목을_누르면_정렬하고_한_번_더_누르면_반대_세_번째에는_원래_순서로_돌아간다()
    {
        var cut = RenderHoldings();
        var original = Names(cut);
        AngleSharp.Dom.IElement Header(string label) =>
            cut.FindAll(".list-card th button.sort").Single(b => b.TextContent.Trim().StartsWith(label));

        Header("수량").Click();   // 숫자는 큰 순부터
        Assert.Equal(["리츠 ETF", "미국 S&P500 ETF", "미국채 10년 ETF", "고배당 ETF", "KOSPI200 ETF", "반도체 개별주 A", "국고채 10년 ETF"], Names(cut));
        Assert.Equal("descending", Header("수량").ParentElement!.GetAttribute("aria-sort"));

        Header("수량").Click();   // 작은 순
        Assert.Equal("리츠 ETF", Names(cut).Last());
        Assert.Equal("ascending", Header("수량").ParentElement!.GetAttribute("aria-sort"));

        Header("수량").Click();   // 원래 순서
        Assert.Equal(original, Names(cut));
        Assert.Equal("none", Header("수량").ParentElement!.GetAttribute("aria-sort"));

        Header("종목명").Click();  // 글자는 가나다순부터
        Assert.Equal(Names(cut).OrderBy(n => n, StringComparer.CurrentCulture), Names(cut));
    }

    [Fact]
    public void 손익률로_정렬하면_가장_많이_오른_종목이_맨_위에_온다()
    {
        var cut = RenderHoldings();

        cut.FindAll(".list-card th button.sort").Single(b => b.TextContent.Trim().StartsWith("손익률")).Click();

        Assert.Equal("고배당 ETF", Names(cut).First());   // +15.38%
        Assert.Equal("리츠 ETF", Names(cut).Last());      // -9.09%
    }

    [Fact]
    public async Task 그룹_추가는_이름을_입력한_뒤에_만들어진다()
    {
        var cut = Render<Groups>();
        cut.WaitForElement(".group-table");
        var repo = new GroupRepository(Db);

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "그룹 추가").Click();

        // 누르는 것만으로는 그룹이 생기지 않는다
        Assert.Equal(3, (await repo.GetAllAsync()).Count);
        Assert.NotNull(cut.Find("#new-group-name"));

        cut.Find("#new-group-name").Change("금");
        await cut.FindAll(".add-panel button").Single(b => b.TextContent.Trim() == "추가").ClickAsync(new());

        cut.WaitForAssertion(() => Assert.Equal(["주식", "채권", "배당", "금"], cut.FindAll(".group-name").Select(Text)));
        Assert.Empty(cut.FindAll(".add-panel"));
        Assert.Equal(0m, (await repo.GetAllAsync()).Single(g => g.Name == "금").TargetWeight);
    }

    [Fact]
    public async Task 그룹_추가를_취소하거나_이름이_겹치면_만들지_않는다()
    {
        var cut = Render<Groups>();
        cut.WaitForElement(".group-table");
        var repo = new GroupRepository(Db);

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "그룹 추가").Click();
        cut.Find("#new-group-name").Change("주식");
        await cut.FindAll(".add-panel button").Single(b => b.TextContent.Trim() == "추가").ClickAsync(new());

        Assert.Equal("같은 이름의 그룹이 이미 있습니다.", cut.Find(".add-panel .field-error").TextContent);

        cut.FindAll(".add-panel button").Single(b => b.TextContent.Trim() == "취소").Click();

        Assert.Empty(cut.FindAll(".add-panel"));
        Assert.Equal(3, (await repo.GetAllAsync()).Count);
    }

    [Fact]
    public void 대시보드는_범례를_누르면_표가_걸러진다는_것을_알려_준다()
    {
        var cut = Render<Home>();
        cut.WaitForElement(".summary-grid");

        Assert.Equal(
            ["항목을 누르면 아래 표에서 그 그룹만 봅니다.", "항목을 누르면 아래 표에서 그 종목만 봅니다."],
            cut.FindAll(".hint").Select(Text));
    }

}
