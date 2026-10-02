namespace Portfolio.Core;

// 설계서 5.2 메모리 모델
public sealed record HoldingView(
    string SymbolCode,
    string SymbolName,
    string GroupName,
    long Quantity,
    decimal AvgPrice,
    decimal CurrentPrice,   // 멀티시세 응답의 현재가
    bool IsStale)           // 시세 지연·실패 여부
{
    public decimal PurchaseAmount => Quantity * AvgPrice;
    public decimal EvalAmount     => Quantity * CurrentPrice;
    public decimal ProfitLoss     => EvalAmount - PurchaseAmount;
}

public sealed record PortfolioSnapshot(
    DateTimeOffset PricedAt,
    IReadOnlyList<HoldingView> Holdings,
    decimal Cash);
