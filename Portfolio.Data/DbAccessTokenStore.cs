using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Portfolio.Core;

namespace Portfolio.Data;

// 접근토큰을 ApiToken 테이블에 암호화해 저장한다 (설계서 4.4, 7.2). 행은 1개만 유지한다.
public sealed class DbAccessTokenStore(
    IDbContextFactory<PortfolioDbContext> dbFactory, IDataProtectionProvider protectionProvider) : IAccessTokenStore
{
    private const int RowId = 1;
    private readonly IDataProtector _protector = protectionProvider.CreateProtector("Portfolio.ApiToken.v1");

    public async Task<AccessToken?> LoadAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.ApiTokens.AsNoTracking().SingleOrDefaultAsync(t => t.Id == RowId, ct);
        if (row is null) return null;
        try
        {
            return new AccessToken(_protector.Unprotect(row.AccessToken), row.ExpiresAt);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;   // 키가 바뀌어 복호화할 수 없으면 새로 발급받는다
        }
    }

    public async Task SaveAsync(AccessToken token, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.ApiTokens.SingleOrDefaultAsync(t => t.Id == RowId, ct);
        if (row is null)
        {
            row = new ApiToken { Id = RowId };
            db.ApiTokens.Add(row);
        }
        row.AccessToken = _protector.Protect(token.Value);
        row.ExpiresAt = token.ExpiresAt;
        await db.SaveChangesAsync(ct);
    }
}
