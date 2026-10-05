using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;

namespace Portfolio.Tests;

// 설계서 8.1: SQLite 파일 일 1회 백업
public sealed class DatabaseBackupTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "portfolio-backup-" + Guid.NewGuid().ToString("N"));
    private string SourcePath => Path.Combine(_dir, "portfolio.db");
    private string BackupDir => Path.Combine(_dir, "backups");
    private string ConnectionString => new SqliteConnectionStringBuilder { DataSource = SourcePath, Pooling = false }.ToString();

    public DatabaseBackupTests() => Directory.CreateDirectory(_dir);

    private PortfolioDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PortfolioDbContext>().UseSqlite(ConnectionString).Options);

    private async Task SeedSourceAsync()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        await SeedData.ApplyAsync(db);
    }

    [Fact]
    public async Task 백업_파일에는_보유종목이_그대로_들어_있다()
    {
        await SeedSourceAsync();

        string? created = await DatabaseBackup.RunAsync(ConnectionString, BackupDir, new DateOnly(2026, 10, 5));

        Assert.Equal(Path.Combine(BackupDir, "portfolio-20261005.db"), created);
        var options = new DbContextOptionsBuilder<PortfolioDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = created, Pooling = false, Mode = SqliteOpenMode.ReadOnly }.ToString()).Options;
        await using var backup = new PortfolioDbContext(options);
        Assert.Equal(7, await backup.Holdings.CountAsync());
        Assert.Equal(3, await backup.AssetGroups.CountAsync());
        Assert.Equal(600, (await backup.Holdings.SingleAsync(h => h.SymbolCode == "SEED01")).Quantity);
    }

    [Fact]
    public async Task 앱이_DB를_쓰는_중에도_백업된다()
    {
        await SeedSourceAsync();
        await using var inUse = CreateContext();
        await inUse.Database.OpenConnectionAsync();
        Assert.Equal(7, await inUse.Holdings.CountAsync());

        string? created = await DatabaseBackup.RunAsync(ConnectionString, BackupDir, new DateOnly(2026, 10, 5));

        Assert.True(File.Exists(created));
        Assert.True(new FileInfo(created!).Length > 0);
    }

    [Fact]
    public async Task 같은_날에는_한_번만_만든다()
    {
        await SeedSourceAsync();
        var day = new DateOnly(2026, 10, 5);

        string? first = await DatabaseBackup.RunAsync(ConnectionString, BackupDir, day);
        var writtenAt = File.GetLastWriteTimeUtc(first!);
        string? second = await DatabaseBackup.RunAsync(ConnectionString, BackupDir, day);

        Assert.NotNull(first);
        Assert.Null(second);
        Assert.Equal(writtenAt, File.GetLastWriteTimeUtc(first!));
        Assert.Single(Directory.GetFiles(BackupDir));
    }

    [Fact]
    public async Task 최근_14일분만_남기고_오래된_백업은_지운다()
    {
        await SeedSourceAsync();
        var start = new DateOnly(2026, 9, 20);

        for (int i = 0; i < 16; i++)
            await DatabaseBackup.RunAsync(ConnectionString, BackupDir, start.AddDays(i));

        var files = Directory.GetFiles(BackupDir).Select(Path.GetFileName).Order().ToList();
        Assert.Equal(14, files.Count);
        Assert.Equal("portfolio-20260922.db", files[0]);    // 9/20, 9/21은 지워짐
        Assert.Equal("portfolio-20261005.db", files[^1]);
    }

    [Fact]
    public async Task 백업_폴더의_다른_파일은_건드리지_않는다()
    {
        await SeedSourceAsync();
        Directory.CreateDirectory(BackupDir);
        string other = Path.Combine(BackupDir, "메모.txt");
        await File.WriteAllTextAsync(other, "지우면 안 되는 파일");

        for (int i = 0; i < 3; i++)
            await DatabaseBackup.RunAsync(ConnectionString, BackupDir, new DateOnly(2026, 10, 1).AddDays(i), keepCount: 1);

        Assert.True(File.Exists(other));
        Assert.Equal(["portfolio-20261003.db"], Directory.GetFiles(BackupDir, "*.db").Select(Path.GetFileName));
        Assert.Empty(Directory.GetFiles(BackupDir, "*.tmp"));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }
}
