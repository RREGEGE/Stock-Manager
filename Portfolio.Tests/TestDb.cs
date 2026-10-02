using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
