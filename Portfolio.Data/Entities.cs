namespace Portfolio.Data;

// 설계서 5.1 테이블 (DailySnapshot은 F-08에서 추가)

// 계좌 (F-11). 이름과 표시 순서만 저장하고 계좌번호는 저장하지 않는다.
public class TradingAccount
{
    public const int DefaultId = 1;          // 처음부터 있는 계좌
    public const string DefaultName = "기본 계좌";

    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
}

public class Holding
{
    public int AccountId { get; set; } = TradingAccount.DefaultId;   // 같은 종목을 계좌마다 따로 보유할 수 있다
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
    public int AccountId { get; set; } = TradingAccount.DefaultId;   // 계좌마다 1행
    public decimal Amount { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class AssetGroup
{
    public int Id { get; set; }
    public int AccountId { get; set; } = TradingAccount.DefaultId;   // 그룹과 목표 비중은 계좌마다 따로 둔다
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

public class ApiToken
{
    public int Id { get; set; }
    public string AccessToken { get; set; } = "";   // Data Protection으로 암호화한 값 (설계서 7.2)
    public DateTimeOffset ExpiresAt { get; set; }
}

public class AppSetting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}
