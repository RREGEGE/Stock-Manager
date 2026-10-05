using System.Diagnostics;
using System.Net.Sockets;

namespace Portfolio.Web.Hosting;

public sealed class LaunchOptions
{
    public const string SectionName = "Launch";
    // 앱이 켜지면 화면을 자동으로 연다 (테스트·개발 환경에서는 끈다)
    public bool OpenBrowser { get; set; } = true;
}

// 실행 파일을 더블클릭했을 때의 동작: 서버를 켠 뒤 화면을 앱 창으로 열어 준다.
public static class AppLauncher
{
    // 실행 파일을 다른 폴더에서 띄워도(바로가기, 탐색기) 설정 파일과 wwwroot를 찾도록 작업 폴더를 맞춘다.
    // 개발 중(`dotnet run`, 작업 폴더 = 프로젝트 폴더)에는 아무것도 하지 않는다.
    public static void UseExecutableDirectoryIfNeeded()
    {
        string current = Directory.GetCurrentDirectory();
        string baseDir = AppContext.BaseDirectory;
        if (!Directory.Exists(Path.Combine(current, "wwwroot")) && File.Exists(Path.Combine(baseDir, "appsettings.json")))
            Directory.SetCurrentDirectory(baseDir);
    }

    // 접속 주소 목록에서 이 PC에서 열 주소를 고른다 (0.0.0.0 등은 127.0.0.1로 바꿔 연다)
    public static string? PickLocalUrl(IEnumerable<string> urls)
    {
        foreach (string url in urls)
        {
            if (ListenAddressCheck.IsLoopback(url)) return url.TrimEnd('/');
            int schemeEnd = url.IndexOf("://", StringComparison.Ordinal);
            int portStart = url.LastIndexOf(':');
            if (schemeEnd > 0 && portStart > schemeEnd + 2)
                return $"{url[..schemeEnd]}://127.0.0.1{url[portStart..].TrimEnd('/')}";
        }
        return null;
    }

    // 같은 주소에서 이미 앱이 듣고 있는지 (두 번 실행했을 때 화면만 열고 끝내기 위함)
    public static bool IsAlreadyListening(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        try
        {
            using var client = new TcpClient();
            return client.ConnectAsync(uri.Host, uri.Port).Wait(TimeSpan.FromMilliseconds(500)) && client.Connected;
        }
        catch (Exception ex) when (ex is SocketException or AggregateException)
        {
            return false;
        }
    }

    // 주소창 없는 앱 창(Edge 앱 모드)으로 열고, 안 되면 기본 브라우저로 연다.
    public static void OpenWindow(string url, Func<ProcessStartInfo, bool>? start = null)
    {
        start ??= info =>
        {
            try { return Process.Start(info) is not null; }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
            {
                return false;
            }
        };

        if (OperatingSystem.IsWindows()
            && start(new ProcessStartInfo("msedge", $"--app={url}") { UseShellExecute = true }))
            return;
        start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
