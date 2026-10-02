using System;
using System.Collections.Generic;
using System.Linq;

namespace Portfolio.Core;

// 설계서 5.5 추가매수 배분 계산 (구현 코드 그대로)

public sealed record GroupState(int GroupId, string Name, decimal TargetWeight, decimal CurrentValue); // TargetWeight: 0~1
public sealed record GroupAllocation(int GroupId, string Name, decimal BuyAmount, decimal ValueAfter, decimal WeightAfter);
public sealed record StockOrder(string SymbolCode, string SymbolName, decimal Price, long Shares, decimal Amount);

public static class Rebalancer
{
    // 그룹별 매수금액 (매수만, 방식 A)
    public static IReadOnlyList<GroupAllocation> AllocateBuyOnly(IReadOnlyList<GroupState> groups, decimal newMoney)
    {
        if (newMoney <= 0) throw new ArgumentOutOfRangeException(nameof(newMoney));
        if (Math.Abs(groups.Sum(g => g.TargetWeight) - 1m) > 0.0001m)
            throw new ArgumentException("목표 비중 합계가 100%가 아닙니다.");

        var active = groups.Where(g => g.TargetWeight > 0).ToList();
        decimal level;
        while (true)
        {
            level = (active.Sum(g => g.CurrentValue) + newMoney) / active.Sum(g => g.TargetWeight);
            var over = active.Where(g => g.TargetWeight * level < g.CurrentValue).ToList();
            if (over.Count == 0) break;
            active = active.Except(over).ToList(); // 매수액 합이 N > 0 이므로 active는 비지 않음
        }

        decimal totalAfter = groups.Sum(g => g.CurrentValue) + newMoney;
        return groups.Select(g =>
        {
            decimal buy = active.Contains(g) ? g.TargetWeight * level - g.CurrentValue : 0m;
            decimal after = g.CurrentValue + buy;
            return new GroupAllocation(g.GroupId, g.Name, Math.Round(buy, 0), after, after / totalAfter);
        }).ToList();
    }

    // 그룹 배정 금액 → 종목별 주수
    public static (IReadOnlyList<StockOrder> Orders, decimal Leftover) ToShares(
        IReadOnlyList<HoldingView> groupHoldings, decimal budget)
    {
        var valid = groupHoldings.Where(h => h.CurrentPrice > 0 && !h.IsStale).ToList();
        if (valid.Count == 0 || budget <= 0) return (Array.Empty<StockOrder>(), budget);

        decimal baseSum = valid.Sum(h => h.EvalAmount);
        var target = valid.ToDictionary(h => h.SymbolCode,
            h => baseSum > 0 ? budget * h.EvalAmount / baseSum : budget / valid.Count);
        var shares = valid.ToDictionary(h => h.SymbolCode,
            h => (long)Math.Floor(target[h.SymbolCode] / h.CurrentPrice));

        decimal left = budget - valid.Sum(h => shares[h.SymbolCode] * h.CurrentPrice);
        while (true)
        {
            var pick = valid.Where(h => h.CurrentPrice <= left)
                .OrderByDescending(h => target[h.SymbolCode] - shares[h.SymbolCode] * h.CurrentPrice)
                .FirstOrDefault();
            if (pick is null) break;
            shares[pick.SymbolCode]++;
            left -= pick.CurrentPrice;
        }

        var orders = valid.Where(h => shares[h.SymbolCode] > 0)
            .Select(h => new StockOrder(h.SymbolCode, h.SymbolName, h.CurrentPrice,
                shares[h.SymbolCode], shares[h.SymbolCode] * h.CurrentPrice))
            .ToList();
        return (orders, left);
    }
}
