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

// 실행 파일을 더블클릭(바로가기)으로 띄워도 설정 파일·wwwroot를 찾게 하고, 검은 창의 한글이 깨지지 않게 한다
AppLauncher.UseExecutableDirectoryIfNeeded();
try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch (IOException) { /* 창이 없는 실행 */ }

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
const string DefaultUrl = "http://127.0.0.1:5137";
if (string.IsNullOrWhiteSpace(builder.Configuration["urls"]))
    builder.WebHost.UseUrls(DefaultUrl);

// 실행 파일로 켰을 때: 화면을 자동으로 연다. 이미 켜져 있으면 화면만 열고 끝낸다.
var launch = builder.Configuration.GetSection(LaunchOptions.SectionName).Get<LaunchOptions>() ?? new LaunchOptions();
// 사람이 직접 띄운 창에서만 화면을 연다 (테스트·도구가 출력을 받아 가는 실행에서는 열지 않는다)
bool openWindow = launch.OpenBrowser && Environment.UserInteractive && !Console.IsOutputRedirected;
bool isSetPassword = args.Contains(SetPasswordCommand.Name);
string? localUrl = AppLauncher.PickLocalUrl((builder.Configuration["urls"] ?? DefaultUrl).Split(';', StringSplitOptions.RemoveEmptyEntries));
if (openWindow && !isSetPassword && localUrl is not null && AppLauncher.IsAlreadyListening(localUrl))
{
    Console.WriteLine($"이미 실행 중입니다. 화면을 엽니다: {localUrl}");
    AppLauncher.OpenWindow(localUrl);
    return 0;
}

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
    builder.Services.AddSingleton<IMarketIndicatorProvider>(sp => new FakeMarketIndicatorProvider(sp.GetRequiredService<TimeProvider>()));
    builder.Services.Configure<KisOptions>(builder.Configuration.GetSection(KisOptions.SectionName));
}
else
{
    builder.Services.AddKisPriceProvider(builder.Configuration);
}
builder.Services.AddSingleton(new PriceSourceSetting(priceSource));
// 화면에서 '왜 시세가 없는지'를 안내하기 위한 정보 (KIS 키 유무, 키를 넣을 파일 위치)
var kisOptions = builder.Configuration.GetSection(KisOptions.SectionName).Get<KisOptions>() ?? new KisOptions();
builder.Services.AddSingleton(new PriceSourceInfo(
    Ready: priceSource == PriceSource.Fake || kisOptions.IsConfigured,
    SettingsPath: useDataDirectory ? paths.SettingsPath : null));

builder.Services.AddSingleton<PriceStore>();
builder.Services.AddSingleton<PortfolioNotifier>();
builder.Services.AddSingleton<PriceUpdater>();
builder.Services.Configure<PricePollingOptions>(builder.Configuration.GetSection(PricePollingOptions.SectionName));
builder.Services.AddHostedService<PricePollingService>();

// 지수·환율 (설계서 F-09): 대시보드 맨 위에 표시
builder.Services.AddSingleton<MarketIndicatorStore>();
builder.Services.AddSingleton(sp =>
{
    var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Portfolio.MarketIndicators");
    return new MarketIndicatorUpdater(
        sp.GetRequiredService<IMarketIndicatorProvider>(), sp.GetRequiredService<MarketIndicatorStore>(),
        sp.GetRequiredService<PortfolioNotifier>(),
        (spec, ex) => log.LogWarning("지표 조회 실패: {Name} ({Message})", spec.Name, ex.Message));
});
builder.Services.AddHostedService<MarketIndicatorPollingService>();
builder.Services.AddKisSymbolMaster();
builder.Services.AddSingleton<SymbolMasterRepository>();
if (builder.Configuration.GetValue("SymbolMaster:AutoRefresh", true))
    builder.Services.AddHostedService<SymbolMasterRefreshService>();

// 일일 백업: 실제 데이터 폴더를 쓸 때만 (개발용 가짜 데이터는 백업하지 않는다)
var backupOptions = builder.Configuration.GetSection(BackupOptions.SectionName).Get<BackupOptions>() ?? new BackupOptions();
if (useDataDirectory && backupOptions.Enabled)
{
    builder.Services.AddHostedService(sp => new DatabaseBackupService(
        connectionString, paths, backupOptions,
        sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ILogger<DatabaseBackupService>>()));
}

