using Portfolio.Core;
using Portfolio.Data;

namespace Portfolio.Web.Hosting;

public sealed class BackupOptions
{
    public const string SectionName = "Backup";
    public bool Enabled { get; set; } = true;
    public int KeepCount { get; set; } = DatabaseBackup.DefaultKeepCount;
}

// 하루 1회 DB 백업 (설계서 8.1). 앱이 켜질 때와 그 뒤 1시간마다 오늘(한국 시간) 백업이 있는지 보고 없으면 만든다.
// 앱을 매일 켜 두지 않아도, 켠 날에는 그날 백업이 남는다.
public sealed class DatabaseBackupService(
    string connectionString,
    AppPaths paths,
    BackupOptions options,
    TimeProvider clock,
    ILogger<DatabaseBackupService> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var today = MarketSchedule.KstDate(clock.GetUtcNow());
                string? created = await DatabaseBackup.RunAsync(
                    connectionString, paths.BackupDirectory, today, options.KeepCount, stoppingToken);
                if (created is not null)
                    logger.LogInformation("DB 백업 완료: {Path} (최근 {Keep}개 보관)", created, options.KeepCount);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "DB 백업 실패");
            }

            await Task.Delay(CheckInterval, clock, stoppingToken);
        }
    }
}
