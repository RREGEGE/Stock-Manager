using Portfolio.Core;

namespace Portfolio.Kis;

// KIS에서 배당 이력을 받아온다 (F-16). 앱키가 없으면 호출하지 않고 빈 목록을 돌려준다.
public sealed class KisDividendProvider(KisClient client) : IDividendProvider
{
    public Task<IReadOnlyList<DividendRecord>> GetAsync(string symbolCode, DateOnly from, DateOnly to, CancellationToken ct = default) =>
        client.IsConfigured ? client.GetDividendsAsync(symbolCode, from, to, ct) : Task.FromResult<IReadOnlyList<DividendRecord>>([]);
}
