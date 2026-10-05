# 포트폴리오 실행 파일을 만들어 설치하고 바탕화면 바로가기를 만든다.
#   설치 위치: %LOCALAPPDATA%\Portfolio\app   (데이터 폴더 %LOCALAPPDATA%\Portfolio 아래)
#   다시 실행하면 새 버전으로 덮어쓴다. 데이터(DB, 백업, settings.json)는 건드리지 않는다.
param(
    [string]$AppDirectory = (Join-Path $env:LOCALAPPDATA 'Portfolio\app'),
    [switch]$NoShortcut
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $AppDirectory 'Portfolio.Web.exe'

if (Get-Process -Name 'Portfolio.Web' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $exe }) {
    Write-Host '포트폴리오가 실행 중입니다. 검은 창을 닫아 앱을 끈 뒤 다시 실행하세요.' -ForegroundColor Yellow
    exit 1
}

Write-Host '실행 파일을 만드는 중입니다. 처음에는 몇 분 걸릴 수 있습니다...'
dotnet publish (Join-Path $repo 'Portfolio.Web') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:DebugType=none -o $AppDirectory --nologo -v quiet
if ($LASTEXITCODE -ne 0) {
    Write-Host '실행 파일을 만들지 못했습니다. 위 오류를 확인하세요.' -ForegroundColor Red
    exit 1
}

if (-not $NoShortcut) {
    $shortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) '포트폴리오.lnk'
    $shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $exe
    $shortcut.WorkingDirectory = $AppDirectory
    $shortcut.IconLocation = "$exe,0"
    $shortcut.Description = '포트폴리오 대시보드'
    $shortcut.Save()
    Write-Host "바탕화면에 '포트폴리오' 바로가기를 만들었습니다."
}

Write-Host "설치 완료: $exe" -ForegroundColor Green
Write-Host '바탕화면의 포트폴리오 아이콘을 더블클릭하면 켜지고 화면이 자동으로 열립니다.'
