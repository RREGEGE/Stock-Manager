using System.Text.RegularExpressions;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Data;
using Portfolio.Web.Auth;
using Portfolio.Web.Components.Layout;
using Portfolio.Web.Components.Pages;
using Portfolio.Web.Components.Shared;
using Portfolio.Web.Services;

namespace Portfolio.Tests.Ui;

// 화면 언어 (설계서 F-12): 한국어 / 영어
public partial class LocalizationTests : UiTestBase
{
    [GeneratedRegex("[가-힣]")]
    private static partial Regex Hangul();

    // 번역하지 않는 것: 입력된 데이터(종목명, 그룹·계좌 이름)와 언어 이름
    private static readonly string[] DataNames =
        [.. SeedData.Holdings.Select(h => h.SymbolName), "주식", "채권", "배당", TradingAccount.DefaultName, "한국어"];

    private static string WithoutData(string text) =>
        DataNames.OrderByDescending(n => n.Length).Aggregate(System.Net.WebUtility.HtmlDecode(text), (t, name) => t.Replace(name, ""));

    private void UseEnglish() => T.Language = Loc.English;

    [Fact]
    public void 기본은_한국어이고_번역표에_없는_문구는_그대로_둔다()
    {
        var t = new Loc();

        Assert.False(t.IsEnglish);
        Assert.Equal("보유 종목", t["보유 종목"]);
        Assert.Equal("7개 종목", t.F("{0}개 종목", 7));

        t.Language = Loc.English;
        Assert.Equal("Holdings", t["보유 종목"]);
        Assert.Equal("7 holdings", t.F("{0}개 종목", 7));
        Assert.Equal("내가 만든 그룹", t["내가 만든 그룹"]);   // 입력된 이름은 그대로
        Assert.Equal("", t[null]);
    }

    [Theory]
    [InlineData("en", "en")]
    [InlineData("ko", "ko")]
    [InlineData("fr", "ko")]      // 지원하지 않는 값은 한국어
    [InlineData(null, "ko")]
    public void 언어_값은_한국어와_영어만_받는다(string? value, string expected) =>
        Assert.Equal(expected, Loc.Normalize(value));

    [Fact]
    public void 영어에서는_금액과_수량과_날짜_표기가_바뀐다()
    {
        var ko = new Loc();
        var en = new Loc { Language = Loc.English };
        var date = new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.FromHours(9));

