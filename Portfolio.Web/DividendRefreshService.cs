using Portfolio.Data;
using Portfolio.Web.Services;

namespace Portfolio.Web;

// 배당 이력 갱신 (F-16): 시작할 때와 그 뒤 한 시간마다, 받은 지 하루가 지난 종목만 조회한다.
// 새로 추가한 종목도 다음 확인 때 받아 온다. 시세를 받아올 수 없는 상태(KIS 키 없음)면 아무것도 하지 않는다.
public sealed class DividendRefreshService(
    DividendRepository dividends,
    PriceSourceInfo priceSource,
    TimeProvider clock,
    ILogger<DividendRefreshService> logger) : BackgroundService
{
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);
    // 시작 직후의 시세 조회와 겹치지 않게 잠시 기다린다
    public static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(20);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!priceSource.Ready) return;
        await Task.Delay(StartDelay, clock, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                int updated = await dividends.RefreshDueAsync(stoppingToken);
                if (updated > 0) logger.LogInformation("배당 이력을 갱신했습니다. ({Count}종목)", updated);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "배당 이력 갱신 중 오류");
            }
            await Task.Delay(CheckInterval, clock, stoppingToken);
        }
    }
}
