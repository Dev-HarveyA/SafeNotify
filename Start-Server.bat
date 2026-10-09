@echo off
setlocal
cd /d "%~dp0"
set "SafeNotify__DataDirectory=%LOCALAPPDATA%\SafeNotify\Server"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Start-Server.ps1" %*
if errorlevel 1 pause
endlocal
