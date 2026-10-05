using Microsoft.Extensions.Options;
using Portfolio.Core;
using Portfolio.Data;
using Portfolio.Kis;

namespace Portfolio.Web;

public sealed class PricePollingOptions
{
    public const string SectionName = "PricePolling";
    public int IntervalSeconds { get; set; } = 60;
}

// 시세 폴링 (설계서 3장, 4.3). 조회 시점은 MarketSchedule이 정하고, 이 서비스는 실행만 담당한다.
public sealed class PricePollingService(
    PriceUpdater updater,
    IOptions<KisOptions> kisOptions,
    IOptions<PricePollingOptions> pollingOptions,
    TimeProvider clock,
    ILogger<PricePollingService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await updater.LoadCacheAsync(stoppingToken);

        if (!kisOptions.Value.IsConfigured)
        {
            logger.LogWarning("KIS AppKey/AppSecret이 설정되지 않아 시세 폴링을 하지 않습니다. (dotnet user-secrets로 설정)");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(10, pollingOptions.Value.IntervalSeconds));
        DateOnly? lastCloseFetch = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = clock.GetUtcNow();
            var plan = MarketSchedule.Plan(now, lastCloseFetch, interval);

            if (plan.Kind != PollKind.None)
            {
                try
                {
                    await updater.RefreshHoldingsAsync(stoppingToken);
                    if (plan.Kind == PollKind.Close)
                    {
                        lastCloseFetch = MarketSchedule.KstDate(now);
                        logger.LogInformation("종가 조회 완료, 다음 조회: {Next}", plan.NextCheck);
                    }
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "시세 갱신 중 오류");
                }
            }

            var delay = plan.NextCheck - clock.GetUtcNow();
            await Task.Delay(delay > TimeSpan.FromSeconds(1) ? delay : TimeSpan.FromSeconds(1), clock, stoppingToken);
        }
    }
}
