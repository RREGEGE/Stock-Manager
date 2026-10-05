using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Portfolio.Core;

namespace Portfolio.Kis;

// 접근토큰 관리 (설계서 4.4)
// - 발급 토큰은 저장소(DB)에 두고 재시작 시 재사용한다. 발급은 1분 1회 제한이 있으므로 매 호출마다 발급하지 않는다.
// - 만료 10분 전에 선제 재발급한다.
// - 인증 오류 시 ReissueAsync로 1회만 재발급한다 (재시도 횟수는 호출하는 쪽에서 1회로 제한).
public sealed class KisTokenManager(
    HttpClient http,
    IOptions<KisOptions> options,
    IAccessTokenStore store,
    TimeProvider? clock = null,
    ILogger<KisTokenManager>? logger = null)
{
    public const string TokenPath = "/oauth2/tokenP";
    public static readonly TimeSpan RefreshBefore = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan Kst = TimeSpan.FromHours(9);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ILogger _logger = logger ?? NullLogger<KisTokenManager>.Instance;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private AccessToken? _current;
    private bool _storeLoaded;

    public async Task<string> GetTokenAsync(CancellationToken ct = default)
    {
        if (IsUsable(_current)) return _current!.Value;

        await _lock.WaitAsync(ct);
        try
        {
            if (!_storeLoaded)
            {
                _current = await store.LoadAsync(ct);
                _storeLoaded = true;
            }
            if (!IsUsable(_current))
                _current = await IssueAsync(ct);
            return _current!.Value;
        }
        finally
        {
            _lock.Release();
        }
    }

    // 서버가 거부한 토큰을 버리고 새로 발급한다. 다른 요청이 이미 재발급했다면 그 토큰을 돌려준다.
    public async Task<string> ReissueAsync(string rejectedToken, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_current is not null && _current.Value != rejectedToken && IsUsable(_current))
                return _current.Value;
            _current = await IssueAsync(ct);
            _storeLoaded = true;
            return _current.Value;
        }
        finally
        {
            _lock.Release();
        }
    }

    private bool IsUsable(AccessToken? token) =>
        token is not null && token.ExpiresAt - RefreshBefore > _clock.GetUtcNow();

    private async Task<AccessToken> IssueAsync(CancellationToken ct)
    {
        var opt = options.Value;
        if (!opt.IsConfigured)
            throw new KisApiException("KIS AppKey/AppSecret이 설정되지 않았습니다.");

        using var response = await http.PostAsJsonAsync(TokenPath, new
        {
            grant_type = "client_credentials",
            appkey = opt.AppKey,
            appsecret = opt.AppSecret,
        }, ct);

        using var doc = await ReadJsonAsync(response, ct);
        var root = doc?.RootElement;
        if (!response.IsSuccessStatusCode || root is null
            || !root.Value.TryGetProperty("access_token", out var tokenElement)
            || string.IsNullOrEmpty(tokenElement.GetString()))
        {
            // 오류 설명만 남긴다 (요청 본문의 APP SECRET은 로그에 남기지 않음)
            string? code = root?.TryGetProperty("error_code", out var c) == true ? c.GetString() : null;
            string? desc = root?.TryGetProperty("error_description", out var d) == true ? d.GetString() : null;
            _logger.LogError("KIS 접근토큰 발급 실패: HTTP {Status} {Code} {Description}",
                (int)response.StatusCode, code, desc);
            throw new KisApiException($"KIS 접근토큰 발급 실패 (HTTP {(int)response.StatusCode} {code} {desc})",
                code, (int)response.StatusCode);
        }

        var token = new AccessToken(tokenElement.GetString()!, ParseExpiry(root.Value));
        await store.SaveAsync(token, ct);
        _logger.LogInformation("KIS 접근토큰 발급 완료 (만료 {ExpiresAt})", token.ExpiresAt);
        return token;
    }

    // access_token_token_expired: "yyyy-MM-dd HH:mm:ss" (한국 시간). 없으면 expires_in(초)로 계산한다.
    private DateTimeOffset ParseExpiry(JsonElement root)
    {
        if (root.TryGetProperty("access_token_token_expired", out var e)
            && DateTime.TryParseExact(e.GetString(), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var local))
            return new DateTimeOffset(local, Kst);

        if (root.TryGetProperty("expires_in", out var s) && s.TryGetInt64(out var seconds))
            return _clock.GetUtcNow().AddSeconds(seconds);

        return _clock.GetUtcNow().AddHours(23);   // 응답에 만료 정보가 없을 때의 보수적 기본값 (유효 24시간)
    }

    internal static async Task<JsonDocument?> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
