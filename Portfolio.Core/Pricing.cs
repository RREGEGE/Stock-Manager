namespace Portfolio.Core;

public sealed record PriceQuote(string SymbolCode, decimal Price, decimal PrevClose, DateTimeOffset FetchedAt);

// 시세 출처 추상화 (설계서 8.2). 결과에 없는 종목은 조회 실패로 본다.
public interface IPriceProvider
{
    Task<IReadOnlyDictionary<string, PriceQuote>> GetPricesAsync(
        IReadOnlyCollection<string> symbolCodes, CancellationToken ct = default);
}

// KIS 없이 고정 가격을 돌려주는 가짜 구현 (설계서 10.4). 전일 종가를 주지 않으면 현재가와 같게 둔다 (등락 0).
public sealed class FakePriceProvider(
    IReadOnlyDictionary<string, decimal> prices, TimeProvider? clock = null, IReadOnlyDictionary<string, decimal>? prevCloses = null)
    : IPriceProvider
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public Task<IReadOnlyDictionary<string, PriceQuote>> GetPricesAsync(
        IReadOnlyCollection<string> symbolCodes, CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow();
        IReadOnlyDictionary<string, PriceQuote> result = symbolCodes
            .Where(prices.ContainsKey)
            .Distinct()
            .ToDictionary(code => code, code => new PriceQuote(code, prices[code], prevCloses?.GetValueOrDefault(code, prices[code]) ?? prices[code], now));
        return Task.FromResult(result);
    }
}
