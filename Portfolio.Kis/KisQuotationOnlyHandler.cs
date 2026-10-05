namespace Portfolio.Kis;

// KIS로 나가는 요청을 토큰 발급과 국내주식 시세 조회로만 제한한다 (설계서 7.2 권한 최소화).
// KIS 키는 주문 권한까지 포함하므로, 코드에 주문 호출이 없더라도 실수·변조로 다른 경로가 호출되는 것을 한 번 더 막는다.
public sealed class KisQuotationOnlyHandler : DelegatingHandler
{
    public const string QuotationPrefix = "/uapi/domestic-stock/v1/quotations/";

    public static bool IsAllowed(HttpMethod method, Uri? uri)
    {
        if (uri is null) return false;
        string path = uri.AbsolutePath;
        if (method == HttpMethod.Post)
            return path == KisTokenManager.TokenPath;
        if (method == HttpMethod.Get)
            return path.StartsWith(QuotationPrefix, StringComparison.Ordinal) && !path.Contains("..");
        return false;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!IsAllowed(request.Method, request.RequestUri))
            throw new KisApiException(
                $"허용되지 않은 KIS 요청을 차단했습니다: {request.Method} {request.RequestUri?.AbsolutePath} (시세 조회·토큰 발급만 허용)");
        return base.SendAsync(request, cancellationToken);
    }
}
