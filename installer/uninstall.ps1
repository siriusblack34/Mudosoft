# ============================================
#  Orchestra Agent Uninstaller
#  Admin olarak calistirin!
# ============================================
$ErrorActionPreference = "Stop"
$ServiceName = "MudosoftAgentService"
$InstallDir = "C:\Program Files\MudoSoft\Agent"

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  Orchestra Agent Uninstaller" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# Admin kontrolu
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "HATA: Bu scripti Admin olarak calistirin!" -ForegroundColor Red
    exit 1
}

# 1. Servisi durdur
Write-Host "[1/3] Servis durduruluyor..." -ForegroundColor Yellow
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    sc.exe stop $ServiceName 2>&1 | Out-Null
    Start-Sleep -Seconds 2
    Get-Process -Name "MudoSoft*" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
    Write-Host "  Servis durduruldu." -ForegroundColor Green
}
else {
    Write-Host "  Servis bulunamadi, devam ediliyor." -ForegroundColor DarkYellow
}

# 2. Servisi sil
Write-Host "[2/3] Servis kaldiriliyor..." -ForegroundColor Yellow
if ($existing) {
    sc.exe delete $ServiceName 2>&1 | Out-Null
    Start-Sleep -Seconds 1
    Write-Host "  Servis kaldirildi." -ForegroundColor Green
}

# 3. Dosyalari sil
Write-Host "[3/3] Dosyalar temizleniyor..." -ForegroundColor Yellow
if (Test-Path $InstallDir) {
    Remove-Item $InstallDir -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "  $InstallDir silindi." -ForegroundColor Green
}
else {
    Write-Host "  Klasor zaten yok." -ForegroundColor DarkYellow
}

# Log dosyalarini temizle
Get-ChildItem "C:\Users\Public\mudosoft_*.log" -ErrorAction SilentlyContinue | 
Remove-Item -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  KALDIRMA TAMAMLANDI!" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""
