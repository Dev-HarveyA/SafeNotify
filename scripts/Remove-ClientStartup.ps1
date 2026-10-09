$ErrorActionPreference = "Stop"
$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
if (Test-Path -LiteralPath $runKey) { Remove-ItemProperty -Path $runKey -Name "SafeNotify" -ErrorAction SilentlyContinue }
Write-Output "SafeNotify auto-start has been removed for this Windows user."
