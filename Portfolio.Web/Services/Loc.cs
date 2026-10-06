using System.Globalization;
using Portfolio.Core;

namespace Portfolio.Web.Services;

// 화면 언어 (F-12). 화면 연결(circuit)·요청마다 하나씩 있고, 고른 언어는 쿠키에 적어 둔다.
// 원문은 한국어이고, 영어일 때만 번역표(Loc.En.cs)에서 찾아 바꾼다: T["보유 종목"] → "Holdings"
// 계좌·그룹 이름과 종목명은 입력된 데이터라 번역하지 않는다.
public sealed partial class Loc
{
    public const string Korean = "ko";
    public const string English = "en";
    public const string CookieName = "portfolio.lang";
    public const string SelectPath = "/language";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly string[] Months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    // 번역표 전체 (번역 누락 검사용)
    public static IReadOnlyDictionary<string, string> EnglishTable => En;

    public string Language { get; set; } = Korean;
    public bool IsEnglish => Language == English;

    // 번역표에 없는 문구(입력된 이름 등)는 그대로 돌려준다
    public string this[string? korean] =>
        korean is null ? "" : IsEnglish && En.TryGetValue(korean, out string? english) ? english : korean;

    // 그룹·차트 항목 이름: 앱이 만든 이름(미분류·현금·기타)만 번역하고, 입력된 그룹 이름은 그대로 둔다
    public string Name(string? name) =>
        name is PortfolioCalculator.UnclassifiedGroupName or PortfolioCalculator.CashName or PortfolioViewModel.OthersName
            ? this[name] : name ?? "";

    // 값이 들어가는 문구: T.F("{0}개 종목", count)
    public string F(string koreanFormat, params object?[] args) => string.Format(Inv, this[koreanFormat], args);

    // 1234567 → "1,234,567원" / "₩1,234,567"
    public string Won(decimal value) => IsEnglish ? WithSign(value, "₩" + DisplayFormat.Number(Math.Abs(value))) : DisplayFormat.Won(value);

    // 4500000 → "+4,500,000원" / "+₩4,500,000"
    public string SignedWon(decimal value) => IsEnglish
        ? (value >= 0 ? "+" : "-") + "₩" + DisplayFormat.Number(Math.Abs(value))
        : DisplayFormat.SignedWon(value);

    // 좁은 자리용: "1.2억 원" / "₩120M"
    public string ShortWon(decimal value)
    {
        if (!IsEnglish) return DisplayFormat.ShortWon(value);
        if (value >= 1_000_000_000m) return "₩" + Scaled(value / 1_000_000_000m) + "B";
        if (value >= 1_000_000m) return "₩" + Scaled(value / 1_000_000m) + "M";
        if (value >= 10_000m) return "₩" + Scaled(value / 1_000m) + "K";
        return Won(value);
    }

    // "1,600주" / "1,600 sh"
    public string Shares(decimal count) => DisplayFormat.Number(count) + (IsEnglish ? " sh" : "주");

    // 한국 시간 기준 "9월 28일" / "Sep 28"
    public string MonthDay(DateTimeOffset value)
    {
        if (!IsEnglish) return DisplayFormat.MonthDay(value);
        var kst = value.ToOffset(MarketSchedule.Kst);
        return $"{Months[kst.Month - 1]} {kst.Day}";
    }

    public static string Normalize(string? language) => language == English ? English : Korean;

    public static string ReadCookie(HttpContext context) => Normalize(context.Request.Cookies[CookieName]);

    // 언어를 바꾸는 주소: 쿠키를 고쳐 쓰고 보던 화면으로 돌아온다
    public static string SelectUrl(string language, string returnPath) =>
        $"{SelectPath.TrimStart('/')}/{Normalize(language)}?returnUrl={Uri.EscapeDataString(returnPath)}";

    private static string WithSign(decimal value, string text) => value < 0 ? "-" + text : text;

    private static string Scaled(decimal value) =>
        Math.Round(value, value >= 100m ? 0 : 1, MidpointRounding.AwayFromZero).ToString("#,0.#", Inv);
}
