namespace Portfolio.Web.Services;

// 화면 모드 (F-13): 시스템 설정 따르기 / 밝게 / 어둡게. 고른 값은 브라우저마다 쿠키에 적어 둔다.
// 색 자체는 app.css의 변수 묶음이 정하고, 여기서는 <html data-theme>에 넣을 값만 다룬다.
public sealed class ThemeState
{
    public const string System = "system";
    public const string Light = "light";
    public const string Dark = "dark";
    public const string CookieName = "portfolio.theme";
    public const string SelectPath = "/theme";

    public string Mode { get; set; } = System;

    // <html data-theme="..."> 값. 시스템 설정을 따를 때는 속성을 두지 않는다 (null).
    public string? HtmlAttribute => Mode == System ? null : Mode;

    public static string Normalize(string? mode) => mode is Light or Dark ? mode : System;

    public static string ReadCookie(HttpContext context) => Normalize(context.Request.Cookies[CookieName]);

    // 모드를 바꾸는 주소: 쿠키를 고쳐 쓰고 보던 화면으로 돌아온다
    public static string SelectUrl(string mode, string returnPath) =>
        $"{SelectPath.TrimStart('/')}/{Normalize(mode)}?returnUrl={Uri.EscapeDataString(returnPath)}";
}
