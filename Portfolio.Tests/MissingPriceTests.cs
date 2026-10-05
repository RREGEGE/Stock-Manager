using Portfolio.Core;

namespace Portfolio.Tests;

// 현재가를 받지 못한 종목의 계산 규칙 (설계서 5.3 보완)
// 시세가 없다고 0원으로 치면 총액이 줄고 '전액 손실'처럼 보이므로, 매입금액으로 대신하고 손익에서는 뺀다.
public class MissingPriceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 10, 0, 0, TimeSpan.FromHours(9));

    private static HoldingView Priced(string code, string group, long qty, decimal avg, decimal price) =>
        new(code, code, group, qty, avg, price, false);

    private static HoldingView Unpriced(string code, string group, long qty, decimal avg) =>
        new(code, code, group, qty, avg, 0m, true);

    [Fact]
    public void 현재가가_하나도_없으면_총액은_매입금액이고_손익은_계산하지_않는다()
    {
        var snapshot = new PortfolioSnapshot(Now,
        [
            Unpriced("A", "주식", 100, 70_000m),   // 7,000,000
            Unpriced("B", "주식", 50, 40_000m),    // 2,000,000
        ], 0m);

        var s = PortfolioCalculator.Calculate(snapshot, includeCash: false);

        Assert.Equal(9_000_000m, s.TotalAmount);      // 0원이 아니다
        Assert.Equal(9_000_000m, s.PurchaseAmount);
        Assert.Equal(0m, s.ProfitLoss);               // -9,000,000원(-100%)이 아니다
        Assert.Equal(0m, s.ReturnRate);
        Assert.Equal((0, 2), (s.PricedCount, s.UnpricedCount));
        Assert.Equal(1m, s.Groups.Single(g => g.Name == "주식").Weight);
    }

    [Fact]
    public void 일부만_현재가가_없으면_손익은_현재가가_있는_종목만으로_계산한다()
    {
        var snapshot = new PortfolioSnapshot(Now,
        [
            Priced("A", "주식", 100, 10_000m, 12_000m),   // 매입 1,000,000 → 평가 1,200,000 (+200,000)
            Unpriced("B", "채권", 100, 8_000m),            // 매입 800,000, 현재가 없음
        ], 0m);

        var s = PortfolioCalculator.Calculate(snapshot, includeCash: false);

        Assert.Equal(2_000_000m, s.TotalAmount);          // 1,200,000 + 800,000(매입금액으로 대신)
        Assert.Equal(1_800_000m, s.PurchaseAmount);
        Assert.Equal(200_000m, s.ProfitLoss);             // B의 -800,000을 손실로 치지 않는다
        Assert.Equal(0.2m, s.ReturnRate);                 // 200,000 ÷ 1,000,000 (A의 매입금액)
        Assert.Equal((1, 1), (s.PricedCount, s.UnpricedCount));
        Assert.Equal(0.6m, s.Groups.Single(g => g.Name == "주식").Weight);
        Assert.Equal(0.4m, s.Groups.Single(g => g.Name == "채권").Weight);
    }

    [Fact]
    public void 마지막으로_받은_가격이_있는_지연_종목은_그_가격으로_계산한다()
    {
        var outdated = new HoldingView("A", "A", "주식", 100, 10_000m, 12_000m, IsStale: true);

        var s = PortfolioCalculator.Calculate(new PortfolioSnapshot(Now, [outdated], 0m), includeCash: false);

        Assert.True(outdated.HasPrice);
        Assert.Equal(1_200_000m, s.TotalAmount);
        Assert.Equal(200_000m, s.ProfitLoss);
        Assert.Equal((1, 0), (s.PricedCount, s.UnpricedCount));
    }

    [Fact]
    public void 추가매수_계산은_현재가_없는_그룹을_종목_없음과_구분한다()
    {
        GroupTarget[] targets = [new(1, "주식", 0.5m), new(2, "채권", 0.3m), new(3, "금", 0.2m)];
        List<HoldingView> holdings =
        [
            Priced("A", "주식", 100, 10_000m, 10_000m),   // 1,000,000, 살 수 있음
            Unpriced("B", "채권", 100, 5_000m),            // 500,000(매입가), 현재가 없어 주수 계산 불가
        ];

        var plan = RebalancePlanner.Plan(targets, holdings, 1_000_000m);

        var stock = plan.Groups.Single(g => g.Name == "주식");
        var bond = plan.Groups.Single(g => g.Name == "채권");
        var gold = plan.Groups.Single(g => g.Name == "금");

        Assert.Equal(500_000m, bond.CurrentValue);                         // 0원이 아니라 매입금액
        Assert.True(bond.HasNoBuyableHoldings && bond.HasHoldings);        // 종목은 있는데 시세가 없음
        Assert.True(gold.HasNoBuyableHoldings && !gold.HasHoldings);       // 종목 자체가 없음
        Assert.False(stock.HasNoBuyableHoldings);
        Assert.True(stock.HasHoldings);
        Assert.DoesNotContain(plan.Orders, o => o.Order.SymbolCode == "B");   // 현재가 없는 종목은 주문에 넣지 않는다
        Assert.Equal(plan.NewMoney - plan.Spent, plan.Leftover);
    }

    [Fact]
    public void 투입_금액이_없어도_그룹에_종목이_있는지는_알려_준다()
    {
        GroupTarget[] targets = [new(1, "주식", 0.6m), new(2, "채권", 0.4m)];

        var plan = RebalancePlanner.Plan(targets, [Unpriced("A", "주식", 10, 1_000m)], 0m);

        Assert.True(plan.Groups.Single(g => g.Name == "주식").HasHoldings);
        Assert.False(plan.Groups.Single(g => g.Name == "채권").HasHoldings);
    }
}
