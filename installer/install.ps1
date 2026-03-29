# ============================================
#  MudoSoft Agent Installer
#  Admin olarak calistirin!
# ============================================
param(
    [Parameter(Mandatory = $true)]
    [string]$BackendUrl,
    
    [Parameter(Mandatory = $true)]
    [string]$StoreCode
)

$ErrorActionPreference = "Stop"
$ServiceName = "MudosoftAgentService"
$InstallDir = "C:\Program Files\MudoSoft\Agent"
$ExePath = Join-Path $InstallDir "MudoSoft.Agent.exe"
$ScriptDir = $PSScriptRoot

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  MudoSoft Agent Installer" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  Backend  : $BackendUrl" -ForegroundColor Gray
Write-Host "  StoreCode: $StoreCode" -ForegroundColor Gray
Write-Host "  Hedef    : $InstallDir" -ForegroundColor Gray
Write-Host ""

# Admin kontrolu
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "HATA: Bu scripti Admin olarak calistirin!" -ForegroundColor Red
    exit 1
}

# 1. Eski servis varsa durdur ve sil
Write-Host "[1/5] Eski kurulum kontrol ediliyor..." -ForegroundColor Yellow
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "  Mevcut servis durduruluyor..." -ForegroundColor DarkYellow
    sc.exe stop $ServiceName 2>&1 | Out-Null
    Start-Sleep -Seconds 2
    # Kill any running processes
    Get-Process -Name "MudoSoft*" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
    sc.exe delete $ServiceName 2>&1 | Out-Null
    Start-Sleep -Seconds 1
    Write-Host "  Eski servis kaldirildi." -ForegroundColor Green
}
else {
    Write-Host "  Temiz kurulum." -ForegroundColor Green
}

# 2. Dosyalari kopyala
Write-Host "[2/5] Dosyalar kopyalaniyor..." -ForegroundColor Yellow
if (-not (Test-Path $InstallDir)) {
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
}

$fileCount = 0
Get-ChildItem $ScriptDir -Recurse | Where-Object { 
    -not $_.PSIsContainer -and 
    $_.Name -ne "install.ps1" -and 
    $_.Name -ne "uninstall.ps1" 
} | ForEach-Object {
    $RelPath = $_.FullName.Substring($ScriptDir.Length)
    $DestPath = Join-Path $InstallDir $RelPath
    $DestDir = Split-Path $DestPath -Parent
    if (-not (Test-Path $DestDir)) { New-Item -ItemType Directory -Path $DestDir -Force | Out-Null }
    Copy-Item $_.FullName -Destination $DestPath -Force
    $fileCount++
}
Write-Host "  $fileCount dosya kopyalandi." -ForegroundColor Green

# 3. appsettings.json'u yapilandir
Write-Host "[3/5] Yapilandirma ayarlaniyor..." -ForegroundColor Yellow
$settingsPath = Join-Path $InstallDir "appsettings.json"
$deviceId = $env:COMPUTERNAME
$settings = @{
    Agent = @{
        DeviceId                   = $deviceId
        BackendUrl                 = $BackendUrl
        StoreCode                  = $StoreCode
        HeartbeatIntervalSeconds   = 15
        CommandPollIntervalSeconds = 5
        IpAddress                  = ""
    }
} | ConvertTo-Json -Depth 3
Set-Content -Path $settingsPath -Value $settings -Encoding UTF8
Write-Host "  DeviceId : $deviceId" -ForegroundColor Gray
Write-Host "  Backend  : $BackendUrl" -ForegroundColor Gray
Write-Host "  StoreCode: $StoreCode" -ForegroundColor Gray
Write-Host "  Yapilandirma tamam." -ForegroundColor Green

# 4. Windows servisini olustur
Write-Host "[4/5] Windows servisi olusturuluyor..." -ForegroundColor Yellow
# Yavas acilan cihazlarda SCM timeout'a dusmemesi icin service startup timeout'u uzat
reg.exe add "HKLM\SYSTEM\CurrentControlSet\Control" /v ServicesPipeTimeout /t REG_DWORD /d 120000 /f 2>&1 | Out-Null

# Reboot sirasindaki yarisi azaltmak icin delayed-auto kullan
sc.exe create $ServiceName binPath= "`"$ExePath`" --service" start= delayed-auto obj= "LocalSystem" DisplayName= "MudosoftAgentService" 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "  HATA: Servis olusturulamadi!" -ForegroundColor Red
    exit 1
}
# Servis aciklamasi
sc.exe description $ServiceName "MudoSoft Remote Management Agent" 2>&1 | Out-Null
# Hata durumunda otomatik yeniden baslat
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/30000 2>&1 | Out-Null
Write-Host "  Servis olusturuldu (DelayedAutoStart, LocalSystem)." -ForegroundColor Green

# 5. Servisi baslat
Write-Host "[5/5] Servis baslatiliyor..." -ForegroundColor Yellow
sc.exe start $ServiceName 2>&1 | Out-Null
Start-Sleep -Seconds 2

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($svc -and $svc.Status -eq "Running") {
    Write-Host "  Servis calisiyor." -ForegroundColor Green
}
else {
    Write-Host "  UYARI: Servis baslatildi ama durum: $($svc.Status)" -ForegroundColor DarkYellow
}

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  KURULUM TAMAMLANDI!" -ForegroundColor Green
Write-Host "  Cihaz: $deviceId" -ForegroundColor Green
Write-Host "  Backend: $BackendUrl" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""
