@echo off
rem 포트폴리오 대시보드 실행 (실제 데이터, 이 PC 안에서만 접속: http://127.0.0.1:5137)
rem 다른 기기에서는 tailscale serve로 접속합니다. docs\operations.md 참고
chcp 65001 >nul
cd /d "%~dp0.."
dotnet run --project Portfolio.Web -c Release --no-launch-profile
pause
