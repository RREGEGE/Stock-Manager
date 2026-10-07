using Microsoft.EntityFrameworkCore;
using Portfolio.Core;
using Portfolio.Data;

namespace Portfolio.Web.Services;

// 시세를 받아올 수 있는 상태인지 (KIS 키가 있거나 개발용 가짜 시세). 화면에서 '왜 시세가 없는지'를 안내하는 데 쓴다.
public sealed record PriceSourceInfo(bool Ready, string? SettingsPath);

// 종목 정보 화면 (F-15): 한 종목을 어느 계좌에 얼마나 들고 있는지
public sealed record StockPosition(TradingAccount Account, long Quantity, decimal AvgPrice);

// 화면 한 번 그리는 데 필요한 데이터 묶음
public sealed record PortfolioState(
    TradingAccount Account,                             // 지금 보고 있는 계좌 (F-11)
    IReadOnlyList<TradingAccount> Accounts,
    PortfolioSnapshot Snapshot,
    PortfolioSummary Summary,
    IReadOnlyList<AssetGroup> Groups,
    IReadOnlyDictionary<string, Holding> HoldingRows,   // 그룹 Id·최종 수정일 조회용
    bool IncludeCash,
    bool MarketOpen,
    DateTimeOffset? PricedAt,
    PriceSourceInfo PriceSource)
{
    // 현재가를 한 번도 받지 못해 매입금액으로 계산한 종목 수
    public int UnpricedCount => Snapshot.Holdings.Count(h => !h.HasPrice);
    // 최근 갱신에 실패해 마지막으로 받은 가격으로 표시하는 종목 수
    public int OutdatedCount => Snapshot.Holdings.Count(h => h.HasPrice && h.IsStale);

    public int UnclassifiedCount => HoldingRows.Values.Count(h => h.GroupId is null);
    public int StaleCount => Snapshot.Holdings.Count(h => h.IsStale);
    public DateTimeOffset? LastEditedAt => HoldingRows.Count > 0 ? HoldingRows.Values.Max(h => h.UpdatedAt) : null;
}

