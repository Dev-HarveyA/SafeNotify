$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$serverProject = Join-Path $root "src\SafeNotify.Server\SafeNotify.Server.csproj"
dotnet run --project $serverProject --configuration Release --no-launch-profile -- --migrate-data-only
if ($LASTEXITCODE -ne 0) {
    throw "SafeNotify data migration failed with exit code $LASTEXITCODE."
}
