using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Portfolio.Core;

namespace Portfolio.Data;

// 보유 종목 현재가를 조회해 PriceStore에 반영하고 마지막 유효가를 PriceCache에 저장한다.
public sealed class PriceUpdater(
    IDbContextFactory<PortfolioDbContext> dbFactory,
    IPriceProvider priceProvider,
    PriceStore priceStore,
    ILogger<PriceUpdater>? logger = null)
{
    private readonly ILogger _logger = logger ?? NullLogger<PriceUpdater>.Instance;

    // 시작 시 1회: 재시작·장외 시간에도 마지막 유효가를 표시하도록 캐시를 불러온다.
    public async Task LoadCacheAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var cached = await db.PriceCaches.AsNoTracking().ToListAsync(ct);
        priceStore.Restore(cached.Select(c => new PriceQuote(c.SymbolCode, c.Price, c.PrevClose, c.FetchedAt)));
    }

    public async Task RefreshHoldingsAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var codes = await db.Holdings.AsNoTracking().Select(h => h.SymbolCode).ToListAsync(ct);
        await RefreshAsync(codes, ct);
    }

    // 종목 추가 직후 신규 종목만 즉시 조회할 때도 쓴다 (설계서 4.3-3, 5.4-4).
    public async Task RefreshAsync(IReadOnlyCollection<string> symbolCodes, CancellationToken ct = default)
    {
        if (symbolCodes.Count == 0) return;

        IReadOnlyDictionary<string, PriceQuote> quotes;
        try
        {
            quotes = await priceProvider.GetPricesAsync(symbolCodes, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "시세 조회 실패, 직전 가격을 유지합니다. ({Count}종목)", symbolCodes.Count);
            quotes = new Dictionary<string, PriceQuote>();
        }

        var updated = priceStore.Apply(symbolCodes, quotes);
        int stale = symbolCodes.Distinct().Count() - updated.Count;
        if (stale > 0)
            _logger.LogWarning("시세 지연 종목 {Stale}개 / 전체 {Total}개", stale, symbolCodes.Distinct().Count());

        if (updated.Count > 0)
            await SaveCacheAsync(updated, ct);
    }

    private async Task SaveCacheAsync(IReadOnlyList<PriceQuote> quotes, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var codes = quotes.Select(q => q.SymbolCode).ToList();
        var rows = await db.PriceCaches.Where(p => codes.Contains(p.SymbolCode)).ToDictionaryAsync(p => p.SymbolCode, ct);
        foreach (var q in quotes)
        {
            if (!rows.TryGetValue(q.SymbolCode, out var row))
            {
                row = new PriceCache { SymbolCode = q.SymbolCode };
                db.PriceCaches.Add(row);
            }
            row.Price = q.Price;
            row.PrevClose = q.PrevClose;
            row.FetchedAt = q.FetchedAt;
        }
        await db.SaveChangesAsync(ct);
    }
}
