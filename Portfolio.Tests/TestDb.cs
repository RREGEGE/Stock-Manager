using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;

namespace Portfolio.Tests;

// SQLite in-memory DB: 연결이 열려 있는 동안만 유지되며 파일을 만들지 않는다.
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection;
    public PortfolioDbContext Context { get; }

    public TestDb()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        Context = new PortfolioDbContext(
            new DbContextOptionsBuilder<PortfolioDbContext>().UseSqlite(_connection).Options);
        Context.Database.Migrate();
    }

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}
