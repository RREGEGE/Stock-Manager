namespace Portfolio.Web.Hosting;

// 운영 데이터 위치. 실제 보유 내역·KIS 키가 저장소 폴더에 섞이지 않도록 저장소 밖 폴더를 쓴다.
// 설치 스크립트(scripts/install.ps1)의 기본 위치는 저장소 옆의 Portfolio 폴더다 (app 실행 파일, data 데이터).
//   {DataDirectory}/portfolio.db     실제 DB
//   {DataDirectory}/settings.json    KIS 키 등 이 PC 전용 설정 (평문, 저장소에 넣지 않음)
//   {DataDirectory}/keys/            토큰 암호화 키 (Data Protection)
//   {DataDirectory}/backups/         일일 백업
public sealed class AppPaths
{
    public const string SettingsFileName = "settings.json";

    public string DataDirectory { get; }
    public string DatabasePath => Path.Combine(DataDirectory, "portfolio.db");
    public string SettingsPath => Path.Combine(DataDirectory, SettingsFileName);
    public string KeysDirectory => Path.Combine(DataDirectory, "keys");
    public string BackupDirectory => Path.Combine(DataDirectory, "backups");

    public AppPaths(string dataDirectory) => DataDirectory = Path.GetFullPath(dataDirectory);

    // 설정값 DataDirectory가 없으면 실행 파일 폴더 옆의 data 폴더를 쓴다.
    //   설치본: <설치 폴더>\app\Portfolio.Web.exe → <설치 폴더>\data
    // 사용자 프로필(C 드라이브)에는 아무것도 두지 않는다.
    public static AppPaths FromConfiguration(IConfiguration configuration)
    {
        string? configured = configuration["DataDirectory"];
        string dir = !string.IsNullOrWhiteSpace(configured)
            ? Environment.ExpandEnvironmentVariables(configured)
            : DefaultDataDirectory(AppContext.BaseDirectory);
        return new AppPaths(dir);
    }

    public static string DefaultDataDirectory(string executableDirectory) =>
        Path.GetFullPath(Path.Combine(executableDirectory, "..", "data"));

    // 처음 실행할 때 폴더와 빈 설정 파일을 만들어 둔다 (키 이름만 있고 값은 비어 있음)
    public void EnsureCreated()
    {
        Directory.CreateDirectory(DataDirectory);
        if (!File.Exists(SettingsPath))
        {
            File.WriteAllText(SettingsPath, """
                {
                  "Kis": {
                    "Environment": "Real",
                    "AppKey": "",
                    "AppSecret": ""
                  }
                }

                """);
        }
    }
}
