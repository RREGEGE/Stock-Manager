# 포트폴리오 실행 파일을 만들어 설치하고 바탕화면 바로가기를 만든다.
#   설치 위치(기본): 저장소 폴더 옆의 Portfolio 폴더  (예: D:\Program\Stock_Manager → D:\Program\Portfolio)
#     app\   실행 파일
#     data\  실제 DB, 백업, KIS 키 설정 파일(settings.json), 암호화 키
#   다시 실행하면 app\만 새 버전으로 덮어쓴다. data\는 건드리지 않는다.
#   다른 곳에 설치하려면: install.ps1 -InstallRoot "E:\어디든"
param(
    [string]$InstallRoot = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'Portfolio'),
    [switch]$NoShortcut
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$InstallRoot = [IO.Path]::GetFullPath($InstallRoot)
$appDirectory = Join-Path $InstallRoot 'app'
$dataDirectory = Join-Path $InstallRoot 'data'
$exe = Join-Path $appDirectory 'Portfolio.Web.exe'

if (Get-Process -Name 'Portfolio.Web' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $exe }) {
    Write-Host '포트폴리오가 실행 중입니다. 검은 창을 닫아 앱을 끈 뒤 다시 실행하세요.' -ForegroundColor Yellow
    exit 1
}

Write-Host "설치 위치: $InstallRoot"
Write-Host '실행 파일을 만드는 중입니다. 처음에는 몇 분 걸릴 수 있습니다...'
dotnet publish (Join-Path $repo 'Portfolio.Web') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:DebugType=none -o $appDirectory --nologo -v quiet
if ($LASTEXITCODE -ne 0) {
    Write-Host '실행 파일을 만들지 못했습니다. 위 오류를 확인하세요.' -ForegroundColor Red
    exit 1
}

# 실행 파일이 데이터 폴더를 찾도록 알려 준다 (운영 환경에서 자동으로 읽히는 설정 파일)
New-Item -ItemType Directory -Force -Path $dataDirectory | Out-Null
$settings = @{ DataDirectory = $dataDirectory } | ConvertTo-Json
[IO.File]::WriteAllText((Join-Path $appDirectory 'appsettings.Production.json'), $settings, (New-Object Text.UTF8Encoding($false)))

if (-not $NoShortcut) {
    $shortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) '포트폴리오.lnk'
    $shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $exe
    $shortcut.WorkingDirectory = $appDirectory
    $shortcut.IconLocation = "$exe,0"
    $shortcut.Description = '포트폴리오 대시보드'
    $shortcut.Save()
    Write-Host "바탕화면에 '포트폴리오' 바로가기를 만들었습니다."
}

Write-Host "설치 완료: $exe" -ForegroundColor Green
Write-Host "데이터 폴더: $dataDirectory"
Write-Host '바탕화면의 포트폴리오 아이콘을 더블클릭하면 켜지고 화면이 자동으로 열립니다.'
