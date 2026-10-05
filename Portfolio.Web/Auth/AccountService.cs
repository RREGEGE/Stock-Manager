using Microsoft.AspNetCore.Identity;
using Portfolio.Data;

namespace Portfolio.Web.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";
    public const string CookieName = "portfolio_auth";
    public const string StampClaim = "pwd_stamp";
    public const int MinPasswordLength = 8;
    public const int MinUserNameLength = 3;
    public const int MaxUserNameLength = 30;
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(90);

    // false는 가짜 시세(개발용 시드 데이터)일 때만 허용한다
    public bool Enabled { get; set; } = true;
}

// 개인 사용 단계의 간소화된 계정: 계정 1개(아이디 + 비밀번호) (설계서 7.3).
// - 가입은 계정이 아직 없을 때만, 웹 화면에서 한다. 한 번 가입하면 가입 화면은 닫힌다.
// - 비밀번호는 해시(PBKDF2)로만 저장한다. 변경은 설정 화면(현재 비밀번호 확인)에서 한다.
// - 비밀번호를 잊었을 때는 앱을 실행하는 PC의 set-password 명령으로 다시 정한다.
public sealed class AccountService(SettingsRepository settings)
{
    private static readonly PasswordHasher<string> Hasher = new();
    private const string HashUser = "owner";

    public Task<string?> GetUserNameAsync(CancellationToken ct = default) => settings.GetUserNameAsync(ct);

    // 아이디와 비밀번호가 모두 있어야 가입된 것으로 본다
    public async Task<bool> IsRegisteredAsync(CancellationToken ct = default) =>
        !string.IsNullOrEmpty(await settings.GetUserNameAsync(ct))
        && !string.IsNullOrEmpty(await settings.GetPasswordHashAsync(ct));

    public static string? ValidateUserName(string? userName)
    {
        userName = userName?.Trim() ?? "";
        if (userName.Length is < AuthOptions.MinUserNameLength or > AuthOptions.MaxUserNameLength)
            return $"아이디는 {AuthOptions.MinUserNameLength}~{AuthOptions.MaxUserNameLength}자여야 합니다.";
        if (userName.Any(char.IsWhiteSpace))
            return "아이디에는 공백을 쓸 수 없습니다.";
        return null;
    }

    public static string? ValidatePassword(string? password) =>
        string.IsNullOrEmpty(password) || password.Length < AuthOptions.MinPasswordLength
            ? $"비밀번호는 {AuthOptions.MinPasswordLength}자 이상이어야 합니다."
            : null;

    // 계정이 없을 때만 만든다. 이미 있으면 InvalidOperationException.
    public async Task RegisterAsync(string? userName, string? password, CancellationToken ct = default)
    {
        if (ValidateUserName(userName) is { } nameError) throw new ArgumentException(nameError);
        if (ValidatePassword(password) is { } passwordError) throw new ArgumentException(passwordError);
        if (await IsRegisteredAsync(ct))
            throw new InvalidOperationException("이미 계정이 있습니다. 로그인하세요.");

        await settings.SetPasswordHashAsync(Hasher.HashPassword(HashUser, password!), ct);
        await settings.SetUserNameAsync(userName!.Trim(), ct);
    }

    public async Task<bool> VerifyAsync(string? userName, string? password, CancellationToken ct = default)
    {
        string? storedName = await settings.GetUserNameAsync(ct);
        if (string.IsNullOrEmpty(storedName)
            || !string.Equals(storedName, userName?.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;
        return await VerifyPasswordAsync(password, ct);
    }

    public async Task<bool> VerifyPasswordAsync(string? password, CancellationToken ct = default)
    {
        string? hash = await settings.GetPasswordHashAsync(ct);
        if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(password)) return false;
        return Hasher.VerifyHashedPassword(HashUser, hash, password) != PasswordVerificationResult.Failed;
    }

    // 현재 비밀번호가 맞을 때만 바꾼다. 틀리면 false.
    public async Task<bool> ChangePasswordAsync(string? currentPassword, string? newPassword, CancellationToken ct = default)
    {
        if (ValidatePassword(newPassword) is { } error) throw new ArgumentException(error);
        if (!await VerifyPasswordAsync(currentPassword, ct)) return false;
        await settings.SetPasswordHashAsync(Hasher.HashPassword(HashUser, newPassword!), ct);
        return true;
    }

    // 현재 비밀번호 확인 없이 다시 정한다. 앱을 실행하는 PC의 set-password 명령(비밀번호 분실 복구)에서만 쓴다.
    public async Task ResetPasswordAsync(string? newPassword, CancellationToken ct = default)
    {
        if (ValidatePassword(newPassword) is { } error) throw new ArgumentException(error);
        if (!await IsRegisteredAsync(ct))
            throw new InvalidOperationException("아직 계정이 없습니다. 앱을 켜고 웹 화면에서 가입하세요.");
        await settings.SetPasswordHashAsync(Hasher.HashPassword(HashUser, newPassword!), ct);
    }

    // 현재 비밀번호를 가리키는 표식. 로그인 쿠키에 넣어 두고, 비밀번호가 바뀌면 달라지므로
    // 이전에 로그인한 기기는 모두 다시 로그인해야 한다 (기기 분실 대비).
    public async Task<string?> GetStampAsync(CancellationToken ct = default)
    {
        string? hash = await settings.GetPasswordHashAsync(ct);
        if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(await settings.GetUserNameAsync(ct))) return null;
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(hash)))[..32];
    }
}

// 연속으로 틀리면 잠깐 막는다. 사용자가 1명이라 기기 구분 없이 전체에 적용한다.
public sealed class LoginThrottle(TimeProvider clock)
{
    public const int MaxFailures = 5;
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(1);

    private readonly object _gate = new();
    private int _failures;
    private DateTimeOffset _lockedUntil = DateTimeOffset.MinValue;

    // 잠겨 있으면 남은 시간, 아니면 null
    public TimeSpan? LockRemaining
    {
        get
        {
            lock (_gate)
            {
                var remaining = _lockedUntil - clock.GetUtcNow();
                return remaining > TimeSpan.Zero ? remaining : null;
            }
        }
    }

    public void RecordFailure()
    {
        lock (_gate)
        {
            if (++_failures >= MaxFailures)
            {
                _lockedUntil = clock.GetUtcNow() + LockDuration;
                _failures = 0;
            }
        }
    }

    public void RecordSuccess()
    {
        lock (_gate) { _failures = 0; }
    }

    public static string Message(TimeSpan remaining) =>
        $"여러 번 틀려서 잠시 막았습니다. {Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds))}초 뒤에 다시 시도하세요.";
}
