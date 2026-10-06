using System.Globalization;
using Portfolio.Core;

namespace Portfolio.Web.Services;

public sealed record ChartSegment(string Key, string Name, string Color, decimal Ratio);

public sealed record TargetRow(string Name, string Color, decimal Current, decimal Target)
{
    public decimal Diff => Current - Target;
    // ±0.05%p 이내면 '목표 도달' (설계서 9.3)
    public bool IsOver => Diff > 0.0005m;
    public bool IsUnder => Diff < -0.0005m;
    public string DiffText(Loc t) => IsOver ? DisplayFormat.SignedPoint1(Diff) + t[" 초과"]
        : IsUnder ? DisplayFormat.SignedPoint1(Diff) + t[" 부족"] : t["목표 도달"];
    public string Tone => IsOver ? "over" : IsUnder ? "under" : "ok";
}

public sealed record HoldingRow(
    HoldingView View, string GroupColor, string SymbolColor, decimal Weight, DateTimeOffset? UpdatedAt, int? GroupId)
{
    // 현재가를 받지 못한 종목은 손익률을 계산하지 않는다 (null)
    public decimal? ReturnRate => View.HasPrice && View.AvgPrice > 0 ? (View.CurrentPrice - View.AvgPrice) / View.AvgPrice : null;
}

// 색상 규칙 (설계서 9.2): 그룹 색은 DB 값, 종목 색은 그룹 색의 음영
public static class ChartColors
{
    public const string Unclassified = "#9AA0A8";
    public const string Cash = "#C9C9C2";
    public const string Others = "#C9C9C2";

    // 초기 그룹 3개의 종목 음영은 목업 값을 그대로 쓴다
    private static readonly Dictionary<string, string[]> KnownShades = new(StringComparer.OrdinalIgnoreCase)
    {
        ["#23395B"] = ["#23395B", "#3F5B86", "#7189AE"],
        ["#E08A2E"] = ["#E08A2E", "#F2B56E"],
        ["#6BB3A8"] = ["#4E9B90", "#A3D3CB"],
    };

    public static string Shade(string groupColor, int index, int count)
    {
        if (KnownShades.TryGetValue(groupColor, out var known) && count <= known.Length)
            return known[index];
        if (count <= 1 || !TryParse(groupColor, out var rgb)) return groupColor;

        // 그 밖의 색·종목 수: 그룹 색에서 흰색 쪽으로 최대 60%까지 단계적으로 밝게
        double t = 0.6 * index / (count - 1);
        return Hex(Mix(rgb.R, t), Mix(rgb.G, t), Mix(rgb.B, t));
    }

    // 색 위에 올릴 글자색: 대비가 더 큰 쪽 (주식=흰색, 채권·배당=#1B1F24)
    public static string TextOn(string background)
    {
        if (!TryParse(background, out var rgb)) return "#FFFFFF";
        double l = Luminance(rgb.R, rgb.G, rgb.B);
        double dark = Luminance(0x1B, 0x1F, 0x24);
        return (1.05) / (l + 0.05) >= (l + 0.05) / (dark + 0.05) ? "#FFFFFF" : "#1B1F24";
    }

    private static int Mix(int channel, double t) => (int)Math.Round(channel + (255 - channel) * t);
    private static string Hex(int r, int g, int b) => $"#{r:X2}{g:X2}{b:X2}";

    private static bool TryParse(string color, out (int R, int G, int B) rgb)
    {
        rgb = default;
        if (color is not { Length: 7 } || color[0] != '#') return false;
        if (!int.TryParse(color.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v)) return false;
        rgb = (v >> 16 & 0xFF, v >> 8 & 0xFF, v & 0xFF);
        return true;
    }

    private static double Luminance(int r, int g, int b)
    {
        static double C(int c)
        {
            double s = c / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * C(r) + 0.7152 * C(g) + 0.0722 * C(b);
    }
}

// PortfolioState → 화면 표시용 값 (대시보드·보유 종목·그룹 관리 공용)
public sealed class PortfolioViewModel
{
    public const int MaxSymbolSegments = 10;   // 넘으면 상위 9개 + '기타' (설계서 6장)
    public const string OthersName = "기타";

    public PortfolioState State { get; }
    public IReadOnlyList<HoldingRow> Rows { get; }
    public IReadOnlyList<ChartSegment> GroupSegments { get; }
    public IReadOnlyList<ChartSegment> SymbolSegments { get; }
    public IReadOnlyList<TargetRow> TargetRows { get; }

