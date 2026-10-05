using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Data;
using Portfolio.Web.Components.Layout;
using Portfolio.Web.Components.Pages;
using Portfolio.Web.Services;

namespace Portfolio.Tests.Ui;

public class DashboardPageTests : UiTestBase
{
    private IRenderedComponent<Home> RenderDashboard()
    {
        var cut = Render<Home>();
        cut.WaitForElement(".summary-grid");
        return cut;
    }

    [Fact]
    public void AC08_대시보드는_목업과_같은_값과_문구를_보여_준다()
    {
        var cut = RenderDashboard();

        Assert.Equal("대시보드", cut.Find("h1").TextContent);
        Assert.Equal(
        [
            "총 평가금액 100,000,000원 예수금 제외",
            "매입금액 95,500,000원 수량 × 평균매입단가",
            "평가손익 +4,500,000원 +4.71%",
            "보유 종목 7개 미분류 0개 · 시세 지연 0개",
        ], cut.FindAll(".summary-card").Select(Text));
        Assert.Contains("up", cut.FindAll(".summary-value")[2].ClassName);

        var donuts = cut.FindAll(".donut");
        Assert.Equal("background: conic-gradient(#23395B 0 60%, #E08A2E 60% 80%, #6BB3A8 80% 100%)", donuts[0].GetAttribute("style"));
        Assert.Equal("background: conic-gradient(#23395B 0 24%, #3F5B86 24% 48%, #7189AE 48% 60%, #E08A2E 60% 70%, #F2B56E 70% 80%, #4E9B90 80% 92%, #A3D3CB 92% 100%)",
            donuts[1].GetAttribute("style"));
        Assert.Equal(["3개 그룹 1억 원", "7개 종목 1억 원"], cut.FindAll(".donut-hole").Select(Text));

        Assert.Equal(
        [
            "주식 60.0% / 목표 50.0% +10.0%p 초과",
            "채권 20.0% / 목표 30.0% -10.0%p 부족",
            "배당 20.0% / 목표 20.0% 목표 도달",
        ], cut.FindAll(".target-head").Select(Text));
        Assert.Equal(["over", "under", "ok"], cut.FindAll(".target-diff").Select(d => d.ClassList.Last()));

        Assert.Equal(["종목명", "그룹", "평가금액", "비중", "손익률"], cut.FindAll(".table th").Select(Text));
        var rows = cut.FindAll(".table tbody tr").Select(Cells).ToList();
        Assert.Equal(7, rows.Count);
        Assert.Equal("KOSPI200 ETF 주식 24,000,000원 24.0% +11.11%", rows[0]);
        Assert.Equal("리츠 ETF 배당 8,000,000원 8.0% -9.09%", rows[6]);
        Assert.Contains("비중은 소수점 1자리 반올림으로, 합계가 100.0%가 아닐 수 있습니다.", cut.Markup);
    }

    [Fact]
    public async Task 시세_지연_종목은_표에_표시하고_개수를_센다()
    {
        var cut = RenderDashboard();

        var partial = SeedData.Prices.Where(kv => kv.Key != "SEED03").ToDictionary();
        await RefreshPricesAsync(partial);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("시세 지연 1개", Text(cut.FindAll(".summary-card")[3]));
            var stale = Assert.Single(cut.FindAll(".table tr.stale"));
            Assert.Contains("반도체 개별주 A", stale.TextContent);
            Assert.Contains("시세 지연", stale.TextContent);
        });
    }

    [Fact]
    public async Task 미분류_종목이_있으면_상단에_그룹_지정_알림을_띄운다()
    {
        await new GroupRepository(Db).DeleteAsync(SeedData.DividendGroupId);

        var cut = RenderDashboard();

        Assert.Contains("그룹이 지정되지 않은 종목이 2개 있습니다", cut.Find(".notice").TextContent);
        Assert.Contains("미분류 2개", Text(cut.FindAll(".summary-card")[3]));
    }

    [Fact]
    public void 범례를_누르면_해당_그룹의_종목만_보여_준다()
    {
        var cut = RenderDashboard();

        cut.FindAll("button.legend-item").First(b => b.TextContent.Contains("채권")).Click();

        Assert.Equal(["국고채 10년 ETF", "미국채 10년 ETF"], cut.FindAll(".table td.name").Select(Text));

        cut.Find(".filter-chip").Click();
        Assert.Equal(7, cut.FindAll(".table tbody tr").Count);
    }

    [Fact]
    public async Task 종목이_10개를_넘으면_상위_9개와_기타로_묶는다()
    {
        var service = Services.GetRequiredService<PortfolioService>();
        var prices = SeedData.Prices.ToDictionary();
        for (int i = 0; i < 5; i++)
        {
            string code = $"EXTRA{i}";
            prices[code] = 1_000m;
            await new HoldingRepository(Db.Context).SaveAsync(code, $"소액 종목 {i}", 10 + i, 1_000m, SeedData.StockGroupId);
        }
        await RefreshPricesAsync(prices);

        var model = new PortfolioViewModel(await service.LoadAsync());

        Assert.Equal(12, model.Rows.Count);
        Assert.Equal(10, model.SymbolSegments.Count);
        Assert.Equal(PortfolioViewModel.OthersName, model.SymbolSegments[^1].Name);
        Assert.Equal(1m, Math.Round(model.SymbolSegments.Sum(s => s.Ratio), 10));
        Assert.DoesNotContain(model.SymbolSegments, s => s.Name.StartsWith("소액 종목 0"));
    }
}
