using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Portfolio.Data;

// SQLite 일 1회 백업 (설계서 8.1). 보유종목 원본이 DB에만 있으므로 날짜별 사본을 남긴다.
// 실행 중인 DB도 안전하게 복사되도록 파일 복사가 아니라 SQLite 백업 기능을 쓴다.
public static class DatabaseBackup
{
    public const int DefaultKeepCount = 14;
    private const string Prefix = "portfolio-";
    private const string Extension = ".db";

    public static string FileName(DateOnly date) =>
        Prefix + date.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + Extension;

    // 해당 날짜 백업이 이미 있으면 아무것도 하지 않고 null, 새로 만들면 그 경로를 돌려준다.
    public static async Task<string?> RunAsync(
        string connectionString, string backupDirectory, DateOnly date, int keepCount = DefaultKeepCount,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(backupDirectory);
        string target = Path.Combine(backupDirectory, FileName(date));
        if (File.Exists(target)) return null;

        // 중간에 실패해도 반쪽짜리 파일이 백업으로 남지 않도록 임시 이름으로 만든 뒤 바꾼다
        string temp = target + ".tmp";
        if (File.Exists(temp)) File.Delete(temp);

        await using (var source = new SqliteConnection(connectionString))
        await using (var destination = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = temp, Pooling = false }.ToString()))
        {
            await source.OpenAsync(ct);
            await destination.OpenAsync(ct);
            source.BackupDatabase(destination);
        }
        File.Move(temp, target);

        Prune(backupDirectory, keepCount);
        return target;
    }

    // 날짜가 최근인 것부터 keepCount개만 남긴다
    public static void Prune(string backupDirectory, int keepCount)
    {
        var old = Directory.GetFiles(backupDirectory, Prefix + "????????" + Extension)
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .Skip(Math.Max(1, keepCount));
        foreach (string file in old)
            File.Delete(file);
    }
}
