namespace Portfolio.Data;

// 설계서 5.1 테이블 (1단계 범위: ApiToken은 2단계, DailySnapshot은 F-08에서 추가)

public class Holding
{
    public string SymbolCode { get; set; } = "";
    public string SymbolName { get; set; } = "";
    public long Quantity { get; set; }
    public decimal AvgPrice { get; set; }
    public int? GroupId { get; set; }   // null = 미분류
    public AssetGroup? Group { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class SymbolMaster
{
    public string SymbolCode { get; set; } = "";
    public string SymbolName { get; set; } = "";
    public string Market { get; set; } = "";
}

public class CashBalance
{
    public int Id { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class AssetGroup
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Color { get; set; } = "";
    public int SortOrder { get; set; }
    public decimal TargetWeight { get; set; }   // 0~1
}

public class PriceCache
{
    public string SymbolCode { get; set; } = "";
    public decimal Price { get; set; }
    public decimal PrevClose { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
}

public class AppSetting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}
