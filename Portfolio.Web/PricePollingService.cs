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

public enum PriceSource { Kis, Fake }

public sealed record PriceSourceSetting(PriceSource Source);

// 시세 폴링 (설계서 3장, 4.3). 조회 시점은 MarketSchedule이 정하고, 이 서비스는 실행만 담당한다.
public sealed class PricePollingService(
    PriceUpdater updater,
    SettingsRepository settings,
    IOptions<KisOptions> kisOptions,
    IOptions<PricePollingOptions> pollingOptions,
    PriceSourceSetting priceSourceSetting,
    Hosting.AppPaths paths,
    TimeProvider clock,
    ILogger<PricePollingService> logger) : BackgroundService
{
    private readonly PriceSource priceSource = priceSourceSetting.Source;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await updater.LoadCacheAsync(stoppingToken);

        if (priceSource == PriceSource.Fake)
        {
            logger.LogWarning("개발용 가짜 시세(PriceSource=Fake)로 동작합니다. 실제 시세가 아닙니다.");
        }
        else if (!kisOptions.Value.IsConfigured)
        {
            logger.LogWarning(
                "KIS 앱키가 없어 시세를 받아오지 않습니다. 보유 종목은 저장되지만 현재가는 '시세 지연'으로 표시됩니다. 키를 발급받으면 {SettingsPath}의 AppKey·AppSecret에 넣고 앱을 다시 켜세요.",
                paths.SettingsPath);
            return;
        }

        DateOnly? lastCloseFetch = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = clock.GetUtcNow();
            var interval = await GetIntervalAsync(stoppingToken);

            // 가짜 시세는 장 시간과 무관하게 주기마다 갱신한다 (화면 확인용)
            var plan = priceSource == PriceSource.Fake
                ? new PollPlan(PollKind.Intraday, now + interval)
                : MarketSchedule.Plan(now, lastCloseFetch, interval);

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

    // 설정 화면에서 바꾼 주기를 다음 조회부터 적용한다. 저장된 값이 없으면 appsettings 값을 쓴다.
    private async Task<TimeSpan> GetIntervalAsync(CancellationToken ct)
    {
        int seconds = pollingOptions.Value.IntervalSeconds;
        try
        {
            seconds = await settings.GetPollingIntervalSecondsAsync(ct) ?? seconds;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "폴링 주기 설정을 읽지 못해 기본값을 씁니다.");
        }
        return TimeSpan.FromSeconds(Math.Max(1, seconds));
    }
}
