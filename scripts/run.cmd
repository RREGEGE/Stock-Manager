@echo off
rem 설치하지 않고 저장소에서 바로 실행 (.NET SDK 필요). 데이터는 설치본과 같은 폴더(저장소 옆 Portfolio\data)를 씁니다.
rem 평소에는 바탕화면의 포트폴리오 아이콘을 쓰세요. docs\operations.md 참고
chcp 65001 >nul
cd /d "%~dp0.."
for %%I in ("%~dp0..\..\Portfolio\data") do set "DATA=%%~fI"
dotnet run --project Portfolio.Web -c Release --no-launch-profile -- --DataDirectory "%DATA%"
pause
