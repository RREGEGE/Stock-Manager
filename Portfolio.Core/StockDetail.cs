namespace Portfolio.Core;

// 종목 1개의 시세 정보 (F-15). 보유하지 않은 종목도 조회할 수 있다.
// 값이 없는 항목(ETF의 PER 등)은 null이다.
public sealed record StockDetail(
    string SymbolCode,
    decimal Price,
    decimal Change,            // 전일 대비 (내리면 음수)
    decimal ChangeRate,        // 전일 대비율, 0.0047 = +0.47%
    decimal PrevClose,
    decimal Open,
    decimal High,
    decimal Low,
    long Volume,
    decimal UpperLimit,        // 상한가
    decimal LowerLimit,        // 하한가
    decimal? MarketCap,        // 시가총액 (원)
    decimal? Per,
    decimal? Pbr,
    decimal? Eps,
    decimal? Week52High,
    DateOnly? Week52HighDate,
    decimal? Week52Low,
    DateOnly? Week52LowDate,
    string Market,             // 대표 시장 이름 (예: KOSPI200, ETF)
    string Sector,             // 업종 이름
    DateTimeOffset FetchedAt);

// 종목 정보 출처. 받아오지 못하면 null을 돌려주거나 예외를 던진다.
public interface IStockDetailProvider
{
    Task<StockDetail?> GetAsync(string symbolCode, CancellationToken ct = default);
}

// KIS 없이 값을 만들어 돌려주는 가짜 구현 (개발용 화면 확인). 현재가·전일 종가만 받아 나머지를 그럴듯하게 채운다.
public sealed class FakeStockDetailProvider(
    IReadOnlyDictionary<string, decimal> prices, IReadOnlyDictionary<string, decimal>? prevCloses = null, TimeProvider? clock = null)
    : IStockDetailProvider
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public Task<StockDetail?> GetAsync(string symbolCode, CancellationToken ct = default)
    {
        if (!prices.TryGetValue(symbolCode, out decimal price))
            return Task.FromResult<StockDetail?>(null);

        decimal prev = prevCloses?.GetValueOrDefault(symbolCode, price) ?? price;
        var today = DateOnly.FromDateTime(_clock.GetUtcNow().ToOffset(MarketSchedule.Kst).Date);
        return Task.FromResult<StockDetail?>(new StockDetail(
            symbolCode, price, price - prev, prev > 0 ? (price - prev) / prev : 0m, prev,
            Open: prev, High: Math.Max(price, prev) * 1.01m, Low: Math.Min(price, prev) * 0.99m, Volume: 1_234_567,
            UpperLimit: Math.Round(prev * 1.3m), LowerLimit: Math.Round(prev * 0.7m),
            MarketCap: price * 10_000_000m, Per: 12.34m, Pbr: 1.23m, Eps: Math.Round(price / 12.34m),
            Week52High: Math.Round(price * 1.25m), Week52HighDate: today.AddDays(-90),
            Week52Low: Math.Round(price * 0.7m), Week52LowDate: today.AddDays(-200),
            Market: "가상", Sector: "가상 업종", FetchedAt: _clock.GetUtcNow()));
    }
}
