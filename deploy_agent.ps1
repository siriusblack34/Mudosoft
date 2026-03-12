$ErrorActionPreference = "Stop"
$RemoteHost = "10.0.102.60"
$ServiceName = "MudosoftAgentService"
$RemoteAgentPath = "\\$RemoteHost\c$\Program Files\MudoSoft\Agent"
$RemoteLogsPath = "\\$RemoteHost\c$\Users\Public"

# Son versiyon ZIP'i otomatik bul
$LatestZip = Get-ChildItem "$PSScriptRoot\AgentDeploy_v*.zip" | Sort-Object Name -Descending | Select-Object -First 1
if (-not $LatestZip) { Write-Host "HATA: AgentDeploy_v*.zip bulunamadi!" -ForegroundColor Red; exit 1 }

$Version = $LatestZip.BaseName -replace "AgentDeploy_", ""
Write-Host ""
Write-Host "================================================" -ForegroundColor Cyan
Write-Host "  MudoSoft Agent Deploy - $Version -> $RemoteHost" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host "  ZIP: $($LatestZip.Name)" -ForegroundColor Gray
Write-Host ""

# 1. Servisi durdur ve ilgili process'leri oldur
Write-Host "[1/5] Servis durduruluyor ve process'ler temizleniyor..." -ForegroundColor Yellow
try {
    sc.exe \\$RemoteHost stop $ServiceName 2>&1 | Out-Null
    Start-Sleep -Seconds 2
    # Kill any remaining helper/tray/agent processes via WMI
    Get-WmiObject Win32_Process -ComputerName $RemoteHost -ErrorAction SilentlyContinue | 
    Where-Object { $_.Name -match "MudoSoft" } | 
    ForEach-Object { $_.Terminate() | Out-Null }
    Start-Sleep -Seconds 3
    Write-Host "  Servis ve process'ler durduruldu." -ForegroundColor Green
}
catch {
    Write-Host "  Servis zaten durmus olabilir, devam ediliyor..." -ForegroundColor DarkYellow
}

# 2. Log dosyalarini temizle
Write-Host "[2/5] Log dosyalari temizleniyor..." -ForegroundColor Yellow
$logPatterns = @("mudosoft_helper*.log", "mudosoft_manager*.log", "mudosoft_session*.log")
foreach ($pattern in $logPatterns) {
    Get-ChildItem "$RemoteLogsPath\$pattern" -ErrorAction SilentlyContinue | ForEach-Object {
        Remove-Item $_.FullName -Force -ErrorAction SilentlyContinue
        Write-Host "  Silindi: $($_.Name)" -ForegroundColor DarkGray
    }
}
Write-Host "  Loglar temizlendi." -ForegroundColor Green

# 3. ZIP'i gecici klasore ac
Write-Host "[3/5] ZIP aciliyor..." -ForegroundColor Yellow
$TempExtract = "$env:TEMP\MudoSoft_Deploy_Temp"
if (Test-Path $TempExtract) { Remove-Item $TempExtract -Recurse -Force }
Expand-Archive -Path $LatestZip.FullName -DestinationPath $TempExtract -Force
Write-Host "  ZIP acildi." -ForegroundColor Green

# 4. Dosyalari hedefe kopyala (uzerine yaz)
Write-Host "[4/5] Dosyalar kopyalaniyor -> $RemoteAgentPath" -ForegroundColor Yellow
if (-not (Test-Path $RemoteAgentPath)) {
    New-Item -ItemType Directory -Path $RemoteAgentPath -Force | Out-Null
}

# ZIP icindeki dosyalari bul (ic ice klasor olabilir)
$SourceFiles = Get-ChildItem $TempExtract -Recurse -File
$SourceRoot = $TempExtract
# Eger tek bir alt klasor varsa onun icine bak
$SubDirs = Get-ChildItem $TempExtract -Directory
if ($SubDirs.Count -eq 1 -and (Get-ChildItem $TempExtract -File).Count -eq 0) {
    $SourceRoot = $SubDirs[0].FullName
}

$fileCount = 0
Get-ChildItem $SourceRoot -Recurse | ForEach-Object {
    $RelPath = $_.FullName.Substring($SourceRoot.Length)
    $DestPath = Join-Path $RemoteAgentPath $RelPath
    if ($_.PSIsContainer) {
        if (-not (Test-Path $DestPath)) { New-Item -ItemType Directory -Path $DestPath -Force | Out-Null }
    }
    else {
        Copy-Item $_.FullName -Destination $DestPath -Force
        $fileCount++
    }
}
Write-Host "  $fileCount dosya kopyalandi." -ForegroundColor Green

# Temp klasoru temizle
Remove-Item $TempExtract -Recurse -Force -ErrorAction SilentlyContinue

# 5. Servisi baslat
Write-Host "[5/5] Servis baslatiliyor..." -ForegroundColor Yellow
sc.exe \\$RemoteHost start $ServiceName 2>&1 | Out-Null
Start-Sleep -Seconds 2
Write-Host "  Servis baslatildi." -ForegroundColor Green

Write-Host ""
Write-Host "================================================" -ForegroundColor Cyan
Write-Host "  DEPLOY TAMAMLANDI! ($Version)" -ForegroundColor Green
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ""
