using Microsoft.Extensions.Configuration;
using Portfolio.Web.Hosting;

namespace Portfolio.Tests;

public class HostingTests
{
    [Theory]
    [InlineData("http://127.0.0.1:5137", true)]
    [InlineData("http://localhost:5137", true)]
    [InlineData("http://[::1]:5137", true)]
    [InlineData("http://0.0.0.0:5137", false)]      // 모든 네트워크
    [InlineData("http://192.168.0.10:5137", false)] // 공유기 안의 다른 기기에서 접속 가능
    [InlineData("http://*:5137", false)]
    [InlineData("http://+:5137", false)]
    public void 이_PC_안에서만_듣는_주소인지_판단한다(string url, bool expected)
    {
        Assert.Equal(expected, ListenAddressCheck.IsLoopback(url));
    }

    [Fact]
    public void 주소가_하나라도_밖으로_열려_있으면_노출로_본다()
    {
        Assert.True(ListenAddressCheck.IsLoopbackOnly(["http://127.0.0.1:5137", "http://localhost:5137"]));
        Assert.False(ListenAddressCheck.IsLoopbackOnly(["http://127.0.0.1:5137", "http://0.0.0.0:5137"]));
    }

    [Fact]
    public void 데이터_폴더를_만들면_값이_비어_있는_설정_파일이_생기고_다시_실행해도_덮어쓰지_않는다()
    {
        string dir = Path.Combine(Path.GetTempPath(), "portfolio-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["DataDirectory"] = dir }).Build();
            var paths = AppPaths.FromConfiguration(config);

            paths.EnsureCreated();

            Assert.Equal(Path.Combine(dir, "portfolio.db"), paths.DatabasePath);
            var settings = new ConfigurationBuilder().AddJsonFile(paths.SettingsPath).Build();
            Assert.Equal("", settings["Kis:AppKey"]);
            Assert.Equal("", settings["Kis:AppSecret"]);

            File.WriteAllText(paths.SettingsPath, """{ "Kis": { "AppKey": "edited" } }""");
            paths.EnsureCreated();
            Assert.Contains("edited", File.ReadAllText(paths.SettingsPath));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void 데이터_폴더를_지정하지_않으면_사용자_폴더_아래_Portfolio를_쓴다()
    {
        var paths = AppPaths.FromConfiguration(new ConfigurationBuilder().Build());

        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Portfolio"),
            paths.DataDirectory);
    }
}
