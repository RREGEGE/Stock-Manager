using Microsoft.EntityFrameworkCore;
using Portfolio.Core;

namespace Portfolio.Data;

// DB의 보유종목·예수금과 IPriceProvider의 현재가를 합쳐 PortfolioSnapshot을 만든다.
public class PortfolioReader(PortfolioDbContext db, IPriceProvider priceProvider, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<PortfolioSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        var holdings = await db.Holdings.AsNoTracking().Include(h => h.Group).ToListAsync(ct);
        var cash = (await db.CashBalances.AsNoTracking().ToListAsync(ct))
            .OrderByDescending(c => c.UpdatedAt)
            .FirstOrDefault()?.Amount ?? 0m;

        var quotes = await priceProvider.GetPricesAsync(holdings.Select(h => h.SymbolCode).ToList(), ct);

        // 시세가 없거나 0원이면 지연으로 표시 (2단계에서 PriceCache의 마지막 유효가로 대체)
        var views = holdings.Select(h =>
        {
            bool ok = quotes.TryGetValue(h.SymbolCode, out var q) && q.Price > 0;
            return new HoldingView(
                h.SymbolCode, h.SymbolName,
                h.Group?.Name ?? PortfolioCalculator.UnclassifiedGroupName,
                h.Quantity, h.AvgPrice,
                ok ? q!.Price : 0m,
                IsStale: !ok);
        }).ToList();

        var pricedAt = quotes.Count > 0 ? quotes.Values.Max(q => q.FetchedAt) : _clock.GetUtcNow();
        return new PortfolioSnapshot(pricedAt, views, cash);
    }
}
