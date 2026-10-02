using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Portfolio.Core;
using Portfolio.Data;

namespace Portfolio.Tests;

// SQLite in-memory DB: 연결이 열려 있는 동안만 유지되며 파일을 만들지 않는다.
public sealed class TestDb : IDisposable, IDbContextFactory<PortfolioDbContext>
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<PortfolioDbContext> _options;
    public PortfolioDbContext Context { get; }

    public TestDb()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<PortfolioDbContext>().UseSqlite(_connection).Options;
        Context = new PortfolioDbContext(_options);
        Context.Database.Migrate();
    }

    // 같은 in-memory DB를 쓰는 새 컨텍스트 (싱글턴 서비스 테스트용)
    public PortfolioDbContext CreateDbContext() => new(_options);

    // 10.4 시드 현재가를 FakePriceProvider → PriceUpdater 경로로 반영한 가격 캐시
    public async Task<PriceStore> SeedPriceStoreAsync()
    {
        var store = new PriceStore();
        await new PriceUpdater(this, new FakePriceProvider(SeedData.Prices), store).RefreshHoldingsAsync();
        return store;
    }

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}

public sealed class ManualClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}
