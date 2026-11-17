# Builds the app, then the installer (installer\Output\GameManagerInstall.exe).
# Usage: .\build.ps1 [-Version 1.0.0]
param(
    [string]$Version = '1.0.1',
    [string]$RepoUrl = 'https://github.com/TheUnknownMurda/GameManager'
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

Write-Host "== Publishing the app ($Version)"
dotnet publish "$root\GestionnaireJeux" -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -p:DebugType=none -p:Version=$Version -o "$root\dist"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 not found: https://jrsoftware.org/isdl.php" }

Write-Host "== Building the installer"
& $iscc "/DAppVersion=$Version" "/DAppUrl=$RepoUrl" "$root\installer\GestionnaireJeux.iss"
if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }

Get-Item "$root\installer\Output\GameManagerInstall.exe"
