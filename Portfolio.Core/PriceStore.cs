using System.Collections.Concurrent;

namespace Portfolio.Core;

public sealed record PriceEntry(decimal Price, decimal PrevClose, DateTimeOffset FetchedAt, bool IsStale);

// 현재가 메모리 캐시 (설계서 5장: 현재가만 메모리로 관리, 마지막 유효가는 PriceCache에 저장)
// 시세 조회 실패·0원 수신 시 직전 가격을 유지하고 '시세 지연'으로 표시한다 (설계서 4.5, AC-07).
public sealed class PriceStore
{
    private readonly ConcurrentDictionary<string, PriceEntry> _entries = new();

    public PriceEntry? Get(string symbolCode) => _entries.GetValueOrDefault(symbolCode);

    // 마지막으로 유효가를 받은 시각 (상단 바의 '시세 갱신' 표시용). 받은 적이 없으면 null.
    public DateTimeOffset? LatestFetchedAt
    {
        get
        {
            var times = _entries.Values.Where(e => e.Price > 0).Select(e => e.FetchedAt).ToList();
            return times.Count > 0 ? times.Max() : null;
        }
    }

    // 재시작 시 PriceCache의 마지막 유효가를 불러온다. 이미 있는 값은 덮지 않는다.
    public void Restore(IEnumerable<PriceQuote> cached)
    {
        foreach (var q in cached.Where(q => q.Price > 0))
            _entries.TryAdd(q.SymbolCode, new PriceEntry(q.Price, q.PrevClose, q.FetchedAt, IsStale: false));
    }

    // 조회 결과를 반영하고, 새로 받은 유효가 목록을 돌려준다 (PriceCache 저장용).
    public IReadOnlyList<PriceQuote> Apply(IEnumerable<string> requested, IReadOnlyDictionary<string, PriceQuote> quotes)
    {
        var updated = new List<PriceQuote>();
        foreach (var code in requested.Distinct())
        {
            if (quotes.TryGetValue(code, out var q) && q.Price > 0)
            {
                _entries[code] = new PriceEntry(q.Price, q.PrevClose, q.FetchedAt, IsStale: false);
                updated.Add(q);
            }
            else
            {
                // 직전 가격이 있으면 유지, 없으면 0원으로 두되 둘 다 지연 표시
                _entries.AddOrUpdate(code,
                    _ => new PriceEntry(0m, 0m, DateTimeOffset.MinValue, IsStale: true),
                    (_, prev) => prev with { IsStale = true });
            }
        }
        return updated;
    }
}
