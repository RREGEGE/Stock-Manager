using Microsoft.EntityFrameworkCore;

namespace Portfolio.Data;

// 보유 종목은 계좌마다 따로 둔다 (F-11). accountId를 주지 않으면 처음부터 있는 계좌를 쓴다.
public class HoldingRepository(PortfolioDbContext db, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public Task<List<Holding>> GetAllAsync(int accountId = TradingAccount.DefaultId, CancellationToken ct = default) =>
        db.Holdings.Include(h => h.Group).Where(h => h.AccountId == accountId).OrderBy(h => h.SymbolCode).ToListAsync(ct);

    // 설계서 5.4: 이미 등록된 종목을 다시 고르면 신규 추가가 아니라 해당 행 수정 (AC-01)
    public async Task<Holding> SaveAsync(
        string symbolCode, string symbolName, long quantity, decimal avgPrice, int? groupId,
        int accountId = TradingAccount.DefaultId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(symbolCode))
            throw new ArgumentException("종목코드가 비어 있습니다.", nameof(symbolCode));
        if (quantity < 1)
            throw new ArgumentOutOfRangeException(nameof(quantity), "수량은 1 이상이어야 합니다.");
        if (avgPrice <= 0)
            throw new ArgumentOutOfRangeException(nameof(avgPrice), "평균매입단가는 0보다 커야 합니다.");
        // 다른 계좌의 그룹에 넣지 못하게 한다
        if (groupId is { } gid && !await db.AssetGroups.AnyAsync(g => g.Id == gid && g.AccountId == accountId, ct))
            throw new ArgumentException("이 계좌에 없는 그룹입니다.", nameof(groupId));

        var holding = await db.Holdings.FindAsync([accountId, symbolCode], ct);
        if (holding is null)
        {
            holding = new Holding { AccountId = accountId, SymbolCode = symbolCode };
            db.Holdings.Add(holding);
        }
        holding.SymbolName = symbolName;
        holding.Quantity = quantity;
        holding.AvgPrice = avgPrice;
        holding.GroupId = groupId;
        holding.UpdatedAt = _clock.GetUtcNow();

        await db.SaveChangesAsync(ct);
        return holding;
    }

    public async Task<bool> DeleteAsync(string symbolCode, int accountId = TradingAccount.DefaultId, CancellationToken ct = default)
    {
        var holding = await db.Holdings.FindAsync([accountId, symbolCode], ct);
        if (holding is null) return false;
        db.Holdings.Remove(holding);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
