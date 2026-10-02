namespace Portfolio.Core;

public sealed record WeightItem(string Name, decimal Amount, decimal Weight);   // Weight: 0~1, 반올림 전

public sealed record PortfolioSummary(
    decimal TotalAmount,      // Σ평가금액 + 예수금(포함 설정 시)
    decimal EvalAmount,       // Σ평가금액
    decimal PurchaseAmount,   // Σ매입금액
    decimal ProfitLoss,
    decimal ReturnRate,       // 평가손익 ÷ 매입금액, 0~1
    IReadOnlyList<WeightItem> Symbols,
    IReadOnlyList<WeightItem> Groups,
    WeightItem? Cash);        // 예수금 포함 시 '현금' 항목

// 설계서 5.3 비중 계산: W_i = E_i / (ΣE_k + C·δ), W_g = Σ_{i∈g} W_i
public static class PortfolioCalculator
{
    public const string UnclassifiedGroupName = "미분류";
    public const string CashName = "현금";

    public static PortfolioSummary Calculate(PortfolioSnapshot snapshot, bool includeCash)
    {
        var holdings = snapshot.Holdings;
        decimal eval = holdings.Sum(h => h.EvalAmount);
        decimal purchase = holdings.Sum(h => h.PurchaseAmount);
        decimal cash = includeCash ? snapshot.Cash : 0m;
        decimal total = eval + cash;

        decimal Weight(decimal amount) => total > 0 ? amount / total : 0m;

        var symbols = holdings
            .Select(h => new WeightItem(h.SymbolName, h.EvalAmount, Weight(h.EvalAmount)))
            .OrderByDescending(x => x.Amount)
            .ToList();

        var groups = holdings
            .GroupBy(h => string.IsNullOrEmpty(h.GroupName) ? UnclassifiedGroupName : h.GroupName)
            .Select(g =>
            {
                decimal amount = g.Sum(h => h.EvalAmount);
                return new WeightItem(g.Key, amount, Weight(amount));
            })
            .OrderByDescending(x => x.Amount)
            .ToList();

        return new PortfolioSummary(
            total, eval, purchase,
            eval - purchase,
            purchase > 0 ? (eval - purchase) / purchase : 0m,
            symbols, groups,
            includeCash ? new WeightItem(CashName, cash, Weight(cash)) : null);
    }
}
