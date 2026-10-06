using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Core;
using Portfolio.Data;
using Portfolio.Web.Components.Layout;
using Portfolio.Web.Components.Pages;
using Portfolio.Web.Services;

namespace Portfolio.Tests.Ui;

public class BottomTabsTests : UiTestBase
{
    [Fact]
    public void AC10_하단_탭_바는_메뉴_4개를_가진다()
    {
        var cut = Render<BottomTabs>();

        Assert.Equal("주 메뉴", cut.Find("nav").GetAttribute("aria-label"));
        Assert.Equal(["대시보드", "보유 종목", "리밸런싱", "그룹 관리"], cut.FindAll("nav.bottom-tabs a").Select(Text));
        Assert.Equal(["", "holdings", "rebalance", "groups"], cut.FindAll("nav.bottom-tabs a").Select(a => a.GetAttribute("href")));
    }
}
