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
    public const string IndexPricePath = "/uapi/domestic-stock/v1/quotations/inquire-index-price";
    public const string IndexPriceTrId = "FHPUP02100000";
    public const string OverseasChartPath = "/uapi/overseas-price/v1/quotations/inquire-daily-chartprice";
    public const string OverseasChartTrId = "FHKST03030100";
    public const int MaxMultiPriceSymbols = 30;

    // 앱키가 설정되어 있는지. 없으면 호출해도 실패하므로 호출하는 쪽에서 미리 건너뛴다.
    public bool IsConfigured => options.Value.IsConfigured;

    private const string MarketKrx = "J";
    private const string TokenExpiredMsgCode = "EGW00123";   // KIS 공식 예제의 토큰 만료 코드
    public const string RateLimitMsgCode = "EGW00201";       // 초당 거래건수 초과 (실제 응답으로 확인)
    public static readonly TimeSpan RateLimitRetryDelay = TimeSpan.FromSeconds(1);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ILogger _logger = logger ?? NullLogger<KisClient>.Instance;

    // 호출 간격 유지: 시세·지수·환율 조회가 모두 이 문을 차례로 지난다
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long? _lastSent;

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

    // 종목 정보 (F-15): 단일 현재가와 같은 API의 응답에서 시가·고가·저가, 52주 최고·최저, PER 등을 함께 읽는다
    public async Task<StockDetail?> GetStockDetailAsync(string symbolCode, CancellationToken ct = default)
    {
        using var doc = await GetAsync(PricePath, PriceTrId,
        [
            new("FID_COND_MRKT_DIV_CODE", MarketKrx),
            new("FID_INPUT_ISCD", symbolCode),
        ], ct);
        return KisResponseParser.ParseStockDetail(doc.RootElement, symbolCode, _clock.GetUtcNow());
    }

    // 국내 업종 지수 현재가 (코스피 0001, 코스닥 1001)
    public async Task<MarketIndicator?> GetDomesticIndexAsync(string key, string indexCode, CancellationToken ct = default)
    {
        using var doc = await GetAsync(IndexPricePath, IndexPriceTrId,
        [
            new("FID_COND_MRKT_DIV_CODE", "U"),
            new("FID_INPUT_ISCD", indexCode),
        ], ct);
        return KisResponseParser.ParseDomesticIndex(doc.RootElement, key, _clock.GetUtcNow());
    }

    // 해외 지수(시장 구분 N)·환율(X)의 현재 값. 기간별 시세 API의 요약(output1)을 쓴다.
    public async Task<MarketIndicator?> GetOverseasIndicatorAsync(
        string key, string marketDivision, string code, CancellationToken ct = default)
    {
        var today = _clock.GetUtcNow().ToOffset(MarketSchedule.Kst);
        using var doc = await GetAsync(OverseasChartPath, OverseasChartTrId,
        [
            new("FID_COND_MRKT_DIV_CODE", marketDivision),
            new("FID_INPUT_ISCD", code),
            new("FID_INPUT_DATE_1", today.AddDays(-7).ToString("yyyyMMdd", CultureInfo.InvariantCulture)),
            new("FID_INPUT_DATE_2", today.ToString("yyyyMMdd", CultureInfo.InvariantCulture)),
            new("FID_PERIOD_DIV_CODE", "D"),
        ], ct);
        return KisResponseParser.ParseOverseasIndicator(doc.RootElement, key, _clock.GetUtcNow());
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

        if (doc is not null && GetString(doc.RootElement, "msg_cd") == RateLimitMsgCode)
        {
            // 호출 제한에 걸리면 잠시 뒤 1회만 다시 시도한다
            doc.Dispose();
            await Task.Delay(RateLimitRetryDelay, _clock, ct);
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

        await _gate.WaitAsync(ct);
        try
        {
            var minInterval = TimeSpan.FromMilliseconds(opt.MinRequestIntervalMs);
            if (_lastSent is { } last && _clock.GetElapsedTime(last) is var elapsed && elapsed < minInterval)
                await Task.Delay(minInterval - elapsed, _clock, ct);

            using var response = await http.SendAsync(request, ct);
            _lastSent = _clock.GetTimestamp();
            return (response.StatusCode, await KisTokenManager.ReadJsonAsync(response, ct));
        }
        finally
        {
            _gate.Release();
        }
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

    // 종목 정보: 항목 이름은 실제 응답으로 확인했다 (2026-10-07, 삼성전자·KODEX 200).
    // 없는 종목코드는 현재가가 0으로 오므로 null을 돌려준다. ETF는 PER·PBR·EPS가 0으로 와서 '없음'으로 본다.
    public static StockDetail? ParseStockDetail(JsonElement root, string symbolCode, DateTimeOffset fetchedAt)
    {
        if (!root.TryGetProperty("output", out var o) || o.ValueKind != JsonValueKind.Object) return null;
        decimal price = Num(o, "stck_prpr");
        if (price <= 0) return null;

        string sign = Str(o, "prdy_vrss_sign");
        return new StockDetail(
            symbolCode, price,
            Signed(Num(o, "prdy_vrss"), sign),
            Signed(Num(o, "prdy_ctrt"), sign) / 100m,
            PrevClose: Num(o, "stck_sdpr"),
            Open: Num(o, "stck_oprc"), High: Num(o, "stck_hgpr"), Low: Num(o, "stck_lwpr"),
            Volume: (long)Num(o, "acml_vol"),
            UpperLimit: Num(o, "stck_mxpr"), LowerLimit: Num(o, "stck_llam"),
            MarketCap: Positive(Num(o, "hts_avls")) * 100_000_000m,   // 응답 단위는 억 원
            Per: Positive(Num(o, "per")), Pbr: Positive(Num(o, "pbr")), Eps: NonZero(Num(o, "eps")),
            Week52High: Positive(Num(o, "w52_hgpr")), Week52HighDate: Date(o, "w52_hgpr_date"),
            Week52Low: Positive(Num(o, "w52_lwpr")), Week52LowDate: Date(o, "w52_lwpr_date"),
            Market: Str(o, "rprs_mrkt_kor_name").Trim(), Sector: Str(o, "bstp_kor_isnm").Trim(),
            fetchedAt);
    }

    private static decimal? Positive(decimal value) => value > 0 ? value : null;
    private static decimal? NonZero(decimal value) => value != 0 ? value : null;

    private static DateOnly? Date(JsonElement e, string name) =>
        DateOnly.TryParseExact(Str(e, name), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    // 국내 업종 지수: output 객체의 현재가·전일 대비·대비율
    public static MarketIndicator? ParseDomesticIndex(JsonElement root, string key, DateTimeOffset fetchedAt)
    {
        if (!root.TryGetProperty("output", out var o) || o.ValueKind != JsonValueKind.Object) return null;
        decimal value = Num(o, "bstp_nmix_prpr");
        if (value <= 0) return null;
        string sign = Str(o, "prdy_vrss_sign");
        return new MarketIndicator(key, value,
            Signed(Num(o, "bstp_nmix_prdy_vrss"), sign),
            Signed(Num(o, "bstp_nmix_prdy_ctrt"), sign) / 100m, fetchedAt);
    }

    // 해외 지수·환율: output1 객체의 현재가·전일 대비·대비율
    public static MarketIndicator? ParseOverseasIndicator(JsonElement root, string key, DateTimeOffset fetchedAt)
    {
        if (!root.TryGetProperty("output1", out var o) || o.ValueKind != JsonValueKind.Object) return null;
        decimal value = Num(o, "ovrs_nmix_prpr");
        if (value <= 0) return null;
        string sign = Str(o, "prdy_vrss_sign");
        return new MarketIndicator(key, value,
            Signed(Num(o, "ovrs_nmix_prdy_vrss"), sign),
            Signed(Num(o, "prdy_ctrt"), sign) / 100m, fetchedAt);
    }

    // 전일 대비 부호(prdy_vrss_sign): 1 상한, 2 상승, 3 보합, 4 하한, 5 하락.
    // 값에 부호가 붙어 오는 경우와 안 붙어 오는 경우를 모두 처리한다: 부호 코드가 있으면 그것을 따른다.
    private static decimal Signed(decimal value, string signCode) => signCode switch
    {
        "4" or "5" => -Math.Abs(value),
        "1" or "2" => Math.Abs(value),
        "3" => 0m,
        _ => value,
    };

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    // 빈 값·해석 불가 값은 0으로 본다 (0원은 호출하는 쪽에서 '유효하지 않은 가격'으로 처리)
    private static decimal Num(JsonElement e, string name) =>
        decimal.TryParse(Str(e, name), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0m;
}
