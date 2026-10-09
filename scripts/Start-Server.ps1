param(
    [string]$BindAddress = "127.0.0.1",
    [int]$Port = 5080,
    [string]$CertificatePath
)
$ErrorActionPreference = "Stop"
$serverDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) "src\SafeNotify.Server"
$certificatePassword = $null
if (-not [System.Net.IPAddress]::IsLoopback([System.Net.IPAddress]::Parse($BindAddress))) {
    if ([string]::IsNullOrWhiteSpace($CertificatePath) -or -not (Test-Path -LiteralPath $CertificatePath -PathType Leaf)) {
        throw "A trusted TLS certificate file is required when the server binds to a network interface."
    }
    $CertificatePath = (Resolve-Path -LiteralPath $CertificatePath).Path
    $securePassword = Read-Host "TLS certificate password" -AsSecureString
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
    try { $certificatePassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
}
$previousAddress = $env:SafeNotify__BindAddress
$previousPort = $env:SafeNotify__Port
$previousCertificate = $env:SafeNotify__CertificatePath
$previousPassword = $env:SAFENOTIFY_CERT_PASSWORD
try {
    $env:SafeNotify__BindAddress = $BindAddress
    $env:SafeNotify__Port = "$Port"
    $env:SafeNotify__CertificatePath = $CertificatePath
    $env:SAFENOTIFY_CERT_PASSWORD = $certificatePassword
    Push-Location $serverDirectory
    dotnet run --project (Join-Path $serverDirectory "SafeNotify.Server.csproj") --no-launch-profile
    if ($LASTEXITCODE -ne 0) { throw "SafeNotify server stopped with exit code $LASTEXITCODE." }
}
finally {
    Pop-Location -ErrorAction SilentlyContinue
    $env:SafeNotify__BindAddress = $previousAddress
    $env:SafeNotify__Port = $previousPort
    $env:SafeNotify__CertificatePath = $previousCertificate
    $env:SAFENOTIFY_CERT_PASSWORD = $previousPassword
    $certificatePassword = $null
}

