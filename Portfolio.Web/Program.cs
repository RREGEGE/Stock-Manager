using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Portfolio.Core;
using Portfolio.Data;
using Portfolio.Kis;
using Portfolio.Web;
using Portfolio.Web.Components;

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
builder.Services.AddKisPriceProvider(builder.Configuration);
builder.Services.AddSingleton<PriceStore>();
builder.Services.AddSingleton<PriceUpdater>();
builder.Services.Configure<PricePollingOptions>(builder.Configuration.GetSection(PricePollingOptions.SectionName));
builder.Services.AddHostedService<PricePollingService>();
builder.Services.AddKisSymbolMaster();
builder.Services.AddSingleton<SymbolMasterRepository>();
builder.Services.AddHostedService<SymbolMasterRefreshService>();

var app = builder.Build();

// 시작 시 DB 스키마를 최신 마이그레이션으로 맞춘다
await using (var db = await app.Services.GetRequiredService<IDbContextFactory<PortfolioDbContext>>().CreateDbContextAsync())
{
    await db.Database.MigrateAsync();
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
