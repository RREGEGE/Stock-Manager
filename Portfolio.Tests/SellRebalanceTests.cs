using Portfolio.Core;

namespace Portfolio.Tests;

// 리밸런싱(매도 포함) 계산 (설계서 F-10, 5.6)
public class SellRebalanceTests
{
    private static readonly IReadOnlyList<GroupTarget> Targets =
    [
        new(1, "주식", 0.5m), new(2, "채권", 0.3m), new(3, "배당", 0.2m),
    ];

    private static HoldingView H(string code, string group, long quantity, decimal price, bool stale = false, decimal avgPrice = 0m) =>
        new(code, code, group, quantity, avgPrice == 0m ? price : avgPrice, price, stale);

    // 설계서 10.4 시드와 같은 구성: 주식 6,000만 / 채권 2,000만 / 배당 2,000만
    private static readonly IReadOnlyList<HoldingView> Seed =
    [
        H("S1", "주식", 600, 40_000m), H("S2", "주식", 1_200, 20_000m), H("S3", "주식", 100, 120_000m),
        H("B1", "채권", 100, 100_000m), H("B2", "채권", 1_000, 10_000m),
        H("D1", "배당", 800, 15_000m), H("D2", "배당", 1_600, 5_000m),
    ];

    [Fact]
    public void 초과_그룹을_팔아_부족_그룹을_사는_금액을_계산한다()
    {
        var plan = SellRebalancePlanner.Plan(Targets, Seed);

        Assert.Equal(
            [("주식", 60_000_000m, 50_000_000m, -10_000_000m), ("채권", 20_000_000m, 30_000_000m, 10_000_000m), ("배당", 20_000_000m, 20_000_000m, 0m)],
            plan.Groups.Select(g => (g.Name, g.CurrentValue, g.TargetValue, g.Adjustment)));
        Assert.Equal(0m, plan.Groups.Sum(g => g.Adjustment));   // 뺀 만큼 넣는다
        Assert.False(plan.IsBalanced);
    }

    [Fact]
    public void 종목별_매도와_매수_주수를_1주_단위로_계산한다()
    {
        var plan = SellRebalancePlanner.Plan(Targets, Seed);

        // 주식 1,000만 매도: 평가금액 비율(4:4:2)로 나누고, 1주가 12만 원인 S3의 자투리는 다른 종목으로 채운다
        Assert.Equal(
            [("S1", true, 101L, 4_040_000m), ("S2", true, 202L, 4_040_000m), ("S3", true, 16L, 1_920_000m),
             ("B1", false, 50L, 5_000_000m), ("B2", false, 500L, 5_000_000m)],
            plan.Trades.Select(t => (t.Order.SymbolCode, t.IsSell, t.Order.Shares, t.Order.Amount)));
        Assert.Equal((10_000_000m, 10_000_000m, 0m), (plan.SellTotal, plan.BuyTotal, plan.Leftover));
        Assert.Equal([0.5m, 0.3m, 0.2m], plan.Groups.Select(g => g.WeightAfter));
    }

    [Fact]
    public void 이미_목표_비중이면_옮길_금액이_없다()
    {
        IReadOnlyList<HoldingView> balanced = [H("S1", "주식", 500, 10_000m), H("B1", "채권", 300, 10_000m), H("D1", "배당", 200, 10_000m)];

        var plan = SellRebalancePlanner.Plan(Targets, balanced);

        Assert.True(plan.IsBalanced);
        Assert.Empty(plan.Trades);
        Assert.Equal((0m, 0m, 0m), (plan.SellTotal, plan.BuyTotal, plan.Leftover));
    }

    [Fact]
    public void 목표_합계가_100퍼센트가_아니면_계산하지_않는다()
    {
        var plan = SellRebalancePlanner.Plan([new(1, "주식", 0.5m), new(2, "채권", 0.3m)], Seed);

        Assert.False(plan.TargetsValid);
        Assert.False(plan.IsBalanced);
        Assert.Empty(plan.Trades);
        Assert.All(plan.Groups, g => Assert.Equal(0m, g.Adjustment));
    }

