$ErrorActionPreference = "Stop"
$RootDir = $PSScriptRoot
$InstallerDir = Join-Path $RootDir "installer"
$AgentFilesDir = "E:\AgentDeploy"
$OutputMsi = Join-Path $RootDir "MudoSoftAgent.msi"

Write-Host "============================================" -ForegroundColor Cyan  
Write-Host "  MudoSoft Agent MSI Builder" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

if (-not (Test-Path $AgentFilesDir)) {
    Write-Host "HATA: Agent dosyalari bulunamadi: $AgentFilesDir" -ForegroundColor Red
    Write-Host "Once build.ps1 calistirin." -ForegroundColor Red
    exit 1
}

# Prepare source directory
$tempDir = "$env:TEMP\MudoSoft_MSI_Source"
if (Test-Path $tempDir) { Remove-Item $tempDir -Recurse -Force }
New-Item $tempDir -ItemType Directory -Force | Out-Null
Copy-Item "$AgentFilesDir\*" $tempDir -Recurse -Force
Remove-Item "$tempDir\install.ps1" -Force -ErrorAction SilentlyContinue
Remove-Item "$tempDir\uninstall.ps1" -Force -ErrorAction SilentlyContinue

Write-Host "[1/2] WiX derleniyor..." -ForegroundColor Yellow
if (Test-Path $OutputMsi) { Remove-Item $OutputMsi -Force }

wix build `
    "$InstallerDir\MudoSoftAgent.wxs" `
    -bindpath "AgentFiles=$tempDir" `
    -arch x64 `
    -o $OutputMsi `
    2>&1

if ($LASTEXITCODE -ne 0) {
    Write-Host "MSI BUILD FAILED!" -ForegroundColor Red
    exit 1
}

Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue

# Copy setup.bat
Copy-Item "$InstallerDir\setup.bat" (Split-Path $OutputMsi) -Force

$sizeMB = [math]::Round((Get-Item $OutputMsi).Length / 1MB, 2)
Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  MSI BASARILI! ($sizeMB MB)" -ForegroundColor Green
Write-Host "  $OutputMsi" -ForegroundColor Green
Write-Host ""
Write-Host "  Manuel: setup.bat (cift tikla)" -ForegroundColor Yellow
Write-Host "  GPO:    msiexec /i MudoSoftAgent.msi BACKENDURL=... STORECODE=150 /qn" -ForegroundColor Yellow
Write-Host "============================================" -ForegroundColor Cyan
