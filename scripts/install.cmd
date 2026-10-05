@echo off
rem 실행 파일을 만들어 설치하고 바탕화면 바로가기를 만듭니다. 앱을 고친 뒤에도 다시 실행하면 새 버전으로 바뀝니다.
chcp 65001 >nul
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"
pause
