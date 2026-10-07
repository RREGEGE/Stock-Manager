using Portfolio.Core;

namespace Portfolio.Kis;

// KIS에서 종목 정보를 받아온다 (F-15). 앱키가 없으면 호출하지 않고 null을 돌려준다.
public sealed class KisStockDetailProvider(KisClient client) : IStockDetailProvider
{
    public Task<StockDetail?> GetAsync(string symbolCode, CancellationToken ct = default) =>
        client.IsConfigured ? client.GetStockDetailAsync(symbolCode, ct) : Task.FromResult<StockDetail?>(null);
}
