using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Portfolio.Core;

namespace Portfolio.Data;

// 배당 이력 저장과 갱신 (F-16). 배당은 자주 바뀌지 않으므로 종목마다 하루 한 번만 KIS에 묻는다.
public sealed class DividendRepository(
    IDbContextFactory<PortfolioDbContext> dbFactory,
    IDividendProvider provider,
    TimeProvider? clock = null,
    PortfolioNotifier? notifier = null,
    ILogger<DividendRepository>? logger = null)
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromDays(1);
    // 예상에는 최근 12개월만 쓰지만, 종목 정보 화면의 이력 표시를 위해 조금 더 길게 받는다
    public const int FetchMonths = 15;

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ILogger _logger = logger ?? NullLogger<DividendRepository>.Instance;

    public DateOnly Today => DateOnly.FromDateTime(_clock.GetUtcNow().ToOffset(MarketSchedule.Kst).DateTime);

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<DividendRecord>>> GetAsync(
        IReadOnlyCollection<string> symbolCodes, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Dividends.AsNoTracking().Where(d => symbolCodes.Contains(d.SymbolCode)).ToListAsync(ct);
        return rows.GroupBy(r => r.SymbolCode).ToDictionary(
            g => g.Key,
            g => (IReadOnlyList<DividendRecord>)g.OrderByDescending(r => r.RecordDate)
                .Select(r => new DividendRecord(r.SymbolCode, r.RecordDate, r.PayDate, r.PerShare, r.Kind)).ToList());
    }

    // 모든 계좌의 보유 종목 중, 받은 지 하루가 지났거나 한 번도 받지 않은 종목만 조회한다.
    // 한 종목이 실패해도 나머지는 계속하고, 실패한 종목은 저장된 이력을 그대로 둔다.
    public async Task<int> RefreshDueAsync(CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow();
        List<string> due;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var held = await db.Holdings.AsNoTracking().Select(h => h.SymbolCode).Distinct().ToListAsync(ct);
            var fetched = await db.DividendFetches.AsNoTracking().ToDictionaryAsync(f => f.SymbolCode, f => f.FetchedAt, ct);
            due = held.Where(code => !fetched.TryGetValue(code, out var at) || now - at >= RefreshInterval).ToList();
        }

        int updated = 0;
        foreach (string code in due)
        {
            try
            {
                var records = await provider.GetAsync(code, Today.AddMonths(-FetchMonths), Today, ct);
                await SaveAsync(code, records, now, ct);
                updated++;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning("배당 이력 조회 실패: {SymbolCode} ({Message})", code, ex.Message);
            }
        }
        if (updated > 0) notifier?.NotifyChanged();
        return updated;
    }

    private async Task SaveAsync(string symbolCode, IReadOnlyList<DividendRecord> records, DateTimeOffset fetchedAt, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Dividends.Where(d => d.SymbolCode == symbolCode).ExecuteDeleteAsync(ct);
        db.Dividends.AddRange(records.Select(r => new DividendRow
        {
            SymbolCode = symbolCode, RecordDate = r.RecordDate, PayDate = r.PayDate, PerShare = r.PerShare, Kind = r.Kind,
        }));
        var fetch = await db.DividendFetches.FindAsync([symbolCode], ct);
        if (fetch is null) db.DividendFetches.Add(new DividendFetch { SymbolCode = symbolCode, FetchedAt = fetchedAt });
        else fetch.FetchedAt = fetchedAt;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
