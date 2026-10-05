namespace Portfolio.Web.Hosting;

// 운영 데이터 위치. 실제 보유 내역·KIS 키가 저장소 폴더에 섞이지 않도록 저장소 밖 폴더를 쓴다.
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

    // 설정값 DataDirectory가 없으면 %LOCALAPPDATA%\Portfolio (관리자 권한 없이 쓸 수 있는 사용자 폴더)
    public static AppPaths FromConfiguration(IConfiguration configuration)
    {
        string? configured = configuration["DataDirectory"];
        string dir = !string.IsNullOrWhiteSpace(configured)
            ? Environment.ExpandEnvironmentVariables(configured)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Portfolio");
        return new AppPaths(dir);
    }

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
