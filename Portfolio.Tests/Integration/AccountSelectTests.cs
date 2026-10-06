using System.Net;
using Portfolio.Web.Services;

namespace Portfolio.Tests.Integration;

// 계좌 전환 주소 (설계서 F-11): 고른 계좌를 이 브라우저의 쿠키에 적고 보던 화면으로 돌아간다
public class AccountSelectTests
{
    private static async Task<HttpClient> LoggedInBrowserAsync(TestApp app)
    {
        await app.RegisterAsync();
        var browser = app.CreateBrowser();
        var login = await TestApp.PostLoginAsync(browser, TestApp.TestPassword);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        return browser;
    }

    [Fact]
    public async Task 계좌를_고르면_쿠키에_적고_보던_화면으로_돌아간다()
    {
        using var app = new TestApp();
        var browser = await LoggedInBrowserAsync(app);

        var response = await browser.GetAsync("/accounts/select/2?returnUrl=%2Fholdings");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/holdings", TestApp.LocalPath(response));
        string cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith(CurrentAccount.CookieName + "="));
        Assert.StartsWith($"{CurrentAccount.CookieName}=2;", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("//example.com/")]
    [InlineData("/\\example.com/")]
    [InlineData("")]
    public async Task 이_사이트_밖의_주소로는_돌려보내지_않는다(string returnUrl)
    {
        using var app = new TestApp();
        var browser = await LoggedInBrowserAsync(app);

        var response = await browser.GetAsync("/accounts/select/1?returnUrl=" + Uri.EscapeDataString(returnUrl));

        Assert.Equal("/", TestApp.LocalPath(response));
    }

    [Fact]
    public async Task 로그인하지_않으면_계좌를_바꿀_수_없다()
    {
        using var app = new TestApp();
        await app.RegisterAsync();
        var browser = app.CreateBrowser();

        var response = await browser.GetAsync("/accounts/select/2?returnUrl=%2F");

        Assert.StartsWith("/login", TestApp.LocalPath(response));
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(c => c.StartsWith(CurrentAccount.CookieName)));
    }
}
