using Bunit;
using Portfolio.Core;
using Portfolio.Web.Components.Pages;
using Portfolio.Web.Components.Shared;
using Portfolio.Web.Services;

namespace Portfolio.Tests.Ui;

// 대시보드 맨 위의 지수·환율 띠 (설계서 F-09)
public class MarketStripTests : UiTestBase
{
    private async Task FillAsync() =>
        await new MarketIndicatorUpdater(new FakeMarketIndicatorProvider(Clock), Indicators, Notifier)
            .RefreshAsync(MarketIndicators.All);

    [Fact]
    public async Task 다섯_지표의_값과_전일_대비를_보여_준다()
    {
        await FillAsync();

        var cut = Render<MarketStrip>();

        Assert.Equal(
        [
            "코스피 2,650.12 ▲ 상승 12.34 (+0.47%)",
            "코스닥 870.45 ▼ 하락 3.21 (-0.37%)",
            "S&P 500 5,750.80 ▲ 상승 28.15 (+0.49%)",
            "나스닥 18,920.33 ▼ 하락 45.60 (-0.24%)",
            "원/달러 환율 1,385.50 ▲ 상승 2.50 (+0.18%)",
        ], cut.FindAll(".market-item").Select(Cells));
        // 오르면 빨강(up), 내리면 파랑(down)
        Assert.Equal(["up", "down", "up", "down", "up"],
            cut.FindAll(".market-change").Select(c => c.ClassList.Last()));
    }

    [Fact]
    public void 아직_값을_받지_못한_지표는_받아오는_중으로_표시한다()
    {
        var cut = Render<MarketStrip>();

        Assert.Equal(5, cut.FindAll(".market-item").Count);
        Assert.All(cut.FindAll(".market-item"), item => Assert.Contains("받아오는 중", item.TextContent));
    }

    [Fact]
    public async Task 값이_갱신되면_새로고침_없이_바뀐다()
    {
        var cut = Render<MarketStrip>();
        Assert.Contains("받아오는 중", cut.Find(".market-item").TextContent);

        await FillAsync();   // 갱신이 끝나면 화면에 알린다

        cut.WaitForAssertion(() => Assert.StartsWith("코스피 2,650.12", Cells(cut.Find(".market-item"))));
    }

    [Fact]
    public void 대시보드_맨_위에_표시된다()
    {
        var cut = Render<Home>();
        cut.WaitForElement(".summary-grid");

        Assert.Equal("시장 지표", cut.Find(".market-strip").GetAttribute("aria-label"));
        // 요약 카드보다 앞에 있다
        Assert.True(cut.Markup.IndexOf("market-strip", StringComparison.Ordinal) < cut.Markup.IndexOf("summary-grid", StringComparison.Ordinal));
    }
}

public class MarketStripWithoutKeyTests : UiTestBase
{
    public MarketStripWithoutKeyTests() => PriceSource = new PriceSourceInfo(Ready: false, SettingsPath: null);

    [Fact]
    public void KIS_키가_없으면_빈_칸_대신_안내_한_줄을_보여_준다()
    {
        var cut = Render<MarketStrip>();

        Assert.Empty(cut.FindAll(".market-item"));
        Assert.Equal("KIS 앱키를 넣으면 코스피·코스닥·S&P 500·나스닥·원/달러 환율이 여기에 표시됩니다.",
            Text(cut.Find(".market-empty")));
    }
}