    [Fact]
    public void 미분류는_목표_0퍼센트라_전부_매도_대상이다()
    {
        IReadOnlyList<HoldingView> holdings = [.. Seed, H("X1", "", 100, 100_000m)];   // 미분류 1,000만 → 합계 1억 1,000만

        var plan = SellRebalancePlanner.Plan(Targets, holdings);

        Assert.True(plan.HasUnclassified);
        var unclassified = plan.Groups.Single(g => g.GroupId == RebalancePlanner.UnclassifiedGroupId);
        Assert.Equal((-10_000_000m, -10_000_000m, 0m), (unclassified.Adjustment, unclassified.Traded, unclassified.WeightAfter));
        Assert.Contains(plan.Trades, t => t is { IsSell: true, Order: { SymbolCode: "X1", Shares: 100 } });   // 보유 수량 전부
    }

    [Fact]
    public void 보유_수량보다_많이_팔지_않는다()
    {
        // 채권 목표 0%: 가진 것을 전부 팔되 그 이상은 팔지 않는다
        IReadOnlyList<GroupTarget> targets = [new(1, "주식", 1m), new(2, "채권", 0m)];
        IReadOnlyList<HoldingView> holdings = [H("S1", "주식", 10, 10_000m), H("B1", "채권", 7, 30_000m), H("B2", "채권", 3, 5_000m)];

        var plan = SellRebalancePlanner.Plan(targets, holdings);

        Assert.Equal([("B1", 7L), ("B2", 3L)], plan.Trades.Where(t => t.IsSell).Select(t => (t.Order.SymbolCode, t.Order.Shares)));
        Assert.Equal(225_000m, plan.SellTotal);
        Assert.Equal(220_000m, plan.BuyTotal);     // 1만 원짜리 22주
        Assert.Equal(5_000m, plan.Leftover);       // 1주를 못 사는 자투리
    }

    [Fact]
    public void 현재가_없는_종목은_금액에는_넣되_매매_대상에서_뺀다()
    {
        // 주식 그룹의 현재가가 없다: 매입금액(6,000만)으로 비중은 계산하지만 몇 주를 팔지는 알 수 없다
        IReadOnlyList<HoldingView> holdings =
        [
            H("S1", "주식", 600, 0m, stale: true, avgPrice: 100_000m),
            H("B1", "채권", 200, 100_000m), H("D1", "배당", 200, 100_000m),
        ];

        var plan = SellRebalancePlanner.Plan(Targets, holdings);

        var stock = plan.Groups[0];
        Assert.Equal((-10_000_000m, 0m, true, true), (stock.Adjustment, stock.Traded, stock.CannotTrade, stock.HasHoldings));
        Assert.Empty(plan.Trades);                 // 판 돈이 없으니 사지도 않는다
        Assert.Equal(0m, plan.BuyTotal);
    }

    [Fact]
    public void 살_그룹에_종목이_없으면_판_돈이_남는다()
    {
        IReadOnlyList<HoldingView> holdings = [H("S1", "주식", 1_000, 10_000m)];   // 채권·배당 그룹은 비어 있음

        var plan = SellRebalancePlanner.Plan(Targets, holdings);

        Assert.Equal(5_000_000m, plan.SellTotal);
        Assert.Equal((0m, 5_000_000m), (plan.BuyTotal, plan.Leftover));
        Assert.All(plan.Groups.Skip(1), g => Assert.True(g is { CannotTrade: true, HasHoldings: false }));
    }

    [Fact]
    public void 보유_종목이_없으면_빈_결과를_돌려준다()
    {
        var plan = SellRebalancePlanner.Plan(Targets, []);

        Assert.Empty(plan.Trades);
        Assert.All(plan.Groups, g => Assert.Equal(0m, g.Adjustment));
    }
}
