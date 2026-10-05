using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Portfolio.Web.Auth;

public static class AccountSignIn
{
    // 이 기기에 90일 유지되는 로그인 쿠키를 발급한다. 쿠키에는 현재 비밀번호의 표식을 넣는다.
    public static async Task SignInAsync(HttpContext context, AccountService accounts)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, await accounts.GetUserNameAsync() ?? ""),
            new Claim(AuthOptions.StampClaim, await accounts.GetStampAsync() ?? ""),
        ], CookieAuthenticationDefaults.AuthenticationScheme);

        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });
    }

    // 로그인 후 이동할 주소는 이 앱 안의 경로만 허용한다
    public static string SafeReturnUrl(string? returnUrl) =>
        returnUrl is { Length: > 0 } url && url[0] == '/' && !url.StartsWith("//") && !url.StartsWith("/\\")
            && !url.StartsWith("/login", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("/signup", StringComparison.OrdinalIgnoreCase)
            ? url : "/";
}
