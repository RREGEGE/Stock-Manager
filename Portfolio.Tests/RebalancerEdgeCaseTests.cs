using Portfolio.Core;

namespace Portfolio.Tests;

// AC-04: 투입 0원, 목표 합계 ≠ 100%, 보유 종목 없는 그룹, 1주 가격 > 배정 금액
public class RebalancerEdgeCaseTests
{
    private static readonly GroupState[] SeedGroups =
    [
        new(1, "주식", 0.5m, 60_000_000m),
        new(2, "채권", 0.3m, 20_000_000m),
        new(3, "배당", 0.2m, 20_000_000m),
    ];

    private static HoldingView H(string code, decimal price, long qty = 10, bool stale = false) =>
        new(code, code, "채권", qty, price, price, stale);

    [Theory]
    [InlineData(0)]
    [InlineData(-1_000_000)]
    public void AC04_투입금액이_0원_이하이면_예외(decimal newMoney)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Rebalancer.AllocateBuyOnly(SeedGroups, newMoney));
    }

    [Theory]
    [InlineData(0.5, 0.3, 0.1)]    // 90%
    [InlineData(0.5, 0.3, 0.3)]    // 110%
    [InlineData(0.0, 0.0, 0.0)]    // 0%
    public void AC04_목표_비중_합계가_100퍼센트가_아니면_예외(decimal stock, decimal bond, decimal dividend)
    {
        var groups = new[]
        {
            SeedGroups[0] with { TargetWeight = stock },
            SeedGroups[1] with { TargetWeight = bond },
            SeedGroups[2] with { TargetWeight = dividend },
        };

        var ex = Assert.Throws<ArgumentException>(() => Rebalancer.AllocateBuyOnly(groups, 10_000_000m));
        Assert.Contains("100%", ex.Message);
    }

    [Fact]
    public void AC04_보유_종목이_없는_그룹은_배정_금액_전체가_잔액으로_남는다()
    {
        // 목표 비중은 있지만 현재 금액 0 → 그룹 배분에서 배정을 받는다
        var groups = new[]
        {
            new GroupState(1, "주식", 0.5m, 50_000_000m),
            new GroupState(2, "채권", 0.5m, 0m),
        };
        var bond = Rebalancer.AllocateBuyOnly(groups, 10_000_000m).Single(a => a.Name == "채권");
        Assert.Equal(10_000_000m, bond.BuyAmount);

        var (orders, leftover) = Rebalancer.ToShares([], bond.BuyAmount);

        Assert.Empty(orders);
        Assert.Equal(10_000_000m, leftover);
    }

    [Fact]
    public void AC04_시세_지연_0원_종목만_있는_그룹도_전액_잔액으로_남는다()
    {
        var (orders, leftover) = Rebalancer.ToShares([H("A", 0m), H("B", 10_000m, stale: true)], 1_000_000m);

        Assert.Empty(orders);
        Assert.Equal(1_000_000m, leftover);
    }

    [Fact]
    public void AC04_1주_가격이_배정_금액보다_크면_매수하지_않고_전액_잔액으로_남는다()
    {
        var (orders, leftover) = Rebalancer.ToShares([H("A", 150_000m), H("B", 120_000m)], 100_000m);

        Assert.Empty(orders);
        Assert.Equal(100_000m, leftover);
    }

    [Fact]
    public void AC04_1주_가격이_큰_종목은_건너뛰고_살_수_있는_종목에_잔액을_쓴다()
    {
        // 평가금액 비례 배정: A 50,000 / B 50,000. A는 1주 80,000이라 못 사고,
        // B는 배정분 5주를 산 뒤 남은 50,000으로 5주를 더 사 총 10주가 된다.
        var (orders, leftover) = Rebalancer.ToShares([H("A", 80_000m, qty: 1), H("B", 10_000m, qty: 8)], 100_000m);

        var order = Assert.Single(orders);
        Assert.Equal("B", order.SymbolCode);
        Assert.Equal(10, order.Shares);
        Assert.Equal(0m, leftover);
    }
}
