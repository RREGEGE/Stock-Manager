using Microsoft.Extensions.Options;
using Portfolio.Core;
using Portfolio.Web.Services;

namespace Portfolio.Web;

// 지수·환율 갱신 (설계서 F-09)
// - 국내 지수(코스피·코스닥): 장중에는 시세 폴링 주기마다, 장 마감 후에는 종가를 1회
// - 해외 지수·환율: 10분마다 (한국 낮에는 미국 장이 닫혀 있어 자주 조회할 필요가 없다)
// 시세를 받아올 수 없는 상태(KIS 키 없음)면 아무것도 하지 않고, 화면이 그 사실을 안내한다.
public sealed class MarketIndicatorPollingService(
    MarketIndicatorUpdater updater,
    PriceSourceInfo priceSource,
    IOptions<PricePollingOptions> pollingOptions,
    TimeProvider clock,
    ILogger<MarketIndicatorPollingService> logger) : BackgroundService
{
    public static readonly TimeSpan OverseasInterval = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!priceSource.Ready) return;

        var interval = TimeSpan.FromSeconds(Math.Max(10, pollingOptions.Value.IntervalSeconds));
        DateTimeOffset? lastDomestic = null, lastOverseas = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = clock.GetUtcNow();
            try
            {
                if (ShouldRefreshOverseas(now, lastOverseas))
                {
                    await updater.RefreshAsync(MarketIndicators.Overseas, stoppingToken);
                    lastOverseas = now;
                }
                if (ShouldRefreshDomestic(now, lastDomestic))
                {
                    await updater.RefreshAsync(MarketIndicators.Domestic, stoppingToken);
                    lastDomestic = now;
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "지수·환율 갱신 중 오류");
            }

            await Task.Delay(interval, clock, stoppingToken);
        }
    }

    public static bool ShouldRefreshOverseas(DateTimeOffset now, DateTimeOffset? last) =>
        last is null || now - last.Value >= OverseasInterval;

    // 처음 한 번, 장중에는 매번, 장 마감 후에는 종가 조회 시각(15:40) 이후 1회
    public static bool ShouldRefreshDomestic(DateTimeOffset now, DateTimeOffset? last)
    {
        if (last is null || MarketSchedule.IsOpen(now)) return true;
        if (!MarketSchedule.IsTradingDay(now)) return false;

        var kst = now.ToOffset(MarketSchedule.Kst);
        var closeFetch = new DateTimeOffset(kst.Date + MarketSchedule.CloseFetch, MarketSchedule.Kst);
        return now >= closeFetch && last.Value < closeFetch;
    }
}
