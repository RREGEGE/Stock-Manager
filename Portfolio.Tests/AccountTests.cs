using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Portfolio.Core;
using Portfolio.Data;

namespace Portfolio.Tests;

// 계좌 (설계서 F-11): 보유 종목·그룹·목표 비중·예수금을 계좌마다 따로 둔다
public class AccountTests
{
    [Fact]
    public async Task 새_DB는_기본_계좌_하나와_그_계좌의_기본_그룹으로_시작한다()
    {
        using var db = new TestDb();

        var accounts = await new TradingAccountRepository(db).GetAllAsync();
        var groups = await new GroupRepository(db).GetAllAsync(accounts[0].Id);

        Assert.Equal([(TradingAccount.DefaultId, "기본 계좌")], accounts.Select(a => (a.Id, a.Name)));
        Assert.Equal(["주식", "채권", "배당"], groups.Select(g => g.Name));
    }

    [Fact]
    public async Task 계좌를_추가하면_맨_뒤에_생기고_기본_그룹_세_개를_갖는다()
    {
        using var db = new TestDb();
        var repo = new TradingAccountRepository(db);

        var added = await repo.AddAsync("  연금저축  ");

        Assert.Equal(["기본 계좌", "연금저축"], (await repo.GetAllAsync()).Select(a => a.Name));
        var groups = await new GroupRepository(db).GetAllAsync(added.Id);
        Assert.Equal([("주식", 0.5m), ("채권", 0.3m), ("배당", 0.2m)], groups.Select(g => (g.Name, g.TargetWeight)));
        Assert.All(groups, g => Assert.Equal(added.Id, g.AccountId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("기본 계좌")]   // 이미 있는 이름
    public async Task 계좌_이름은_비어_있거나_겹칠_수_없다(string name)
    {
        using var db = new TestDb();
        var repo = new TradingAccountRepository(db);

        await Assert.ThrowsAsync<ArgumentException>(() => repo.AddAsync(name));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.AddAsync(new string('가', 51)));
    }

    [Fact]
    public async Task 계좌_이름을_바꾸고_순서를_옮길_수_있다()
    {
        using var db = new TestDb();
        var repo = new TradingAccountRepository(db);
        var second = await repo.AddAsync("연금저축");

        await repo.RenameAsync(TradingAccount.DefaultId, "ISA");
        await repo.MoveAsync(second.Id, -1);

        Assert.Equal(["연금저축", "ISA"], (await repo.GetAllAsync()).Select(a => a.Name));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.RenameAsync(second.Id, "ISA"));   // 다른 계좌와 같은 이름
        await repo.RenameAsync(second.Id, "연금저축");                                            // 자기 이름 그대로는 허용
    }

    [Fact]
    public async Task 같은_종목을_계좌마다_따로_보유할_수_있다()
    {
        using var db = new TestDb();
        var second = await new TradingAccountRepository(db).AddAsync("연금저축");
        var repo = new HoldingRepository(db.Context);

        await repo.SaveAsync("005930", "삼성전자", 10, 70_000m, SeedData.StockGroupId);
        await repo.SaveAsync("005930", "삼성전자", 3, 80_000m, null, second.Id);
        await repo.SaveAsync("005930", "삼성전자", 5, 81_000m, null, second.Id);   // 같은 계좌에서는 수정

        Assert.Equal([(10L, 70_000m)], (await repo.GetAllAsync()).Select(h => (h.Quantity, h.AvgPrice)));
        Assert.Equal([(5L, 81_000m)], (await repo.GetAllAsync(second.Id)).Select(h => (h.Quantity, h.AvgPrice)));

        Assert.True(await repo.DeleteAsync("005930", second.Id));
        Assert.Single(await repo.GetAllAsync());                // 다른 계좌의 종목은 그대로
        Assert.False(await repo.DeleteAsync("005930", second.Id));
    }

