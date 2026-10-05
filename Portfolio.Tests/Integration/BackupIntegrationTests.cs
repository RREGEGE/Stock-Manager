namespace Portfolio.Tests.Integration;

public class BackupIntegrationTests
{
    [Fact]
    public async Task 앱을_켜면_데이터_폴더의_backups에_오늘_백업이_생긴다()
    {
        using var app = new TestApp().WithSetting("Backup:Enabled", "true");
        app.CreateBrowser();   // 앱 기동
        string backups = Path.Combine(app.DataDirectory, "backups");

        // 백그라운드 서비스가 시작 직후 1회 실행한다
        for (int i = 0; i < 100 && !(Directory.Exists(backups) && Directory.GetFiles(backups, "portfolio-*.db").Length > 0); i++)
            await Task.Delay(100);

        var file = Assert.Single(Directory.GetFiles(backups, "portfolio-*.db"));
        Assert.True(new FileInfo(file).Length > 0);
        Assert.True(File.Exists(Path.Combine(app.DataDirectory, "portfolio.db")));
        Assert.True(File.Exists(Path.Combine(app.DataDirectory, "settings.json")));
    }

    [Fact]
    public void 백업을_끄면_backups_폴더를_만들지_않는다()
    {
        using var app = new TestApp();   // TestApp 기본값: Backup:Enabled=false
        app.CreateBrowser();

        Assert.False(Directory.Exists(Path.Combine(app.DataDirectory, "backups")));
    }
}
