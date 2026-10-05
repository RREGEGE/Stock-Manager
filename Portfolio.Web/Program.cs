using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Portfolio.Core;
using Portfolio.Data;
using Portfolio.Kis;
using Portfolio.Web;
using Portfolio.Web.Components;
using Portfolio.Web.Dev;
using Portfolio.Web.Hosting;
using Portfolio.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// 운영 데이터 폴더 (저장소 밖). 개발 환경에서는 DataDirectory를 직접 지정했을 때만 쓴다.
var paths = AppPaths.FromConfiguration(builder.Configuration);
bool useDataDirectory = !builder.Environment.IsDevelopment()
    || !string.IsNullOrWhiteSpace(builder.Configuration["DataDirectory"]);
if (useDataDirectory)
{
    paths.EnsureCreated();
    // 이 PC 전용 설정(KIS 키 등). 명령줄 인자가 이 파일보다 우선하도록 인자를 다시 얹는다.
    builder.Configuration.AddJsonFile(paths.SettingsPath, optional: true, reloadOnChange: false);
    builder.Configuration.AddCommandLine(args);
}
builder.Services.AddSingleton(paths);

// 접속 주소: 따로 지정하지 않으면 이 PC 안에서만 듣는다. 다른 기기는 tailscale serve를 거쳐 들어온다.
if (string.IsNullOrWhiteSpace(builder.Configuration["urls"]))
    builder.WebHost.UseUrls("http://127.0.0.1:5137");

// tailscale serve(이 PC의 프록시)가 붙여 주는 원래 주소·HTTPS 여부를 받아들인다. 기본값으로 로컬 프록시만 신뢰한다.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// 데이터·시세 (설계서 3장, 4장)
string connectionString = builder.Configuration.GetConnectionString("Portfolio")
    ?? $"Data Source={paths.DatabasePath}";
builder.Services.AddDbContextFactory<PortfolioDbContext>(o => o.UseSqlite(connectionString));

var dataProtection = builder.Services.AddDataProtection().SetApplicationName("Portfolio");
if (useDataDirectory)
{
    // 토큰 암호화 키를 데이터 폴더에 두고, Windows에서는 현재 사용자만 풀 수 있게 보호한다 (DPAPI)
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(paths.KeysDirectory));
    if (OperatingSystem.IsWindows())
        dataProtection.ProtectKeysWithDpapi();
}

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
if (builder.Configuration.GetValue("SymbolMaster:AutoRefresh", true))
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
app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// HTTPS는 tailscale serve가 맡는다 (인증서 자동 발급·갱신). 앱은 127.0.0.1에서 HTTP로만 듣는다.

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Lifetime.ApplicationStarted.Register(() =>
{
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Portfolio.Web.Hosting");
    ListenAddressCheck.WarnIfExposed(app.Urls, logger);
    if (useDataDirectory)
        logger.LogInformation("데이터 폴더: {DataDirectory}", paths.DataDirectory);
});

app.Run();

// 통합 테스트(WebApplicationFactory)에서 참조하기 위한 선언
public partial class Program;
