using System.Globalization;

namespace Portfolio.Core;

// 화면 표시 형식 (설계서 9.3 표시 규칙). 문화권 설정과 무관하게 같은 결과를 낸다.
public static class DisplayFormat
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // 1234567 → "1,234,567"
    public static string Number(decimal value) =>
        Math.Round(value, 0, MidpointRounding.AwayFromZero).ToString("#,0", Inv);

    // 1234567 → "1,234,567원"
    public static string Won(decimal value) => Number(value) + "원";

    // 4500000 → "+4,500,000원"
    public static string SignedWon(decimal value) => (value >= 0 ? "+" : "") + Won(value);

    // 0.6 → "60.0%"
    public static string Percent1(decimal ratio) =>
        Math.Round(ratio * 100m, 1, MidpointRounding.AwayFromZero).ToString("0.0", Inv) + "%";

    // 0.0471 → "+4.71%"
    public static string SignedPercent2(decimal ratio) =>
        (ratio >= 0 ? "+" : "") + Math.Round(ratio * 100m, 2, MidpointRounding.AwayFromZero).ToString("0.00", Inv) + "%";

    // 0.1 → "+10.0%p", -0.1 → "-10.0%p"
    public static string SignedPoint1(decimal ratioDiff) =>
        (ratioDiff > 0 ? "+" : "") + Math.Round(ratioDiff * 100m, 1, MidpointRounding.AwayFromZero).ToString("0.0", Inv) + "%p";

    // 도넛 가운데 등 좁은 자리용: 100000000 → "1억 원", 123400000 → "1.2억 원", 5500000 → "550만 원"
    public static string ShortWon(decimal value)
    {
        if (value >= 100_000_000m)
            return Math.Round(value / 100_000_000m, 1, MidpointRounding.AwayFromZero).ToString("#,0.#", Inv) + "억 원";
        if (value >= 10_000m)
            return Math.Round(value / 10_000m, 0, MidpointRounding.AwayFromZero).ToString("#,0", Inv) + "만 원";
        return Won(value);
    }

    // 한국 시간 기준 "9월 28일"
    public static string MonthDay(DateTimeOffset value)
    {
        var kst = value.ToOffset(MarketSchedule.Kst);
        return $"{kst.Month}월 {kst.Day}일";
    }

    // 한국 시간 기준 "09:41"
    public static string Time(DateTimeOffset value) =>
        value.ToOffset(MarketSchedule.Kst).ToString("HH:mm", Inv);

    // "1,600" / "1600원" 같은 입력에서 숫자만 읽는다. 숫자가 없으면 0.
    public static decimal ParseNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0m;
        var digits = new string(text.Where(c => char.IsAsciiDigit(c) || c == '.').ToArray());
        return decimal.TryParse(digits, NumberStyles.Number, Inv, out var d) ? d : 0m;
    }
}
