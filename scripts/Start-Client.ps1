$ErrorActionPreference = "Stop"
$project = Join-Path (Split-Path -Parent $PSScriptRoot) "src\SafeNotify.Client\SafeNotify.Client.csproj"
dotnet run --project $project
if ($LASTEXITCODE -ne 0) { throw "SafeNotify client stopped with exit code $LASTEXITCODE." }
