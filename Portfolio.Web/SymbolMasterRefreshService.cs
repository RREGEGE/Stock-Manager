using Portfolio.Data;
using Portfolio.Kis;

namespace Portfolio.Web;

// 종목 마스터 주 1회 갱신 (설계서 5.4-5). 마지막 갱신 후 7일이 지났으면 내려받아 교체한다.
// 실패하면 기존 마스터를 그대로 두고 하루 뒤 다시 시도한다.
public sealed class SymbolMasterRefreshService(
    IServiceScopeFactory scopeFactory,
    SymbolMasterRepository repository,
    TimeProvider clock,
    ILogger<SymbolMasterRefreshService> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var updatedAt = await repository.GetUpdatedAtAsync(stoppingToken);
                if (SymbolMasterRepository.IsRefreshDue(updatedAt, clock.GetUtcNow()))
                {
                    using var scope = scopeFactory.CreateScope();
                    var client = scope.ServiceProvider.GetRequiredService<KisSymbolMasterClient>();
                    var symbols = await client.DownloadAsync(stoppingToken);
                    await repository.ReplaceAsync(symbols, clock.GetUtcNow(), stoppingToken);
                    logger.LogInformation("종목 마스터 갱신 완료: {Count}종목", symbols.Count);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "종목 마스터 갱신 실패, 기존 마스터를 유지합니다.");
            }

            await Task.Delay(CheckInterval, clock, stoppingToken);
        }
    }
}
