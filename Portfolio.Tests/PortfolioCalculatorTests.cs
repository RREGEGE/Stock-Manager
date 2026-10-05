using Portfolio.Core;
using Portfolio.Data;

namespace Portfolio.Tests;

public class PortfolioCalculatorTests
{
    private static async Task<PortfolioSnapshot> SeedSnapshotAsync(TestDb db, decimal cash = 0m)
    {
        await SeedData.ApplyAsync(db.Context);
        if (cash > 0)
        {
            db.Context.CashBalances.Add(new CashBalance { Amount = cash, UpdatedAt = DateTimeOffset.UtcNow });
            await db.Context.SaveChangesAsync();
        }
        var reader = new PortfolioReader(db.Context, await db.SeedPriceStoreAsync());
        return await reader.GetSnapshotAsync();
    }

    private static decimal Pct1(decimal weight) => Math.Round(weight * 100m, 1, MidpointRounding.AwayFromZero);

    [Fact]
    public async Task AC02_시드_데이터로_총액_손익_그룹비중이_설계서와_같다()
    {
        using var db = new TestDb();
        var snapshot = await SeedSnapshotAsync(db);

        var s = PortfolioCalculator.Calculate(snapshot, includeCash: false);

        Assert.Equal(100_000_000m, s.TotalAmount);
        Assert.Equal(95_500_000m, s.PurchaseAmount);
        Assert.Equal(4_500_000m, s.ProfitLoss);
        Assert.Equal(4.71m, Math.Round(s.ReturnRate * 100m, 2, MidpointRounding.AwayFromZero));

        var groups = s.Groups.ToDictionary(g => g.Name, g => Pct1(g.Weight));
        Assert.Equal(3, groups.Count);
        Assert.Equal(60.0m, groups["주식"]);
        Assert.Equal(20.0m, groups["채권"]);
        Assert.Equal(20.0m, groups["배당"]);
        Assert.All(snapshot.Holdings, h => Assert.False(h.IsStale));
    }

    [Fact]
    public async Task 종목_비중은_총_평가금액_대비_비율이다()
    {
        using var db = new TestDb();
        var s = PortfolioCalculator.Calculate(await SeedSnapshotAsync(db), includeCash: false);

        var symbols = s.Symbols.ToDictionary(x => x.Name, x => x.Weight);
        Assert.Equal(0.24m, symbols["KOSPI200 ETF"]);
        Assert.Equal(0.08m, symbols["리츠 ETF"]);
        Assert.Equal(1m, s.Symbols.Sum(x => x.Weight));
    }

    [Fact]
    public async Task 예수금_포함_시_분모에_더하고_현금_항목을_만든다()
    {
        using var db = new TestDb();
        var snapshot = await SeedSnapshotAsync(db, cash: 25_000_000m);

        var excluded = PortfolioCalculator.Calculate(snapshot, includeCash: false);
        var included = PortfolioCalculator.Calculate(snapshot, includeCash: true);

        Assert.Null(excluded.Cash);
        Assert.Equal(100_000_000m, excluded.TotalAmount);
        Assert.Equal(125_000_000m, included.TotalAmount);
        Assert.Equal(0.2m, included.Cash!.Weight);
        Assert.Equal(0.48m, included.Groups.Single(g => g.Name == "주식").Weight);
    }

    [Fact]
    public void 그룹_미지정_종목은_미분류로_합산한다()
    {
        var snapshot = new PortfolioSnapshot(DateTimeOffset.UtcNow,
        [
            new HoldingView("A", "가", "주식", 10, 1_000m, 1_000m, false),
            new HoldingView("B", "나", PortfolioCalculator.UnclassifiedGroupName, 10, 1_000m, 1_000m, false),
            new HoldingView("C", "다", "", 20, 1_000m, 1_000m, false),
        ], 0m);

        var s = PortfolioCalculator.Calculate(snapshot, includeCash: false);

        Assert.Equal(0.75m, s.Groups.Single(g => g.Name == "미분류").Weight);
    }
}
