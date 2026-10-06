using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Core;
using Portfolio.Data;
using Portfolio.Web.Components.Layout;
using Portfolio.Web.Components.Pages;
using Portfolio.Web.Services;

namespace Portfolio.Tests.Ui;

public class LayoutTests : UiTestBase
{
    [Fact]
    public void 상단_바는_메뉴_4개와_장_상태를_보여_준다()
    {
        var cut = Render<TopNav>();
        cut.WaitForElement(".mobile-total");

        Assert.Equal("포트폴리오", cut.Find(".brand").TextContent);
        Assert.Equal(["대시보드", "보유 종목", "리밸런싱", "그룹 관리"], cut.FindAll(".topnav a").Select(Text));
        Assert.Equal(["", "holdings", "rebalance", "groups"], cut.FindAll(".topnav a").Select(a => a.GetAttribute("href")));
        Assert.Contains("live", cut.Find(".market-dot").ClassName);
    }

    [Fact]
    public void 장_마감_후에는_장_마감으로_표시한다()
    {
        Clock.Now = new DateTimeOffset(2026, 10, 2, 16, 0, 0, TimeSpan.FromHours(9));

        var cut = Render<TopNav>();
        cut.WaitForElement(".mobile-total");

        Assert.Equal("장 마감 · 09:41 시세 갱신", cut.Find(".market-text").TextContent);
        Assert.DoesNotContain("live", cut.Find(".market-dot").ClassName);
    }

}
