using Portfolio.Core;

namespace Portfolio.Kis;

// KIS에서 지수·환율을 받아온다 (설계서 F-09)
// - 국내 지수: 업종 지수 현재가
// - 해외 지수·환율: 종목_지수_환율 기간별 시세의 요약 값 (시장 구분 N: 해외지수, X: 환율)
// 앱키가 없으면 호출하지 않고 null을 돌려준다.
public sealed class KisMarketIndicatorProvider(KisClient client) : IMarketIndicatorProvider
{
    public Task<MarketIndicator?> GetAsync(IndicatorSpec spec, CancellationToken ct = default)
    {
        if (!client.IsConfigured) return Task.FromResult<MarketIndicator?>(null);

        return spec.Kind switch
        {
            IndicatorKind.DomesticIndex => client.GetDomesticIndexAsync(spec.Key, spec.Code, ct),
            IndicatorKind.OverseasIndex => client.GetOverseasIndicatorAsync(spec.Key, "N", spec.Code, ct),
            IndicatorKind.ExchangeRate => client.GetOverseasIndicatorAsync(spec.Key, "X", spec.Code, ct),
            _ => Task.FromResult<MarketIndicator?>(null),
        };
    }
}
