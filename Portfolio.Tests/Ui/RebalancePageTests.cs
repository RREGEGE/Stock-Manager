using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Core;
using Portfolio.Data;
using Portfolio.Web.Components.Layout;
using Portfolio.Web.Components.Pages;
using Portfolio.Web.Services;

namespace Portfolio.Tests.Ui;

public class RebalancePageTests : UiTestBase
{
    private IRenderedComponent<Rebalance> RenderRebalance()
    {
        var cut = Render<Rebalance>();
        cut.WaitForElement(".amount-card");
        return cut;
    }

    [Fact]
    public void AC08_추가매수_화면은_목업과_같은_값과_문구를_보여_준다()
    {
        var cut = RenderRebalance();

        Assert.Equal("추가매수 계산", cut.Find("h1").TextContent);
        Assert.Contains("매도 없이 매수만으로 목표 비중에 가깝게 배분합니다. 09:41 시세 기준이며 수수료·세금은 제외합니다.", Text(cut.Find(".page-head")));
        Assert.Equal("10,000,000", cut.Find("#amount").GetAttribute("value"));
        Assert.Equal(["+100만", "+500만", "+1,000만", "지우기"], cut.FindAll(".quick button").Select(Text));
        Assert.Equal("10,000,000원", cut.Find("#spent").TextContent);
        Assert.Equal("0원", cut.Find("#leftover").TextContent);

        // 현재 / 매수 후 / 목표 누적 막대
        Assert.Equal(["현재", "매수 후", "목표"], cut.FindAll(".stacked-label").Select(Text));
        Assert.Equal(
            ["60.0% 20.0% 20.0%", "54.5% 27.3% 18.2%", "50.0% 30.0% 20.0%"],
            cut.FindAll(".stacked-track").Select(Cells));
        var firstSeg = cut.Find(".stacked-seg").GetAttribute("style")!;
        Assert.Contains("background: #23395B", firstSeg);
        Assert.Contains("color: #FFFFFF", firstSeg);      // 주식 위 글자는 흰색
        Assert.Contains("color: #1B1F24", cut.FindAll(".stacked-seg")[1].GetAttribute("style"));   // 채권 위 글자는 어두운 색

        Assert.Equal(
            ["주식 60.0% 50.0% 0원 54.5%", "채권 20.0% 30.0% 10,000,000원 27.3%", "배당 20.0% 20.0% 0원 18.2%"],
            cut.FindAll(".group-card tbody tr").Select(Cells));
        Assert.Equal(
            ["국고채 10년 ETF 채권 100,000원 50주 5,000,000원", "미국채 10년 ETF 채권 10,000원 500주 5,000,000원"],
            cut.FindAll(".order-card tbody tr").Select(Cells));
    }

    [Fact]
    public void 금액을_바꾸면_즉시_다시_계산한다()
    {
        var cut = RenderRebalance();

        cut.FindAll(".quick button")[0].Click();   // +100만
        Assert.Equal("11,000,000", cut.Find("#amount").GetAttribute("value"));
        Assert.Equal("채권 20.0% 30.0% 10,600,000원 27.6%", Cells(cut.FindAll(".group-card tbody tr")[1]));

        cut.Find("#amount").Input("33,333");
        Assert.Equal("30,000원", cut.Find("#spent").TextContent);
        Assert.Equal("3,333원", cut.Find("#leftover").TextContent);

        cut.FindAll(".quick button")[3].Click();   // 지우기
        Assert.Equal("0", cut.Find("#amount").GetAttribute("value"));
        Assert.Equal("투입 금액을 입력하면 종목별 매수 수량이 표시됩니다.", cut.Find(".no-orders").TextContent);
    }

