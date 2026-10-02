namespace Portfolio.Core;

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

// 시세 API 접근토큰 저장소 (설계서 4.4: DB에 저장하고 재시작 시 재사용)
public interface IAccessTokenStore
{
    Task<AccessToken?> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(AccessToken token, CancellationToken ct = default);
}
