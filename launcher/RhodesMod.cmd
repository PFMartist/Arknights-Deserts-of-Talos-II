@echo off
setlocal
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0RhodesMod.ps1" -Action Menu
set "RhodesExit=%errorlevel%"
if not "%RhodesExit%"=="0" pause
exit /b %RhodesExit%