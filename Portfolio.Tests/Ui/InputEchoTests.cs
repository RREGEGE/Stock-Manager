using Bunit;
using Portfolio.Web.Components.Pages;
using Portfolio.Web.Components.Shared;

namespace Portfolio.Tests.Ui;

// 입력하는 동안 서버가 입력창 내용을 다시 써 넣지 않는다.
// (칠 때마다 값을 되돌려 쓰면 한글처럼 조합 중인 글자가 두 번 입력되고, 빠르게 치면 글자가 겹치거나 커서가 튄다)
public class InputEchoTests : UiTestBase
{
    [Fact]
    public async Task 종목_검색창은_입력한_글자를_되돌려_쓰지_않고_검색만_한다()
    {
        var cut = Render<Holdings>();
        cut.WaitForElement("#search");

        await cut.Find("#search").InputAsync(new() { Value = "리" });
        await cut.Find("#search").InputAsync(new() { Value = "리츠" });

        // 서버가 써 넣는 값은 그대로 비어 있다 → 브라우저에 있는 입력 내용을 건드리지 않는다
        Assert.Equal("", cut.Find("#search").GetAttribute("value") ?? "");
        // 검색은 입력한 글자로 동작한다
        Assert.Contains("리츠 ETF", cut.WaitForElement(".results .result").TextContent);
    }

    [Fact]
    public async Task 표에서_수정을_누르면_검색창에_종목명을_써_넣는다()
    {
        var cut = Render<Holdings>();
        cut.WaitForElement(".list-card");
        await cut.Find("#search").InputAsync(new() { Value = "아무거나" });

        await cut.FindAll(".list-card button").First(b => b.GetAttribute("aria-label") == "리츠 ETF 수정").ClickAsync(new());

        // 사용자가 입력한 것이 아니라 앱이 정한 값이므로 이때는 써 넣는다
        cut.WaitForAssertion(() => Assert.Equal("리츠 ETF", cut.Find("#search").GetAttribute("value")));
    }

    [Fact]
    public void 숫자_입력창은_입력하는_동안_쉼표를_다시_넣지_않고_입력을_마치면_넣는다()
    {
        decimal bound = 0;
        var cut = Render<MoneyInput>(p => p
            .Add(x => x.Id, "money")
            .Add(x => x.Value, 0m)
            .Add(x => x.ValueChanged, v => bound = v));

        cut.Find("#money").Input("1234567");

        Assert.Equal(1_234_567m, bound);                                   // 값은 바로 전달된다
        Assert.Equal("0", cut.Find("#money").GetAttribute("value"));       // 입력창 내용은 건드리지 않는다

        cut.Find("#money").Change("1234567");                              // 포커스 이동·Enter

        Assert.Equal("1,234,567", cut.Find("#money").GetAttribute("value"));
    }

    [Fact]
    public void 숫자_입력창은_값이_밖에서_바뀌면_다시_써_넣는다()
    {
        var cut = Render<MoneyInput>(p => p.Add(x => x.Id, "money").Add(x => x.Value, 10_000_000m));
        Assert.Equal("10,000,000", cut.Find("#money").GetAttribute("value"));

        cut.Find("#money").Input("5");
        cut.Render(p => p.Add(x => x.Value, 11_000_000m));   // 빠른 추가 버튼 등

        Assert.Equal("11,000,000", cut.Find("#money").GetAttribute("value"));
    }

    [Fact]
    public void 숫자_입력창은_입력_중에_화면만_다시_그려질_때_입력_내용을_지우지_않는다()
    {
        // 예수금 입력처럼 값이 저장 전까지 밖으로 전달되지 않는 경우, 시세 갱신으로 화면이 다시 그려져도 유지
        var cut = Render<MoneyInput>(p => p.Add(x => x.Id, "money").Add(x => x.Value, 0m));
        int versionBefore = InputVersion(cut.Instance);

        cut.Find("#money").Input("25000000");
        cut.Render(p => p.Add(x => x.Value, 0m));   // 같은 값으로 다시 그림

        // 입력창을 새로 만들지 않았다 (새로 만들면 브라우저의 입력 내용과 포커스가 사라진다)
        Assert.Equal(versionBefore, InputVersion(cut.Instance));
        Assert.Equal("0", cut.Find("#money").GetAttribute("value"));

        // 입력을 마치면 그때 반영한다
        cut.Find("#money").Change("25000000");
        Assert.Equal("25,000,000", cut.Find("#money").GetAttribute("value"));
    }

    // 입력창을 새로 그릴 때마다 올라가는 내부 번호 (@key)
    private static int InputVersion(MoneyInput input) =>
        (int)typeof(MoneyInput).GetField("_version", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(input)!;

    [Fact]
    public void 목표_비중_입력창도_입력한_글자를_되돌려_쓰지_않는다()
    {
        var cut = Render<Groups>();
        cut.WaitForElement(".group-table");

        cut.FindAll(".target-input input")[0].Input("55");

        Assert.Equal("50", cut.FindAll(".target-input input")[0].GetAttribute("value"));   // 써 넣은 값은 그대로
        Assert.Equal("105.0%", cut.Find(".sum-target").TextContent);                        // 합계는 입력값으로 계산
    }
}
