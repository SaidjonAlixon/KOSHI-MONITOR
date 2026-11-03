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

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 not found. Install with: winget install JRSoftware.InnoSetup" }

Remove-Item -Recurse -Force artifacts\agent, artifacts\service -ErrorAction SilentlyContinue
$common = @("-c", "Release", "-r", "win-x64", "--self-contained", "true", "-p:Version=$Version", "-p:DebugType=none", "-nologo")

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
& $iscc "/DAppVersion=$Version" deploy\installer\koshi-agent.iss
if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }

$setup = Get-Item "artifacts\installer\KOSHI-Agent-Setup-$Version.exe"
Write-Host ("Installer: {0} ({1:N1} MB)" -f $setup.FullName, ($setup.Length / 1MB))
Write-Host ("SHA256: {0}" -f (Get-FileHash $setup.FullName -Algorithm SHA256).Hash)
