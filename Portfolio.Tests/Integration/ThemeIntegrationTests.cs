using System.Net;
using Portfolio.Web.Services;

namespace Portfolio.Tests.Integration;

// 화면 모드 전환 주소와 첫 화면의 모드 (설계서 F-13)
public class ThemeIntegrationTests
{
    [Fact]
    public async Task 고른_화면_모드를_쿠키로_기억하고_첫_화면부터_적용한다()
    {
        using var app = new TestApp();
        await app.RegisterAsync();
        var browser = app.CreateBrowser();

        // 처음에는 시스템 설정을 따른다: 속성이 없다
        string first = await browser.GetStringAsync("/login");
        Assert.Contains("<html lang=\"ko\">", first);
        Assert.DoesNotContain("data-theme", first);

        var response = await browser.GetAsync("/theme/dark?returnUrl=%2Flogin");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login", TestApp.LocalPath(response));
        string cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith(ThemeState.CookieName + "="));
        Assert.StartsWith($"{ThemeState.CookieName}=dark;", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);

        // 화면을 그리기 전에 서버가 속성을 넣으므로 밝은 화면이 잠깐 보였다 바뀌지 않는다
        Assert.Contains("<html lang=\"ko\" data-theme=\"dark\">", await browser.GetStringAsync("/login"));

        await browser.GetAsync("/theme/light?returnUrl=%2Flogin");
        Assert.Contains("data-theme=\"light\"", await browser.GetStringAsync("/login"));

        await browser.GetAsync("/theme/system?returnUrl=%2Flogin");
        Assert.DoesNotContain("data-theme", await browser.GetStringAsync("/login"));
    }

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("//example.com/")]
    public async Task 화면_모드를_바꾼_뒤에도_이_사이트_밖으로는_보내지_않는다(string returnUrl)
    {
        using var app = new TestApp();
        await app.RegisterAsync();
        var browser = app.CreateBrowser();

        var response = await browser.GetAsync("/theme/dark?returnUrl=" + Uri.EscapeDataString(returnUrl));

        Assert.Equal("/", TestApp.LocalPath(response));
    }
}
