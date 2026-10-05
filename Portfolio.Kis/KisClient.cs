using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Portfolio.Core;

namespace Portfolio.Kis;

// KIS 국내주식 시세 조회 (설계서 4.1, 4.5). 주문 API 호출 경로는 두지 않는다 (설계서 7.2).
public sealed class KisClient(
    HttpClient http,
    KisTokenManager tokens,
    IOptions<KisOptions> options,
    TimeProvider? clock = null,
    ILogger<KisClient>? logger = null)
{
    public const string MultiPricePath = "/uapi/domestic-stock/v1/quotations/intstock-multprice";
    public const string MultiPriceTrId = "FHKST11300006";
    public const string PricePath = "/uapi/domestic-stock/v1/quotations/inquire-price";
    public const string PriceTrId = "FHKST01010100";
    public const int MaxMultiPriceSymbols = 30;

    private const string MarketKrx = "J";
    private const string TokenExpiredMsgCode = "EGW00123";   // KIS 공식 예제의 토큰 만료 코드

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ILogger _logger = logger ?? NullLogger<KisClient>.Instance;

    // 멀티종목 시세: 1회 최대 30종목
    public async Task<IReadOnlyList<PriceQuote>> GetMultiPriceAsync(
        IReadOnlyList<string> symbolCodes, CancellationToken ct = default)
    {
        if (symbolCodes.Count == 0) return [];
        if (symbolCodes.Count > MaxMultiPriceSymbols)
            throw new ArgumentException($"멀티시세는 1회 최대 {MaxMultiPriceSymbols}종목입니다.", nameof(symbolCodes));

        var query = new List<KeyValuePair<string, string>>();
        for (int i = 0; i < symbolCodes.Count; i++)
        {
            query.Add(new($"FID_COND_MRKT_DIV_CODE_{i + 1}", MarketKrx));
            query.Add(new($"FID_INPUT_ISCD_{i + 1}", symbolCodes[i]));
        }

        using var doc = await GetAsync(MultiPricePath, MultiPriceTrId, query, ct);
        return KisResponseParser.ParseMultiPrice(doc.RootElement, _clock.GetUtcNow());
    }

    // 단일종목 현재가: 종목 추가 시 코드 검증, 멀티시세 실패 시 대체 경로
    public async Task<PriceQuote> GetPriceAsync(string symbolCode, CancellationToken ct = default)
    {
        using var doc = await GetAsync(PricePath, PriceTrId,
        [
            new("FID_COND_MRKT_DIV_CODE", MarketKrx),
            new("FID_INPUT_ISCD", symbolCode),
        ], ct);
        return KisResponseParser.ParsePrice(doc.RootElement, symbolCode, _clock.GetUtcNow());
    }

    private async Task<JsonDocument> GetAsync(
        string path, string trId, IReadOnlyList<KeyValuePair<string, string>> query, CancellationToken ct)
    {
        string token = await tokens.GetTokenAsync(ct);
        var (status, doc) = await SendAsync(path, trId, query, token, ct);

        if (IsAuthError(status, doc))
        {
            // 인증 오류는 1회만 재발급 후 재시도한다 (무한 재시도 금지, 설계서 4.4)
            doc?.Dispose();
            _logger.LogWarning("KIS 인증 오류로 접근토큰을 재발급합니다. ({TrId})", trId);
            token = await tokens.ReissueAsync(token, ct);
            (status, doc) = await SendAsync(path, trId, query, token, ct);
        }

        if (doc is null)
            throw new KisApiException($"KIS 응답을 해석할 수 없습니다. (HTTP {(int)status}, {trId})", httpStatus: (int)status);

        var root = doc.RootElement;
        string rtCd = GetString(root, "rt_cd");
        if (status != HttpStatusCode.OK || rtCd != "0")
        {
            string msgCd = GetString(root, "msg_cd");
            string msg1 = GetString(root, "msg1");
            doc.Dispose();
            _logger.LogWarning("KIS 시세 조회 실패: {TrId} HTTP {Status} rt_cd={RtCd} msg_cd={MsgCd} msg1={Msg1}",
                trId, (int)status, rtCd, msgCd, msg1);
            throw new KisApiException($"KIS 조회 실패: {msgCd} {msg1}", msgCd, (int)status);
        }
        return doc;
    }

    private async Task<(HttpStatusCode Status, JsonDocument? Doc)> SendAsync(
        string path, string trId, IReadOnlyList<KeyValuePair<string, string>> query, string token, CancellationToken ct)
    {
        var opt = options.Value;
        string queryString = string.Join("&",
            query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{path}?{queryString}");
        request.Headers.TryAddWithoutValidation("authorization", $"Bearer {token}");
        request.Headers.TryAddWithoutValidation("appkey", opt.AppKey);
        request.Headers.TryAddWithoutValidation("appsecret", opt.AppSecret);
        request.Headers.TryAddWithoutValidation("tr_id", trId);
        request.Headers.TryAddWithoutValidation("custtype", "P");

        using var response = await http.SendAsync(request, ct);
        return (response.StatusCode, await KisTokenManager.ReadJsonAsync(response, ct));
    }

    private static bool IsAuthError(HttpStatusCode status, JsonDocument? doc) =>
        status == HttpStatusCode.Unauthorized
        || (doc is not null && GetString(doc.RootElement, "msg_cd") == TokenExpiredMsgCode);

    private static string GetString(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? "" : "";
}

// KIS 응답 파싱. 숫자 필드는 문자열로 오므로 InvariantCulture로 변환한다 (설계서 4.5).
public static class KisResponseParser
{
    public static IReadOnlyList<PriceQuote> ParseMultiPrice(JsonElement root, DateTimeOffset fetchedAt)
    {
        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
            return [];

        return output.EnumerateArray()
            .Select(item => new PriceQuote(
                Str(item, "inter_shrn_iscd").Trim(),
                Num(item, "inter2_prpr"),
                Num(item, "inter2_prdy_clpr"),
                fetchedAt))
            .Where(q => q.SymbolCode.Length > 0)
            .ToList();
    }

    public static PriceQuote ParsePrice(JsonElement root, string symbolCode, DateTimeOffset fetchedAt)
    {
        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Object)
            throw new KisApiException($"KIS 현재가 응답에 output이 없습니다. ({symbolCode})");

        // stck_sdpr(기준가)를 전일 종가로 쓴다
        return new PriceQuote(symbolCode, Num(output, "stck_prpr"), Num(output, "stck_sdpr"), fetchedAt);
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    // 빈 값·해석 불가 값은 0으로 본다 (0원은 호출하는 쪽에서 '유효하지 않은 가격'으로 처리)
    private static decimal Num(JsonElement e, string name) =>
        decimal.TryParse(Str(e, name), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0m;
}
