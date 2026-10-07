using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Core;
using Portfolio.Data;
using Portfolio.Web.Components.Layout;
using Portfolio.Web.Components.Pages;
using Portfolio.Web.Services;

namespace Portfolio.Tests.Ui;

public class HoldingsPageTests : UiTestBase
{
    private IRenderedComponent<Holdings> RenderHoldings()
    {
        var cut = Render<Holdings>();
        cut.WaitForElement(".list-card");
        return cut;
    }

    private static AngleSharp.Dom.IElement Button(IRenderedComponent<Holdings> cut, string text) =>
        cut.FindAll(".editor button").Single(b => b.TextContent.Trim() == text);

    [Fact]
    public void AC08_보유_종목_화면은_목업과_같은_열과_값을_보여_준다()
    {
        var cut = RenderHoldings();

        Assert.Equal("보유 종목", cut.Find("h1").TextContent);
        Assert.Contains("삼성증권 잔고 화면의 수량과 평균매입단가를 옮겨 적습니다. 현재가는 자동으로 갱신됩니다.", cut.Markup);
        Assert.Equal("7개 종목", cut.Find("#h-list").TextContent);
        Assert.Equal(["종목명", "그룹", "수량", "평균매입단가", "현재가", "오늘", "평가금액", "손익률", "수정일", "작업"],
            cut.FindAll(".list-card th").Select(Text));

        var rows = cut.FindAll(".list-card tbody tr").Select(Cells).ToList();
        Assert.StartsWith("KOSPI200 ETF 주식 600 36,000 40,000 +1.01% 24,000,000 +11.11%", rows[0]);
        Assert.StartsWith("리츠 ETF 배당 1,600 5,500 5,000 +0.00% 8,000,000 -9.09%", rows[6]);
        // 각 줄에 수정·삭제 버튼
        Assert.All(cut.FindAll(".list-card tbody tr"), r =>
            Assert.Equal(["수정", "삭제"], r.QuerySelectorAll("td.action button").Select(b => b.TextContent.Trim())));

        Assert.Equal("종목 추가", cut.Find("#h-form").TextContent);
        Assert.Contains("이미 보유 중인 종목을 고르면 기존 행을 수정합니다.", cut.Markup);
        Assert.Equal("종목명 또는 종목코드", cut.Find("#search").GetAttribute("placeholder"));
        Assert.Contains("총 보유금액에 포함", cut.Find(".cash-row").TextContent);
    }

    [Fact]
    public async Task 보유_중인_종목을_검색해_고르면_기존_값으로_수정_화면이_된다()
    {
        var cut = RenderHoldings();

        await cut.Find("#search").InputAsync(new() { Value = "리츠" });
        var result = cut.WaitForElement(".results .result");
        Assert.Equal("리츠 ETF SEED07 보유 중 · 수정", Text(result));
        await result.ClickAsync(new());

        Assert.Equal("종목 수정", cut.Find("#h-form").TextContent);
        Assert.Equal("1,600", cut.Find("#qty").GetAttribute("value"));
        Assert.Equal("5,500", cut.Find("#avg").GetAttribute("value"));
        Assert.Equal("현재가 5,000원 기준 평가금액 8,000,000원", Text(cut.Find(".preview")));
        Assert.Contains("selected", cut.FindAll(".list-card tbody tr")[6].ClassName);
    }

