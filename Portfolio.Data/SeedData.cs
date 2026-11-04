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

    // 전일 종가 (F-14 오늘 등락 확인용 가상 값): 오른 종목, 내린 종목, 그대로인 종목을 섞었다.
    public static IReadOnlyDictionary<string, decimal> PrevCloses => new Dictionary<string, decimal>
    {
        ["SEED01"] = 39_600m, ["SEED02"] = 20_200m, ["SEED03"] = 118_000m, ["SEED04"] = 100_000m,
        ["SEED05"] = 10_050m, ["SEED06"] = 14_850m, ["SEED07"] = 5_000m,
    };

    // 배당 이력 (F-16 예상 배당 확인용 가상 값). today 기준 최근 12개월의 월말을 기준일로, 다음 달 3일을 지급일로 만든다.
    //   SEED07 리츠 ETF: 매월 20원 / SEED06 고배당 ETF: 3·6·9·12월 150원 / SEED02 미국 S&P500 ETF: 1·4·7·10월 60원
    public static IReadOnlyDictionary<string, IReadOnlyList<Core.DividendRecord>> Dividends(DateOnly today)
    {
        var monthEnds = Enumerable.Range(0, 13)
            .Select(i => new DateOnly(today.Year, today.Month, 1).AddMonths(-i).AddMonths(1).AddDays(-1))
            .Where(d => d <= today && d > today.AddMonths(-Core.DividendEstimator.LookbackMonths))
            .ToList();
        Core.DividendRecord Record(string code, DateOnly recordDate, decimal perShare) =>
            new(code, recordDate, new DateOnly(recordDate.Year, recordDate.Month, 1).AddMonths(1).AddDays(2), perShare);

        return new Dictionary<string, IReadOnlyList<Core.DividendRecord>>
        {
            ["SEED07"] = monthEnds.Select(d => Record("SEED07", d, 20m)).ToList(),
            ["SEED06"] = monthEnds.Where(d => d.Month % 3 == 0).Select(d => Record("SEED06", d, 150m)).ToList(),
            ["SEED02"] = monthEnds.Where(d => d.Month % 3 == 1).Select(d => Record("SEED02", d, 60m)).ToList(),
        };
    }

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
