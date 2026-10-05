namespace Portfolio.Core;

public sealed record GroupTarget(int GroupId, string Name, decimal TargetWeight);   // TargetWeight: 0~1

public sealed record GroupPlan(
    int GroupId, string Name,
    decimal CurrentValue, decimal CurrentWeight, decimal TargetWeight,
    decimal BuyAmount, decimal WeightAfter,
    decimal Spent, decimal Leftover,
    bool HasNoBuyableHoldings);   // 배정은 받았지만 살 수 있는 종목이 없음 ('종목 없음')

public sealed record OrderPlan(int GroupId, string GroupName, StockOrder Order);

public sealed record RebalancePlan(
    decimal NewMoney,
    IReadOnlyList<GroupPlan> Groups,
    IReadOnlyList<OrderPlan> Orders,
    decimal Spent,
    decimal Leftover,
    bool TargetsValid,          // 목표 비중 합계가 100%인지
    bool HasUnclassified);      // 미분류 종목이 있는지 (계산 전 경고)

// 추가매수 계산 화면용 조립 (설계서 5.5, 6장). 그룹 배분과 종목별 주수 환산은 Rebalancer가 한다.
// 계산 기준 자산은 보유종목 평가금액 합이며 예수금은 제외한다.
public static class RebalancePlanner
{
    public const int UnclassifiedGroupId = 0;

    public static RebalancePlan Plan(
        IReadOnlyList<GroupTarget> groups, IReadOnlyList<HoldingView> holdings, decimal newMoney)
    {
        var names = groups.Select(g => g.Name).ToHashSet();
        var unclassified = holdings.Where(h => !names.Contains(h.GroupName)).ToList();

        var states = groups
            .Select(g => new GroupState(g.GroupId, g.Name, g.TargetWeight,
                holdings.Where(h => h.GroupName == g.Name).Sum(h => h.EvalAmount)))
            .ToList();
        if (unclassified.Count > 0)   // 미분류는 목표 0%로 고정
            states.Add(new GroupState(UnclassifiedGroupId, PortfolioCalculator.UnclassifiedGroupName, 0m,
                unclassified.Sum(h => h.EvalAmount)));

        decimal total = states.Sum(s => s.CurrentValue);
        bool targetsValid = Math.Abs(states.Sum(s => s.TargetWeight) - 1m) <= 0.0001m;
        decimal Weight(decimal value, decimal sum) => sum > 0 ? value / sum : 0m;

        // 투입 금액이 없거나 목표 합계가 맞지 않으면 현재 상태만 보여 준다
        if (newMoney <= 0 || !targetsValid)
        {
            var idle = states.Select(s => new GroupPlan(s.GroupId, s.Name, s.CurrentValue,
                Weight(s.CurrentValue, total), s.TargetWeight, 0m, Weight(s.CurrentValue, total), 0m, 0m, false)).ToList();
            return new RebalancePlan(Math.Max(0m, newMoney), idle, [], 0m, Math.Max(0m, newMoney),
                targetsValid, unclassified.Count > 0);
        }

        var allocations = Rebalancer.AllocateBuyOnly(states, newMoney);
        var groupPlans = new List<GroupPlan>();
        var orders = new List<OrderPlan>();

        foreach (var (state, alloc) in states.Zip(allocations))
        {
            var groupHoldings = state.GroupId == UnclassifiedGroupId
                ? unclassified
                : holdings.Where(h => h.GroupName == state.Name).ToList();
            var (groupOrders, leftover) = Rebalancer.ToShares(groupHoldings, alloc.BuyAmount);

            orders.AddRange(groupOrders.Select(o => new OrderPlan(state.GroupId, state.Name, o)));
            decimal spent = groupOrders.Sum(o => o.Amount);
            bool noBuyable = alloc.BuyAmount > 0
                && !groupHoldings.Any(h => h.CurrentPrice > 0 && !h.IsStale);

            groupPlans.Add(new GroupPlan(state.GroupId, state.Name, state.CurrentValue,
                Weight(state.CurrentValue, total), state.TargetWeight,
                alloc.BuyAmount, alloc.WeightAfter, spent, leftover, noBuyable));
        }

        decimal totalSpent = orders.Sum(o => o.Order.Amount);
        return new RebalancePlan(newMoney, groupPlans, orders, totalSpent, newMoney - totalSpent,
            true, unclassified.Count > 0);
    }
}
