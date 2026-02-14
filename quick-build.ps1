# Hızlı Agent Build Script
# Kullanım: .\quick-build.ps1

$ErrorActionPreference = "Stop"
$RootDir = $PSScriptRoot
$AgentDir = Join-Path $RootDir "agent"
$OutputDir = Join-Path $RootDir "quick-release"

Write-Host "Hizli Agent Builder" -ForegroundColor Cyan

# Clean
if (Test-Path $OutputDir) { Remove-Item $OutputDir -Recurse -Force }
New-Item -ItemType Directory $OutputDir -Force | Out-Null

# Build (self-contained OLMADAN - çok daha hızlı)
Write-Host "[1/2] Agent derleniyor..." -ForegroundColor Yellow
Set-Location $AgentDir
& dotnet publish -c Release -o $OutputDir --no-self-contained 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host "Build FAILED!" -ForegroundColor Red; exit 1 }
Write-Host "  Agent OK" -ForegroundColor Green

# ZIP
Write-Host "[2/2] ZIP olusturuluyor..." -ForegroundColor Yellow
$version = (Get-Item "$OutputDir\MudoSoft.Agent.dll").VersionInfo.FileVersion
$zipPath = Join-Path $RootDir "MudoSoft-Agent-v$version-quick.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Get-ChildItem $OutputDir -File | Compress-Archive -DestinationPath $zipPath -Force
Write-Host "  ZIP: $zipPath" -ForegroundColor Green

Set-Location $RootDir
Write-Host "`nBuild tamamlandi: $zipPath" -ForegroundColor Cyan
