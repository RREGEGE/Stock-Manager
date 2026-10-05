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

    // 종목명 일부 또는 종목코드 앞부분으로 검색한다 (설계서 5.4-1). 코드가 정확히 맞는 종목, 이름이 검색어로 시작하는 종목 순.
    public async Task<IReadOnlyList<SymbolInfo>> SearchAsync(string? query, int limit = 8, CancellationToken ct = default)
    {
        query = (query ?? "").Trim();
        if (query.Length == 0) return [];

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        string upper = query.ToUpperInvariant();
        // LIKE는 영문 대소문자를 구분하지 않는다 (kodex → KODEX). 검색어의 %, _는 문자 그대로 찾는다.
        string like = "%" + query.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        var matches = await db.SymbolMasters.AsNoTracking()
            .Where(s => EF.Functions.Like(s.SymbolName, like, "\\") || s.SymbolCode.StartsWith(upper))
            .OrderBy(s => s.SymbolCode)   // 개수를 제한하기 전에 순서를 정해 결과가 매번 같게 한다
            .Take(200)
            .ToListAsync(ct);

        return matches
            .OrderBy(s => s.SymbolCode == upper ? 0 : 1)
            .ThenBy(s => s.SymbolName.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(s => s.SymbolName.Length)
            .ThenBy(s => s.SymbolName, StringComparer.Ordinal)
            .Take(limit)
            .Select(s => new SymbolInfo(s.SymbolCode, s.SymbolName, s.Market))
            .ToList();
    }

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
