using Microsoft.EntityFrameworkCore;

namespace Portfolio.Data;

// 설계서 10.4 테스트용 시드 데이터. 실제 종목·시세가 아닌 가상 데이터이며,
// 실제 보유 내역과 섞이지 않도록 자동 적용하지 않고 테스트·개발에서 명시적으로만 넣는다.
// 종목코드는 실제 상장 코드와 겹치지 않도록 SEED01~SEED07을 쓴다.
public static class SeedData
{
    public sealed record SeedHolding(
        string SymbolCode, string SymbolName, int GroupId, long Quantity, decimal AvgPrice, decimal CurrentPrice);

    public const int StockGroupId = 1;
    public const int BondGroupId = 2;
    public const int DividendGroupId = 3;

    public static readonly IReadOnlyList<SeedHolding> Holdings =
    [
        new("SEED01", "KOSPI200 ETF",    StockGroupId,    600,   36_000m,  40_000m),
        new("SEED02", "미국 S&P500 ETF", StockGroupId,    1_200, 17_500m,  20_000m),
        new("SEED03", "반도체 개별주 A", StockGroupId,    100,   130_000m, 120_000m),
        new("SEED04", "국고채 10년 ETF", BondGroupId,     100,   102_000m, 100_000m),
        new("SEED05", "미국채 10년 ETF", BondGroupId,     1_000, 10_500m,  10_000m),
        new("SEED06", "고배당 ETF",      DividendGroupId, 800,   13_000m,  15_000m),
        new("SEED07", "리츠 ETF",        DividendGroupId, 1_600, 5_500m,   5_000m),
    ];

    // FakePriceProvider에 넣을 현재가
    public static IReadOnlyDictionary<string, decimal> Prices =>
        Holdings.ToDictionary(h => h.SymbolCode, h => h.CurrentPrice);

    // 보유종목이 비어 있을 때만 시드를 넣는다. 그룹(주식/채권/배당, 목표 50/30/20%)은 마이그레이션 초기값을 쓴다.
    public static async Task ApplyAsync(PortfolioDbContext db, CancellationToken ct = default)
    {
        if (await db.Holdings.AnyAsync(ct)) return;

        var now = DateTimeOffset.UtcNow;
        db.Holdings.AddRange(Holdings.Select(h => new Holding
        {
            SymbolCode = h.SymbolCode,
            SymbolName = h.SymbolName,
            Quantity = h.Quantity,
            AvgPrice = h.AvgPrice,
            GroupId = h.GroupId,
            UpdatedAt = now,
        }));
        db.SymbolMasters.AddRange(Holdings.Select(h => new SymbolMaster
        {
            SymbolCode = h.SymbolCode,
            SymbolName = h.SymbolName,
            Market = "SEED",
        }));
        await db.SaveChangesAsync(ct);
    }
}
