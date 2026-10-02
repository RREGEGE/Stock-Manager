using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Portfolio.Core;
using Portfolio.Data;

namespace Portfolio.Tests;

public class DbAccessTokenStoreTests
{
    [Fact]
    public async Task 토큰은_암호화되어_저장되고_다시_읽으면_원래_값이다()
    {
        using var db = new TestDb();
        var store = new DbAccessTokenStore(db, new EphemeralDataProtectionProvider());
        var expires = new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.FromHours(9));

        await store.SaveAsync(new AccessToken("plain-token-value", expires));

        var raw = await db.Context.ApiTokens.AsNoTracking().SingleAsync();
        Assert.DoesNotContain("plain-token-value", raw.AccessToken);
        Assert.Equal(new AccessToken("plain-token-value", expires), await store.LoadAsync());
    }

    [Fact]
    public async Task 다시_저장하면_행이_1개로_유지된다()
    {
        using var db = new TestDb();
        var store = new DbAccessTokenStore(db, new EphemeralDataProtectionProvider());

        await store.SaveAsync(new AccessToken("a", DateTimeOffset.UtcNow));
        await store.SaveAsync(new AccessToken("b", DateTimeOffset.UtcNow.AddHours(1)));

        Assert.Equal(1, await db.Context.ApiTokens.CountAsync());
        Assert.Equal("b", (await store.LoadAsync())!.Value);
    }

    [Fact]
    public async Task 다른_키로는_복호화되지_않아_null을_돌려준다()
    {
        using var db = new TestDb();
        await new DbAccessTokenStore(db, new EphemeralDataProtectionProvider())
            .SaveAsync(new AccessToken("a", DateTimeOffset.UtcNow));

        var otherKeyStore = new DbAccessTokenStore(db, new EphemeralDataProtectionProvider());

        Assert.Null(await otherKeyStore.LoadAsync());
    }
}