    public PortfolioViewModel(PortfolioState state)
    {
        State = state;
        var summary = state.Summary;
        decimal total = summary.TotalAmount;
        decimal evalTotal = summary.EvalAmount;
        decimal Ratio(decimal amount, decimal sum) => sum > 0 ? amount / sum : 0m;

        var groupOrder = state.Groups.Select((g, i) => (g.Name, i)).ToDictionary(x => x.Name, x => x.i);
        var groupColor = state.Groups.ToDictionary(g => g.Name, g => g.Color);
        string ColorOf(string groupName) => groupColor.GetValueOrDefault(groupName, ChartColors.Unclassified);

        // 종목 순서: 그룹 순서 → 평가금액 큰 순 → 종목코드. 종목 색은 그룹 안 순번으로 정한다.
        var ordered = state.Snapshot.Holdings
            .OrderBy(h => groupOrder.GetValueOrDefault(h.GroupName, int.MaxValue))
            .ThenByDescending(h => h.EstimatedAmount)
            .ThenBy(h => h.SymbolCode, StringComparer.Ordinal)
            .ToList();
        var rows = new List<HoldingRow>();
        foreach (var group in ordered.GroupBy(h => h.GroupName))
        {
            var members = group.ToList();
            for (int i = 0; i < members.Count; i++)
            {
                var h = members[i];
                state.HoldingRows.TryGetValue(h.SymbolCode, out var entity);
                rows.Add(new HoldingRow(h, ColorOf(h.GroupName), ChartColors.Shade(ColorOf(h.GroupName), i, members.Count),
                    Ratio(h.EstimatedAmount, total), entity?.UpdatedAt, entity?.GroupId));
            }
        }
        Rows = rows;

        // 그룹 도넛: 그룹 순서대로, 미분류, (포함 설정 시) 현금
        var groupSegments = new List<ChartSegment>();
        foreach (var g in state.Groups)
        {
            decimal amount = rows.Where(r => r.View.GroupName == g.Name).Sum(r => r.View.EstimatedAmount);
            groupSegments.Add(new ChartSegment(g.Name, g.Name, g.Color, Ratio(amount, total)));
        }
        decimal unclassified = rows.Where(r => !groupOrder.ContainsKey(r.View.GroupName)).Sum(r => r.View.EstimatedAmount);
        if (unclassified > 0 || state.UnclassifiedCount > 0)
            groupSegments.Add(new ChartSegment(PortfolioCalculator.UnclassifiedGroupName,
                PortfolioCalculator.UnclassifiedGroupName, ChartColors.Unclassified, Ratio(unclassified, total)));
        if (summary.Cash is { } cash)
            groupSegments.Add(new ChartSegment(PortfolioCalculator.CashName, PortfolioCalculator.CashName, ChartColors.Cash, cash.Weight));
        GroupSegments = groupSegments;

        // 종목 도넛: 10개를 넘으면 비중 상위 9개 + 기타
        var symbolSegments = rows
            .Select(r => new ChartSegment(r.View.SymbolCode, r.View.SymbolName, r.SymbolColor, r.Weight)).ToList();
        if (symbolSegments.Count > MaxSymbolSegments)
        {
            var top = symbolSegments.OrderByDescending(s => s.Ratio).Take(MaxSymbolSegments - 1)
                .Select(s => s.Key).ToHashSet();
            decimal rest = symbolSegments.Where(s => !top.Contains(s.Key)).Sum(s => s.Ratio);
            symbolSegments = symbolSegments.Where(s => top.Contains(s.Key)).ToList();
            symbolSegments.Add(new ChartSegment(OthersName, OthersName, ChartColors.Others, rest));
        }
        if (summary.Cash is { } cash2)
            symbolSegments.Add(new ChartSegment(PortfolioCalculator.CashName, PortfolioCalculator.CashName, ChartColors.Cash, cash2.Weight));
        SymbolSegments = symbolSegments;

        // 목표 대비: 추가매수 계산과 같은 기준(보유종목 평가금액 합, 예수금 제외)으로 비교한다 (설계서 5.5)
        TargetRows = state.Groups
            .Select(g => new TargetRow(g.Name, g.Color,
                Ratio(rows.Where(r => r.View.GroupName == g.Name).Sum(r => r.View.EstimatedAmount), evalTotal), g.TargetWeight))
            .ToList();
    }

    public string MarketText(Loc t) => t[State.MarketOpen ? "장중" : "장 마감"] + " · "
        + (State.PricedAt is { } at ? t.F("{0} 시세 갱신", DisplayFormat.Time(at)) : t["시세 없음"]);

    public string MarketTextShort(Loc t) => State.PricedAt is { } at ? t.F("{0} 갱신", DisplayFormat.Time(at)) : t["시세 없음"];

    // conic-gradient(#23395B 0 60%, #E08A2E 60% 80%, ...)
    public static string ConicGradient(IReadOnlyList<ChartSegment> segments)
    {
        var visible = segments.Where(s => s.Ratio > 0).ToList();
        if (visible.Count == 0) return "var(--bg-track)";   // 빈 도넛: 화면 모드에 맞는 바탕색

        decimal sum = visible.Sum(s => s.Ratio);
        var parts = new List<string>();
        decimal start = 0m;
        for (int i = 0; i < visible.Count; i++)
        {
            decimal end = i == visible.Count - 1 ? 100m : start + visible[i].Ratio / sum * 100m;
            parts.Add($"{visible[i].Color} {Pct(start)} {Pct(end)}");
            start = end;
        }
        return $"conic-gradient({string.Join(", ", parts)})";

        static string Pct(decimal v) => v == 0m ? "0" : Math.Round(v, 2).ToString("0.##", CultureInfo.InvariantCulture) + "%";
    }

    public static string CssPercent(decimal ratio) =>
        Math.Round(Math.Clamp(ratio, 0m, 1m) * 100m, 2).ToString("0.##", CultureInfo.InvariantCulture) + "%";
}
