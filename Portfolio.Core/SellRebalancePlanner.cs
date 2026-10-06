namespace Portfolio.Core;

// 리밸런싱(매도 포함) 계산 결과 (설계서 F-10, 5.6)
public sealed record GroupAdjustment(
    int GroupId, string Name,
    decimal CurrentValue, decimal CurrentWeight,
    decimal TargetValue, decimal TargetWeight,
    decimal Adjustment,           // 목표 금액 − 현재 금액. 음수면 빼고(매도), 양수면 넣는다(매수)
    decimal Traded,               // 1주 단위로 실제 계산된 매매 금액 (매도는 음수)
    decimal WeightAfter,
    bool CannotTrade,             // 조정이 필요한데 매매할 수 있는 종목이 없음
    bool HasHoldings);

public sealed record TradePlan(int GroupId, string GroupName, bool IsSell, StockOrder Order);

public sealed record SellRebalancePlan(
    IReadOnlyList<GroupAdjustment> Groups,
    IReadOnlyList<TradePlan> Trades,
    decimal SellTotal,
    decimal BuyTotal,
    decimal Leftover,             // 판 돈 중 사지 못하고 남는 금액
    bool TargetsValid,
    bool HasUnclassified)
{
    // 옮길 금액이 없다 = 이미 목표 비중과 같다
    public bool IsBalanced => TargetsValid && Groups.All(g => g.Adjustment == 0);
}

// 새 돈 없이 지금 보유한 금액 안에서 목표 비중을 맞춘다: 목표보다 많은 그룹을 팔아 부족한 그룹을 산다.
// 전제는 추가매수 계산(5.5)과 같다: 기준 자산은 보유종목 평가금액 합(예수금 제외), 미분류는 목표 0%,
// 수수료·세금 미반영, 현재가를 받지 못한 종목은 금액에는 넣되 주수를 계산할 수 없어 매매 대상에서 뺀다.
public static class SellRebalancePlanner
{
    public static SellRebalancePlan Plan(IReadOnlyList<GroupTarget> groups, IReadOnlyList<HoldingView> holdings)
    {
        var names = groups.Select(g => g.Name).ToHashSet();
        var unclassified = holdings.Where(h => !names.Contains(h.GroupName)).ToList();

        var states = groups
            .Select(g => (Target: g, Holdings: holdings.Where(h => h.GroupName == g.Name).ToList()))
            .ToList();
        if (unclassified.Count > 0)
            states.Add((new GroupTarget(RebalancePlanner.UnclassifiedGroupId, PortfolioCalculator.UnclassifiedGroupName, 0m), unclassified));

        decimal Value(List<HoldingView> list) => list.Sum(h => h.EstimatedAmount);
        decimal total = states.Sum(s => Value(s.Holdings));
        bool targetsValid = Math.Abs(states.Sum(s => s.Target.TargetWeight) - 1m) <= 0.0001m;
        decimal Weight(decimal value, decimal sum) => sum > 0 ? value / sum : 0m;

        if (!targetsValid || total <= 0)
        {
            var idle = states.Select(s => new GroupAdjustment(s.Target.GroupId, s.Target.Name,
                Value(s.Holdings), Weight(Value(s.Holdings), total), Value(s.Holdings), s.Target.TargetWeight,
                0m, 0m, Weight(Value(s.Holdings), total), false, s.Holdings.Count > 0)).ToList();
            return new SellRebalancePlan(idle, [], 0m, 0m, 0m, targetsValid, unclassified.Count > 0);
        }

        var adjustments = states
            .Select(s => Math.Round(total * s.Target.TargetWeight - Value(s.Holdings), 0))
            .ToList();

        // 1) 매도: 초과 그룹에서 초과 금액만큼 판다
        var trades = new List<TradePlan>();
        var traded = new decimal[states.Count];
        for (int i = 0; i < states.Count; i++)
        {
            if (adjustments[i] >= 0) continue;
            var orders = SellShares(states[i].Holdings, -adjustments[i]);
            trades.AddRange(orders.Select(o => new TradePlan(states[i].Target.GroupId, states[i].Target.Name, true, o)));
            traded[i] = -orders.Sum(o => o.Amount);
        }
        decimal sellTotal = -traded.Sum();

        // 2) 매수: 실제로 판 금액을 부족 그룹에 부족액 비율로 나눠 산다
        decimal shortage = adjustments.Where(a => a > 0).Sum();
        for (int i = 0; i < states.Count; i++)
        {
            if (adjustments[i] <= 0 || shortage <= 0) continue;
            decimal budget = Math.Floor(Math.Min(sellTotal, shortage) * adjustments[i] / shortage);
            var (orders, _) = Rebalancer.ToShares(states[i].Holdings, budget);
            trades.AddRange(orders.Select(o => new TradePlan(states[i].Target.GroupId, states[i].Target.Name, false, o)));
            traded[i] = orders.Sum(o => o.Amount);
        }
        decimal buyTotal = traded.Where(t => t > 0).Sum();

        decimal totalAfter = total - sellTotal + buyTotal;
        var plans = states.Select((s, i) =>
        {
            decimal current = Value(s.Holdings);
            return new GroupAdjustment(s.Target.GroupId, s.Target.Name,
                current, Weight(current, total), total * s.Target.TargetWeight, s.Target.TargetWeight,
                adjustments[i], traded[i], Weight(current + traded[i], totalAfter),
                adjustments[i] != 0 && !s.Holdings.Any(Tradable), s.Holdings.Count > 0);
        }).ToList();

        return new SellRebalancePlan(plans, trades, sellTotal, buyTotal, sellTotal - buyTotal, true, unclassified.Count > 0);
    }

    private static bool Tradable(HoldingView h) => h.CurrentPrice > 0 && !h.IsStale;

    // 팔 금액 → 종목별 매도 주수. 매수(Rebalancer.ToShares)와 같은 방식이되 보유 수량을 넘지 않는다:
    // 그룹 안 평가금액 비율로 나눠 1주 단위로 내림하고, 모자란 금액은 가장 덜 판 종목을 1주씩 더 판다.
    private static IReadOnlyList<StockOrder> SellShares(IReadOnlyList<HoldingView> groupHoldings, decimal amount)
    {
        var valid = groupHoldings.Where(Tradable).ToList();
        decimal baseSum = valid.Sum(h => h.EvalAmount);
        if (valid.Count == 0 || amount <= 0 || baseSum <= 0) return [];

        amount = Math.Min(amount, baseSum);
        var target = valid.ToDictionary(h => h.SymbolCode, h => amount * h.EvalAmount / baseSum);
        var shares = valid.ToDictionary(h => h.SymbolCode,
            h => Math.Min(h.Quantity, (long)Math.Floor(target[h.SymbolCode] / h.CurrentPrice)));

        decimal left = amount - valid.Sum(h => shares[h.SymbolCode] * h.CurrentPrice);
        while (true)
        {
            var pick = valid.Where(h => h.CurrentPrice <= left && shares[h.SymbolCode] < h.Quantity)
                .OrderByDescending(h => target[h.SymbolCode] - shares[h.SymbolCode] * h.CurrentPrice)
                .FirstOrDefault();
            if (pick is null) break;
            shares[pick.SymbolCode]++;
            left -= pick.CurrentPrice;
        }

        return valid.Where(h => shares[h.SymbolCode] > 0)
            .Select(h => new StockOrder(h.SymbolCode, h.SymbolName, h.CurrentPrice,
                shares[h.SymbolCode], shares[h.SymbolCode] * h.CurrentPrice))
            .ToList();
    }
}
