namespace Portfolio.Core;

public sealed record WeightItem(string Name, decimal Amount, decimal Weight);   // Weight: 0~1, 반올림 전

public sealed record PortfolioSummary(
    decimal TotalAmount,      // Σ평가금액 + 예수금(포함 설정 시)
    decimal EvalAmount,       // Σ평가금액 (현재가가 없는 종목은 매입금액으로 대신)
    decimal PurchaseAmount,   // Σ매입금액 (전체)
    decimal ProfitLoss,       // 현재가가 있는 종목만의 평가손익
    decimal ReturnRate,       // 평가손익 ÷ 현재가가 있는 종목의 매입금액, 0~1
    IReadOnlyList<WeightItem> Symbols,
    IReadOnlyList<WeightItem> Groups,
    WeightItem? Cash,         // 예수금 포함 시 '현금' 항목
    int PricedCount = 0,      // 현재가가 있는 종목 수
    int UnpricedCount = 0);   // 현재가를 받지 못해 매입금액으로 계산한 종목 수

// 설계서 5.3 비중 계산: W_i = E_i / (ΣE_k + C·δ), W_g = Σ_{i∈g} W_i
// 현재가를 받지 못한 종목은 E_i 대신 매입금액을 쓰고, 평가손익·수익률 계산에서는 뺀다.
public static class PortfolioCalculator
{
    public const string UnclassifiedGroupName = "미분류";
    public const string CashName = "현금";

    public static PortfolioSummary Calculate(PortfolioSnapshot snapshot, bool includeCash)
    {
        var holdings = snapshot.Holdings;
        decimal eval = holdings.Sum(h => h.EstimatedAmount);
        var priced = holdings.Where(h => h.HasPrice).ToList();
        decimal pricedPurchase = priced.Sum(h => h.PurchaseAmount);
        decimal profitLoss = priced.Sum(h => h.ProfitLoss);
        decimal purchase = holdings.Sum(h => h.PurchaseAmount);
        decimal cash = includeCash ? snapshot.Cash : 0m;
        decimal total = eval + cash;

        decimal Weight(decimal amount) => total > 0 ? amount / total : 0m;

        var symbols = holdings
            .Select(h => new WeightItem(h.SymbolName, h.EstimatedAmount, Weight(h.EstimatedAmount)))
            .OrderByDescending(x => x.Amount)
            .ToList();

        var groups = holdings
            .GroupBy(h => string.IsNullOrEmpty(h.GroupName) ? UnclassifiedGroupName : h.GroupName)
            .Select(g =>
            {
                decimal amount = g.Sum(h => h.EstimatedAmount);
                return new WeightItem(g.Key, amount, Weight(amount));
            })
            .OrderByDescending(x => x.Amount)
            .ToList();

        return new PortfolioSummary(
            total, eval, purchase,
            profitLoss,
            pricedPurchase > 0 ? profitLoss / pricedPurchase : 0m,
            symbols, groups,
            includeCash ? new WeightItem(CashName, cash, Weight(cash)) : null,
            priced.Count, holdings.Count - priced.Count);
    }
}
