using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Portfolio.Core;
using Portfolio.Data;
using Portfolio.Kis;
using Portfolio.Web;
using Portfolio.Web.Components;
using Portfolio.Web.Dev;
using Portfolio.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// 데이터·시세 (설계서 3장, 4장)
builder.Services.AddDbContextFactory<PortfolioDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Portfolio")));
builder.Services.AddDataProtection().SetApplicationName("Portfolio");
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IAccessTokenStore, DbAccessTokenStore>();

// 시세 출처: 기본은 KIS. 개발 환경에서만 PriceSource=Fake로 10.4 시드 데이터와 가짜 시세를 쓸 수 있다.
var priceSource = builder.Configuration.GetValue("PriceSource", PriceSource.Kis);
if (priceSource == PriceSource.Fake && !builder.Environment.IsDevelopment())
    throw new InvalidOperationException("PriceSource=Fake는 Development 환경에서만 쓸 수 있습니다. (실제 DB에 시드 데이터가 들어가는 것을 막기 위함)");

if (priceSource == PriceSource.Fake)
{
    decimal fluctuation = builder.Configuration.GetValue("FakePrices:FluctuationPercent", 0m);
    builder.Services.AddSingleton<IPriceProvider>(sp =>
    {
        IPriceProvider fake = new FakePriceProvider(SeedData.Prices, sp.GetRequiredService<TimeProvider>());
        return fluctuation > 0 ? new FluctuatingPriceProvider(fake, fluctuation) : fake;
    });
    builder.Services.Configure<KisOptions>(builder.Configuration.GetSection(KisOptions.SectionName));
}
else
{
    builder.Services.AddKisPriceProvider(builder.Configuration);
}
builder.Services.AddSingleton(new PriceSourceSetting(priceSource));

builder.Services.AddSingleton<PriceStore>();
builder.Services.AddSingleton<PortfolioNotifier>();
builder.Services.AddSingleton<PriceUpdater>();
builder.Services.Configure<PricePollingOptions>(builder.Configuration.GetSection(PricePollingOptions.SectionName));
builder.Services.AddHostedService<PricePollingService>();
builder.Services.AddKisSymbolMaster();
builder.Services.AddSingleton<SymbolMasterRepository>();
builder.Services.AddHostedService<SymbolMasterRefreshService>();

// 화면용 서비스
builder.Services.AddSingleton<GroupRepository>();
builder.Services.AddSingleton<SettingsRepository>();
builder.Services.AddSingleton<PortfolioService>();

var app = builder.Build();

// 시작 시 DB 스키마를 최신 마이그레이션으로 맞춘다
await using (var db = await app.Services.GetRequiredService<IDbContextFactory<PortfolioDbContext>>().CreateDbContextAsync())
{
    await db.Database.MigrateAsync();
    if (priceSource == PriceSource.Fake)
        await SeedData.ApplyAsync(db);   // 개발용 DB에만, 보유종목이 비어 있을 때만 넣는다
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