// 화면용 서비스
builder.Services.AddSingleton<GroupRepository>();
builder.Services.AddSingleton<SettingsRepository>();
builder.Services.AddSingleton<TradingAccountRepository>();
builder.Services.AddSingleton<PortfolioService>();
builder.Services.AddScoped<CurrentAccount>();   // 화면 연결마다 보고 있는 계좌 (F-11)

// 로그인: 계정 1개(아이디 + 비밀번호), 쿠키로 90일 유지 (설계서 7.3 개인 사용 단계의 간소화)
var authOptions = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
if (!authOptions.Enabled && priceSource != PriceSource.Fake)
    throw new InvalidOperationException("Auth:Enabled=false는 가짜 시세(PriceSource=Fake)일 때만 쓸 수 있습니다. 실제 보유 내역은 로그인 없이 열 수 없습니다.");

builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.AddSingleton<AccountService>();
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
        // 비밀번호가 바뀌면 이전에 발급한 로그인 쿠키를 모두 무효로 한다 (기기 분실 대비)
        o.Events.OnValidatePrincipal = async context =>
        {
            var passwords = context.HttpContext.RequestServices.GetRequiredService<AccountService>();
            string? current = await passwords.GetStampAsync(context.HttpContext.RequestAborted);
            if (current is null || context.Principal?.FindFirst(AuthOptions.StampClaim)?.Value != current)
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
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

// `set-password`: 비밀번호를 잊었을 때의 복구용. 웹 서버를 띄우지 않고 비밀번호만 다시 정하고 끝낸다.
// (가입과 평소의 비밀번호 변경은 웹 화면에서 한다)
if (isSetPassword)
{
    return await SetPasswordCommand.RunAsync(
        app.Services.GetRequiredService<AccountService>(), SetPasswordCommand.ReadHidden, Console.Out);
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

// 계좌 전환 (F-11): 고른 계좌를 이 브라우저의 쿠키에 적고 보던 화면으로 돌아간다.
// 계좌번호가 아니라 계좌의 순번(Id)만 담는다. 없는 계좌면 화면이 첫 번째 계좌를 보여 준다.
app.MapGet(CurrentAccount.SelectPath + "/{id:int}", (int id, string? returnUrl, HttpContext context) =>
{
    context.Response.Cookies.Append(CurrentAccount.CookieName, id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        new CookieOptions
        {
            HttpOnly = true, SameSite = SameSiteMode.Lax, IsEssential = true,
            Secure = context.Request.IsHttps, MaxAge = TimeSpan.FromDays(365),
        });
    // 이 사이트 안의 주소로만 돌아간다
    bool local = !string.IsNullOrEmpty(returnUrl) && returnUrl[0] == '/'
        && (returnUrl.Length == 1 || (returnUrl[1] != '/' && returnUrl[1] != '\\'));
    return Results.LocalRedirect(local ? returnUrl! : "/");
});

app.Lifetime.ApplicationStarted.Register(() =>
{
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Portfolio.Web.Hosting");
    ListenAddressCheck.WarnIfExposed(app.Urls, logger);
    if (useDataDirectory)
        logger.LogInformation("데이터 폴더: {DataDirectory}", paths.DataDirectory);

    if (openWindow && AppLauncher.PickLocalUrl(app.Urls) is { } url)
    {
        try { Console.Title = "포트폴리오 - 이 창을 닫으면 앱이 꺼집니다"; } catch (IOException) { }
        Console.WriteLine();
        Console.WriteLine($"  포트폴리오가 켜졌습니다: {url}");
        Console.WriteLine("  화면이 자동으로 열립니다. 이 창을 닫으면 앱이 꺼집니다.");
        Console.WriteLine();
        AppLauncher.OpenWindow(url);
    }
});

app.Run();
return 0;

// 통합 테스트(WebApplicationFactory)에서 참조하기 위한 선언
public partial class Program;
