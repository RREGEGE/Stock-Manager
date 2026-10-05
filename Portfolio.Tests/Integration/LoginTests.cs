using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Data;
using Portfolio.Web.Auth;

namespace Portfolio.Tests.Integration;

// AC-12 (개인 사용 단계의 간소화: 계정 1개, 아이디 + 비밀번호, TOTP 없음 — 설계서 7.3)
public class LoginTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/holdings")]
    [InlineData("/rebalance")]
    [InlineData("/groups")]
    [InlineData("/settings")]
    [InlineData("/account/password")]
    [InlineData("/no-such-page")]
    public async Task AC12_로그인_없이_접근하면_로그인_화면으로_이동한다(string path)
    {
        using var app = new TestApp();
        await app.RegisterAsync();
        var browser = app.CreateBrowser();

        var response = await browser.GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/login", response.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task AC12_화면_연결_경로도_로그인_없이는_쓸_수_없다()
    {
        using var app = new TestApp();
        await app.RegisterAsync();
        var browser = app.CreateBrowser();

        var response = await browser.PostAsync("/_blazor/negotiate?negotiateVersion=1", null);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/login", response.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task 로그인_화면과_그_화면의_CSS는_로그인_전에도_열린다()
    {
        using var app = new TestApp();
        await app.RegisterAsync();
        var browser = app.CreateBrowser();

        var login = await browser.GetAsync("/login");
        string html = await TestApp.ReadTextAsync(login);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains("name=\"UserName\"", html);
        Assert.Contains("type=\"password\"", html);
        Assert.DoesNotContain("보유 종목", html);   // 메뉴·내역은 보이지 않는다

        // 응답 코드만이 아니라 실제 내용이 오는지 확인한다 (운영 모드에서 빈 파일이 오던 문제 방지)
        var cssPath = System.Text.RegularExpressions.Regex.Match(html, "href=\"(app[^\"]*\\.css)\"").Groups[1].Value;
        string css = await browser.GetStringAsync("/" + cssPath);
        Assert.Contains("--bg-page", css);
        Assert.Contains(".auth-card", css);
    }

    [Fact]
    public async Task 로그인하면_화면용_스크립트와_CSS가_내용과_함께_온다()
    {
        using var app = new TestApp();
        await app.RegisterAsync();
        var browser = app.CreateBrowser();
        await TestApp.PostLoginAsync(browser, TestApp.TestPassword);

        string html = await browser.GetStringAsync("/");
        var scriptPath = System.Text.RegularExpressions.Regex.Match(html, "src=\"(_framework/blazor\\.web[^\"]*\\.js)\"").Groups[1].Value;
        var scopedCssPath = System.Text.RegularExpressions.Regex.Match(html, "href=\"(Portfolio\\.Web[^\"]*\\.styles\\.css)\"").Groups[1].Value;

        Assert.NotEqual("", scriptPath);
        Assert.True((await browser.GetStringAsync("/" + scriptPath)).Length > 10_000);
        Assert.Contains(".topbar", await browser.GetStringAsync("/" + scopedCssPath));
    }

    [Fact]
    public async Task 맞는_아이디와_비밀번호로_로그인하면_원래_가려던_화면으로_가고_이후_접근이_된다()
    {
        using var app = new TestApp();
        await app.RegisterAsync();
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
    public async Task 아이디는_대소문자를_구분하지_않는다()
    {
        using var app = new TestApp();
        await app.RegisterAsync();
        var browser = app.CreateBrowser();

        var response = await TestApp.PostLoginAsync(browser, TestApp.TestPassword, userName: "TESTER");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Theory]
    [InlineData("tester", "wrong-password")]
    [InlineData("someone-else", TestApp.TestPassword)]
    [InlineData("", TestApp.TestPassword)]
    public async Task 아이디나_비밀번호가_틀리면_로그인되지_않는다(string userName, string password)
    {
        using var app = new TestApp();
        await app.RegisterAsync();
        var browser = app.CreateBrowser();

        var response = await TestApp.PostLoginAsync(browser, password, userName: userName);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // 어느 쪽이 틀렸는지는 알려 주지 않는다
        Assert.Contains("아이디 또는 비밀번호가 맞지 않습니다.", await TestApp.ReadTextAsync(response));
        Assert.DoesNotContain(response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [],
            c => c.StartsWith(AuthOptions.CookieName + "="));
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task 연속_5회_틀리면_맞는_비밀번호도_잠시_받지_않는다()
    {
        using var app = new TestApp();
        await app.RegisterAsync();
        var browser = app.CreateBrowser();

        for (int i = 0; i < LoginThrottle.MaxFailures; i++)
            await TestApp.PostLoginAsync(browser, "wrong-password");

        var response = await TestApp.PostLoginAsync(browser, TestApp.TestPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("여러 번 틀려서 잠시 막았습니다.", await TestApp.ReadTextAsync(response));
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task 로그아웃하면_다시_로그인_화면으로_이동한다()
    {
        using var app = new TestApp();
        await app.RegisterAsync();
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
        await app.RegisterAsync();
        var browser = app.CreateBrowser();

        var response = await TestApp.PostLoginAsync(browser, TestApp.TestPassword, "/login?ReturnUrl=" + Uri.EscapeDataString(returnUrl));

        Assert.Equal("/", TestApp.LocalPath(response));
    }

    [Fact]
    public async Task 비밀번호는_해시로만_저장된다()
    {
        using var app = new TestApp();
        await app.RegisterAsync();

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

// 웹에서 가입(계정이 없을 때만)하고 비밀번호를 바꾼다
public class SignupAndPasswordTests
{
    private static Dictionary<string, string> SignupForm(string userName, string password, string? confirm = null) => new()
    {
        ["UserName"] = userName, ["Password"] = password, ["ConfirmPassword"] = confirm ?? password,
    };

    private static Dictionary<string, string> ChangeForm(string current, string next, string? confirm = null) => new()
    {
        ["CurrentPassword"] = current, ["NewPassword"] = next, ["ConfirmPassword"] = confirm ?? next,
    };

    [Fact]
    public async Task 계정이_없으면_처음_접속이_가입_화면으로_이어진다()
    {
        using var app = new TestApp();
        var browser = app.CreateBrowser();

        var first = await browser.GetAsync("/");
        Assert.StartsWith("/login", first.Headers.Location!.PathAndQuery);
        var second = await browser.GetAsync(first.Headers.Location);
        Assert.Equal("/signup", TestApp.LocalPath(second));

        string html = await TestApp.ReadTextAsync(await browser.GetAsync("/signup"));
        Assert.Contains("처음 사용합니다. 로그인에 쓸 계정을 만드세요.", html);
        Assert.Contains("name=\"UserName\"", html);
        Assert.Contains("name=\"ConfirmPassword\"", html);
    }

    [Fact]
    public async Task 가입하면_계정이_만들어지고_그_기기는_바로_로그인된다()
    {
        using var app = new TestApp();
        var browser = app.CreateBrowser();

        var response = await TestApp.PostFormAsync(browser, "/signup", SignupForm("my-id", TestApp.TestPassword));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", TestApp.LocalPath(response));
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/")).StatusCode);

        // 다른 기기(다른 PC·휴대폰)에서는 같은 아이디로 로그인한다
        var phone = app.CreateBrowser();
        Assert.Equal(HttpStatusCode.Redirect, (await phone.GetAsync("/")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await TestApp.PostLoginAsync(phone, TestApp.TestPassword, userName: "my-id")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await phone.GetAsync("/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/")).StatusCode);   // 먼저 로그인한 기기도 그대로 유지
    }

    [Fact]
    public async Task 계정이_이미_있으면_가입_화면은_닫혀_있고_두_번째_가입은_거부된다()
    {
        using var app = new TestApp();
        await app.RegisterAsync();
        var browser = app.CreateBrowser();

        var page = await browser.GetAsync("/signup");
        Assert.Equal("/login", TestApp.LocalPath(page));

        // 가입 화면을 거치지 않고 가입 요청을 직접 보내도(다른 계정으로 덮어쓰기 시도) 계정은 바뀌지 않는다
        var accounts = app.Services.GetRequiredService<AccountService>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => accounts.RegisterAsync("attacker", "attacker-password-1"));
        Assert.Equal(TestApp.TestUserName, await accounts.GetUserNameAsync());
        Assert.True(await accounts.VerifyAsync(TestApp.TestUserName, TestApp.TestPassword));
        Assert.False(await accounts.VerifyAsync("attacker", "attacker-password-1"));
    }

    [Theory]
    [InlineData("ab", "test-only-password-1", "test-only-password-1", "아이디는 3~30자여야 합니다.")]
    [InlineData("my id", "test-only-password-1", "test-only-password-1", "아이디에는 공백을 쓸 수 없습니다.")]
    [InlineData("my-id", "short", "short", "비밀번호는 8자 이상이어야 합니다.")]
    [InlineData("my-id", "test-only-password-1", "test-only-password-2", "비밀번호와 비밀번호 확인이 서로 다릅니다.")]
    public async Task 가입_입력이_잘못되면_이유를_알리고_계정을_만들지_않는다(string userName, string password, string confirm, string message)
    {
        using var app = new TestApp();
        var browser = app.CreateBrowser();

        var response = await TestApp.PostFormAsync(browser, "/signup", SignupForm(userName, password, confirm));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(message, await TestApp.ReadTextAsync(response));
        Assert.False(await app.Services.GetRequiredService<AccountService>().IsRegisteredAsync());
    }

    [Fact]
    public async Task 설정에서_비밀번호를_바꾸면_이_기기는_유지되고_다른_기기는_다시_로그인해야_한다()
    {
        using var app = new TestApp();
        await app.RegisterAsync();
        var pc = app.CreateBrowser();
        var phone = app.CreateBrowser();
        await TestApp.PostLoginAsync(pc, TestApp.TestPassword);
        await TestApp.PostLoginAsync(phone, TestApp.TestPassword);
        const string newPassword = "test-only-password-2";

        var response = await TestApp.PostFormAsync(pc, "/account/password", ChangeForm(TestApp.TestPassword, newPassword));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("비밀번호를 바꿨습니다.", await TestApp.ReadTextAsync(response));
        Assert.Equal(HttpStatusCode.OK, (await pc.GetAsync("/")).StatusCode);            // 바꾼 기기는 유지
        Assert.Equal(HttpStatusCode.Redirect, (await phone.GetAsync("/")).StatusCode);   // 다른 기기는 로그아웃됨

        Assert.Equal(HttpStatusCode.OK, (await TestApp.PostLoginAsync(phone, TestApp.TestPassword)).StatusCode);   // 옛 비밀번호 거부
        Assert.Equal(HttpStatusCode.Redirect, (await TestApp.PostLoginAsync(phone, newPassword)).StatusCode);
    }

    [Theory]
    [InlineData("wrong-current-password", "test-only-password-2", "test-only-password-2", "현재 비밀번호가 맞지 않습니다.")]
    [InlineData(TestApp.TestPassword, "short", "short", "비밀번호는 8자 이상이어야 합니다.")]
    [InlineData(TestApp.TestPassword, "test-only-password-2", "test-only-password-3", "새 비밀번호와 확인이 서로 다릅니다.")]
    public async Task 비밀번호_변경_입력이_잘못되면_바꾸지_않는다(string current, string next, string confirm, string message)
    {
        using var app = new TestApp();
        await app.RegisterAsync();
        var browser = app.CreateBrowser();
        await TestApp.PostLoginAsync(browser, TestApp.TestPassword);

        var response = await TestApp.PostFormAsync(browser, "/account/password", ChangeForm(current, next, confirm));

        Assert.Contains(message, await TestApp.ReadTextAsync(response));
        Assert.True(await app.Services.GetRequiredService<AccountService>().VerifyAsync(TestApp.TestUserName, TestApp.TestPassword));
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/")).StatusCode);   // 로그인은 그대로
    }

    [Fact]
    public async Task 이전_방식으로_비밀번호만_저장된_DB는_가입부터_다시_한다()
    {
        // set-password 명령으로 비밀번호만 정해 둔 예전 상태 (아이디 없음)
        using var app = new TestApp();
        await app.Services.GetRequiredService<SettingsRepository>().SetPasswordHashAsync("old-hash-without-user-name");
        var browser = app.CreateBrowser();

        var login = await browser.GetAsync("/login");

        Assert.Equal("/signup", TestApp.LocalPath(login));
        Assert.False(await app.Services.GetRequiredService<AccountService>().IsRegisteredAsync());
    }
}

public class SetPasswordCommandTests
{
    private static async Task<(int Code, string Output, AccountService Service)> RunAsync(TestDb db, params string?[] inputs)
    {
        var service = new AccountService(new SettingsRepository(db));
        var queue = new Queue<string?>(inputs);
        var output = new StringWriter();
        int code = await SetPasswordCommand.RunAsync(service, _ => queue.Dequeue(), output);
        return (code, output.ToString(), service);
    }

    private static Task RegisterAsync(TestDb db) =>
        new AccountService(new SettingsRepository(db)).RegisterAsync("tester", "test-only-password-1");

    [Fact]
    public async Task 비밀번호를_잊었을_때_현재_비밀번호_없이_다시_정한다()
    {
        using var db = new TestDb();
        await RegisterAsync(db);

        var (code, output, service) = await RunAsync(db, "test-only-password-2", "test-only-password-2");

        Assert.Equal(0, code);
        Assert.Contains("'tester' 계정의 비밀번호를 다시 정합니다.", output);
        Assert.Contains("저장했습니다", output);
        Assert.False(await service.VerifyAsync("tester", "test-only-password-1"));
        Assert.True(await service.VerifyAsync("tester", "test-only-password-2"));
    }

    [Fact]
    public async Task 두_입력이_다르거나_8자_미만이면_바꾸지_않는다()
    {
        using var db = new TestDb();
        await RegisterAsync(db);

        var mismatch = await RunAsync(db, "test-only-password-2", "test-only-password-3");
        var tooShort = await RunAsync(db, "short", "short");

        Assert.Equal((1, 1), (mismatch.Code, tooShort.Code));
        Assert.Contains("서로 다릅니다", mismatch.Output);
        Assert.Contains("8자 이상", tooShort.Output);
        Assert.True(await tooShort.Service.VerifyAsync("tester", "test-only-password-1"));
    }

    [Fact]
    public async Task 계정이_없으면_웹에서_가입하라고_안내하고_아무것도_만들지_않는다()
    {
        using var db = new TestDb();

        var (code, output, service) = await RunAsync(db);

        Assert.Equal(1, code);
        Assert.Contains("웹 화면에서 가입하세요", output);
        Assert.False(await service.IsRegisteredAsync());
    }
}
