using System.Net;
using System.Text.Json;

namespace Portfolio.Tests.Integration;

// 휴대폰 홈 화면에 추가해 앱처럼 여는 설정 (설계서 9.6)
public class HomeScreenAppTests
{
    [Fact]
    public async Task 앱_정보_파일과_아이콘은_로그인_전에도_받을_수_있다()
    {
        using var app = new TestApp();
        await app.RegisterAsync();
        var browser = app.CreateBrowser();

        // 브라우저는 이 파일을 로그인 쿠키 없이 요청한다
        var response = await browser.GetAsync("/manifest.webmanifest");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var manifest = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = manifest.RootElement;

        Assert.Equal("포트폴리오", root.GetProperty("short_name").GetString());
        Assert.Equal("standalone", root.GetProperty("display").GetString());
        Assert.Equal("/", root.GetProperty("start_url").GetString());
        Assert.Equal("#1B1F24", root.GetProperty("theme_color").GetString());

        byte[] pngSignature = [0x89, 0x50, 0x4E, 0x47];
        var icons = root.GetProperty("icons").EnumerateArray().Select(i => i.GetProperty("src").GetString()!)
            .Append("apple-touch-icon.png").Distinct();
        foreach (string icon in icons)
        {
            byte[] bytes = await browser.GetByteArrayAsync("/" + icon);
            Assert.Equal(pngSignature, bytes.Take(4));
        }
    }

    [Fact]
    public async Task 모든_화면의_머리말에_앱_정보_파일과_아이콘_연결이_있다()
    {
        using var app = new TestApp();
        await app.RegisterAsync();
        var browser = app.CreateBrowser();

        string login = await browser.GetStringAsync("/login");

        Assert.Contains("rel=\"manifest\" href=\"manifest.webmanifest\"", login);
        Assert.Contains("rel=\"apple-touch-icon\" href=\"apple-touch-icon.png\"", login);
        Assert.Contains("name=\"theme-color\" content=\"#1B1F24\"", login);
        Assert.Contains("name=\"viewport\" content=\"width=device-width, initial-scale=1.0\"", login);
    }
}