    [Fact]
    public async Task 다른_계좌의_그룹에는_종목을_넣을_수_없다()
    {
        using var db = new TestDb();
        var second = await new TradingAccountRepository(db).AddAsync("연금저축");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new HoldingRepository(db.Context).SaveAsync("005930", "삼성전자", 1, 70_000m, SeedData.StockGroupId, second.Id));
    }

    [Fact]
    public async Task 그룹과_목표_비중은_계좌마다_따로다()
    {
        using var db = new TestDb();
        var second = await new TradingAccountRepository(db).AddAsync("연금저축");
        var groups = new GroupRepository(db);

        // 두 번째 계좌에만 그룹을 추가하고 목표를 바꾼다. 같은 이름의 그룹이 계좌마다 있어도 된다.
        var extra = await groups.AddAsync("금", null, second.Id);
        var secondGroups = await groups.GetAllAsync(second.Id);
        await groups.SaveTargetsAsync(secondGroups.ToDictionary(g => g.Id, g => g.Id == extra.Id ? 1m : 0m), second.Id);
        await groups.AddAsync("금");

        Assert.Equal([0.5m, 0.3m, 0.2m, 0m], (await groups.GetAllAsync()).Select(g => g.TargetWeight));
        Assert.Equal([0m, 0m, 0m, 1m], (await groups.GetAllAsync(second.Id)).Select(g => g.TargetWeight));
        await Assert.ThrowsAsync<ArgumentException>(() => groups.AddAsync("금", null, second.Id));   // 같은 계좌 안에서는 겹칠 수 없다

        // 순서 옮기기는 그 계좌 안에서만
        await groups.MoveAsync(extra.Id, -1);
        Assert.Equal(["주식", "채권", "금", "배당"], (await groups.GetAllAsync(second.Id)).Select(g => g.Name));
        Assert.Equal(["주식", "채권", "배당", "금"], (await groups.GetAllAsync()).Select(g => g.Name));
    }

    [Fact]
    public async Task 예수금과_보유_내역은_계좌마다_따로_읽는다()
    {
        using var db = new TestDb();
        await SeedData.ApplyAsync(db.Context);
        var second = await new TradingAccountRepository(db).AddAsync("연금저축");
        var settings = new SettingsRepository(db);
        await settings.SetCashAsync(1_000_000m);
        await settings.SetCashAsync(250_000m, second.Id);
        await new HoldingRepository(db.Context).SaveAsync("SEED01", "KOSPI200 ETF", 10, 39_000m, null, second.Id);
        var reader = new PortfolioReader(db.Context, await db.SeedPriceStoreAsync());

        var first = await reader.GetSnapshotAsync();
        var other = await reader.GetSnapshotAsync(second.Id);

        Assert.Equal((7, 1_000_000m), (first.Holdings.Count, first.Cash));
        Assert.Equal(100_000_000m, first.Holdings.Sum(h => h.EvalAmount));
        Assert.Equal((1, 250_000m), (other.Holdings.Count, other.Cash));
        Assert.Equal(400_000m, other.Holdings.Sum(h => h.EvalAmount));   // 같은 종목의 현재가는 함께 쓴다
    }

    [Fact]
    public async Task 여러_계좌에_있는_종목은_시세를_한_번만_조회한다()
    {
        using var db = new TestDb();
        await SeedData.ApplyAsync(db.Context);
        var second = await new TradingAccountRepository(db).AddAsync("연금저축");
        await new HoldingRepository(db.Context).SaveAsync("SEED01", "KOSPI200 ETF", 10, 39_000m, null, second.Id);
        var provider = new CountingProvider(SeedData.Prices);

        await new PriceUpdater(db, provider, new PriceStore()).RefreshHoldingsAsync();

        Assert.Equal(7, provider.Asked.Count);
        Assert.Equal(provider.Asked.Count, provider.Asked.Distinct().Count());
    }

    private sealed class CountingProvider(IReadOnlyDictionary<string, decimal> prices) : IPriceProvider
    {
        public List<string> Asked { get; } = [];
        public Task<IReadOnlyDictionary<string, PriceQuote>> GetPricesAsync(IReadOnlyCollection<string> symbolCodes, CancellationToken ct = default)
        {
            Asked.AddRange(symbolCodes);
            return new FakePriceProvider(prices).GetPricesAsync(symbolCodes, ct);
        }
    }

    [Fact]
    public async Task 계좌를_지우면_그_계좌의_종목_그룹_예수금만_함께_지워진다()
    {
        using var db = new TestDb();
        await SeedData.ApplyAsync(db.Context);
        var repo = new TradingAccountRepository(db);
        var second = await repo.AddAsync("연금저축");
        await new HoldingRepository(db.Context).SaveAsync("SEED01", "KOSPI200 ETF", 10, 39_000m, null, second.Id);
        await new SettingsRepository(db).SetCashAsync(250_000m, second.Id);
        Assert.Equal(1, await repo.CountHoldingsAsync(second.Id));

        await repo.DeleteAsync(second.Id);

        await using var check = db.CreateDbContext();
        Assert.Equal(["기본 계좌"], await check.Accounts.Select(a => a.Name).ToListAsync());
        Assert.Equal(7, await check.Holdings.CountAsync());
        Assert.Equal(3, await check.AssetGroups.CountAsync());
        Assert.Empty(await check.CashBalances.ToListAsync());
    }

    [Fact]
    public async Task 마지막_남은_계좌는_지울_수_없다()
    {
        using var db = new TestDb();
        var repo = new TradingAccountRepository(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.DeleteAsync(TradingAccount.DefaultId));
        Assert.Single(await repo.GetAllAsync());
    }

    // 계좌 기능 이전의 DB를 올릴 때: 넣어 둔 종목·그룹·예수금이 모두 ISA 계좌로 들어간다 (사용자 요청)
    [Fact]
    public async Task 기존_데이터는_ISA_계좌로_옮겨진다()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<PortfolioDbContext>().UseSqlite(connection).Options;
        await using (var before = new PortfolioDbContext(options))
        {
            await before.GetService<IMigrator>().MigrateAsync("AddApiToken");   // 계좌가 없던 마지막 버전
            await before.Database.ExecuteSqlRawAsync("""
                INSERT INTO AssetGroups (Id, Name, Color, SortOrder, TargetWeight) VALUES (4, '금', '#7A8088', 4, '0.0');
                INSERT INTO Holdings (SymbolCode, SymbolName, Quantity, AvgPrice, GroupId, UpdatedAt) VALUES
                    ('005930', '삼성전자', 10, '70000.0', 1, '2026-10-01 00:00:00+00:00'),
                    ('411060', 'ACE KRX금현물', 20, '15000.0', 4, '2026-10-02 00:00:00+00:00'),
                    ('148070', 'KOSEF 국고채10년', 5, '110000.0', NULL, '2026-10-03 00:00:00+00:00');
                INSERT INTO CashBalances (Id, Amount, UpdatedAt) VALUES (1, '1234567.0', '2026-10-03 00:00:00+00:00');
                """);
        }

        await using var db = new PortfolioDbContext(options);
        await db.Database.MigrateAsync();

        var account = await db.Accounts.SingleAsync();
        Assert.Equal((TradingAccount.DefaultId, "ISA"), (account.Id, account.Name));
        var holdings = await db.Holdings.OrderBy(h => h.SymbolCode).ToListAsync();
        Assert.Equal(
            [("005930", 10L, 70_000m, (int?)1), ("148070", 5L, 110_000m, null), ("411060", 20L, 15_000m, 4)],
            holdings.Select(h => (h.SymbolCode, h.Quantity, h.AvgPrice, h.GroupId)));
        Assert.All(holdings, h => Assert.Equal(account.Id, h.AccountId));
        Assert.Equal(["주식", "채권", "배당", "금"], await db.AssetGroups.Where(g => g.AccountId == account.Id).OrderBy(g => g.SortOrder).Select(g => g.Name).ToListAsync());
        var cash = await db.CashBalances.SingleAsync();
        Assert.Equal((account.Id, 1_234_567m), (cash.AccountId, cash.Amount));
    }
}
