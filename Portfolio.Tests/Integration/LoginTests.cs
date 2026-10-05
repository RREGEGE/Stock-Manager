using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Data;
using Portfolio.Web.Auth;

namespace Portfolio.Tests.Integration;

// AC-12 (개인 사용 단계의 간소화: 비밀번호 1개, TOTP 없음 — 설계서 7.3)
public class LoginTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/holdings")]
    [InlineData("/rebalance")]
    [InlineData("/groups")]
    [InlineData("/settings")]
    [InlineData("/no-such-page")]
    public async Task AC12_로그인_없이_접근하면_로그인_화면으로_이동한다(string path)
    {
        using var app = new TestApp();
        await app.SetPasswordAsync();
        var browser = app.CreateBrowser();

        var response = await browser.GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/login", response.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task AC12_화면_연결_경로도_로그인_없이는_쓸_수_없다()
    {
        using var app = new TestApp();
        await app.SetPasswordAsync();
        var browser = app.CreateBrowser();

        var response = await browser.PostAsync("/_blazor/negotiate?negotiateVersion=1", null);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/login", response.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task 로그인_화면과_그_화면의_CSS는_로그인_전에도_열린다()
    {
        using var app = new TestApp();
        await app.SetPasswordAsync();
        var browser = app.CreateBrowser();

        var login = await browser.GetAsync("/login");
        string html = await login.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains("type=\"password\"", html);
        Assert.DoesNotContain("보유 종목", html);   // 메뉴·내역은 보이지 않는다

        // 응답 코드만이 아니라 실제 내용이 오는지 확인한다 (운영 모드에서 빈 파일이 오던 문제 방지)
        var cssPath = System.Text.RegularExpressions.Regex.Match(html, "href=\"(app[^\"]*\\.css)\"").Groups[1].Value;
        Assert.Contains("--bg-page", await browser.GetStringAsync("/" + cssPath));

        var scopedCssPath = System.Text.RegularExpressions.Regex.Match(html, "href=\"(Portfolio\\.Web[^\"]*\\.styles\\.css)\"").Groups[1].Value;
        Assert.Contains(".login-card", await browser.GetStringAsync("/" + scopedCssPath));
    }

    [Fact]
    public async Task 로그인하면_화면용_스크립트와_CSS가_내용과_함께_온다()
    {
        using var app = new TestApp();
        await app.SetPasswordAsync();
        var browser = app.CreateBrowser();
        await TestApp.PostLoginAsync(browser, TestApp.TestPassword);

        string html = await browser.GetStringAsync("/");
        var scriptPath = System.Text.RegularExpressions.Regex.Match(html, "src=\"(_framework/blazor\\.web[^\"]*\\.js)\"").Groups[1].Value;

        Assert.NotEqual("", scriptPath);
        Assert.True((await browser.GetStringAsync("/" + scriptPath)).Length > 10_000);
    }

    [Fact]
    public async Task 맞는_비밀번호로_로그인하면_원래_가려던_화면으로_가고_이후_접근이_된다()
    {
        using var app = new TestApp();
        await app.SetPasswordAsync();
        var browser = app.CreateBrowser();

        var response = await TestApp.PostLoginAsync(browser, TestApp.TestPassword, "/login?ReturnUrl=%2Fholdings");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/holdings", TestApp.LocalPath(response));
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith(AuthOptions.CookieName + "="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires=", cookie, StringComparison.OrdinalIgnoreCase);   // 90일 유지되는 쿠키

        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/holdings")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task 틀린_비밀번호로는_로그인되지_않는다()
    {
        using var app = new TestApp();
        await app.SetPasswordAsync();
        var browser = app.CreateBrowser();

        var response = await TestApp.PostLoginAsync(browser, "wrong-password");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("비밀번호가 맞지 않습니다.", await TestApp.ReadTextAsync(response));
        Assert.DoesNotContain(response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [],
            c => c.StartsWith(AuthOptions.CookieName + "="));
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task 연속_5회_틀리면_맞는_비밀번호도_잠시_받지_않는다()
    {
        using var app = new TestApp();
        await app.SetPasswordAsync();
        var browser = app.CreateBrowser();

        for (int i = 0; i < LoginThrottle.MaxFailures; i++)
            await TestApp.PostLoginAsync(browser, "wrong-password");

        var response = await TestApp.PostLoginAsync(browser, TestApp.TestPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("여러 번 틀려서 잠시 막았습니다.", await TestApp.ReadTextAsync(response));
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task 비밀번호를_설정하기_전에는_아무도_로그인할_수_없고_설정_방법을_안내한다()
    {
        using var app = new TestApp();
        var browser = app.CreateBrowser();

        string html = await browser.GetStringAsync("/login");

        Assert.Contains("아직 비밀번호가 설정되지 않았습니다.", html);
        Assert.Contains("set-password", html);
        Assert.DoesNotContain("type=\"password\"", html);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task 로그아웃하면_다시_로그인_화면으로_이동한다()
    {
        using var app = new TestApp();
        await app.SetPasswordAsync();
        var browser = app.CreateBrowser();
        await TestApp.PostLoginAsync(browser, TestApp.TestPassword);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/")).StatusCode);

        var logout = await browser.GetAsync("/logout");

        Assert.Equal("/login", TestApp.LocalPath(logout));
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/")).StatusCode);
    }

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example/")]
    public async Task 로그인_후_이동_주소가_앱_밖이면_대시보드로_보낸다(string returnUrl)
    {
        using var app = new TestApp();
        await app.SetPasswordAsync();
        var browser = app.CreateBrowser();

        var response = await TestApp.PostLoginAsync(browser, TestApp.TestPassword, "/login?ReturnUrl=" + Uri.EscapeDataString(returnUrl));

        Assert.Equal("/", TestApp.LocalPath(response));
    }

    [Fact]
    public async Task 비밀번호는_해시로만_저장된다()
    {
        using var app = new TestApp();
        await app.SetPasswordAsync();

        await using var db = await app.Services.GetRequiredService<IDbContextFactory<PortfolioDbContext>>().CreateDbContextAsync();
        var stored = await db.AppSettings.AsNoTracking().SingleAsync(s => s.Key == SettingsRepository.PasswordHashKey);

        Assert.DoesNotContain(TestApp.TestPassword, stored.Value);
        Assert.True(stored.Value.Length > 40);
    }

    [Fact]
    public void 로그인을_끄는_설정은_실제_시세_구성에서는_기동을_막는다()
    {
        using var app = new TestApp().WithSetting("Auth:Enabled", "false");

        var ex = Assert.ThrowsAny<Exception>(() => app.CreateBrowser());

        Assert.Contains("Auth:Enabled=false", ex.ToString());
    }
}

public class SetPasswordCommandTests
{
    private static async Task<(int Code, string Output, PasswordService Service)> RunAsync(TestDb db, params string?[] inputs)
    {
        var service = new PasswordService(new SettingsRepository(db));
        var queue = new Queue<string?>(inputs);
        var output = new StringWriter();
        int code = await SetPasswordCommand.RunAsync(service, _ => queue.Dequeue(), output);
        return (code, output.ToString(), service);
    }

    [Fact]
    public async Task 같은_값을_두_번_입력하면_비밀번호가_설정된다()
    {
        using var db = new TestDb();

        var (code, output, service) = await RunAsync(db, "test-only-password-1", "test-only-password-1");

        Assert.Equal(0, code);
        Assert.Contains("저장했습니다", output);
        Assert.True(await service.VerifyAsync("test-only-password-1"));
        Assert.False(await service.VerifyAsync("test-only-password-2"));
    }

    [Fact]
    public async Task 두_입력이_다르거나_8자_미만이면_바꾸지_않는다()
    {
        using var db = new TestDb();

        var mismatch = await RunAsync(db, "test-only-password-1", "test-only-password-2");
        var tooShort = await RunAsync(db, "short", "short");

        Assert.Equal((1, 1), (mismatch.Code, tooShort.Code));
        Assert.Contains("서로 다릅니다", mismatch.Output);
        Assert.Contains("8자 이상", tooShort.Output);
        Assert.False(await tooShort.Service.IsConfiguredAsync());
    }

    [Fact]
    public async Task 다시_실행하면_비밀번호가_바뀐다()
    {
        using var db = new TestDb();
        await RunAsync(db, "test-only-password-1", "test-only-password-1");

        var (_, output, service) = await RunAsync(db, "test-only-password-2", "test-only-password-2");

        Assert.Contains("변경합니다", output);
        Assert.False(await service.VerifyAsync("test-only-password-1"));
        Assert.True(await service.VerifyAsync("test-only-password-2"));
    }
}
