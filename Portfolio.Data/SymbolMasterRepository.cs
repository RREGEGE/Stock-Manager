using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Portfolio.Core;

namespace Portfolio.Data;

// SymbolMaster 갱신 (설계서 5.4-5: 주 1회)
public sealed class SymbolMasterRepository(IDbContextFactory<PortfolioDbContext> dbFactory)
{
    public const string UpdatedAtKey = "SymbolMaster.UpdatedAt";
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromDays(7);

    public static bool IsRefreshDue(DateTimeOffset? updatedAt, DateTimeOffset now) =>
        updatedAt is null || now - updatedAt.Value >= RefreshInterval;

    public async Task<DateTimeOffset?> GetUpdatedAtAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.AppSettings.AsNoTracking().SingleOrDefaultAsync(s => s.Key == UpdatedAtKey, ct);
        return row is not null && DateTimeOffset.TryParse(row.Value, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var at) ? at : null;
    }

    // 받은 시장(KOSPI/KOSDAQ)의 행만 통째로 바꾼다. 다른 시장 값(예: 시드 SEED)은 건드리지 않는다.
    public async Task ReplaceAsync(IReadOnlyCollection<SymbolInfo> symbols, DateTimeOffset now, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var markets = symbols.Select(s => s.Market).Distinct().ToList();
        await db.SymbolMasters.Where(s => markets.Contains(s.Market)).ExecuteDeleteAsync(ct);

        // 같은 코드가 두 시장에 있으면 먼저 나온 것을 쓴다
        db.SymbolMasters.AddRange(symbols.DistinctBy(s => s.SymbolCode).Select(s => new SymbolMaster
        {
            SymbolCode = s.SymbolCode,
            SymbolName = s.SymbolName,
            Market = s.Market,
        }));

        var setting = await db.AppSettings.SingleOrDefaultAsync(s => s.Key == UpdatedAtKey, ct);
        if (setting is null)
        {
            setting = new AppSetting { Key = UpdatedAtKey };
            db.AppSettings.Add(setting);
        }
        setting.Value = now.ToString("O", CultureInfo.InvariantCulture);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