        Assert.Equal(("1,234,567원", "₩1,234,567"), (ko.Won(1_234_567m), en.Won(1_234_567m)));
        Assert.Equal(("+4,500,000원", "+₩4,500,000"), (ko.SignedWon(4_500_000m), en.SignedWon(4_500_000m)));
        Assert.Equal(("-4,500,000원", "-₩4,500,000"), (ko.SignedWon(-4_500_000m), en.SignedWon(-4_500_000m)));
        Assert.Equal(("1억 원", "₩100M"), (ko.ShortWon(100_000_000m), en.ShortWon(100_000_000m)));
        Assert.Equal(("1.2억 원", "₩123M"), (ko.ShortWon(123_400_000m), en.ShortWon(123_400_000m)));
        Assert.Equal(("550만 원", "₩5.5M"), (ko.ShortWon(5_500_000m), en.ShortWon(5_500_000m)));
        Assert.Equal(("12억 원", "₩1.2B"), (ko.ShortWon(1_200_000_000m), en.ShortWon(1_200_000_000m)));
        Assert.Equal(("5만 원", "₩50K"), (ko.ShortWon(50_000m), en.ShortWon(50_000m)));
        Assert.Equal(("1,600주", "1,600 sh"), (ko.Shares(1_600), en.Shares(1_600)));
        Assert.Equal(("9월 28일", "Sep 28"), (ko.MonthDay(date), en.MonthDay(date)));
    }

    [Fact]
    public void 앱이_만든_항목_이름만_번역하고_입력된_그룹_이름은_번역하지_않는다()
    {
        var en = new Loc { Language = Loc.English };

        Assert.Equal(["Unclassified", "Cash", "Others"], new[] { "미분류", "현금", "기타" }.Select(en.Name));
        Assert.Equal(["주식", "매수", "설정"], new[] { "주식", "매수", "설정" }.Select(en.Name));   // 번역표에 있는 낱말이어도 그룹 이름이면 그대로
    }

    [Fact]
    public void 번역에는_한글이_남지_않고_값이_들어갈_자리가_원문과_같다()
    {
        Assert.All(Loc.EnglishTable, pair =>
        {
            Assert.False(Hangul().IsMatch(pair.Value), $"번역에 한글이 남음: {pair.Key}");
            Assert.Equal(Placeholders(pair.Key), Placeholders(pair.Value));
        });

        static string Placeholders(string text) =>
            string.Join(",", Regex.Matches(text, @"\{\d\}").Select(m => m.Value).Distinct().Order());
    }

    // 화면 소스의 모든 한글 문구가 번역표에 있는지 확인한다 (문구를 넣고 번역을 빠뜨리는 실수 방지)
    [Fact]
    public void 화면에_쓰는_모든_문구에_영어_번역이_있다()
    {
        string root = FindRepositoryRoot();
        string[] targets =
        [
            "Portfolio.Web/Components", "Portfolio.Web/Services/ViewModels.cs", "Portfolio.Web/Auth/AccountService.cs",
            "Portfolio.Data/GroupRepository.cs", "Portfolio.Data/TradingAccountRepository.cs", "Portfolio.Data/HoldingRepository.cs",
            "Portfolio.Data/SettingsRepository.cs", "Portfolio.Core/MarketIndicators.cs", "Portfolio.Core/PortfolioCalculator.cs",
        ];
        // 번역 대상이 아닌 것: 데이터로 저장되는 기본 그룹 이름, 언어 이름, 단위 글자, 값이 끼워지는 원문(아래에서 완성된 문장으로 따로 확인)
        string[] notTranslated = ["주식", "채권", "배당", "한국어", "원", "1,000원"];
        var literal = new Regex(@"(?<!\\)""((?:[^""\\가-힣]|\\.)*[가-힣](?:[^""\\]|\\.)*)""");

        var missing = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string target in targets)
        {
            string path = Path.Combine(root, target);
            var files = Directory.Exists(path)
                ? Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories).Where(f => f.EndsWith(".razor") || f.EndsWith(".cs"))
                : [path];
            foreach (string file in files)
            {
                bool inComment = false;
                foreach (string raw in File.ReadAllLines(file))
                {
                    string line = raw.Trim();
                    if (line.Contains("@*")) inComment = true;
                    bool skip = inComment || line.StartsWith("//");
                    if (line.Contains("*@")) inComment = false;
                    if (skip) continue;
                    foreach (Match m in literal.Matches(line))
                    {
                        string text = Regex.Unescape(m.Groups[1].Value);
                        if (notTranslated.Contains(text) || Regex.IsMatch(text, @"\{[A-Za-z]")) continue;
                        if (!Loc.EnglishTable.ContainsKey(text)) missing.Add($"{Path.GetFileName(file)}: {text}");
                    }
                }
            }
        }

        Assert.True(missing.Count == 0, "번역표(Loc.En.cs)에 없는 문구:\n" + string.Join("\n", missing));
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Portfolio.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("저장소 폴더를 찾지 못했습니다.");
    }

    // 값이 끼워져 만들어지는 오류 문구도 번역된다
    [Fact]
    public void 입력_오류_문구도_번역된다()
    {
        var en = new Loc { Language = Loc.English };

        Assert.Equal("The user name must be 3–30 characters.", en[AccountService.ValidateUserName("a")]);
        Assert.Equal("The user name cannot contain spaces.", en[AccountService.ValidateUserName("a b c")]);
        Assert.Equal("The password must be at least 8 characters.", en[AccountService.ValidatePassword("short")]);
        Assert.Equal("Too many failed attempts. Try again in 42 seconds.", LoginThrottle.Message(TimeSpan.FromSeconds(41.2), en));
        Assert.Equal("여러 번 틀려서 잠시 막았습니다. 42초 뒤에 다시 시도하세요.", LoginThrottle.Message(TimeSpan.FromSeconds(41.2)));
    }

    [Fact]
    public async Task 저장소가_거절한_이유도_영어로_보여_준다()
    {
        UseEnglish();
        var cut = Render<AccountManager>();
        cut.WaitForElement(".account-row");

        cut.Find("#new-account").Input(TradingAccount.DefaultName);   // 이미 있는 이름
        cut.Find(".account-add button").Click();

        cut.WaitForAssertion(() => Assert.Equal("An account with that name already exists.", Text(cut.Find(".status.text-error"))));
        await Task.CompletedTask;
    }

    [Fact]
    public void 영어_대시보드는_메뉴와_요약과_표를_영어로_보여_준다()
    {
        UseEnglish();

        var nav = Render<TopNav>();
        nav.WaitForElement(".mobile-total");
        Assert.Equal(["Dashboard", "Holdings", "Rebalance", "Groups"], nav.FindAll(".topnav a").Select(Text));
        Assert.Equal("Market open · prices as of 09:41", nav.Find(".market-text").TextContent);
        Assert.Equal("₩100,000,000", Text(nav.Find(".mobile-total-amount")));
        Assert.Equal("+₩4,500,000 (+4.71%)", Text(nav.Find(".mobile-total-pl")));

        var home = Render<Home>();
        home.WaitForElement(".summary-grid");
        Assert.Equal(
            ["Total value ₩100,000,000 Cash excluded", "Cost ₩95,500,000 Quantity × average cost", "Unrealized P/L +₩4,500,000 +4.71%", "Holdings 7 0 unclassified · 0 stale"],
            home.FindAll(".summary-card").Select(Text));
        Assert.Equal(["Name", "Group", "Value", "Weight", "Return"], home.FindAll("thead th").Select(Text));
        Assert.Equal("3 groups", home.Find(".donut-label").TextContent);
        Assert.Equal("₩100M", home.Find(".donut-value").TextContent);
        Assert.Equal("+10.0%p over", Text(home.Find(".target-diff")));
        // 그룹 이름과 종목명은 입력된 그대로
        Assert.Contains("주식", home.Find(".legend").TextContent);
        Assert.Contains("KOSPI200 ETF", home.Find("tbody").TextContent);
    }

    [Fact]
    public void 영어_리밸런싱은_금액과_주수를_영어_표기로_보여_준다()
    {
        UseEnglish();
        var cut = Render<Rebalance>();
        cut.WaitForElement(".amount-card");

        Assert.Equal("Buy plan", cut.Find("h1").TextContent);
        Assert.Equal(["Buy only", "Rebalance"], cut.FindAll(".mode-tab").Select(Text));
        Assert.Equal("국고채 10년 ETF 채권 ₩100,000 50 sh ₩5,000,000", Cells(cut.Find(".order-card tbody tr")));

        cut.FindAll(".mode-tab")[1].Click();

        Assert.Equal("Rebalance plan", cut.Find("h1").TextContent);
        Assert.Equal(
            ["주식 ₩60,000,000 60.0% 50.0% Sell ₩10,000,000 50.0%", "채권 ₩20,000,000 20.0% 30.0% Buy ₩10,000,000 30.0%", "배당 ₩20,000,000 20.0% 20.0% Hold 20.0%"],
            cut.FindAll(".group-card tbody tr").Select(Cells));
        Assert.Equal("KOSPI200 ETF 주식 Sell ₩40,000 101 sh ₩4,040,000", Cells(cut.Find(".order-card tbody tr")));
    }

    // 영어로 바꿨을 때 한글이 남는 화면이 없는지 본다 (입력된 이름과 종목명은 제외)
    [Fact]
    public async Task 영어_화면에는_데이터_말고_한글이_남지_않는다()
    {
        UseEnglish();
        // 미분류·예수금·시세 없는 종목이 섞인 상태에서도 확인한다
        await new GroupRepository(Db).DeleteAsync(SeedData.DividendGroupId);
        await new SettingsRepository(Db).SetCashAsync(1_000_000m);
        await new SettingsRepository(Db).SetIncludeCashAsync(true);
        await new HoldingRepository(Db.Context).SaveAsync("NOPRICE", "No Price Corp", 1, 1_000m, SeedData.StockGroupId);
        Notifier.NotifyChanged();

        var screens = new Dictionary<string, Func<string>>
        {
            ["TopNav"] = () => Markup<TopNav>(".mobile-total"),
            ["BottomTabs"] = () => Render<BottomTabs>().Markup,
            ["Home"] = () => Markup<Home>(".summary-grid"),
            ["Holdings"] = () => Markup<Holdings>(".list-card"),
            ["Rebalance"] = () => Markup<Rebalance>(".amount-card"),
            ["Rebalance(sell)"] = () =>
            {
                var cut = Render<Rebalance>();
                cut.WaitForElement(".amount-card");
                cut.FindAll(".mode-tab")[1].Click();
                return cut.Markup;
            },
            ["Groups"] = () => Markup<Groups>(".group-card"),
            ["Settings"] = () => Markup<Settings>(".accounts-card .account-row"),
            ["MarketStrip"] = () => Render<MarketStrip>().Markup,
            ["NotFound"] = () => Render<NotFound>().Markup,
        };

        foreach (var (name, render) in screens)
        {
            string rest = WithoutData(render());
            var left = Hangul().Matches(rest);
            Assert.True(left.Count == 0, $"{name} 화면에 한글이 남음: …{Around(rest, left.FirstOrDefault())}…");
        }

        string Markup<TComponent>(string waitFor) where TComponent : IComponent
        {
            var cut = Render<TComponent>();
            cut.WaitForElement(waitFor);
            return cut.Markup;
        }

        static string Around(string text, Match? m) =>
            m is null ? "" : text.Substring(Math.Max(0, m.Index - 40), Math.Min(text.Length - Math.Max(0, m.Index - 40), 90));
    }

    [Fact]
    public void 설정_화면에서_언어를_고르면_쿠키를_적는_주소로_이동한다()
    {
        var cut = Render<Settings>();
        cut.WaitForElement(".language-options");

        Assert.Equal(["한국어", "English"], cut.FindAll(".language-options:not(.theme-options) button").Select(Text));
        Assert.Equal(["true", "false"], cut.FindAll(".language-options:not(.theme-options) button").Select(b => b.GetAttribute("aria-pressed")));

        cut.FindAll(".language-options:not(.theme-options) button")[1].Click();

        Assert.Equal("http://localhost/language/en?returnUrl=%2Fsettings", Services.GetRequiredService<NavigationManager>().Uri);
    }
}
