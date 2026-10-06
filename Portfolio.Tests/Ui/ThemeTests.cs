using System.Globalization;
using System.Text.RegularExpressions;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Web.Components.Pages;
using Portfolio.Web.Services;

namespace Portfolio.Tests.Ui;

// 화면 모드 (설계서 F-13): 시스템 설정 따르기 / 밝게 / 어둡게
public class ThemeTests : UiTestBase
{
    [Theory]
    [InlineData("dark", "dark", "dark")]
    [InlineData("light", "light", "light")]
    [InlineData("system", "system", null)]     // 시스템 설정을 따를 때는 속성을 두지 않는다
    [InlineData("blue", "system", null)]       // 모르는 값은 시스템 설정 따르기
    [InlineData(null, "system", null)]
    public void 화면_모드_값은_세_가지만_받는다(string? value, string mode, string? htmlAttribute)
    {
        var theme = new ThemeState { Mode = ThemeState.Normalize(value) };

        Assert.Equal((mode, htmlAttribute), (theme.Mode, theme.HtmlAttribute));
    }

    [Fact]
    public void 설정_화면에서_화면_모드를_고르면_쿠키를_적는_주소로_이동한다()
    {
        var cut = Render<Settings>();
        cut.WaitForElement("#h-theme");
        var buttons = cut.FindAll("section[aria-labelledby=h-theme] button");

        Assert.Equal(["시스템 설정 따르기", "밝게", "어둡게"], buttons.Select(Text));
        Assert.Equal(["true", "false", "false"], buttons.Select(b => b.GetAttribute("aria-pressed")));

        buttons[2].Click();

        Assert.Equal("http://localhost/theme/dark?returnUrl=%2Fsettings", Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact]
    public void 고른_화면_모드가_선택된_것으로_표시된다()
    {
        Theme.Mode = ThemeState.Dark;
        T.Language = Loc.English;

        var cut = Render<Settings>();
        cut.WaitForElement("#h-theme");
        var buttons = cut.FindAll("section[aria-labelledby=h-theme] button");

        Assert.Equal(["Match system", "Light", "Dark"], buttons.Select(Text));
        Assert.Equal(["false", "false", "true"], buttons.Select(b => b.GetAttribute("aria-pressed")));
    }

    // ----- 색 값 검사: app.css의 변수 묶음을 읽어 확인한다 -----

    private static readonly Lazy<string> Css = new(() =>
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Portfolio.sln"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, "Portfolio.Web", "wwwroot", "app.css"));
    });

