using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Portfolio.Web.Hosting;

namespace Portfolio.Tests;

// 실행 파일을 더블클릭했을 때의 동작 (화면 자동 열기)
public class AppLauncherTests
{
    [Theory]
    [InlineData("http://127.0.0.1:5137", "http://127.0.0.1:5137")]
    [InlineData("http://localhost:5137/", "http://localhost:5137")]
    [InlineData("http://0.0.0.0:5137", "http://127.0.0.1:5137")]   // 밖으로 열어 띄웠어도 이 PC에서는 내부 주소로 연다
    [InlineData("http://*:8080", "http://127.0.0.1:8080")]
    public void 이_PC에서_열_주소를_고른다(string listenUrl, string expected)
    {
        Assert.Equal(expected, AppLauncher.PickLocalUrl([listenUrl]));
    }

    [Fact]
    public void 주소가_없으면_열지_않는다()
    {
        Assert.Null(AppLauncher.PickLocalUrl([]));
    }

    [Fact]
    public void 화면은_주소창_없는_앱_창으로_먼저_연다()
    {
        var started = new List<ProcessStartInfo>();

        AppLauncher.OpenWindow("http://127.0.0.1:5137", info => { started.Add(info); return true; });

        var info = Assert.Single(started);
        Assert.Equal("msedge", info.FileName);
        Assert.Equal("--app=http://127.0.0.1:5137", info.Arguments);
    }

    [Fact]
    public void 앱_창으로_못_열면_기본_브라우저로_연다()
    {
        var started = new List<ProcessStartInfo>();

        AppLauncher.OpenWindow("http://127.0.0.1:5137", info => { started.Add(info); return info.FileName != "msedge"; });

        Assert.Equal(["msedge", "http://127.0.0.1:5137"], started.Select(s => s.FileName));
        Assert.True(started[1].UseShellExecute);
    }

    [Fact]
    public void 같은_주소에서_이미_듣고_있는지_알아낸다()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            Assert.True(AppLauncher.IsAlreadyListening($"http://127.0.0.1:{port}"));
        }
        finally
        {
            listener.Stop();
        }

        Assert.False(AppLauncher.IsAlreadyListening($"http://127.0.0.1:{port}"));   // 닫힌 뒤
        Assert.False(AppLauncher.IsAlreadyListening("주소 아님"));
    }
}
