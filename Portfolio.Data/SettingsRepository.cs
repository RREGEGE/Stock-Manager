using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace Portfolio.Data;

// 예수금과 설정값 (설계서 5.1 CashBalance·AppSetting, 6장 설정). 예수금은 계좌마다 따로 둔다 (F-11).
public sealed class SettingsRepository(IDbContextFactory<PortfolioDbContext> dbFactory, TimeProvider? clock = null)
{
    public const string IncludeCashKey = "IncludeCash";
    public const string PollingIntervalKey = "PollingIntervalSeconds";
    public const int MinPollingIntervalSeconds = 10;
    public const int MaxPollingIntervalSeconds = 3600;

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<decimal> GetCashAsync(int accountId = TradingAccount.DefaultId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return (await db.CashBalances.AsNoTracking().SingleOrDefaultAsync(c => c.AccountId == accountId, ct))?.Amount ?? 0m;
    }

    public async Task SetCashAsync(decimal amount, int accountId = TradingAccount.DefaultId, CancellationToken ct = default)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), "예수금은 0 이상이어야 합니다.");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.CashBalances.SingleOrDefaultAsync(c => c.AccountId == accountId, ct);
        if (row is null)
        {
            row = new CashBalance { AccountId = accountId };
            db.CashBalances.Add(row);
        }
        row.Amount = amount;
        row.UpdatedAt = _clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> GetIncludeCashAsync(CancellationToken ct = default) =>
        await GetAsync(IncludeCashKey, ct) == "true";

    public Task SetIncludeCashAsync(bool include, CancellationToken ct = default) =>
        SetAsync(IncludeCashKey, include ? "true" : "false", ct);

    // 저장된 값이 없으면 null (호출하는 쪽이 appsettings 기본값을 쓴다)
    public async Task<int?> GetPollingIntervalSecondsAsync(CancellationToken ct = default) =>
        int.TryParse(await GetAsync(PollingIntervalKey, ct), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s)
            ? Math.Clamp(s, MinPollingIntervalSeconds, MaxPollingIntervalSeconds) : null;

    public Task SetPollingIntervalSecondsAsync(int seconds, CancellationToken ct = default)
    {
        if (seconds is < MinPollingIntervalSeconds or > MaxPollingIntervalSeconds)
            throw new ArgumentOutOfRangeException(nameof(seconds),
                $"폴링 주기는 {MinPollingIntervalSeconds}~{MaxPollingIntervalSeconds}초여야 합니다.");
        return SetAsync(PollingIntervalKey, seconds.ToString(CultureInfo.InvariantCulture), ct);
    }

    // 로그인 비밀번호의 해시 (평문은 저장하지 않는다). 설정한 적이 없으면 null.
    public const string PasswordHashKey = "Auth.PasswordHash";

    public Task<string?> GetPasswordHashAsync(CancellationToken ct = default) => GetAsync(PasswordHashKey, ct);

    public Task SetPasswordHashAsync(string hash, CancellationToken ct = default) => SetAsync(PasswordHashKey, hash, ct);

    // 로그인 아이디 (계정은 1개)
    public const string UserNameKey = "Auth.UserName";

    public Task<string?> GetUserNameAsync(CancellationToken ct = default) => GetAsync(UserNameKey, ct);

    public Task SetUserNameAsync(string userName, CancellationToken ct = default) => SetAsync(UserNameKey, userName, ct);

    private async Task<string?> GetAsync(string key, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return (await db.AppSettings.AsNoTracking().SingleOrDefaultAsync(s => s.Key == key, ct))?.Value;
    }

    private async Task SetAsync(string key, string value, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.AppSettings.SingleOrDefaultAsync(s => s.Key == key, ct);
        if (row is null)
        {
            row = new AppSetting { Key = key };
            db.AppSettings.Add(row);
        }
        row.Value = value;
        await db.SaveChangesAsync(ct);
    }
}
