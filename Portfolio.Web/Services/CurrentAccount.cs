using Portfolio.Data;

namespace Portfolio.Web.Services;

// 이 브라우저 화면이 보고 있는 계좌 (F-11). 화면 연결(circuit)마다 하나씩 있고,
// 고른 계좌는 쿠키에 적어 두어 다음에 열 때도 같은 계좌로 시작한다.
public sealed class CurrentAccount
{
    public const string CookieName = "portfolio.account";
    public const string SelectPath = "/accounts/select";

    public int Id { get; set; } = TradingAccount.DefaultId;

    // 계좌를 바꾸는 주소: 쿠키를 고쳐 쓰고 보던 화면으로 돌아온다
    public static string SelectUrl(int accountId, string returnPath) =>
        $"{SelectPath.TrimStart('/')}/{accountId}?returnUrl={Uri.EscapeDataString(returnPath)}";

    public static int ReadCookie(HttpContext context) =>
        int.TryParse(context.Request.Cookies[CookieName], out int id) && id > 0 ? id : TradingAccount.DefaultId;
}
