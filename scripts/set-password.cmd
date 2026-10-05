@echo off
rem 로그인 비밀번호 설정·변경. 앱을 실행하는 이 PC에서만 실행합니다.
chcp 65001 >nul
cd /d "%~dp0.."
dotnet run --project Portfolio.Web -c Release --no-launch-profile -- set-password
pause
