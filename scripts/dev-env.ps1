<#
.SYNOPSIS
  Loads local development environment variables for KOSHI into the current PowerShell session.
.DESCRIPTION
  Secrets are generated once and stored OUTSIDE the repository in %LOCALAPPDATA%\KOSHI-dev\dev-secrets.json.
  Usage:  . .\scripts\dev-env.ps1      (note the leading dot to dot-source)
#>
param(
    [string]$PgHost = "localhost",
    [int]$PgPort = 5432,
    [string]$Database = "koshi_dev",
    [string]$PgUser = "postgres"
)

$devDir = Join-Path $env:LOCALAPPDATA "KOSHI-dev"
New-Item -ItemType Directory -Force $devDir | Out-Null
$secretsFile = Join-Path $devDir "dev-secrets.json"

function New-RandomString([int]$length, [string]$chars) {
    $bytes = New-Object byte[] $length
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    if (($bytes | Measure-Object -Sum).Sum -eq 0) { throw "Random generator returned no entropy" }
    -join ($bytes | ForEach-Object { $chars[$_ % $chars.Length] })
}

if (-not (Test-Path $secretsFile)) {
    $alnum = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789"
    $secrets = [ordered]@{
        JwtSigningKey = New-RandomString 64 $alnum
        AdminEmail    = "admin@koshi.local"
        AdminPassword = (New-RandomString 14 $alnum) + "!9aA"
    }
    $secrets | ConvertTo-Json | Set-Content -Encoding UTF8 $secretsFile
    Write-Host "Generated development secrets in $secretsFile"
}
$s = Get-Content $secretsFile -Raw | ConvertFrom-Json

$pgPassFile = Join-Path $devDir "pgpass.txt"
$pgPassword = if (Test-Path $pgPassFile) { (Get-Content $pgPassFile -Raw).Trim() } else { $env:PGPASSWORD }

$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:KOSHI_ConnectionStrings__Koshi = "Host=$PgHost;Port=$PgPort;Database=$Database;Username=$PgUser;Password=$pgPassword"
$env:KOSHI_Jwt__SigningKey = $s.JwtSigningKey
$env:KOSHI_Bootstrap__AdminEmail = $s.AdminEmail
$env:KOSHI_Bootstrap__AdminPassword = $s.AdminPassword
$env:KOSHI_TEST_PG = "Host=$PgHost;Port=$PgPort;Username=$PgUser;Password=$pgPassword"

Write-Host "KOSHI dev environment loaded. Admin login: $($s.AdminEmail) (password in $secretsFile)"
