using Microsoft.EntityFrameworkCore;

namespace Portfolio.Data;

// 그룹 관리 (설계서 2장 F-04·F-06, 6장 그룹 관리)
public sealed class GroupRepository(IDbContextFactory<PortfolioDbContext> dbFactory)
{
    public const string DefaultColor = "#7A8088";

    public async Task<List<AssetGroup>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.AssetGroups.AsNoTracking().OrderBy(g => g.SortOrder).ThenBy(g => g.Id).ToListAsync(ct);
    }

    // 새 그룹은 목표 0%로 맨 뒤에 추가한다 (목표 합계는 그대로 유지)
    public async Task<AssetGroup> AddAsync(string name, string? color = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        name = await ValidateNameAsync(db, name, exceptId: null, ct);
        int nextOrder = (await db.AssetGroups.MaxAsync(g => (int?)g.SortOrder, ct) ?? 0) + 1;
        var group = new AssetGroup { Name = name, Color = NormalizeColor(color), SortOrder = nextOrder, TargetWeight = 0m };
        db.AssetGroups.Add(group);
        await db.SaveChangesAsync(ct);
        return group;
    }

    public async Task UpdateAsync(int id, string name, string color, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var group = await db.AssetGroups.FindAsync([id], ct) ?? throw new KeyNotFoundException("그룹을 찾을 수 없습니다.");
        group.Name = await ValidateNameAsync(db, name, id, ct);
        group.Color = NormalizeColor(color);
        await db.SaveChangesAsync(ct);
    }

    // 표시 순서를 한 칸 위(-1) 또는 아래(+1)로 옮긴다
    public async Task MoveAsync(int id, int direction, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var groups = await db.AssetGroups.OrderBy(g => g.SortOrder).ThenBy(g => g.Id).ToListAsync(ct);
        int index = groups.FindIndex(g => g.Id == id);
        int target = index + Math.Sign(direction);
        if (index < 0 || target < 0 || target >= groups.Count) return;

        (groups[index], groups[target]) = (groups[target], groups[index]);
        for (int i = 0; i < groups.Count; i++) groups[i].SortOrder = i + 1;
        await db.SaveChangesAsync(ct);
    }

    // 삭제 시 소속 종목은 미분류로 이동한다 (FK SetNull)
    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var group = await db.AssetGroups.FindAsync([id], ct);
        if (group is null) return;
        // SQLite 연결 설정과 무관하게 동작하도록 소속 종목을 직접 미분류로 옮긴다
        await db.Holdings.Where(h => h.GroupId == id).ExecuteUpdateAsync(s => s.SetProperty(h => h.GroupId, (int?)null), ct);
        db.AssetGroups.Remove(group);
        await db.SaveChangesAsync(ct);
    }

    public static bool IsTargetSumValid(IEnumerable<decimal> targetWeights) =>
        Math.Abs(targetWeights.Sum() - 1m) <= 0.00001m;

    // 그룹별 목표 비중(0~1) 저장. 합계가 100%가 아니면 저장하지 않는다 (설계서 6장, AC-11).
    public async Task SaveTargetsAsync(IReadOnlyDictionary<int, decimal> targetWeights, CancellationToken ct = default)
    {
        if (targetWeights.Values.Any(w => w < 0m))
            throw new ArgumentException("목표 비중은 0% 이상이어야 합니다.");
        if (!IsTargetSumValid(targetWeights.Values))
            throw new ArgumentException("목표 비중 합계가 100%가 아닙니다.");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var groups = await db.AssetGroups.ToListAsync(ct);
        if (groups.Count != targetWeights.Count || groups.Any(g => !targetWeights.ContainsKey(g.Id)))
            throw new ArgumentException("모든 그룹의 목표 비중을 함께 저장해야 합니다.");

        foreach (var g in groups) g.TargetWeight = targetWeights[g.Id];
        await db.SaveChangesAsync(ct);
    }

    private static async Task<string> ValidateNameAsync(PortfolioDbContext db, string name, int? exceptId, CancellationToken ct)
    {
        name = (name ?? "").Trim();
        if (name.Length is 0 or > 50)
            throw new ArgumentException("그룹 이름은 1~50자여야 합니다.");
        if (name == Core.PortfolioCalculator.UnclassifiedGroupName)
            throw new ArgumentException("'미분류'는 그룹 이름으로 쓸 수 없습니다.");
        if (await db.AssetGroups.AnyAsync(g => g.Name == name && g.Id != exceptId, ct))
            throw new ArgumentException("같은 이름의 그룹이 이미 있습니다.");
        return name;
    }

    private static string NormalizeColor(string? color) =>
        color is { Length: 7 } && color[0] == '#' && color[1..].All(Uri.IsHexDigit) ? color.ToUpperInvariant() : DefaultColor;
}
