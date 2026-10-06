using Microsoft.EntityFrameworkCore;

namespace Portfolio.Data;

// 계좌 관리 (F-11): 추가, 이름 변경, 순서 변경, 삭제. 계좌번호는 다루지 않는다.
public sealed class TradingAccountRepository(IDbContextFactory<PortfolioDbContext> dbFactory)
{
    public async Task<List<TradingAccount>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Accounts.AsNoTracking().OrderBy(a => a.SortOrder).ThenBy(a => a.Id).ToListAsync(ct);
    }

    // 새 계좌는 맨 뒤에 추가하고, 처음 계좌와 같은 기본 그룹(주식 50 / 채권 30 / 배당 20)으로 시작한다
    public async Task<TradingAccount> AddAsync(string name, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        name = await ValidateNameAsync(db, name, exceptId: null, ct);
        int nextOrder = (await db.Accounts.MaxAsync(a => (int?)a.SortOrder, ct) ?? 0) + 1;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var account = new TradingAccount { Name = name, SortOrder = nextOrder };
        db.Accounts.Add(account);
        await db.SaveChangesAsync(ct);
        db.AssetGroups.AddRange(
            new AssetGroup { AccountId = account.Id, Name = "주식", Color = "#23395B", SortOrder = 1, TargetWeight = 0.5m },
            new AssetGroup { AccountId = account.Id, Name = "채권", Color = "#E08A2E", SortOrder = 2, TargetWeight = 0.3m },
            new AssetGroup { AccountId = account.Id, Name = "배당", Color = "#6BB3A8", SortOrder = 3, TargetWeight = 0.2m });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return account;
    }

    public async Task RenameAsync(int id, string name, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var account = await db.Accounts.FindAsync([id], ct) ?? throw new KeyNotFoundException("계좌를 찾을 수 없습니다.");
        account.Name = await ValidateNameAsync(db, name, id, ct);
        await db.SaveChangesAsync(ct);
    }

    // 표시 순서를 한 칸 위(-1) 또는 아래(+1)로 옮긴다
    public async Task MoveAsync(int id, int direction, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var accounts = await db.Accounts.OrderBy(a => a.SortOrder).ThenBy(a => a.Id).ToListAsync(ct);
        int index = accounts.FindIndex(a => a.Id == id);
        int target = index + Math.Sign(direction);
        if (index < 0 || target < 0 || target >= accounts.Count) return;

        (accounts[index], accounts[target]) = (accounts[target], accounts[index]);
        for (int i = 0; i < accounts.Count; i++) accounts[i].SortOrder = i + 1;
        await db.SaveChangesAsync(ct);
    }

    // 계좌와 그 계좌의 보유 종목·그룹·예수금을 함께 지운다. 마지막 남은 계좌는 지울 수 없다.
    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var account = await db.Accounts.FindAsync([id], ct);
        if (account is null) return;
        if (await db.Accounts.CountAsync(ct) <= 1)
            throw new InvalidOperationException("마지막 남은 계좌는 삭제할 수 없습니다.");

        // SQLite 연결 설정과 무관하게 동작하도록 딸린 데이터를 직접 지운다
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Holdings.Where(h => h.AccountId == id).ExecuteDeleteAsync(ct);
        await db.CashBalances.Where(c => c.AccountId == id).ExecuteDeleteAsync(ct);
        await db.AssetGroups.Where(g => g.AccountId == id).ExecuteDeleteAsync(ct);
        db.Accounts.Remove(account);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<int> CountHoldingsAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Holdings.CountAsync(h => h.AccountId == id, ct);
    }

    private static async Task<string> ValidateNameAsync(PortfolioDbContext db, string name, int? exceptId, CancellationToken ct)
    {
        name = (name ?? "").Trim();
        if (name.Length is 0 or > 50)
            throw new ArgumentException("계좌 이름은 1~50자여야 합니다.");
        if (await db.Accounts.AnyAsync(a => a.Name == name && a.Id != exceptId, ct))
            throw new ArgumentException("같은 이름의 계좌가 이미 있습니다.");
        return name;
    }
}
