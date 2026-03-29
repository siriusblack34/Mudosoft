param(
    [string]$SourceBat = "$PSScriptRoot\installer\uninstall_mudosoft_and_vnc_deep_clean.bat"
)

$ErrorActionPreference = "Stop"

$targets = @(
    9, 23, 24, 26, 32, 39, 43, 51, 52, 55,
    56, 57, 58, 59, 60, 76, 88, 91, 100, 102,
    104, 107, 110, 113, 114, 117, 121, 122, 125, 129,
    136, 139, 143, 147, 151, 152, 155, 158, 159, 161,
    173, 176, 181, 182, 191, 195, 202, 206, 210, 211,
    216, 217, 218, 219, 238, 239, 243, 247, 248, 249,
    251
)

function Resolve-TargetHost {
    param([int]$StoreCode)
    return "192.168.$StoreCode.2"
}

if (-not (Test-Path $SourceBat)) {
    throw "Kaynak bat bulunamadi: $SourceBat"
}

$results = @()

Write-Host ""
Write-Host "================================================" -ForegroundColor Cyan
Write-Host "  Win7 Uninstall BAT Copy" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host "  Source: $SourceBat" -ForegroundColor Gray
Write-Host ""

foreach ($storeCode in $targets) {
    $hostName = Resolve-TargetHost $storeCode
    $remoteTemp = "\\$hostName\c$\temp"
    $remoteBat = Join-Path $remoteTemp "uninstall_mudosoft_and_vnc_deep_clean.bat"

    try {
        Write-Host "[$hostName] Ping kontrolu..." -ForegroundColor Cyan
        if (-not (Test-Connection -ComputerName $hostName -Count 1 -Quiet)) {
            throw "Ping yok"
        }

        Write-Host "[$hostName] Admin share kontrolu..." -ForegroundColor Cyan
        if (-not (Test-Path "\\$hostName\c$")) {
            throw "C$ erisimi yok"
        }

        if (-not (Test-Path $remoteTemp)) {
            New-Item -ItemType Directory -Path $remoteTemp -Force | Out-Null
        }

        Copy-Item $SourceBat -Destination $remoteBat -Force

        Write-Host "[$hostName] KOPYALANDI -> C:\temp\uninstall_mudosoft_and_vnc_deep_clean.bat" -ForegroundColor Green
        $results += [pscustomobject]@{
            StoreCode = $storeCode
            Host = $hostName
            Status = "COPIED"
            RemotePath = "C:\temp\uninstall_mudosoft_and_vnc_deep_clean.bat"
        }
    }
    catch {
        Write-Host "[$hostName] HATA: $($_.Exception.Message)" -ForegroundColor Red
        $results += [pscustomobject]@{
            StoreCode = $storeCode
            Host = $hostName
            Status = "FAIL"
            RemotePath = "-"
        }
    }

    Write-Host ""
}

Write-Host "================ Summary ================" -ForegroundColor Cyan
$results | Format-Table -AutoSize
Write-Host "=========================================" -ForegroundColor Cyan
