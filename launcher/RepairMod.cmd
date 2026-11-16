@echo off
setlocal
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0RhodesMod.ps1" -Action Repair
set "RhodesExit=%errorlevel%"
exit /b %RhodesExit%
