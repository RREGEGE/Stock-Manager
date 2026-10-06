using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Data;
using Portfolio.Web.Components.Layout;
using Portfolio.Web.Components.Pages;
using Portfolio.Web.Components.Shared;
using Portfolio.Web.Services;

namespace Portfolio.Tests.Ui;

// 계좌 전환과 계좌 관리 화면 (설계서 F-11)
public class AccountUiTests : UiTestBase
{
    private PortfolioService Service => Services.GetRequiredService<PortfolioService>();

    // 두 번째 계좌: KOSPI200 ETF 10주(40만 원)와 예수금 25만 원
    private async Task<TradingAccount> AddSecondAccountAsync()
    {
        var second = await Service.AddAccountAsync("연금저축");
        await Service.SaveHoldingAsync(second.Id, "SEED01", "KOSPI200 ETF", 10, 39_000m, null);
        await Service.SetCashAsync(second.Id, 250_000m);
        return second;
    }

    [Fact]
    public async Task 상단_바에서_계좌를_고를_수_있다()
    {
        var second = await AddSecondAccountAsync();

        var cut = Render<TopNav>();
        var select = cut.WaitForElement(".account-select");

        Assert.Equal("계좌", select.GetAttribute("aria-label"));
        Assert.Equal(["기본 계좌", "연금저축"], cut.FindAll(".account-select option").Select(Text));
        Assert.Equal([true, false], cut.FindAll(".account-select option").Select(o => o.HasAttribute("selected")));

        // 고르면 쿠키를 적는 주소를 거쳐 보던 화면으로 돌아온다
        select.Change(second.Id.ToString());
        Assert.Equal($"http://localhost/accounts/select/{second.Id}?returnUrl=%2F", Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact]
    public async Task 고른_계좌의_내역만_보여_준다()
    {
        var second = await AddSecondAccountAsync();
        Account.Id = second.Id;

        var home = Render<Home>();
        home.WaitForElement(".summary-grid");
        Assert.Contains("연금저축", Text(home.Find(".page-head")));
        Assert.Equal("총 평가금액 400,000원 예수금 제외", Text(home.FindAll(".summary-card")[0]));

        var holdings = Render<Holdings>();
        holdings.WaitForElement(".list-card");
        Assert.Equal("1개 종목", Text(holdings.Find("#h-list")));
        Assert.Equal("250,000", holdings.Find("#cash").GetAttribute("value"));

        var nav = Render<TopNav>();
        nav.WaitForElement(".mobile-total");
        Assert.Equal([false, true], nav.FindAll(".account-select option").Select(o => o.HasAttribute("selected")));
        Assert.Equal("400,000원", Text(nav.Find(".mobile-total-amount")));
    }

    [Fact]
    public async Task 한_계좌에서_고친_내용은_다른_계좌에_영향을_주지_않는다()
    {
        var second = await AddSecondAccountAsync();

        await Service.DeleteHoldingAsync(second.Id, "SEED01");
        await Service.SetCashAsync(second.Id, 0m);

        var first = await Service.LoadAsync(TradingAccount.DefaultId);
        Assert.Equal(7, first.Snapshot.Holdings.Count);                 // 같은 종목(SEED01)이 첫 계좌에는 그대로 있다
        Assert.Equal(100_000_000m, first.Summary.EvalAmount);
        Assert.Empty((await Service.LoadAsync(second.Id)).Snapshot.Holdings);
    }

    [Fact]
    public async Task 리밸런싱도_고른_계좌의_그룹과_목표로_계산한다()
    {
        var second = await AddSecondAccountAsync();
        var groups = (await Service.LoadAsync(second.Id)).Groups;
        // 두 번째 계좌: 주식 그룹에 40만 원, 채권 그룹에 60만 원, 목표는 기본값 50/30/20
        await Service.SaveHoldingAsync(second.Id, "SEED01", "KOSPI200 ETF", 10, 39_000m, groups[0].Id);
        await Service.SaveHoldingAsync(second.Id, "SEED05", "미국채 10년 ETF", 60, 10_000m, groups[1].Id);
        Account.Id = second.Id;

        var cut = Render<Rebalance>();
        cut.WaitForElement(".amount-card");
        cut.FindAll(".mode-tab")[1].Click();

        // 배당 그룹에는 종목이 없어 살 수 없으므로, 판 돈 30만 원 중 주식 몫 10만 원으로 2주(8만 원)만 산다
        Assert.Equal(
            ["400,000원 40.0% 50.0% 100,000원 매수 61.5%", "600,000원 60.0% 30.0% 300,000원 매도 38.5%", "0원 0.0% 20.0% 200,000원 매수 0.0%"],
            cut.FindAll(".group-card tbody tr").Select(r => string.Join(" ", r.Children.Skip(1).Select(Text))));
        Assert.Equal(("300,000원", "80,000원", "220,000원"),
            (cut.Find("#sell-total").TextContent, cut.Find("#buy-total").TextContent, cut.Find("#sell-leftover").TextContent));
    }

    [Fact]
    public async Task 고른_계좌가_지워졌으면_첫_번째_계좌를_보여_준다()
    {
        Account.Id = 999;

        var state = await Service.LoadAsync(Account.Id);
        var cut = Render<Home>();
        cut.WaitForElement(".summary-grid");

        Assert.Equal(TradingAccount.DefaultId, state.Account.Id);
        Assert.Equal(TradingAccount.DefaultId, Account.Id);
        Assert.Equal("총 평가금액 100,000,000원 예수금 제외", Text(cut.FindAll(".summary-card")[0]));
    }

    [Fact]
    public void 설정_화면에서_계좌를_추가하고_이름을_바꿀_수_있다()
    {
        var cut = Render<AccountManager>();
        cut.WaitForElement(".account-row");

        Assert.Equal(["기본 계좌 보는 중"], cut.FindAll(".account-name").Select(Cells));
        Assert.True(cut.FindAll(".account-row .danger")[0].HasAttribute("disabled"));   // 마지막 계좌는 삭제 불가

        cut.Find("#new-account").Input("연금저축");
        cut.Find(".account-add button").Click();
        cut.WaitForAssertion(() => Assert.Equal(["기본 계좌 보는 중", "연금저축"], cut.FindAll(".account-name").Select(Cells)));
        Assert.StartsWith("연금저축 계좌를 추가했습니다.", Text(cut.Find(".status")));
        Assert.Equal("", cut.Find("#new-account").GetAttribute("value") ?? "");

        // 이름 변경: 지금 쓰는 계좌를 ISA로
        cut.FindAll(".account-row")[0].QuerySelectorAll("button").First(b => b.TextContent == "이름 변경").Click();
        cut.Find(".account-name-input").Input("ISA");
        cut.FindAll(".account-row")[0].QuerySelectorAll("button").First(b => b.TextContent == "저장").Click();
        cut.WaitForAssertion(() => Assert.Equal(["ISA 보는 중", "연금저축"], cut.FindAll(".account-name").Select(Cells)));

        // 겹치는 이름은 거절하고 이유를 알려 준다
        cut.Find("#new-account").Input("ISA");
        cut.Find(".account-add button").Click();
        cut.WaitForAssertion(() => Assert.Equal("같은 이름의 계좌가 이미 있습니다.", Text(cut.Find(".status.text-error"))));
    }

    [Fact]
    public async Task 계좌_삭제는_딸린_종목_수를_알리고_한_번_더_확인한다()
    {
        var second = await AddSecondAccountAsync();
        var cut = Render<AccountManager>();
        cut.WaitForElement(".account-row");

        cut.FindAll(".account-row")[1].QuerySelector(".danger")!.Click();
        cut.WaitForAssertion(() => Assert.Equal(
            "연금저축 계좌를 삭제할까요? 보유 종목 1개와 그룹·예수금이 함께 지워지고 되돌릴 수 없습니다.",
            Text(cut.Find(".delete-confirm"))));
        Assert.Equal(2, (await Service.LoadAsync(second.Id)).Accounts.Count);   // 아직 지우지 않았다

        cut.Find(".account-row .btn-danger").Click();
        cut.WaitForAssertion(() => Assert.Equal(["기본 계좌 보는 중"], cut.FindAll(".account-name").Select(Cells)));
        Assert.Equal("계좌를 삭제했습니다.", Text(cut.Find(".status")));
    }

    [Fact]
    public async Task 다른_계좌는_보기를_눌러_전환한다()
    {
        var second = await AddSecondAccountAsync();
        var cut = Render<AccountManager>();
        cut.WaitForElement(".account-row");

        cut.FindAll(".account-row")[1].QuerySelectorAll("button").First(b => b.TextContent == "보기").Click();

        Assert.Equal($"http://localhost/accounts/select/{second.Id}?returnUrl=%2F", Services.GetRequiredService<NavigationManager>().Uri);
    }
}
