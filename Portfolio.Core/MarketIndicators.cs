using System.Collections.Concurrent;

namespace Portfolio.Core;

public enum IndicatorKind { DomesticIndex, OverseasIndex, ExchangeRate }

// 대시보드 맨 위에 보여 주는 시장 지표 정의 (설계서 F-09)
public sealed record IndicatorSpec(string Key, string Name, IndicatorKind Kind, string Code);

// ChangeRate: 전일 대비율, 0.0047 = +0.47%
public sealed record MarketIndicator(string Key, decimal Value, decimal Change, decimal ChangeRate, DateTimeOffset FetchedAt);

public static class MarketIndicators
{
    // 종목코드는 KIS 코드 파일로 확인했다 (국내 업종코드, 해외지수 코드 파일 frgn_code.mst, 2026-10-05)
    public static readonly IReadOnlyList<IndicatorSpec> All =
    [
        new("KOSPI", "코스피", IndicatorKind.DomesticIndex, "0001"),
        new("KOSDAQ", "코스닥", IndicatorKind.DomesticIndex, "1001"),
        new("SPX", "S&P 500", IndicatorKind.OverseasIndex, "SPX"),
        new("NASDAQ", "나스닥", IndicatorKind.OverseasIndex, "COMP"),
        new("USDKRW", "원/달러 환율", IndicatorKind.ExchangeRate, "FX@KRW"),
    ];

    public static IEnumerable<IndicatorSpec> Domestic => All.Where(s => s.Kind == IndicatorKind.DomesticIndex);
    public static IEnumerable<IndicatorSpec> Overseas => All.Where(s => s.Kind != IndicatorKind.DomesticIndex);
}

// 지표 출처. 받아오지 못하면 null을 돌려주거나 예외를 던진다 (호출하는 쪽이 직전 값을 유지).
public interface IMarketIndicatorProvider
{
    Task<MarketIndicator?> GetAsync(IndicatorSpec spec, CancellationToken ct = default);
}

// 마지막으로 받은 지표 값 (메모리). 조회에 실패하면 직전 값을 그대로 둔다.
public sealed class MarketIndicatorStore
{
    private readonly ConcurrentDictionary<string, MarketIndicator> _values = new();

    public MarketIndicator? Get(string key) => _values.GetValueOrDefault(key);

    public bool HasAny => !_values.IsEmpty;

    public void Set(MarketIndicator indicator)
    {
        if (indicator.Value > 0)
            _values[indicator.Key] = indicator;
    }
}

// 지표를 조회해 저장소에 반영한다. 한 지표가 실패해도 나머지는 계속 조회한다.
public sealed class MarketIndicatorUpdater(
    IMarketIndicatorProvider provider, MarketIndicatorStore store, PortfolioNotifier? notifier = null,
    Action<IndicatorSpec, Exception>? onError = null)
{
    public async Task<int> RefreshAsync(IEnumerable<IndicatorSpec> specs, CancellationToken ct = default)
    {
        int updated = 0;
        foreach (var spec in specs)
        {
            try
            {
                if (await provider.GetAsync(spec, ct) is { Value: > 0 } indicator)
                {
                    store.Set(indicator);
                    updated++;
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                onError?.Invoke(spec, ex);
            }
        }
        if (updated > 0) notifier?.NotifyChanged();
        return updated;
    }
}

// KIS 없이 고정 값을 돌려주는 가짜 구현 (개발용 화면 확인)
public sealed class FakeMarketIndicatorProvider(TimeProvider? clock = null) : IMarketIndicatorProvider
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    private static readonly Dictionary<string, (decimal Value, decimal Change, decimal Rate)> Values = new()
    {
        ["KOSPI"] = (2_650.12m, 12.34m, 0.0047m),
        ["KOSDAQ"] = (870.45m, -3.21m, -0.0037m),
        ["SPX"] = (5_750.80m, 28.15m, 0.0049m),
        ["NASDAQ"] = (18_920.33m, -45.60m, -0.0024m),
        ["USDKRW"] = (1_385.50m, 2.50m, 0.0018m),
    };

    public Task<MarketIndicator?> GetAsync(IndicatorSpec spec, CancellationToken ct = default) =>
        Task.FromResult<MarketIndicator?>(Values.TryGetValue(spec.Key, out var v)
            ? new MarketIndicator(spec.Key, v.Value, v.Change, v.Rate, _clock.GetUtcNow())
            : null);
}
