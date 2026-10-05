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

    // 현재가를 한 번이라도 받았는지. 받지 못한 종목(0원)은 손익을 계산할 수 없다.
    public bool HasPrice => CurrentPrice > 0;

    // 비중·총액 계산에 쓰는 금액: 현재가가 있으면 평가금액, 없으면 매입금액으로 대신한다.
    // (시세가 없다고 0원으로 치면 총액이 줄고 '전액 손실'처럼 보인다)
    public decimal EstimatedAmount => HasPrice ? EvalAmount : PurchaseAmount;

    public decimal ProfitLoss => HasPrice ? EvalAmount - PurchaseAmount : 0m;
}

public sealed record PortfolioSnapshot(
    DateTimeOffset PricedAt,
    IReadOnlyList<HoldingView> Holdings,
    decimal Cash);
