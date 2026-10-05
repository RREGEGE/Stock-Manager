using Bunit;
using Portfolio.Data;
using Portfolio.Web.Components.Pages;

namespace Portfolio.Tests.Ui;

public class GroupsPageTests : UiTestBase
{
    private static AngleSharp.Dom.IElement SaveButton(IRenderedComponent<Groups> cut) =>
        cut.FindAll(".footer-buttons button").Single(b => b.TextContent.Trim() == "저장");

    [Fact]
    public void AC11_목표_합계가_100퍼센트가_아니면_저장_버튼이_비활성화된다()
    {
        var cut = Render<Groups>();
        cut.WaitForElement(".group-table");

        // 처음: 50 + 30 + 20 = 100 → 저장 가능
        Assert.False(SaveButton(cut).HasAttribute("disabled"));
        Assert.Equal("합계 100% · 저장할 수 있습니다", cut.Find(".status").TextContent);

        // 주식 50 → 55: 합계 105%
        cut.FindAll(".target-input input")[0].Input("55");

        Assert.True(SaveButton(cut).HasAttribute("disabled"));
        Assert.Equal("105.0%", cut.Find(".sum-target").TextContent);
        Assert.Equal("합계가 100%가 되도록 조정하세요 (현재 105.0%)", cut.Find(".status").TextContent);
        Assert.Contains("text-error", cut.Find(".status").ClassName);

        // 채권 30 → 25: 다시 100%
        cut.FindAll(".target-input input")[1].Input("25");

        Assert.False(SaveButton(cut).HasAttribute("disabled"));
        Assert.Equal("100.0%", cut.Find(".sum-target").TextContent);
    }

    [Theory]
    [InlineData("")]        // 빈 값
    [InlineData("abc")]     // 숫자 아님
    [InlineData("49.9")]    // 99.9%
    public void AC11_빈_값이나_합계가_모자란_입력도_저장할_수_없다(string stockTarget)
    {
        var cut = Render<Groups>();
        cut.WaitForElement(".group-table");

        cut.FindAll(".target-input input")[0].Input(stockTarget);

        Assert.True(SaveButton(cut).HasAttribute("disabled"));
    }

    [Fact]
    public async Task 합계가_100퍼센트면_저장되고_추가매수_계산에_쓰는_목표가_바뀐다()
    {
        var cut = Render<Groups>();
        cut.WaitForElement(".group-table");
        cut.FindAll(".target-input input")[0].Input("40");
        cut.FindAll(".target-input input")[1].Input("35");
        cut.FindAll(".target-input input")[2].Input("25");

        await SaveButton(cut).ClickAsync(new());

        var groups = await new GroupRepository(Db).GetAllAsync();
        Assert.Equal([0.4m, 0.35m, 0.25m], groups.Select(g => g.TargetWeight));
        cut.WaitForAssertion(() => Assert.StartsWith("저장했습니다", cut.Find(".status").TextContent));
    }

    [Fact]
    public void 되돌리기는_저장된_목표_비중으로_돌린다()
    {
        var cut = Render<Groups>();
        cut.WaitForElement(".group-table");
        cut.FindAll(".target-input input")[0].Input("70");

        cut.FindAll(".footer-buttons button").Single(b => b.TextContent.Trim() == "되돌리기").Click();

        Assert.Equal(["50", "30", "20"], cut.FindAll(".target-input input").Select(i => i.GetAttribute("value")));
        Assert.False(SaveButton(cut).HasAttribute("disabled"));
    }

    [Fact]
    public void AC08_그룹_관리_화면은_목업과_같은_값과_문구를_보여_준다()
    {
        var cut = Render<Groups>();
        cut.WaitForElement(".group-table");

        Assert.Equal("그룹 관리", cut.Find("h1").TextContent);
        Assert.Equal(["그룹", "종목 수", "현재 비중", "목표 비중", "차이", "작업"],
            cut.FindAll(".group-table th").Select(Text));

        var rows = cut.FindAll(".group-table tbody tr").Select(Cells).ToList();
        Assert.Equal("주식 3 60.0% % +10.0%p 편집", rows[0]);
        Assert.Equal("채권 2 20.0% % -10.0%p 편집", rows[1]);
        Assert.Equal("배당 2 20.0% % 0.0%p 편집", rows[2]);
        Assert.Equal("합계 7 100.0% 100.0%", rows[3]);

        Assert.Contains("모든 종목에 그룹이 지정되어 있습니다.", cut.Markup);
        Assert.Contains("소속 종목은 미분류로 옮겨지고, 목표 비중은 다른 그룹에 다시 나눠 입력해야 합니다.", cut.Markup);
    }

    [Fact]
    public async Task 그룹을_삭제하면_소속_종목이_미분류_카드에_나온다()
    {
        var cut = Render<Groups>();
        cut.WaitForElement(".group-table");

        cut.FindAll(".group-table button").Single(b => b.GetAttribute("aria-label") == "채권 편집").Click();
        cut.FindAll(".edit-panel button").Single(b => b.TextContent.Trim() == "그룹 삭제").Click();
        await cut.FindAll(".edit-panel button").Single(b => b.TextContent.Trim() == "삭제").ClickAsync(new());

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(["주식", "배당"], cut.FindAll(".group-name").Select(Text));
            var unclassified = cut.FindAll(".unclassified li").Select(Text).ToList();
            Assert.Equal(2, unclassified.Count);
            Assert.Contains(unclassified, u => u.StartsWith("국고채 10년 ETF"));
            // 목표 50 + 20 = 70% → 저장 불가
            Assert.True(SaveButton(cut).HasAttribute("disabled"));
        });
    }
}
