@echo off
setlocal
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0RhodesMod.ps1" -Action Start
set "RhodesExit=%errorlevel%"
exit /b %RhodesExit%
