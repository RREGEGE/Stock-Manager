using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Portfolio.Core;

namespace Portfolio.Kis;

// KIS 시세 출처 (설계서 4.2 방식 A, 4.3)
// - 보유 종목을 30개 단위로 나누어 멀티시세를 직렬 호출한다.
// - 멀티시세 묶음이 실패하면 그 묶음만 단일 현재가로 종목별 대체 조회한다.
// - 끝내 조회하지 못한 종목은 결과에서 빠지며, 호출하는 쪽이 '시세 지연'으로 처리한다.
public sealed class KisPriceProvider(KisClient client, ILogger<KisPriceProvider>? logger = null) : IPriceProvider
{
    private readonly ILogger _logger = logger ?? NullLogger<KisPriceProvider>.Instance;

    public async Task<IReadOnlyDictionary<string, PriceQuote>> GetPricesAsync(
        IReadOnlyCollection<string> symbolCodes, CancellationToken ct = default)
    {
        var result = new Dictionary<string, PriceQuote>();
        foreach (var chunk in symbolCodes.Distinct().Chunk(KisClient.MaxMultiPriceSymbols))
        {
            try
            {
                foreach (var quote in await client.GetMultiPriceAsync(chunk, ct))
                    result[quote.SymbolCode] = quote;
            }
            catch (Exception ex) when ((ex is KisApiException or HttpRequestException or TaskCanceledException)
                                       && !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "멀티시세 조회 실패, 단일 현재가로 대체합니다. ({Count}종목)", chunk.Length);
                await FallbackAsync(chunk, result, ct);
            }
        }
        return result;
    }

    private async Task FallbackAsync(IEnumerable<string> codes, Dictionary<string, PriceQuote> result, CancellationToken ct)
    {
        foreach (var code in codes)
        {
            try
            {
                result[code] = await client.GetPriceAsync(code, ct);
            }
            catch (Exception ex) when ((ex is KisApiException or HttpRequestException or TaskCanceledException)
                                       && !ct.IsCancellationRequested)
            {
                _logger.LogWarning("단일 현재가 조회 실패: {SymbolCode} ({Message})", code, ex.Message);
            }
        }
    }
}
