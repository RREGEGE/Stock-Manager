using Microsoft.EntityFrameworkCore;
using Portfolio.Data;

namespace Portfolio.Tests;

public class HoldingRepositoryTests
{
    [Fact]
    public async Task AC01_이미_등록된_종목을_다시_추가하면_기존_행이_수정된다()
    {
        using var db = new TestDb();
        var repo = new HoldingRepository(db.Context);

        await repo.SaveAsync("SEED01", "KOSPI200 ETF", 600, 36_000m, groupId: 1);
        await repo.SaveAsync("SEED01", "KOSPI200 ETF", 650, 36_500m, groupId: 3);

        db.Context.ChangeTracker.Clear();
        var rows = await db.Context.Holdings.ToListAsync();
        var row = Assert.Single(rows);
        Assert.Equal(650, row.Quantity);
        Assert.Equal(36_500m, row.AvgPrice);
        Assert.Equal(3, row.GroupId);
    }

    [Fact]
    public async Task 수정하면_최종_수정일이_갱신된다()
    {
        using var db = new TestDb();
        var clock = new ManualClock(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
        var repo = new HoldingRepository(db.Context, clock);

        await repo.SaveAsync("SEED01", "KOSPI200 ETF", 600, 36_000m, 1);
        clock.Now = clock.Now.AddDays(1);
        var updated = await repo.SaveAsync("SEED01", "KOSPI200 ETF", 700, 36_000m, 1);

        Assert.Equal(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero), updated.UpdatedAt);
    }

    [Theory]
    [InlineData(0, 1000)]     // 수량 1 미만
    [InlineData(10, 0)]       // 단가 0
    [InlineData(10, -1)]      // 단가 음수
    public async Task 수량_1_미만_또는_단가_0_이하는_거부한다(long quantity, decimal avgPrice)
    {
        using var db = new TestDb();
        var repo = new HoldingRepository(db.Context);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => repo.SaveAsync("SEED01", "KOSPI200 ETF", quantity, avgPrice, 1));
        Assert.Empty(await db.Context.Holdings.ToListAsync());
    }

    [Fact]
    public async Task 삭제하면_행이_없어진다()
    {
        using var db = new TestDb();
        var repo = new HoldingRepository(db.Context);
        await repo.SaveAsync("SEED01", "KOSPI200 ETF", 600, 36_000m, 1);

        Assert.True(await repo.DeleteAsync("SEED01"));
        Assert.False(await repo.DeleteAsync("SEED01"));
        Assert.Empty(await db.Context.Holdings.ToListAsync());
    }
}
