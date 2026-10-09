param(
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath
)
$ErrorActionPreference = "Stop"
$fullPath = [System.IO.Path]::GetFullPath($ExecutablePath)
if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) { throw "Client executable not found: $fullPath" }
$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
New-Item -Path $runKey -Force | Out-Null
Set-ItemProperty -Path $runKey -Name "SafeNotify" -Value "`"$fullPath`""
Write-Output "SafeNotify will start when this Windows user signs in."
