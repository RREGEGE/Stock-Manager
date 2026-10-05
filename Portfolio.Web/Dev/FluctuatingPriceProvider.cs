using Portfolio.Core;

namespace Portfolio.Web.Dev;

// 개발 전용: 고정 가격을 ±범위 안에서 흔들어 자동 갱신(AC-09)을 눈으로 확인할 수 있게 한다.
// PriceSource=Fake이고 FakePrices:FluctuationPercent > 0일 때만 쓰인다.
public sealed class FluctuatingPriceProvider(IPriceProvider inner, decimal percent) : IPriceProvider
{
    public async Task<IReadOnlyDictionary<string, PriceQuote>> GetPricesAsync(
        IReadOnlyCollection<string> symbolCodes, CancellationToken ct = default)
    {
        var quotes = await inner.GetPricesAsync(symbolCodes, ct);
        return quotes.ToDictionary(kv => kv.Key, kv =>
        {
            decimal factor = 1m + (decimal)(Random.Shared.NextDouble() * 2 - 1) * percent / 100m;
            // 호가 단위 흉내: 5원 단위로 맞춘다
            decimal price = Math.Max(5m, Math.Round(kv.Value.Price * factor / 5m, 0) * 5m);
            return kv.Value with { Price = price };
        });
    }
}
