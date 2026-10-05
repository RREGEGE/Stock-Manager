using Microsoft.EntityFrameworkCore;
using Portfolio.Core;
using Portfolio.Data;

namespace Portfolio.Web.Services;

// 화면 한 번 그리는 데 필요한 데이터 묶음
public sealed record PortfolioState(
    PortfolioSnapshot Snapshot,
    PortfolioSummary Summary,
    IReadOnlyList<AssetGroup> Groups,
    IReadOnlyDictionary<string, Holding> HoldingRows,   // 그룹 Id·최종 수정일 조회용
    bool IncludeCash,
    bool MarketOpen,
    DateTimeOffset? PricedAt)
{
    public int UnclassifiedCount => HoldingRows.Values.Count(h => h.GroupId is null);
    public int StaleCount => Snapshot.Holdings.Count(h => h.IsStale);
    public DateTimeOffset? LastEditedAt => HoldingRows.Count > 0 ? HoldingRows.Values.Max(h => h.UpdatedAt) : null;
}

// 화면과 데이터 계층 사이의 얇은 연결. 변경 후에는 열려 있는 다른 화면에도 알린다.
public sealed class PortfolioService(
    IDbContextFactory<PortfolioDbContext> dbFactory,
    PriceStore priceStore,
    PriceUpdater priceUpdater,
    GroupRepository groups,
    SettingsRepository settings,
    SymbolMasterRepository symbols,
    PortfolioNotifier notifier,
    TimeProvider clock)
{
    public async Task<PortfolioState> LoadAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var snapshot = await new PortfolioReader(db, priceStore, clock).GetSnapshotAsync(ct);
        var rows = await db.Holdings.AsNoTracking().ToDictionaryAsync(h => h.SymbolCode, ct);
        var groupList = await groups.GetAllAsync(ct);
        bool includeCash = await settings.GetIncludeCashAsync(ct);

        return new PortfolioState(
            snapshot,
            PortfolioCalculator.Calculate(snapshot, includeCash),
            groupList, rows, includeCash,
            MarketSchedule.IsOpen(clock.GetUtcNow()),
            priceStore.LatestFetchedAt);
    }

    public Task<IReadOnlyList<SymbolInfo>> SearchSymbolsAsync(string? query, CancellationToken ct = default) =>
        symbols.SearchAsync(query, 8, ct);

    public PriceEntry? GetPrice(string symbolCode) => priceStore.Get(symbolCode);

    // 저장 즉시 해당 종목 현재가를 1회 조회해 반영한다 (설계서 5.4-4)
    public async Task SaveHoldingAsync(
        string symbolCode, string symbolName, long quantity, decimal avgPrice, int? groupId, CancellationToken ct = default)
    {
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            await new HoldingRepository(db, clock).SaveAsync(symbolCode, symbolName, quantity, avgPrice, groupId, ct);
        }
        await priceUpdater.RefreshAsync([symbolCode], ct);   // 끝나면 화면 알림까지 보낸다
    }

    public async Task DeleteHoldingAsync(string symbolCode, CancellationToken ct = default)
    {
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            await new HoldingRepository(db, clock).DeleteAsync(symbolCode, ct);
        }
        notifier.NotifyChanged();
    }

    public async Task SetCashAsync(decimal amount, CancellationToken ct = default)
    {
        await settings.SetCashAsync(amount, ct);
        notifier.NotifyChanged();
    }

    public async Task SetIncludeCashAsync(bool include, CancellationToken ct = default)
    {
        await settings.SetIncludeCashAsync(include, ct);
        notifier.NotifyChanged();
    }

    public async Task<AssetGroup> AddGroupAsync(string name, CancellationToken ct = default)
    {
        var group = await groups.AddAsync(name, null, ct);
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

    public async Task SaveTargetsAsync(IReadOnlyDictionary<int, decimal> targetWeights, CancellationToken ct = default)
    {
        await groups.SaveTargetsAsync(targetWeights, ct);
        notifier.NotifyChanged();
    }
}
