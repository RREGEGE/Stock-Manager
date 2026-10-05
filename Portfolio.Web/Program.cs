using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Portfolio.Core;
using Portfolio.Data;
using Portfolio.Kis;
using Portfolio.Web;
using Portfolio.Web.Auth;
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

// 게시(publish)하지 않고 `dotnet run`으로 운영 모드를 띄워도 CSS·스크립트를 찾도록 한다.
// (개발 환경에서는 자동으로 켜지고, 게시본에서는 아무 일도 하지 않는다)
builder.WebHost.UseStaticWebAssets();

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

// 로그인: 비밀번호 1개, 쿠키로 90일 유지 (설계서 7.3 개인 사용 단계의 간소화)
var authOptions = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
if (!authOptions.Enabled && priceSource != PriceSource.Fake)
    throw new InvalidOperationException("Auth:Enabled=false는 가짜 시세(PriceSource=Fake)일 때만 쓸 수 있습니다. 실제 보유 내역은 로그인 없이 열 수 없습니다.");

builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.AddSingleton<PasswordService>();
builder.Services.AddSingleton<LoginThrottle>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = AuthOptions.CookieName;
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;   // tailscale serve(HTTPS)를 거치면 Secure 쿠키가 된다
        o.LoginPath = "/login";
        o.ExpireTimeSpan = AuthOptions.SessionLifetime;
        o.SlidingExpiration = true;
    });
builder.Services.AddAuthorization(o =>
{
    // 따로 허용한 것(로그인 화면, 정적 파일) 말고는 모두 로그인을 요구한다
    if (authOptions.Enabled)
        o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});

var app = builder.Build();

// 시작 시 DB 스키마를 최신 마이그레이션으로 맞춘다
await using (var db = await app.Services.GetRequiredService<IDbContextFactory<PortfolioDbContext>>().CreateDbContextAsync())
{
    await db.Database.MigrateAsync();
    if (priceSource == PriceSource.Fake)
        await SeedData.ApplyAsync(db);   // 개발용 DB에만, 보유종목이 비어 있을 때만 넣는다
}

// `set-password`: 웹 서버를 띄우지 않고 비밀번호만 설정하고 끝낸다
if (args.Contains(SetPasswordCommand.Name))
{
    return await SetPasswordCommand.RunAsync(
        app.Services.GetRequiredService<PasswordService>(), SetPasswordCommand.ReadHidden, Console.Out);
}

// Configure the HTTP request pipeline.
app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// HTTPS는 tailscale serve가 맡는다 (인증서 자동 발급·갱신). 앱은 127.0.0.1에서 HTTP로만 듣는다.

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// 로그인 화면이 쓰는 CSS·글꼴·아이콘은 로그인 전에도 받을 수 있어야 한다
app.MapStaticAssets().AllowAnonymous();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet("/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
}).AllowAnonymous();

app.Lifetime.ApplicationStarted.Register(() =>
{
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Portfolio.Web.Hosting");
    ListenAddressCheck.WarnIfExposed(app.Urls, logger);
    if (useDataDirectory)
        logger.LogInformation("데이터 폴더: {DataDirectory}", paths.DataDirectory);
});

app.Run();
return 0;

// 통합 테스트(WebApplicationFactory)에서 참조하기 위한 선언
public partial class Program;
