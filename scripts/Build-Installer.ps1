param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = "1.0.4"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publishScript = Join-Path $PSScriptRoot "Publish.ps1"
$installerScript = Join-Path $PSScriptRoot "SafeNotify.iss"
$compiler = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
if ([string]::IsNullOrWhiteSpace($compiler)) {
    $candidates = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe")
    $compiler = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($compiler)) {
    throw "Inno Setup 6 is required. Install it from https://jrsoftware.org/isinfo.php, then run this script again."
}
& $publishScript
if ($LASTEXITCODE -ne 0) { throw "Publishing failed with exit code $LASTEXITCODE." }
& $compiler "/DAppVersion=$Version" $installerScript
if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed with exit code $LASTEXITCODE." }
$installer = Join-Path $root "publish\installer\SafeNotify-Setup-$Version-win-x64.exe"
Write-Output $installer
