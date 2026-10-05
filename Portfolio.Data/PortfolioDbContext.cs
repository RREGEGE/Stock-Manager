using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Portfolio.Data;

public class PortfolioDbContext(DbContextOptions<PortfolioDbContext> options) : DbContext(options)
{
    public DbSet<Holding> Holdings => Set<Holding>();
    public DbSet<SymbolMaster> SymbolMasters => Set<SymbolMaster>();
    public DbSet<CashBalance> CashBalances => Set<CashBalance>();
    public DbSet<AssetGroup> AssetGroups => Set<AssetGroup>();
    public DbSet<PriceCache> PriceCaches => Set<PriceCache>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<ApiToken> ApiTokens => Set<ApiToken>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Holding>(e =>
        {
            e.HasKey(x => x.SymbolCode);
            e.Property(x => x.SymbolCode).HasMaxLength(12);
            e.Property(x => x.SymbolName).HasMaxLength(100);
            // 그룹 삭제 시 소속 종목은 미분류로 이동 (설계서 6장 그룹 관리)
            e.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<SymbolMaster>(e =>
        {
            e.HasKey(x => x.SymbolCode);
            e.Property(x => x.SymbolCode).HasMaxLength(12);
            e.Property(x => x.SymbolName).HasMaxLength(100);
            e.Property(x => x.Market).HasMaxLength(20);
        });
        b.Entity<AssetGroup>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(50);
            e.Property(x => x.Color).HasMaxLength(9);
            // 초기 그룹 3개 (설계서 2장 계산 기준, 9.2 색상, 10.4 목표 비중)
            e.HasData(
                new AssetGroup { Id = 1, Name = "주식", Color = "#23395B", SortOrder = 1, TargetWeight = 0.5m },
                new AssetGroup { Id = 2, Name = "채권", Color = "#E08A2E", SortOrder = 2, TargetWeight = 0.3m },
                new AssetGroup { Id = 3, Name = "배당", Color = "#6BB3A8", SortOrder = 3, TargetWeight = 0.2m });
        });
        b.Entity<PriceCache>(e =>
        {
            e.HasKey(x => x.SymbolCode);
            e.Property(x => x.SymbolCode).HasMaxLength(12);
        });
        b.Entity<ApiToken>(e => e.Property(x => x.Id).ValueGeneratedNever());
        b.Entity<AppSetting>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(50);
        });
    }
}

// dotnet ef 마이그레이션 생성용 (실행 시에는 Web에서 연결 문자열을 주입)
public class PortfolioDbContextFactory : IDesignTimeDbContextFactory<PortfolioDbContext>
{
    public PortfolioDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PortfolioDbContext>().UseSqlite("Data Source=portfolio.db").Options);
}