    [Fact]
    public void 구간_폭이_8퍼센트_미만이면_막대_안_숫자를_숨긴다()
    {
        var cut = RenderRebalance();
        cut.Find("#amount").Input("0");

        // 미분류 없이 현재 60/20/20 → 모두 표시
        Assert.All(cut.FindAll(".stacked-seg"), s => Assert.NotEqual("", s.TextContent));

        // 리츠 ETF(8%)만 남도록 배당 그룹의 고배당 ETF를 지우면 배당 비중이 8/88 = 9.1% → 표시
        // 직접 계산으로 경계 확인: 7.9%는 숨기고 8.0%는 표시
        var bar = Render<Portfolio.Web.Components.Shared.StackedBar>(p => p
            .Add(x => x.Label, "현재")
            .Add(x => x.Segments,
            [
                new Portfolio.Web.Services.ChartSegment("a", "가", "#23395B", 0.841m),
                new Portfolio.Web.Services.ChartSegment("b", "나", "#E08A2E", 0.08m),
                new Portfolio.Web.Services.ChartSegment("c", "다", "#6BB3A8", 0.079m),
            ]));
        Assert.Equal(["84.1%", "8.0%", ""], bar.FindAll(".stacked-seg").Select(s => s.TextContent));
    }

    [Fact]
    public async Task 목표_합계가_100퍼센트가_아니면_계산_대신_안내를_보여_준다()
    {
        await new GroupRepository(Db).DeleteAsync(SeedData.DividendGroupId);   // 목표 50 + 30 = 80%, 배당 종목은 미분류

        var cut = RenderRebalance();

        var notices = cut.FindAll(".notice").Select(Text).ToList();
        Assert.Contains(notices, n => n.StartsWith("그룹 목표 비중 합계가 100%가 아니어서 계산할 수 없습니다."));
        Assert.Contains(notices, n => n.StartsWith("미분류 종목이 있습니다."));
        Assert.Empty(cut.FindAll(".order-card tbody tr"));
    }

    // 리밸런싱(매도 포함) 방식 (설계서 F-10)
    private IRenderedComponent<Rebalance> RenderSellMode()
    {
        var cut = RenderRebalance();
        cut.FindAll(".mode-tab")[1].Click();
        return cut;
    }

    [Fact]
    public void 처음에는_추가매수_방식이고_리밸런싱으로_전환할_수_있다()
    {
        var cut = RenderRebalance();

        Assert.Equal(["추가매수", "리밸런싱"], cut.FindAll(".mode-tab").Select(Text));
        Assert.Equal(["true", "false"], cut.FindAll(".mode-tab").Select(b => b.GetAttribute("aria-pressed")));

        cut.FindAll(".mode-tab")[1].Click();

        Assert.Equal("리밸런싱 계산", cut.Find("h1").TextContent);
        Assert.Contains("새 돈을 넣지 않고, 목표보다 많은 그룹을 팔아 부족한 그룹을 삽니다. 09:41 시세 기준이며 수수료·세금은 제외합니다.", Text(cut.Find(".page-head")));
        Assert.Empty(cut.FindAll(".amount-card"));          // 투입 금액 입력은 없다
        Assert.Equal(["false", "true"], cut.FindAll(".mode-tab").Select(b => b.GetAttribute("aria-pressed")));

        cut.FindAll(".mode-tab")[0].Click();
        Assert.Equal("추가매수 계산", cut.Find("h1").TextContent);
        Assert.Equal("10,000,000", cut.Find("#amount").GetAttribute("value"));   // 넣어 둔 금액은 그대로
    }

