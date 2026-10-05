using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Web.Auth;

namespace Portfolio.Tests.Integration;

// 운영 설정(Production, 로그인 켜짐)으로 앱 전체를 메모리에서 띄운다.
// 데이터는 테스트마다 임시 폴더를 쓰고, 외부 통신(KIS, 종목 마스터)은 하지 않는다.
public sealed class TestApp : WebApplicationFactory<Program>
{
    // 테스트 전용 계정 (실제 계정 아님)
    public const string TestUserName = "tester";
    public const string TestPassword = "test-only-password-1";

    public string DataDirectory { get; } =
        Path.Combine(Path.GetTempPath(), "portfolio-it-" + Guid.NewGuid().ToString("N"));

    private readonly Dictionary<string, string> _settings = [];

    public TestApp WithSetting(string key, string value)
    {
        _settings[key] = value;
        return this;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("DataDirectory", DataDirectory);
        builder.UseSetting("SymbolMaster:AutoRefresh", "false");
        builder.UseSetting("Backup:Enabled", "false");
        builder.UseSetting("Launch:OpenBrowser", "false");
        foreach (var (key, value) in _settings)
            builder.UseSetting(key, value);
    }

    // 가입된 상태로 만든다 (웹 가입 화면을 거치지 않고 바로)
    public Task RegisterAsync(string userName = TestUserName, string password = TestPassword) =>
        Services.GetRequiredService<AccountService>().RegisterAsync(userName, password);

    // 리디렉션을 따라가지 않고 쿠키는 유지하는 클라이언트 (브라우저 한 대에 해당)
    public HttpClient CreateBrowser() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    // 화면을 받아 폼의 숨은 값(위조 방지 토큰 등)과 함께 입력값을 전송한다
    public static async Task<HttpResponseMessage> PostFormAsync(HttpClient browser, string path, Dictionary<string, string> values)
    {
        string html = await browser.GetStringAsync(path);
        var fields = Regex.Matches(html, """<input type="hidden" name="([^"]+)" value="([^"]*)" */?>""")
            .ToDictionary(m => m.Groups[1].Value, m => WebUtility.HtmlDecode(m.Groups[2].Value));
        foreach (var (key, value) in values) fields[key] = value;
        return await browser.PostAsync(path, new FormUrlEncodedContent(fields));
    }

    public static Task<HttpResponseMessage> PostLoginAsync(
        HttpClient browser, string password, string path = "/login", string userName = TestUserName) =>
        PostFormAsync(browser, path, new() { ["UserName"] = userName, ["Password"] = password });

    // 리디렉션 대상이 이 앱(같은 호스트)일 때만 경로를 돌려준다. 앱 밖이면 전체 주소를 그대로 돌려준다.
    public static string LocalPath(HttpResponseMessage response)
    {
        var location = response.Headers.Location!;
        if (!location.IsAbsoluteUri) return location.OriginalString;
        return location.Host == "localhost" ? location.PathAndQuery : location.AbsoluteUri;
    }

    // 서버 렌더링 HTML은 한글을 문자 코드로 내보내므로 풀어서 읽는다
    public static async Task<string> ReadTextAsync(HttpResponseMessage response) =>
        WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, recursive: true); }
        catch (IOException) { /* 임시 폴더 정리는 실패해도 테스트 결과와 무관 */ }
    }
}
