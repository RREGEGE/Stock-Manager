namespace Portfolio.Kis;

// KIS 응답 실패. 메시지에는 msg_cd·msg1만 담고 토큰·APP SECRET은 넣지 않는다 (설계서 7.2).
public sealed class KisApiException(string message, string? msgCode = null, int? httpStatus = null)
    : Exception(message)
{
    public string? MsgCode { get; } = msgCode;
    public int? HttpStatus { get; } = httpStatus;
}
