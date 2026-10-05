using Portfolio.Core;
using Portfolio.Data;

namespace Portfolio.Tests;

public class RebalancePlannerTests
{
    private static readonly GroupTarget[] Targets =
    [
        new(1, "주식", 0.5m), new(2, "채권", 0.3m), new(3, "배당", 0.2m),
    ];

    private static readonly string[] GroupNames = ["", "주식", "채권", "배당"];

    private static List<HoldingView> SeedHoldings() => SeedData.Holdings
        .Select(h => new HoldingView(h.SymbolCode, h.SymbolName, GroupNames[h.GroupId], h.Quantity, h.AvgPrice, h.CurrentPrice, false))
        .ToList();

    [Fact]
    public void 시드에_1000만원_투입하면_목업_화면의_값과_같다()
    {
        var plan = RebalancePlanner.Plan(Targets, SeedHoldings(), 10_000_000m);

        Assert.True(plan.TargetsValid);
        Assert.False(plan.HasUnclassified);
        Assert.Equal(10_000_000m, plan.Spent);
        Assert.Equal(0m, plan.Leftover);

        Assert.Equal(["60.0%", "20.0%", "20.0%"], plan.Groups.Select(g => DisplayFormat.Percent1(g.CurrentWeight)));
        Assert.Equal([0m, 10_000_000m, 0m], plan.Groups.Select(g => g.BuyAmount));
        Assert.Equal(["54.5%", "27.3%", "18.2%"], plan.Groups.Select(g => DisplayFormat.Percent1(g.WeightAfter)));

        Assert.Equal(
            [("국고채 10년 ETF", "채권", 50L, 5_000_000m), ("미국채 10년 ETF", "채권", 500L, 5_000_000m)],
            plan.Orders.Select(o => (o.Order.SymbolName, o.GroupName, o.Order.Shares, o.Order.Amount)));
    }

    [Fact]
    public void 투입_금액이_0이면_현재_상태만_보여_주고_주문이_없다()
    {
        var plan = RebalancePlanner.Plan(Targets, SeedHoldings(), 0m);

        Assert.Empty(plan.Orders);
        Assert.Equal((0m, 0m), (plan.Spent, plan.Leftover));
        Assert.All(plan.Groups, g => Assert.Equal(g.CurrentWeight, g.WeightAfter));
        Assert.All(plan.Groups, g => Assert.Equal(0m, g.BuyAmount));
    }

    [Fact]
    public void 목표_합계가_100퍼센트가_아니면_계산하지_않고_알린다()
    {
        GroupTarget[] targets = [new(1, "주식", 0.5m), new(2, "채권", 0.3m), new(3, "배당", 0.1m)];

        var plan = RebalancePlanner.Plan(targets, SeedHoldings(), 10_000_000m);

        Assert.False(plan.TargetsValid);
        Assert.Empty(plan.Orders);
        Assert.Equal(10_000_000m, plan.Leftover);
    }

    [Fact]
    public void 미분류_종목은_목표_0퍼센트_그룹으로_합산하고_경고한다()
    {
        var holdings = SeedHoldings();
        holdings.Add(new HoldingView("X", "미분류 종목", PortfolioCalculator.UnclassifiedGroupName, 100, 10_000m, 10_000m, false));

        var plan = RebalancePlanner.Plan(Targets, holdings, 10_000_000m);

        Assert.True(plan.HasUnclassified);
        var unclassified = plan.Groups.Single(g => g.GroupId == RebalancePlanner.UnclassifiedGroupId);
        Assert.Equal((1_000_000m, 0m, 0m), (unclassified.CurrentValue, unclassified.TargetWeight, unclassified.BuyAmount));
        Assert.DoesNotContain(plan.Orders, o => o.Order.SymbolCode == "X");
    }

    [Fact]
    public void 목표는_있는데_종목이_없는_그룹은_종목_없음으로_표시하고_배정_금액이_잔액이_된다()
    {
        GroupTarget[] targets = [new(1, "주식", 0.5m), new(2, "채권", 0.3m), new(3, "배당", 0.1m), new(4, "금", 0.1m)];

        var plan = RebalancePlanner.Plan(targets, SeedHoldings(), 10_000_000m);

        var gold = plan.Groups.Single(g => g.Name == "금");
        Assert.True(gold.HasNoBuyableHoldings);
        Assert.True(gold.BuyAmount > 0);
        Assert.Equal(gold.BuyAmount, gold.Leftover);
        Assert.Equal(plan.NewMoney - plan.Spent, plan.Leftover);
        Assert.True(plan.Leftover >= gold.BuyAmount);
    }

    [Fact]
    public void 시세_지연_종목만_있는_그룹도_종목_없음으로_표시한다()
    {
        var holdings = SeedHoldings()
            .Select(h => h.GroupName == "채권" ? h with { IsStale = true } : h).ToList();

        var plan = RebalancePlanner.Plan(Targets, holdings, 10_000_000m);

        Assert.True(plan.Groups.Single(g => g.Name == "채권").HasNoBuyableHoldings);
        Assert.Empty(plan.Orders);
        Assert.Equal(10_000_000m, plan.Leftover);
    }
}
