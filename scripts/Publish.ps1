param(
    [ValidateSet("win-x64")]
    [string]$Runtime = "win-x64"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$serverProject = Join-Path $root "src\SafeNotify.Server\SafeNotify.Server.csproj"
$clientProject = Join-Path $root "src\SafeNotify.Client\SafeNotify.Client.csproj"
$serverOutput = Join-Path $root "publish\preproduction\server"
$clientOutput = Join-Path $root "publish\preproduction\client"
$publishDirectory = Join-Path $root "publish\preproduction"
if (Test-Path -LiteralPath $publishDirectory) {
    $resolvedPublishDirectory = [System.IO.Path]::GetFullPath($publishDirectory)
    $resolvedRoot = [System.IO.Path]::GetFullPath((Join-Path $root "publish"))
    if (-not $resolvedPublishDirectory.StartsWith($resolvedRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clear an output folder outside the SafeNotify publish directory."
    }
    Remove-Item -LiteralPath $resolvedPublishDirectory -Recurse -Force
}
dotnet publish $serverProject --configuration Release --runtime $Runtime --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false --output $serverOutput
if ($LASTEXITCODE -ne 0) { throw "Server publishing failed with exit code $LASTEXITCODE." }
dotnet publish $clientProject --configuration Release --runtime $Runtime --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false --output $clientOutput
if ($LASTEXITCODE -ne 0) { throw "Windows client publishing failed with exit code $LASTEXITCODE." }
$developmentSettings = Join-Path $serverOutput "appsettings.Development.json"
$iisConfiguration = Join-Path $serverOutput "web.config"
foreach ($generatedFile in @($developmentSettings, $iisConfiguration)) {
    if (Test-Path -LiteralPath $generatedFile) {
        Remove-Item -LiteralPath $generatedFile -Force
    }
}
Write-Output "Server: $serverOutput\SafeNotify.Server.exe"
Write-Output "Client: $clientOutput\SafeNotify.Client.exe"

