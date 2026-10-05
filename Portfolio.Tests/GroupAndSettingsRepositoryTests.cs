using Microsoft.EntityFrameworkCore;
using Portfolio.Core;
using Portfolio.Data;

namespace Portfolio.Tests;

public class GroupAndSettingsRepositoryTests
{
    [Fact]
    public async Task 초기_그룹은_주식_채권_배당_순서이고_목표는_50_30_20이다()
    {
        using var db = new TestDb();

        var groups = await new GroupRepository(db).GetAllAsync();

        Assert.Equal([("주식", 0.5m), ("채권", 0.3m), ("배당", 0.2m)], groups.Select(g => (g.Name, g.TargetWeight)));
    }

    [Fact]
    public async Task 목표_합계가_100퍼센트가_아니면_저장하지_않는다()
    {
        using var db = new TestDb();
        var repo = new GroupRepository(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            repo.SaveTargetsAsync(new Dictionary<int, decimal> { [1] = 0.5m, [2] = 0.3m, [3] = 0.3m }));

        Assert.Equal([0.5m, 0.3m, 0.2m], (await repo.GetAllAsync()).Select(g => g.TargetWeight));
    }

    [Fact]
    public async Task 목표_합계가_100퍼센트면_저장한다()
    {
        using var db = new TestDb();
        var repo = new GroupRepository(db);

        await repo.SaveTargetsAsync(new Dictionary<int, decimal> { [1] = 0.4m, [2] = 0.35m, [3] = 0.25m });

        Assert.Equal([0.4m, 0.35m, 0.25m], (await repo.GetAllAsync()).Select(g => g.TargetWeight));
    }

    [Fact]
    public async Task 일부_그룹만_또는_음수_목표는_거부한다()
    {
        using var db = new TestDb();
        var repo = new GroupRepository(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            repo.SaveTargetsAsync(new Dictionary<int, decimal> { [1] = 0.5m, [2] = 0.5m }));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            repo.SaveTargetsAsync(new Dictionary<int, decimal> { [1] = 1.2m, [2] = -0.2m, [3] = 0m }));
    }

    [Fact]
    public async Task 그룹을_추가하면_목표_0퍼센트로_맨_뒤에_들어간다()
    {
        using var db = new TestDb();
        var repo = new GroupRepository(db);

        var added = await repo.AddAsync("금", "#c9a227");

        var groups = await repo.GetAllAsync();
        Assert.Equal(["주식", "채권", "배당", "금"], groups.Select(g => g.Name));
        Assert.Equal((0m, "#C9A227"), (added.TargetWeight, added.Color));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("주식")]      // 중복
    [InlineData("미분류")]    // 예약 이름
    public async Task 잘못된_그룹_이름은_거부한다(string name)
    {
        using var db = new TestDb();

        await Assert.ThrowsAsync<ArgumentException>(() => new GroupRepository(db).AddAsync(name));
    }

    [Fact]
    public async Task 이름과_색상을_바꾸고_순서를_옮긴다()
    {
        using var db = new TestDb();
        var repo = new GroupRepository(db);

        await repo.UpdateAsync(3, "배당주", "#112233");
        await repo.MoveAsync(3, -1);
        await repo.MoveAsync(1, -1);   // 이미 맨 위: 변화 없음

        var groups = await repo.GetAllAsync();
        Assert.Equal(["주식", "배당주", "채권"], groups.Select(g => g.Name));
        Assert.Equal("#112233", groups[1].Color);
    }

    [Fact]
    public async Task 그룹을_삭제하면_소속_종목은_미분류가_된다()
    {
        using var db = new TestDb();
        await SeedData.ApplyAsync(db.Context);

        await new GroupRepository(db).DeleteAsync(SeedData.BondGroupId);

        db.Context.ChangeTracker.Clear();
        var snapshot = await new PortfolioReader(db.Context, await db.SeedPriceStoreAsync()).GetSnapshotAsync();
        Assert.Equal(7, snapshot.Holdings.Count);
        Assert.Equal(["국고채 10년 ETF", "미국채 10년 ETF"],
            snapshot.Holdings.Where(h => h.GroupName == PortfolioCalculator.UnclassifiedGroupName).Select(h => h.SymbolName).Order());
        Assert.Equal(2, await db.Context.AssetGroups.CountAsync());
    }

    [Fact]
    public async Task 예수금과_포함_여부를_저장한다()
    {
        using var db = new TestDb();
        var repo = new SettingsRepository(db);

        Assert.Equal(0m, await repo.GetCashAsync());
        Assert.False(await repo.GetIncludeCashAsync());

        await repo.SetCashAsync(3_000_000m);
        await repo.SetCashAsync(2_500_000m);
        await repo.SetIncludeCashAsync(true);

        Assert.Equal(2_500_000m, await repo.GetCashAsync());
        Assert.True(await repo.GetIncludeCashAsync());
        Assert.Equal(1, await db.Context.CashBalances.CountAsync());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repo.SetCashAsync(-1m));
    }

    [Fact]
    public async Task 폴링_주기는_10초에서_3600초_사이만_저장한다()
    {
        using var db = new TestDb();
        var repo = new SettingsRepository(db);

        Assert.Null(await repo.GetPollingIntervalSecondsAsync());

        await repo.SetPollingIntervalSecondsAsync(30);
        Assert.Equal(30, await repo.GetPollingIntervalSecondsAsync());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repo.SetPollingIntervalSecondsAsync(5));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repo.SetPollingIntervalSecondsAsync(7200));
    }

    [Fact]
    public async Task 종목은_이름_일부_또는_코드_앞부분으로_검색한다()
    {
        using var db = new TestDb();
        var repo = new SymbolMasterRepository(db);
        await repo.ReplaceAsync(
        [
            new("069500", "KODEX 200", "KOSPI"),
            new("0219E0", "KODEX 200커버드콜액티브", "KOSPI"),
            new("088260", "이리츠코크렙", "KOSPI"),
            new("0030R0", "대신밸류리츠", "KOSPI"),
            new("005930", "삼성전자", "KOSPI"),
        ], DateTimeOffset.UtcNow);

        Assert.Equal(["KODEX 200", "KODEX 200커버드콜액티브"], (await repo.SearchAsync("kodex")).Select(s => s.SymbolName));
        Assert.Equal(["대신밸류리츠", "이리츠코크렙"], (await repo.SearchAsync("리츠")).Select(s => s.SymbolName));
        Assert.Equal(["삼성전자"], (await repo.SearchAsync("0059")).Select(s => s.SymbolName));
        Assert.Equal(["KODEX 200커버드콜액티브"], (await repo.SearchAsync("0219e0")).Select(s => s.SymbolName));
        Assert.Empty(await repo.SearchAsync("  "));
        Assert.Empty(await repo.SearchAsync("%"));
        Assert.Single(await repo.SearchAsync("KODEX", limit: 1));
    }

    [Fact]
    public async Task 시세_갱신이_끝나면_화면에_알린다()
    {
        using var db = new TestDb();
        await SeedData.ApplyAsync(db.Context);
        var notifier = new PortfolioNotifier();
        int notified = 0;
        notifier.Changed += () => notified++;
        notifier.Changed += () => throw new InvalidOperationException("닫힌 화면");   // 다른 구독자를 막지 않아야 함
        notifier.Changed += () => notified++;

        await new PriceUpdater(db, new FakePriceProvider(SeedData.Prices), new PriceStore(), null, notifier)
            .RefreshHoldingsAsync();

        Assert.Equal(2, notified);
    }
}
