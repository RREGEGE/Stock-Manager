using Microsoft.EntityFrameworkCore;
using Portfolio.Core;
using Portfolio.Data;

namespace Portfolio.Tests;

public class PriceUpdaterTests
{
    private static readonly DateTimeOffset T1 = new(2026, 10, 2, 10, 0, 0, TimeSpan.FromHours(9));
    private static readonly DateTimeOffset T2 = T1.AddMinutes(1);

    // 호출마다 다음 응답을 돌려주는 시세 출처. null 응답은 예외(통신 실패)로 본다.
    private sealed class ScriptedPriceProvider(params Dictionary<string, decimal>?[] responses) : IPriceProvider
    {
        private int _call;
        public DateTimeOffset Now { get; set; } = T1;

        public Task<IReadOnlyDictionary<string, PriceQuote>> GetPricesAsync(
            IReadOnlyCollection<string> symbolCodes, CancellationToken ct = default)
        {
            var r = responses[Math.Min(_call++, responses.Length - 1)]
                    ?? throw new HttpRequestException("통신 실패");
            IReadOnlyDictionary<string, PriceQuote> result = r.Where(kv => symbolCodes.Contains(kv.Key))
                .ToDictionary(kv => kv.Key, kv => new PriceQuote(kv.Key, kv.Value, kv.Value, Now));
            return Task.FromResult(result);
        }
    }

    private static async Task<HoldingView> HoldingAsync(TestDb db, PriceStore store, string code)
    {
        db.Context.ChangeTracker.Clear();
        var snapshot = await new PortfolioReader(db.Context, store).GetSnapshotAsync();
        return snapshot.Holdings.Single(h => h.SymbolCode == code);
    }

    private static async Task<TestDb> SeededDbAsync()
    {
        var db = new TestDb();
        await SeedData.ApplyAsync(db.Context);
        return db;
    }

    [Fact]
    public async Task AC07_시세_조회_실패_시_직전_가격을_유지하고_시세_지연으로_표시한다()
    {
        using var db = await SeededDbAsync();
        var store = new PriceStore();
        var provider = new ScriptedPriceProvider(SeedData.Prices.ToDictionary(), null);
        var updater = new PriceUpdater(db, provider, store);

        await updater.RefreshHoldingsAsync();
        var before = await HoldingAsync(db, store, "SEED01");
        Assert.Equal((40_000m, false), (before.CurrentPrice, before.IsStale));

        provider.Now = T2;
        await updater.RefreshHoldingsAsync();   // 통신 실패

        var after = await HoldingAsync(db, store, "SEED01");
        Assert.Equal(40_000m, after.CurrentPrice);
        Assert.True(after.IsStale);
        Assert.Equal(T1, store.Get("SEED01")!.FetchedAt);
    }

    [Fact]
    public async Task AC07_일부_종목만_응답에_없으면_그_종목만_지연으로_표시한다()
    {
        using var db = await SeededDbAsync();
        var store = new PriceStore();
        var partial = SeedData.Prices.Where(kv => kv.Key != "SEED03").ToDictionary();
        var updater = new PriceUpdater(db, new ScriptedPriceProvider(SeedData.Prices.ToDictionary(), partial), store);

        await updater.RefreshHoldingsAsync();
        await updater.RefreshHoldingsAsync();

        db.Context.ChangeTracker.Clear();
        var snapshot = await new PortfolioReader(db.Context, store).GetSnapshotAsync();
        var stale = Assert.Single(snapshot.Holdings, h => h.IsStale);
        Assert.Equal(("SEED03", 120_000m), (stale.SymbolCode, stale.CurrentPrice));
    }

    [Fact]
    public async Task AC07_현재가_0원_수신_시_마지막_유효가를_쓰고_지연으로_표시한다()
    {
        using var db = await SeededDbAsync();
        var store = new PriceStore();
        var zero = SeedData.Prices.ToDictionary();
        zero["SEED04"] = 0m;   // 거래정지 등
        var updater = new PriceUpdater(db, new ScriptedPriceProvider(SeedData.Prices.ToDictionary(), zero), store);

        await updater.RefreshHoldingsAsync();
        await updater.RefreshHoldingsAsync();

        var h = await HoldingAsync(db, store, "SEED04");
        Assert.Equal((100_000m, true), (h.CurrentPrice, h.IsStale));
    }

    [Fact]
    public async Task 지연_후_다시_조회에_성공하면_지연_표시가_풀린다()
    {
        using var db = await SeededDbAsync();
        var store = new PriceStore();
        var updater = new PriceUpdater(db,
            new ScriptedPriceProvider(SeedData.Prices.ToDictionary(), null, SeedData.Prices.ToDictionary()), store);

        await updater.RefreshHoldingsAsync();
        await updater.RefreshHoldingsAsync();
        Assert.True((await HoldingAsync(db, store, "SEED01")).IsStale);

        await updater.RefreshHoldingsAsync();
        Assert.False((await HoldingAsync(db, store, "SEED01")).IsStale);
    }

    [Fact]
    public async Task 시세를_한_번도_받지_못한_종목은_0원_지연이고_추가매수_계산에서_제외된다()
    {
        using var db = await SeededDbAsync();
        var store = new PriceStore();
        await new PriceUpdater(db, new ScriptedPriceProvider((Dictionary<string, decimal>?)null), store).RefreshHoldingsAsync();

        var h = await HoldingAsync(db, store, "SEED05");
        Assert.Equal((0m, true), (h.CurrentPrice, h.IsStale));

        var (orders, leftover) = Rebalancer.ToShares([h], 1_000_000m);
        Assert.Empty(orders);
        Assert.Equal(1_000_000m, leftover);
    }

    [Fact]
    public async Task 유효가는_PriceCache에_저장되고_재시작하면_불러온다()
    {
        using var db = await SeededDbAsync();
        await new PriceUpdater(db, new ScriptedPriceProvider(SeedData.Prices.ToDictionary()), new PriceStore())
            .RefreshHoldingsAsync();
        Assert.Equal(7, await db.Context.PriceCaches.CountAsync());

        // 재시작: 빈 메모리 캐시, 시세 서버 응답 없음
        var restarted = new PriceStore();
        var updater = new PriceUpdater(db, new ScriptedPriceProvider((Dictionary<string, decimal>?)null), restarted);
        await updater.LoadCacheAsync();

        var h = await HoldingAsync(db, restarted, "SEED02");
        Assert.Equal((20_000m, false), (h.CurrentPrice, h.IsStale));
        Assert.Equal(T1, restarted.Get("SEED02")!.FetchedAt);
    }

    [Fact]
    public async Task 실패한_조회는_PriceCache의_마지막_유효가를_덮어쓰지_않는다()
    {
        using var db = await SeededDbAsync();
        var zero = SeedData.Prices.ToDictionary();
        zero["SEED01"] = 0m;
        var provider = new ScriptedPriceProvider(SeedData.Prices.ToDictionary(), zero);
        var updater = new PriceUpdater(db, provider, new PriceStore());

        await updater.RefreshHoldingsAsync();
        provider.Now = T2;
        await updater.RefreshHoldingsAsync();

        var row = await db.Context.PriceCaches.AsNoTracking().SingleAsync(p => p.SymbolCode == "SEED01");
        Assert.Equal((40_000m, T1), (row.Price, row.FetchedAt));
        var other = await db.Context.PriceCaches.AsNoTracking().SingleAsync(p => p.SymbolCode == "SEED02");
        Assert.Equal(T2, other.FetchedAt);
    }
}