    [Fact]
    public void 리밸런싱은_그룹별_조정_금액과_종목별_매도_매수_수량을_보여_준다()
    {
        var cut = RenderSellMode();

        Assert.Equal(("10,000,000원", "10,000,000원", "0원"),
            (cut.Find("#sell-total").TextContent, cut.Find("#buy-total").TextContent, cut.Find("#sell-leftover").TextContent));
        Assert.Equal(["현재", "조정 후", "목표"], cut.FindAll(".stacked-label").Select(Text));
        Assert.Equal(
            ["60.0% 20.0% 20.0%", "50.0% 30.0% 20.0%", "50.0% 30.0% 20.0%"],
            cut.FindAll(".stacked-track").Select(Cells));

        Assert.Equal(
            ["주식 60,000,000원 60.0% 50.0% 10,000,000원 매도 50.0%",
             "채권 20,000,000원 20.0% 30.0% 10,000,000원 매수 30.0%",
             "배당 20,000,000원 20.0% 20.0% 유지 20.0%"],
            cut.FindAll(".group-card tbody tr").Select(Cells));
        // 팔 때는 파랑(down), 살 때는 빨강(up)
        Assert.Equal([true, false, false], cut.FindAll(".group-card tbody tr").Select(r => r.Children[4].ClassList.Contains("down")));
        Assert.True(cut.FindAll(".group-card tbody tr")[1].Children[4].ClassList.Contains("up"));

        Assert.Equal(
            ["KOSPI200 ETF 주식 매도 40,000원 101주 4,040,000원",
             "미국 S&P500 ETF 주식 매도 20,000원 202주 4,040,000원",
             "반도체 개별주 A 주식 매도 120,000원 16주 1,920,000원",
             "국고채 10년 ETF 채권 매수 100,000원 50주 5,000,000원",
             "미국채 10년 ETF 채권 매수 10,000원 500주 5,000,000원"],
            cut.FindAll(".order-card tbody tr").Select(Cells));
    }

    [Fact]
    public async Task 리밸런싱은_시세가_바뀌면_새로고침_없이_다시_계산한다()
    {
        var cut = RenderSellMode();

        // 채권 값이 두 배가 되면 주식 60 / 채권 40 / 배당 20 (합계 1억 2,000만)
        var prices = SeedData.Prices.ToDictionary(p => p.Key, p => p.Value);
        prices["SEED04"] = 200_000m;
        prices["SEED05"] = 20_000m;
        await RefreshPricesAsync(prices);

        cut.WaitForAssertion(() => Assert.Equal(
            ["주식 60,000,000원 50.0% 50.0% 유지 50.0%",
             "채권 40,000,000원 33.3% 30.0% 4,000,000원 매도 30.0%",
             "배당 20,000,000원 16.7% 20.0% 4,000,000원 매수 20.0%"],
            cut.FindAll(".group-card tbody tr").Select(Cells)));
    }

    [Fact]
    public async Task 리밸런싱은_이미_목표_비중이면_옮길_금액이_없다고_알린다()
    {
        // 목표를 현재 비중(60/20/20)과 같게 바꾼다
        var groups = new GroupRepository(Db);
        await groups.SaveTargetsAsync(new Dictionary<int, decimal>
        {
            [SeedData.StockGroupId] = 0.6m, [SeedData.BondGroupId] = 0.2m, [SeedData.DividendGroupId] = 0.2m,
        });

        var cut = RenderSellMode();

        Assert.Equal("이미 목표 비중과 같습니다. 옮길 금액이 없습니다.", Text(cut.Find(".balanced")));
        Assert.Empty(cut.FindAll("#sell-total"));
        Assert.Equal("매도하거나 매수할 종목이 없습니다.", cut.Find(".no-orders").TextContent);
    }

    [Fact]
    public async Task 리밸런싱은_미분류가_전부_매도_대상이라고_경고한다()
    {
        await new GroupRepository(Db).DeleteAsync(SeedData.DividendGroupId);   // 배당 종목이 미분류가 되고 목표 합계는 80%

        var cut = RenderSellMode();

        var notices = cut.FindAll(".notice").Select(Text).ToList();
        Assert.Contains(notices, n => n.StartsWith("미분류 종목이 있습니다. 미분류는 목표 0%로 계산되어 전부 매도 대상이 됩니다."));
        Assert.Contains(notices, n => n.StartsWith("그룹 목표 비중 합계가 100%가 아니어서 계산할 수 없습니다."));
        Assert.Equal("그룹 목표 비중 합계를 100%로 맞추면 계산됩니다.", cut.Find(".no-orders").TextContent);
    }

}
