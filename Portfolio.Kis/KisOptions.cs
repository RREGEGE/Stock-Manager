namespace Portfolio.Kis;

public enum KisEnvironment { Real, Mock }

// appsettings.json의 "Kis" 섹션. 키 값은 비워 두고, 실제 값은 저장소 밖 데이터 폴더의 settings.json에 넣는다 (설계서 7.3, 10.1.1).
public sealed class KisOptions
{
    public const string SectionName = "Kis";

    public KisEnvironment Environment { get; set; } = KisEnvironment.Real;
    public string AppKey { get; set; } = "";
    public string AppSecret { get; set; } = "";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(AppKey) && !string.IsNullOrWhiteSpace(AppSecret);

    public Uri BaseAddress => Environment == KisEnvironment.Real
        ? new Uri("https://openapi.koreainvestment.com:9443")
        : new Uri("https://openapivts.koreainvestment.com:29443");
}
