using Microsoft.EntityFrameworkCore;
using Portfolio.Core;
using Portfolio.Data;

namespace Portfolio.Tests;

public class RebalancerTests
{
    // 시드 데이터(10.4) + DB 초기 그룹(목표 50/30/20%) → 그룹 상태와 종목 목록
    private static async Task<(IReadOnlyList<GroupState> Groups, IReadOnlyList<HoldingView> Holdings)> SeedAsync(TestDb db)
    {
        await SeedData.ApplyAsync(db.Context);
        var snapshot = await new PortfolioReader(db.Context, await db.SeedPriceStoreAsync()).GetSnapshotAsync();
        var groups = await db.Context.AssetGroups.OrderBy(g => g.SortOrder).ToListAsync();
        var states = groups.Select(g => new GroupState(g.Id, g.Name, g.TargetWeight,
            snapshot.Holdings.Where(h => h.GroupName == g.Name).Sum(h => h.EvalAmount))).ToList();
        return (states, snapshot.Holdings);
    }

    [Fact]
    public async Task AC03_시드_데이터에_1000만원_투입하면_채권에만_배정되고_잔액이_0이다()
    {
        using var db = new TestDb();
        var (groups, holdings) = await SeedAsync(db);

        var alloc = Rebalancer.AllocateBuyOnly(groups, 10_000_000m).ToDictionary(a => a.Name);

        Assert.Equal(0m, alloc["주식"].BuyAmount);
        Assert.Equal(10_000_000m, alloc["채권"].BuyAmount);
        Assert.Equal(0m, alloc["배당"].BuyAmount);
        // 설계서 5.5 계산 예시: 매수 후 비중 54.5 / 27.3 / 18.2%
        Assert.Equal(54.5m, Math.Round(alloc["주식"].WeightAfter * 100m, 1));
        Assert.Equal(27.3m, Math.Round(alloc["채권"].WeightAfter * 100m, 1));
        Assert.Equal(18.2m, Math.Round(alloc["배당"].WeightAfter * 100m, 1));

        var bonds = holdings.Where(h => h.GroupName == "채권").ToList();
        var (orders, leftover) = Rebalancer.ToShares(bonds, alloc["채권"].BuyAmount);

        var bySymbol = orders.ToDictionary(o => o.SymbolName);
        Assert.Equal(2, orders.Count);
        Assert.Equal(50, bySymbol["국고채 10년 ETF"].Shares);
        Assert.Equal(5_000_000m, bySymbol["국고채 10년 ETF"].Amount);
        Assert.Equal(500, bySymbol["미국채 10년 ETF"].Shares);
        Assert.Equal(5_000_000m, bySymbol["미국채 10년 ETF"].Amount);
        Assert.Equal(0m, leftover);
    }

    [Fact]
    public void 투입금액이_충분하면_모든_그룹이_목표_비중에_도달한다()
    {
        var groups = new[]
        {
            new GroupState(1, "주식", 0.5m, 60_000_000m),
            new GroupState(2, "채권", 0.3m, 20_000_000m),
            new GroupState(3, "배당", 0.2m, 20_000_000m),
        };

        var alloc = Rebalancer.AllocateBuyOnly(groups, 20_000_000m);

        Assert.Equal([0m, 16_000_000m, 4_000_000m], alloc.Select(a => a.BuyAmount));
        Assert.Equal([0.5m, 0.3m, 0.2m], alloc.Select(a => a.WeightAfter));
    }
}
