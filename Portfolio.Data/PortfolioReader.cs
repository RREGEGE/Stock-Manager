using Microsoft.EntityFrameworkCore;
using Portfolio.Core;

namespace Portfolio.Data;

// DB의 보유종목·예수금과 PriceStore의 현재가를 합쳐 PortfolioSnapshot을 만든다.
public class PortfolioReader(PortfolioDbContext db, PriceStore priceStore, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    // 한 계좌의 보유종목·예수금 (F-11)
    public async Task<PortfolioSnapshot> GetSnapshotAsync(int accountId = TradingAccount.DefaultId, CancellationToken ct = default)
    {
        var holdings = await db.Holdings.AsNoTracking().Include(h => h.Group).Where(h => h.AccountId == accountId).ToListAsync(ct);
        var cash = (await db.CashBalances.AsNoTracking().Where(c => c.AccountId == accountId).ToListAsync(ct))
            .OrderByDescending(c => c.UpdatedAt)
            .FirstOrDefault()?.Amount ?? 0m;

        var entries = holdings.ToDictionary(h => h.SymbolCode, h => priceStore.Get(h.SymbolCode));

        // 시세를 한 번도 받지 못한 종목도 지연으로 표시한다 (현재가 0원)
        var views = holdings.Select(h =>
        {
            var e = entries[h.SymbolCode];
            return new HoldingView(
                h.SymbolCode, h.SymbolName,
                h.Group?.Name ?? PortfolioCalculator.UnclassifiedGroupName,
                h.Quantity, h.AvgPrice,
                e?.Price ?? 0m,
                IsStale: e is null || e.IsStale || e.Price <= 0);
        }).ToList();

        var fetched = entries.Values.Where(e => e is { IsStale: false }).Select(e => e!.FetchedAt).ToList();
        var pricedAt = fetched.Count > 0 ? fetched.Max() : _clock.GetUtcNow();
        return new PortfolioSnapshot(pricedAt, views, cash);
    }
}
