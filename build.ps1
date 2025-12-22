# MudoSoft Agent + Tray Build Script
# Kullanim: .\build.ps1 -Zip

param(
    [switch]$Zip,
    [switch]$Clean
)

$ErrorActionPreference = "Stop"
$RootDir = $PSScriptRoot
$OutputDir = Join-Path $RootDir "release"
$AgentDir = Join-Path $RootDir "agent"
$TrayDir = Join-Path $RootDir "tray"

Write-Host "========================================"
Write-Host "  MudoSoft Agent + Tray Builder"
Write-Host "========================================"
Write-Host ""

# Clean previous build
Write-Host "[1/5] Temizleniyor..."
if (Test-Path $OutputDir) {
    Remove-Item -Path $OutputDir -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

# Build Agent
Write-Host "[2/5] Agent derleniyor..."
Set-Location $AgentDir
& dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $OutputDir 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) { 
    Write-Host "Agent build FAILED!" -ForegroundColor Red
    exit 1
}
Write-Host "  Agent OK" -ForegroundColor Green

# Build Tray
Write-Host "[3/5] Tray derleniyor..."
$TrayTemp = Join-Path $OutputDir "tray_temp"
Set-Location $TrayDir
& dotnet publish -c Release -o $TrayTemp 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) { 
    Write-Host "Tray build FAILED!" -ForegroundColor Red
    exit 1
}
Write-Host "  Tray OK" -ForegroundColor Green

# Copy Tray files to output (exclude duplicates)
Write-Host "[4/5] Dosyalar birlestiriliyor..."
Get-ChildItem $TrayTemp | ForEach-Object {
    $destPath = Join-Path $OutputDir $_.Name
    if (-not (Test-Path $destPath)) {
        Copy-Item $_.FullName $destPath -Force
    }
}
Remove-Item $TrayTemp -Recurse -Force
Write-Host "  Dosyalar birlestirildi" -ForegroundColor Green

# Create version info file
$agentExe = Join-Path $OutputDir "MudoSoft.Agent.exe"
$trayExe = Join-Path $OutputDir "MudoSoft.Tray.exe"
$agentVersion = (Get-Item $agentExe).VersionInfo.FileVersion
$trayVersion = (Get-Item $trayExe).VersionInfo.FileVersion
$buildDate = Get-Date -Format "yyyy-MM-dd HH:mm:ss"

$readme = @"
MudoSoft Agent Package
======================
Agent Version: $agentVersion
Tray Version: $trayVersion
Build Date: $buildDate

Kurulum:
1. Bu klasoru hedef makineye kopyalayin (orn: C:\MudoSoftAgent)
2. Yonetici olarak: MudoSoft.Agent.exe /Install
3. Tray baslatmak icin: MudoSoft.Tray.exe (otomatik baslar)
"@
$readme | Out-File (Join-Path $OutputDir "README.txt") -Encoding UTF8
Write-Host "  README olusturuldu" -ForegroundColor Green

# Create ZIP if requested
if ($Zip) {
    Write-Host "[5/5] ZIP olusturuluyor..."
    $zipName = "MudoSoft-Agent-v$agentVersion.zip"
    $zipPath = Join-Path $RootDir $zipName
    
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path (Join-Path $OutputDir "*") -DestinationPath $zipPath -Force
    
    Write-Host "  ZIP: $zipName" -ForegroundColor Green
}

Set-Location $RootDir

Write-Host ""
Write-Host "========================================"
Write-Host "  BUILD BASARILI!"
Write-Host "========================================"
Write-Host ""
Write-Host "Cikti: $OutputDir"
Write-Host ""
