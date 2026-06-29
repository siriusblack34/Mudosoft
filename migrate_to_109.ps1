param(
    [Parameter(Mandatory = $true)]
    [string[]]$Targets,

    [string]$NewBackendUrl = "http://10.75.1.109",
    [string]$ServiceName = "MudosoftAgentService",
    [string]$InstallDir = "C$\Program Files\MudoSoft\Agent"
)

$ErrorActionPreference = "Stop"

function Resolve-TargetHost {
    param([string]$Target)
    if ($Target -match '^\d{1,3}$') { return "192.168.$Target.2" }
    return $Target
}

$results = @()

foreach ($target in $Targets) {
    $hostName = Resolve-TargetHost $target
    $remoteSettingsPath = "\\$hostName\$InstallDir\appsettings.json"

    Write-Host ""
    Write-Host "[$hostName] Basliyor..." -ForegroundColor Cyan

    try {
        Write-Host "[$hostName] Ping kontrolu..." -ForegroundColor Gray
        if (-not (Test-Connection -ComputerName $hostName -Count 1 -Quiet)) {
            throw "Makineye ping yok"
        }

        Write-Host "[$hostName] Admin share kontrolu..." -ForegroundColor Gray
        if (-not (Test-Path "\\$hostName\c$")) {
            throw "C$ erisimi yok"
        }

        Write-Host "[$hostName] appsettings.json okunuyor..." -ForegroundColor Gray
        if (-not (Test-Path $remoteSettingsPath)) {
            throw "appsettings.json bulunamadi: $remoteSettingsPath"
        }

        $raw = [System.IO.File]::ReadAllText($remoteSettingsPath, [System.Text.Encoding]::UTF8)

        # Mevcut URL'i goster
        $urlMatch = [regex]::Match($raw, '"BackendUrl"\s*:\s*"([^"]*)"')
        $currentUrl = if ($urlMatch.Success) { $urlMatch.Groups[1].Value } else { "(bulunamadi)" }
        Write-Host "[$hostName] Mevcut BackendUrl: $currentUrl" -ForegroundColor Yellow

        if ($currentUrl -eq $NewBackendUrl) {
            Write-Host "[$hostName] Zaten $NewBackendUrl -- atlanıyor." -ForegroundColor Green
            $results += [pscustomobject]@{ Target = $target; Host = $hostName; Status = "SKIP"; Note = "Zaten $NewBackendUrl" }
            continue
        }

        # Dogrudan string replace -- ConvertTo-Json PS5.1'de BOM ve encoding sorunlari yaratir
        $escapedNew = $NewBackendUrl -replace '\\', '\\'
        $newRaw = $raw -replace '"BackendUrl"\s*:\s*"[^"]*"', ('"BackendUrl": "' + $escapedNew + '"')
        [System.IO.File]::WriteAllText($remoteSettingsPath, $newRaw, [System.Text.Encoding]::UTF8)

        # Dogrula
        $verify = [System.IO.File]::ReadAllText($remoteSettingsPath, [System.Text.Encoding]::UTF8)
        $verifyMatch = [regex]::Match($verify, '"BackendUrl"\s*:\s*"([^"]*)"')
        $verifiedUrl = if ($verifyMatch.Success) { $verifyMatch.Groups[1].Value } else { "" }
        if ($verifiedUrl -ne $NewBackendUrl) {
            throw "Yazma dogrulanamadi -- okunan deger: $verifiedUrl"
        }
        Write-Host "[$hostName] Yazma OK. Servis yeniden baslatiliyor..." -ForegroundColor Cyan

        sc.exe "\\$hostName" stop $ServiceName | Out-Null
        Start-Sleep -Seconds 3
        sc.exe "\\$hostName" start $ServiceName | Out-Null
        Start-Sleep -Seconds 3

        $svcStatus = sc.exe "\\$hostName" query $ServiceName
        $isRunning = $svcStatus -match "RUNNING"
        if (-not $isRunning) {
            throw "Servis RUNNING durumuna gecmedi"
        }

        Write-Host "[$hostName] OK - servis calisiyor, 109a baglanacak." -ForegroundColor Green
        $results += [pscustomobject]@{ Target = $target; Host = $hostName; Status = "OK"; Note = "$currentUrl -> $NewBackendUrl" }
    }
    catch {
        Write-Host "[$hostName] HATA: $($_.Exception.Message)" -ForegroundColor Red
        $results += [pscustomobject]@{ Target = $target; Host = $hostName; Status = "FAIL"; Note = $_.Exception.Message }
    }
}

Write-Host ""
Write-Host "========== Sonuc ==========" -ForegroundColor Cyan
$results | Format-Table -AutoSize
Write-Host "===========================" -ForegroundColor Cyan