    // 선택자 바로 뒤 { } 안의 '--이름: 값' 을 읽는다
    private static Dictionary<string, string> Tokens(string selector)
    {
        int start = Css.Value.IndexOf(selector + " {", StringComparison.Ordinal);
        Assert.True(start >= 0, $"app.css에서 '{selector}' 묶음을 찾지 못했습니다.");
        int open = Css.Value.IndexOf('{', start);
        string body = Css.Value[(open + 1)..Css.Value.IndexOf('}', open)];
        return Regex.Matches(body, @"(--[\w-]+)\s*:\s*([^;]+);")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.Trim());
    }

    private static Dictionary<string, string> LightTokens => Tokens(":root");
    private static Dictionary<string, string> DarkTokens => Tokens(":root[data-theme=\"dark\"]");

    [Fact]
    public void 어두운_모드의_두_묶음은_같은_값이다()
    {
        // 시스템 설정을 따를 때와 '어둡게'로 고정했을 때 화면이 같아야 한다
        Assert.Equal(DarkTokens.OrderBy(p => p.Key), Tokens(":root:not([data-theme=\"light\"])").OrderBy(p => p.Key));
    }

    [Fact]
    public void 어두운_모드는_밝은_모드의_모든_색_변수를_다시_정한다()
    {
        // 공통으로 쓰는 값(header-text, live, on-primary)도 적어 두어 빠진 변수가 없는지 한눈에 보이게 한다
        Assert.Equal(LightTokens.Keys.Order(), DarkTokens.Keys.Order());
        Assert.Contains("color-scheme: dark", Css.Value);
    }

    public static TheoryData<string, string, double> ContrastPairs => new()
    {
        // 글자, 바탕, 최소 대비 (일반 글자 4.5:1)
        { "--text", "--bg-page", 4.5 }, { "--text", "--bg-card", 4.5 }, { "--text", "--bg-input", 4.5 }, { "--text", "--primary-soft", 4.5 },
        { "--text-muted", "--bg-page", 4.5 }, { "--text-muted", "--bg-card", 4.5 }, { "--text-muted", "--bg-track", 4.5 },
        { "--primary-text", "--bg-card", 4.5 }, { "--primary-text", "--bg-input", 4.5 }, { "--primary-text", "--primary-soft", 4.5 },
        { "--on-primary", "--primary", 4.5 },
        { "--up", "--bg-card", 4.5 }, { "--down", "--bg-card", 4.5 }, { "--error", "--bg-card", 4.5 }, { "--ok-fg", "--bg-card", 4.5 },
        { "--over-fg", "--over-bg", 4.5 }, { "--under-fg", "--under-bg", 4.5 }, { "--ok-fg", "--ok-bg", 4.5 },
        { "--over-fg", "--bg-card", 4.5 }, { "--under-fg", "--bg-card", 4.5 },
        { "--header-text", "--header-bg", 4.5 }, { "--header-text", "--header-active", 4.5 },
        { "--up-on-dark", "--header-bg", 4.5 }, { "--down-on-dark", "--header-bg", 4.5 },
        // 입력창 테두리처럼 글자가 아닌 것은 3:1
        { "--line-input", "--bg-card", 3.0 }, { "--line-input", "--bg-input", 3.0 },
    };

    [Theory]
    [MemberData(nameof(ContrastPairs))]
    public void 어두운_모드에서_글자와_바탕의_대비가_기준을_넘는다(string foreground, string background, double minimum)
    {
        double ratio = Contrast(DarkTokens[foreground], DarkTokens[background]);

        Assert.True(ratio >= minimum, $"{foreground} / {background} = {ratio:0.00}:1 (기준 {minimum}:1)");
    }

    private static double Contrast(string a, string b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(string hex)
    {
        Assert.Matches("^#[0-9A-Fa-f]{6}$", hex);
        int v = int.Parse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return 0.2126 * C(v >> 16 & 0xFF) + 0.7152 * C(v >> 8 & 0xFF) + 0.0722 * C(v & 0xFF);

        static double C(int c)
        {
            double s = c / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
    }

    [Fact]
    public void 화면_스타일에_변수_밖의_바탕색이_남아_있지_않다()
    {
        // 흰색을 직접 쓰면 어두운 모드에서 그 부분만 하얗게 남는다
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Portfolio.sln"))) dir = dir.Parent;
        string web = Path.Combine(dir!.FullName, "Portfolio.Web");
        var files = Directory.EnumerateFiles(Path.Combine(web, "Components"), "*.css", SearchOption.AllDirectories)
            .Append(Path.Combine(web, "wwwroot", "app.css"));

        var found = new List<string>();
        foreach (string file in files)
        {
            int number = 0;
            foreach (string line in File.ReadLines(file))
            {
                number++;
                if (line.TrimStart().StartsWith("--")) continue;   // 변수 정의
                if (Regex.IsMatch(line, @"background(-color)?\s*:\s*(#[0-9A-Fa-f]{3,6}|white)\b"))
                    found.Add($"{Path.GetFileName(file)}:{number}: {line.Trim()}");
            }
        }

        // 재연결 안내창의 단추와 알림 점은 두 모드에서 같은 색을 쓴다
        Assert.All(found, f => Assert.StartsWith("ReconnectModal.razor.css", f));
    }
}
