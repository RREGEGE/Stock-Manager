using System.Net;

namespace Portfolio.Web.Hosting;

// 앱은 이 PC 안(127.0.0.1)에서만 듣고, 다른 기기는 Tailscale(tailscale serve)을 거쳐 들어온다.
// 다른 주소로 띄우면 같은 네트워크의 모든 기기에 로그인 화면이 노출되므로 경고한다.
public static class ListenAddressCheck
{
    public static bool IsLoopbackOnly(IEnumerable<string> urls) => urls.All(IsLoopback);

    public static bool IsLoopback(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;   // http://*:5137, http://+:5137 등
        string host = uri.Host.Trim('[', ']');
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        return IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip);
    }

    public static void WarnIfExposed(IEnumerable<string> urls, ILogger logger)
    {
        var exposed = urls.Where(u => !IsLoopback(u)).ToList();
        if (exposed.Count > 0)
            logger.LogWarning(
                "앱이 이 PC 밖에서도 접속 가능한 주소로 떠 있습니다: {Urls}. 127.0.0.1로만 띄우고 다른 기기는 tailscale serve로 접속하세요. (docs/operations.md)",
                string.Join(", ", exposed));
    }
}
