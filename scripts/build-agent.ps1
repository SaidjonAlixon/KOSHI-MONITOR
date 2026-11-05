<#
.SYNOPSIS
  Builds the KOSHI Windows agent (tray + service) and the Inno Setup installer.
.EXAMPLE
  .\scripts\build-agent.ps1 -Version 1.0.0
  Output: artifacts\installer\KOSHI-Agent-Setup-<version>.exe
#>
param([string]$Version = "1.0.0")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
if ($env:PATH -notlike "*$env:USERPROFILE\.dotnet*") { $env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH" }

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 not found. Install with: winget install JRSoftware.InnoSetup" }

Remove-Item -Recurse -Force artifacts\agent, artifacts\service -ErrorAction SilentlyContinue
$buildDate = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd')
$infoVersion = "$Version+$((Get-Date).ToUniversalTime().ToString('yyyyMMdd'))"
$common = @("-c", "Release", "-r", "win-x64", "--self-contained", "true", "-p:Version=$Version", "-p:InformationalVersion=$infoVersion", "-p:DebugType=none", "-nologo")

Write-Host "Publishing upgrade hold helper..."
Remove-Item -Recurse -Force artifacts\updhold -ErrorAction SilentlyContinue
dotnet publish src\KOSHI.SetupHold -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o artifacts\updhold -nologo
if ($LASTEXITCODE -ne 0) { throw "SetupHold publish failed" }
if (-not (Test-Path "artifacts\updhold\resolutionhost.exe")) { throw "resolutionhost.exe missing" }

Write-Host "Publishing tray agent..."
dotnet publish src\KOSHI.Agent @common -o artifacts\agent
if ($LASTEXITCODE -ne 0) { throw "Agent publish failed" }

Write-Host "Publishing service..."
dotnet publish src\KOSHI.Agent.Service @common -o artifacts\service
if ($LASTEXITCODE -ne 0) { throw "Service publish failed" }

foreach ($exe in "artifacts\agent\KOSHI.Agent.exe", "artifacts\service\KOSHI.Agent.Service.exe") {
    if (-not (Test-Path $exe)) { throw "$exe missing after publish" }
}

Write-Host "Compiling installer..."
for ($attempt = 1; $attempt -le 3; $attempt++) {
    & $iscc "/DAppVersion=$Version" deploy\installer\koshi-agent.iss
    if ($LASTEXITCODE -eq 0) { break }
    if ($attempt -eq 3) { throw "ISCC failed" }
    Start-Sleep -Seconds 2
}

$setup = Get-Item "artifacts\installer\KOSHI-Agent-Setup-$Version.exe"
$sha = (Get-FileHash $setup.FullName -Algorithm SHA256).Hash
# Metadata served next to the installer so the site can show the exact version and build date.
$latestJson = [ordered]@{
    version   = $Version
    buildDate = $buildDate
    fileName  = $setup.Name
    size      = $setup.Length
    sha256    = $sha
} | ConvertTo-Json
# Write without BOM so strict JSON parsers (agent) accept it.
[System.IO.File]::WriteAllText((Join-Path $root "artifacts\installer\latest.json"), $latestJson, (New-Object System.Text.UTF8Encoding $false))
Write-Host ("Installer: {0} ({1:N1} MB)" -f $setup.FullName, ($setup.Length / 1MB))
Write-Host ("Version: {0}  BuildDate: {1}" -f $Version, $buildDate)
Write-Host ("SHA256: {0}" -f $sha)
