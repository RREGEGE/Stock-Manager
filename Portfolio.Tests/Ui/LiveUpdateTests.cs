using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Core;
using Portfolio.Data;
using Portfolio.Web.Components.Layout;
using Portfolio.Web.Components.Pages;
using Portfolio.Web.Services;

namespace Portfolio.Tests.Ui;

// AC-09: 시세가 갱신되면 새로고침 없이 화면이 바뀐다
public class LiveUpdateTests : UiTestBase
{
    [Fact]
    public async Task AC09_시세가_갱신되면_새로고침_없이_금액이_바뀐다()
    {
        var cut = Render<Home>();
        cut.WaitForElement(".summary-grid");
        Assert.Equal("100,000,000원", Text(cut.Find(".summary-value")));

        // 폴링 1회: KOSPI200 ETF 40,000 → 41,000원 (600주, +600,000원)
        var next = SeedData.Prices.ToDictionary();
        next["SEED01"] = 41_000m;
        await RefreshPricesAsync(next);

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("100,600,000원", Text(cut.Find(".summary-value")));
            Assert.Equal("+5,100,000원", Text(cut.FindAll(".summary-value")[2]));
            Assert.StartsWith("KOSPI200 ETF 주식 +3.54% 24,600,000원", Cells(cut.Find(".table tbody tr")));
        });
    }

    [Fact]
    public async Task AC09_상단_바의_시세_갱신_시각과_모바일_헤더_금액도_함께_바뀐다()
    {
        var cut = Render<TopNav>();
        cut.WaitForElement(".mobile-total");
        Assert.Equal("장중 · 09:41 시세 갱신", cut.Find(".market-text").TextContent);
        Assert.Equal("100,000,000원", Text(cut.Find(".mobile-total-amount")));

        Clock.Now = Clock.Now.AddMinutes(1);
        var next = SeedData.Prices.ToDictionary();
        next["SEED01"] = 41_000m;
        await RefreshPricesAsync(next);

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("장중 · 09:42 시세 갱신", cut.Find(".market-text").TextContent);
            Assert.Equal("09:42 갱신", cut.Find(".market-text-short").TextContent);
            Assert.Equal("100,600,000원", Text(cut.Find(".mobile-total-amount")));
            Assert.Equal("+5,100,000원 (+5.34%)", Text(cut.Find(".mobile-total-pl")));
        });
    }

    [Fact]
    public async Task AC09_시세가_갱신되면_계산_결과도_다시_계산된다()
    {
        var cut = Render<Rebalance>();
        cut.WaitForElement(".amount-card");
        Assert.Equal("미국채 10년 ETF 채권 10,000원 500주 5,000,000원", Cells(cut.FindAll(".order-card tbody tr")[1]));

        var next = SeedData.Prices.ToDictionary();
        next["SEED05"] = 12_500m;   // 미국채 10년 ETF 10,000 → 12,500원
        await RefreshPricesAsync(next);

        cut.WaitForAssertion(() =>
            Assert.Contains("12,500원", Cells(cut.FindAll(".order-card tbody tr").Single(r => r.TextContent.Contains("미국채")))));
    }
}
