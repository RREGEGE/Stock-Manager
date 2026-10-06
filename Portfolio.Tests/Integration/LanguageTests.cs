using System.Net;
using Portfolio.Web.Services;

namespace Portfolio.Tests.Integration;

// 언어 전환 주소와 로그인 화면의 언어 (설계서 F-12)
public class LanguageTests
{
    [Fact]
    public async Task 로그인_전에도_언어를_고를_수_있고_쿠키로_기억한다()
    {
        using var app = new TestApp();
        await app.RegisterAsync();
        var browser = app.CreateBrowser();

        // 처음에는 한국어
        string korean = await TestApp.ReadTextAsync(await browser.GetAsync("/login"));
        Assert.Contains("<html lang=\"ko\">", korean);
        Assert.Contains("한 번 로그인하면 이 기기에서 90일 동안 유지됩니다.", korean);
        Assert.Contains("href=\"language/en?returnUrl=%2Flogin\"", korean);

        var response = await browser.GetAsync("/language/en?returnUrl=%2Flogin");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login", TestApp.LocalPath(response));
        string cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith(Loc.CookieName + "="));
        Assert.StartsWith($"{Loc.CookieName}=en;", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);

        // 쿠키가 있으면 영어
        string english = await TestApp.ReadTextAsync(await browser.GetAsync("/login"));
        Assert.Contains("<html lang=\"en\">", english);
        Assert.Contains("You stay signed in on this device for 90 days.", english);
        Assert.Contains(">Sign in</button>", english);
        Assert.DoesNotContain("로그인", english);

        // 틀린 비밀번호의 안내도 영어
        var failed = await TestApp.PostLoginAsync(browser, "wrong-password");
        Assert.Contains("The user name or password is incorrect.", await TestApp.ReadTextAsync(failed));
    }

    [Theory]
    [InlineData("fr", "ko")]             // 지원하지 않는 언어는 한국어
    [InlineData("ko", "ko")]
    public async Task 지원하지_않는_언어는_한국어로_적는다(string requested, string stored)
    {
        using var app = new TestApp();
        await app.RegisterAsync();
        var browser = app.CreateBrowser();

        var response = await browser.GetAsync($"/language/{requested}?returnUrl=%2Flogin");

        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith($"{Loc.CookieName}={stored};"));
    }

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("//example.com/")]
    [InlineData("")]
    public async Task 언어를_바꾼_뒤에도_이_사이트_밖으로는_보내지_않는다(string returnUrl)
    {
        using var app = new TestApp();
        await app.RegisterAsync();
        var browser = app.CreateBrowser();

        var response = await browser.GetAsync("/language/en?returnUrl=" + Uri.EscapeDataString(returnUrl));

        Assert.Equal("/", TestApp.LocalPath(response));
    }
}
