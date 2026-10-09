@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; $targets = @(Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'SafeNotify.Server.exe' -or ($_.Name -eq 'dotnet.exe' -and $_.CommandLine -match 'SafeNotify\.Server(\.dll|\.csproj|\.exe)') }); if ($targets.Count -eq 0) { Write-Output 'SafeNotify server is not running.'; exit 0 }; foreach ($target in $targets) { Stop-Process -Id $target.ProcessId -Force }; Write-Output 'SafeNotify server stopped.'"
pause
endlocal