// 화면과 데이터 계층 사이의 얇은 연결. 변경 후에는 열려 있는 다른 화면에도 알린다.
// 보유 종목·그룹·예수금은 계좌마다 따로 있으므로 어느 계좌인지(accountId)를 받는다 (F-11).
public sealed class PortfolioService(
    IDbContextFactory<PortfolioDbContext> dbFactory,
    PriceStore priceStore,
    PriceUpdater priceUpdater,
    GroupRepository groups,
    TradingAccountRepository accounts,
    SettingsRepository settings,
    SymbolMasterRepository symbols,
    PortfolioNotifier notifier,
    TimeProvider clock,
    PriceSourceInfo priceSource,
    IStockDetailProvider stockDetails)
{
    // 고른 계좌가 없어졌으면(삭제 등) 첫 번째 계좌를 보여 준다
    public async Task<PortfolioState> LoadAsync(int accountId, CancellationToken ct = default)
    {
        var accountList = await accounts.GetAllAsync(ct);
        var account = accountList.FirstOrDefault(a => a.Id == accountId) ?? accountList[0];

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var snapshot = await new PortfolioReader(db, priceStore, clock).GetSnapshotAsync(account.Id, ct);
        var rows = await db.Holdings.AsNoTracking().Where(h => h.AccountId == account.Id).ToDictionaryAsync(h => h.SymbolCode, ct);
        var groupList = await groups.GetAllAsync(account.Id, ct);
        bool includeCash = await settings.GetIncludeCashAsync(ct);

        return new PortfolioState(
            account, accountList,
            snapshot,
            PortfolioCalculator.Calculate(snapshot, includeCash),
            groupList, rows, includeCash,
            MarketSchedule.IsOpen(clock.GetUtcNow()),
            priceStore.LatestFetchedAt,
            priceSource);
    }

    public Task<IReadOnlyList<SymbolInfo>> SearchSymbolsAsync(string? query, CancellationToken ct = default) =>
        symbols.SearchAsync(query, 8, ct);

    public PriceEntry? GetPrice(string symbolCode) => priceStore.Get(symbolCode);

    // 종목 정보 (F-15). 화면을 열 때 한 번 조회한다. 없는 종목이거나 받아오지 못하면 null.
    public Task<SymbolInfo?> FindSymbolAsync(string symbolCode, CancellationToken ct = default) =>
        symbols.FindAsync(symbolCode, ct);

    public Task<StockDetail?> GetStockDetailAsync(string symbolCode, CancellationToken ct = default) =>
        stockDetails.GetAsync(symbolCode, ct);

    public bool PriceSourceReady => priceSource.Ready;

    // 이 종목을 들고 있는 계좌들 (계좌 표시 순서대로)
    public async Task<IReadOnlyList<StockPosition>> GetPositionsAsync(string symbolCode, CancellationToken ct = default)
    {
        var accountList = await accounts.GetAllAsync(ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var held = await db.Holdings.AsNoTracking().Where(h => h.SymbolCode == symbolCode).ToListAsync(ct);
        return accountList
            .Join(held, a => a.Id, h => h.AccountId, (a, h) => new StockPosition(a, h.Quantity, h.AvgPrice))
            .ToList();
    }

    // 저장 즉시 해당 종목 현재가를 1회 조회해 반영한다 (설계서 5.4-4)
    public async Task SaveHoldingAsync(
        int accountId, string symbolCode, string symbolName, long quantity, decimal avgPrice, int? groupId, CancellationToken ct = default)
    {
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            await new HoldingRepository(db, clock).SaveAsync(symbolCode, symbolName, quantity, avgPrice, groupId, accountId, ct);
        }
        await priceUpdater.RefreshAsync([symbolCode], ct);   // 끝나면 화면 알림까지 보낸다
    }

    public async Task DeleteHoldingAsync(int accountId, string symbolCode, CancellationToken ct = default)
    {
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            await new HoldingRepository(db, clock).DeleteAsync(symbolCode, accountId, ct);
        }
        notifier.NotifyChanged();
    }

    public async Task SetCashAsync(int accountId, decimal amount, CancellationToken ct = default)
    {
        await settings.SetCashAsync(amount, accountId, ct);
        notifier.NotifyChanged();
    }

    public async Task SetIncludeCashAsync(bool include, CancellationToken ct = default)
    {
        await settings.SetIncludeCashAsync(include, ct);
        notifier.NotifyChanged();
    }

    public async Task<AssetGroup> AddGroupAsync(int accountId, string name, CancellationToken ct = default)
    {
        var group = await groups.AddAsync(name, null, accountId, ct);
        notifier.NotifyChanged();
        return group;
    }

    public async Task UpdateGroupAsync(int id, string name, string color, CancellationToken ct = default)
    {
        await groups.UpdateAsync(id, name, color, ct);
        notifier.NotifyChanged();
    }

    public async Task MoveGroupAsync(int id, int direction, CancellationToken ct = default)
    {
        await groups.MoveAsync(id, direction, ct);
        notifier.NotifyChanged();
    }

    public async Task DeleteGroupAsync(int id, CancellationToken ct = default)
    {
        await groups.DeleteAsync(id, ct);
        notifier.NotifyChanged();
    }

    public async Task SaveTargetsAsync(int accountId, IReadOnlyDictionary<int, decimal> targetWeights, CancellationToken ct = default)
    {
        await groups.SaveTargetsAsync(targetWeights, accountId, ct);
        notifier.NotifyChanged();
    }

    // 계좌 관리 (F-11)
    public async Task<TradingAccount> AddAccountAsync(string name, CancellationToken ct = default)
    {
        var account = await accounts.AddAsync(name, ct);
        notifier.NotifyChanged();
        return account;
    }

    public async Task RenameAccountAsync(int id, string name, CancellationToken ct = default)
    {
        await accounts.RenameAsync(id, name, ct);
        notifier.NotifyChanged();
    }

    public async Task MoveAccountAsync(int id, int direction, CancellationToken ct = default)
    {
        await accounts.MoveAsync(id, direction, ct);
        notifier.NotifyChanged();
    }

    public async Task DeleteAccountAsync(int id, CancellationToken ct = default)
    {
        await accounts.DeleteAsync(id, ct);
        notifier.NotifyChanged();
    }

    public Task<int> CountHoldingsAsync(int accountId, CancellationToken ct = default) =>
        accounts.CountHoldingsAsync(accountId, ct);
}
