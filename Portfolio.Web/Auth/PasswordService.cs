using Microsoft.AspNetCore.Identity;
using Portfolio.Data;

namespace Portfolio.Web.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";
    public const string CookieName = "portfolio_auth";
    public const int MinPasswordLength = 8;
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(90);

    // false는 가짜 시세(개발용 시드 데이터)일 때만 허용한다
    public bool Enabled { get; set; } = true;
}

// 개인 사용 단계의 간소화된 로그인: 비밀번호 1개 (설계서 7.3).
// 비밀번호는 해시(PBKDF2)로만 저장하고, 설정은 이 PC에서 실행하는 set-password 명령으로만 한다.
public sealed class PasswordService(SettingsRepository settings)
{
    private static readonly PasswordHasher<string> Hasher = new();
    private const string User = "owner";

    public async Task<bool> IsConfiguredAsync(CancellationToken ct = default) =>
        !string.IsNullOrEmpty(await settings.GetPasswordHashAsync(ct));

    public async Task SetAsync(string password, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(password) || password.Length < AuthOptions.MinPasswordLength)
            throw new ArgumentException($"비밀번호는 {AuthOptions.MinPasswordLength}자 이상이어야 합니다.");
        await settings.SetPasswordHashAsync(Hasher.HashPassword(User, password), ct);
    }

    public async Task<bool> VerifyAsync(string? password, CancellationToken ct = default)
    {
        string? hash = await settings.GetPasswordHashAsync(ct);
        if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(password)) return false;
        return Hasher.VerifyHashedPassword(User, hash, password) != PasswordVerificationResult.Failed;
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
}
