namespace Portfolio.Core;

// 예상 배당금 (F-16). 최근 12개월에 실제로 지급된(또는 지급이 정해진) 배당이 앞으로도 같은 달, 같은 금액으로 나온다고 본다.

// 배당 1건: 기준일(이날 보유해야 받음), 지급일, 주당 현금 배당금
public sealed record DividendRecord(string SymbolCode, DateOnly RecordDate, DateOnly? PayDate, decimal PerShare, string Kind = "");

// 계좌의 세금 방식. 배당을 받을 때 원천징수되는지만 구분한다.
public enum AccountTaxType
{
    Taxable = 0,   // 일반 계좌: 배당소득세 15.4% 원천징수
    Isa = 1,       // ISA: 계좌 안에서는 원천징수 없음 (해지할 때 과세)
    Pension = 2,   // 연금저축·IRP: 과세 이연 (연금을 받을 때 과세)
}

// 배당 이력 출처. 받아오지 못하면 예외를 던진다.
public interface IDividendProvider
{
    Task<IReadOnlyList<DividendRecord>> GetAsync(string symbolCode, DateOnly from, DateOnly to, CancellationToken ct = default);
}

public sealed record HoldingDividend(
    string SymbolCode, string SymbolName, long Quantity,
    decimal AnnualPerShare,          // 최근 12개월 주당 배당금 합
    decimal AnnualAmount,            // × 보유 수량 (세전)
    IReadOnlyList<int> PayMonths,    // 지급 달 (1~12, 오름차순)
    decimal EvalAmount);             // 수익률 계산용 평가금액

public sealed record MonthlyDividend(int Year, int Month, decimal Amount);

public sealed record DividendForecast(
    IReadOnlyList<HoldingDividend> Holdings,   // 배당이 있는 종목만, 연 예상 금액이 큰 순
    IReadOnlyList<MonthlyDividend> Months,     // 이번 달부터 12개월
    decimal AnnualAmount,                      // 세전
    decimal AnnualAfterTax,
    decimal MonthlyAverage,                    // 세전 연간 ÷ 12
    decimal MonthlyAverageAfterTax,
    decimal Yield,                             // 연 배당(세전) ÷ 보유 종목 평가금액
    AccountTaxType TaxType)
{
    public bool HasDividends => AnnualAmount > 0;
}

public static class DividendEstimator
{
    public const decimal WithholdingRate = 0.154m;   // 배당소득세 14% + 지방소득세 1.4%
    public const int LookbackMonths = 12;

    public static decimal AfterTax(decimal amount, AccountTaxType taxType) =>
        taxType == AccountTaxType.Taxable ? Math.Round(amount * (1m - WithholdingRate), 0, MidpointRounding.AwayFromZero) : amount;

    // 최근 12개월 = 기준일이 (오늘 − 12개월, 오늘] 안. 기준일은 지났고 지급일이 앞날인 배당(받을 것이 정해진 배당)도 포함한다.
    public static IReadOnlyList<DividendRecord> Recent(IEnumerable<DividendRecord> records, DateOnly today) =>
        records.Where(r => r.PerShare > 0 && r.RecordDate > today.AddMonths(-LookbackMonths) && r.RecordDate <= today)
            .ToList();

    // 지급일이 없으면 기준일 다음 달로 본다
    private static DateOnly PayDateOf(DividendRecord r) => r.PayDate ?? r.RecordDate.AddMonths(1);

    public static DividendForecast Estimate(
        IReadOnlyList<HoldingView> holdings, IReadOnlyDictionary<string, IReadOnlyList<DividendRecord>> records,
        DateOnly today, AccountTaxType taxType)
    {
        var firstMonth = new DateOnly(today.Year, today.Month, 1);
        var months = Enumerable.Range(0, 12).Select(i => firstMonth.AddMonths(i)).ToList();
        var monthly = months.ToDictionary(m => m, _ => 0m);
        var perHolding = new List<HoldingDividend>();

        foreach (var h in holdings)
        {
            if (!records.TryGetValue(h.SymbolCode, out var list)) continue;
            var recent = Recent(list, today);
            if (recent.Count == 0) continue;

            foreach (var r in recent)
            {
                // 이번 달보다 앞선 지급은 1년 뒤에 다시 나온다고 본다. 이번 달 지급은 이미 받았어도 이번 달에 센다.
                var pay = PayDateOf(r);
                var month = new DateOnly(pay.Year, pay.Month, 1);
                while (month < firstMonth) month = month.AddYears(1);
                if (monthly.ContainsKey(month)) monthly[month] += r.PerShare * h.Quantity;
            }

            decimal perShare = recent.Sum(r => r.PerShare);
            perHolding.Add(new HoldingDividend(h.SymbolCode, h.SymbolName, h.Quantity, perShare, perShare * h.Quantity,
                recent.Select(r => PayDateOf(r).Month).Distinct().Order().ToList(), h.EstimatedAmount));
        }

        decimal annual = perHolding.Sum(x => x.AnnualAmount);
        decimal eval = holdings.Sum(h => h.EstimatedAmount);
        return new DividendForecast(
            perHolding.OrderByDescending(x => x.AnnualAmount).ThenBy(x => x.SymbolCode, StringComparer.Ordinal).ToList(),
            months.Select(m => new MonthlyDividend(m.Year, m.Month, monthly[m])).ToList(),
            annual, AfterTax(annual, taxType),
            annual / 12m, AfterTax(annual, taxType) / 12m,
            eval > 0 ? annual / eval : 0m,
            taxType);
    }
}

// KIS 없이 고정 배당을 돌려주는 가짜 구현 (개발용 화면 확인)
public sealed class FakeDividendProvider(IReadOnlyDictionary<string, IReadOnlyList<DividendRecord>> records) : IDividendProvider
{
    public Task<IReadOnlyList<DividendRecord>> GetAsync(string symbolCode, DateOnly from, DateOnly to, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DividendRecord>>(records.TryGetValue(symbolCode, out var list)
            ? list.Where(r => r.RecordDate >= from && r.RecordDate <= to).ToList()
            : []);
}
