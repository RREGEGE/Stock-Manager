@echo off
rem 비밀번호를 잊었을 때 다시 정합니다. 앱을 끄고, 앱을 실행하는 이 PC에서만 실행합니다.
rem 평소의 가입·비밀번호 변경은 웹 화면에서 합니다.
chcp 65001 >nul
cd /d "%~dp0.."
for %%I in ("%~dp0..\..\Portfolio\data") do set "DATA=%%~fI"
dotnet run --project Portfolio.Web -c Release --no-launch-profile -- set-password --DataDirectory "%DATA%"
pause