    [Fact]
    public async Task AC01_수정_저장은_새_행을_만들지_않고_기존_행을_바꾼다()
    {
        var cut = RenderHoldings();
        await cut.FindAll(".list-card button").Single(b => b.GetAttribute("aria-label") == "리츠 ETF 수정").ClickAsync(new());
        cut.WaitForAssertion(() => Assert.Equal("종목 수정", cut.Find("#h-form").TextContent));

        cut.Find("#qty").Input("2000");
        Assert.Equal("현재가 5,000원 기준 평가금액 10,000,000원", Text(cut.Find(".preview")));
        await Button(cut, "저장").ClickAsync(new());

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("7개 종목", cut.Find("#h-list").TextContent);
            Assert.StartsWith("리츠 ETF 배당 2,000 5,500 5,000 +0.00% 10,000,000", Cells(cut.FindAll(".list-card tbody tr")[6]));
            Assert.Equal("종목 추가", cut.Find("#h-form").TextContent);
        });
        Assert.Equal(7, await Db.CreateDbContext().Holdings.CountAsync());
    }

    [Fact]
    public async Task 입력값이_잘못되면_저장하지_않고_이유를_알린다()
    {
        var cut = RenderHoldings();

        await Button(cut, "저장").ClickAsync(new());
        Assert.Equal("종목을 검색해 선택하세요.", cut.Find(".editor .field-error").TextContent);

        await new SymbolMasterRepository(Db).ReplaceAsync([new SymbolInfo("005930", "삼성전자", "KOSPI")], Clock.Now);
        await cut.Find("#search").InputAsync(new() { Value = "삼성" });
        await cut.WaitForElement(".results .result").ClickAsync(new());

        await Button(cut, "저장").ClickAsync(new());
        Assert.Equal("수량은 1주 이상이어야 합니다.", cut.Find(".editor .field-error").TextContent);

        cut.Find("#qty").Input("10");
        await Button(cut, "저장").ClickAsync(new());
        Assert.Equal("평균매입단가는 0원보다 커야 합니다.", cut.Find(".editor .field-error").TextContent);

        Assert.Equal(7, await Db.CreateDbContext().Holdings.CountAsync());
    }

    [Fact]
    public async Task 새_종목은_저장_후_표에_추가되고_시세를_받기_전에는_시세_없음으로_표시된다()
    {
        var cut = RenderHoldings();
        await new SymbolMasterRepository(Db).ReplaceAsync([new SymbolInfo("005930", "삼성전자", "KOSPI")], Clock.Now);

        await cut.Find("#search").InputAsync(new() { Value = "0059" });
        var result = cut.WaitForElement(".results .result");
        Assert.EndsWith("신규", Text(result));
        await result.ClickAsync(new());
        cut.Find("#qty").Input("10");
        cut.Find("#avg").Input("70000");
        await Button(cut, "저장").ClickAsync(new());

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("8개 종목", cut.Find("#h-list").TextContent);
            var row = cut.FindAll(".list-card tbody tr").Single(r => r.TextContent.Contains("삼성전자"));
            Assert.Contains("stale", row.ClassName);   // FakePriceProvider에 가격이 없는 종목
            Assert.Contains("시세 없음", row.TextContent);
            Assert.Contains("매입가", row.TextContent);   // 평가금액은 매입금액으로 대신 표시
        });
    }

    [Fact]
    public async Task 삭제는_확인을_거친_뒤에_처리한다()
    {
        var cut = RenderHoldings();
        await cut.FindAll(".list-card button").Single(b => b.GetAttribute("aria-label") == "리츠 ETF 수정").ClickAsync(new());
        cut.WaitForAssertion(() => Assert.Equal("종목 수정", cut.Find("#h-form").TextContent));

        Button(cut, "삭제").Click();
        Assert.Contains("리츠 ETF을(를) 삭제할까요?", cut.Find(".confirm").TextContent);
        Assert.Equal(7, await Db.CreateDbContext().Holdings.CountAsync());   // 아직 삭제 전

        await cut.FindAll(".confirm button").Single(b => b.TextContent.Trim() == "삭제").ClickAsync(new());

        cut.WaitForAssertion(() => Assert.Equal("6개 종목", cut.Find("#h-list").TextContent));
        Assert.Equal(6, await Db.CreateDbContext().Holdings.CountAsync());
    }

    [Fact]
    public async Task 예수금을_입력하고_포함을_체크하면_저장된다()
    {
        var cut = RenderHoldings();
        var settings = new SettingsRepository(Db);

        await cut.Find("#cash").ChangeAsync(new() { Value = "25,000,000" });
        await cut.Find(".cash-row input[type=checkbox]").ChangeAsync(new() { Value = true });

        Assert.Equal(25_000_000m, await settings.GetCashAsync());
        Assert.True(await settings.GetIncludeCashAsync());
    }
}
