using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Core;
using Portfolio.Data;
using Portfolio.Web;
using Portfolio.Web.Services;

namespace Portfolio.Tests.Ui;

// 화면 컴포넌트 테스트 공통: in-memory DB에 10.4 시드 데이터, FakePriceProvider 시세
public abstract class UiTestBase : BunitContext
{
    protected TestDb Db { get; } = new();
    protected PriceStore Prices { get; } = new();
    protected PortfolioNotifier Notifier { get; } = new();
    // 2026-10-02(금) 09:41 KST — 목업의 '장중 · 09:41 시세 갱신'
    protected ManualClock Clock { get; } = new(new DateTimeOffset(2026, 10, 2, 9, 41, 0, TimeSpan.FromHours(9)));

    protected UiTestBase()
    {
        SeedData.ApplyAsync(Db.Context).GetAwaiter().GetResult();
        RefreshPricesAsync(SeedData.Prices).GetAwaiter().GetResult();

        Services.AddSingleton<IDbContextFactory<PortfolioDbContext>>(Db);
        Services.AddSingleton<TimeProvider>(Clock);
        Services.AddSingleton(Prices);
        Services.AddSingleton(Notifier);
        Services.AddSingleton(sp => new PriceUpdater(Db, new FakePriceProvider(SeedData.Prices, Clock), Prices, null, Notifier));
        Services.AddSingleton<GroupRepository>();
        Services.AddSingleton<SettingsRepository>();
        Services.AddSingleton<SymbolMasterRepository>();
        Services.AddSingleton<PortfolioService>();
        Services.Configure<PricePollingOptions>(_ => { });
    }

    // 시세 폴링 1회에 해당: 새 가격을 받아 반영하고 화면에 알린다
    protected Task RefreshPricesAsync(IReadOnlyDictionary<string, decimal> prices) =>
        new PriceUpdater(Db, new FakePriceProvider(prices, Clock), Prices, null, Notifier).RefreshHoldingsAsync();

    protected async Task<PortfolioViewModel> LoadModelAsync() =>
        new(await Services.GetRequiredService<PortfolioService>().LoadAsync());

    protected static string Text(AngleSharp.Dom.IElement element) =>
        string.Join(" ", element.TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    // 표의 행·누적 막대처럼 자식 요소가 칸인 경우: 칸마다 글자를 읽어 공백으로 잇는다
    protected static string Cells(AngleSharp.Dom.IElement element) =>
        string.Join(" ", element.Children.Select(Text).Where(t => t.Length > 0));

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) Db.Dispose();
    }
}
