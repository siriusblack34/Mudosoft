param(
    [Parameter(Mandatory = $true)]
    [string[]]$Targets,

    [string]$SourcePath = "",
    [string]$ServiceName = "MudosoftAgentService",
    [string]$InstallDir = "C$\Program Files\MudoSoft\Agent"
)

$ErrorActionPreference = "Stop"

function Resolve-TargetHost {
    param([string]$Target)

    if ($Target -match '^\d{1,3}$') {
        return "192.168.$Target.2"
    }

    return $Target
}

function Resolve-StoreCode {
    param(
        [string]$Target,
        [string]$HostName
    )

    if ($Target -match '^\d{1,3}$') {
        return $Target
    }

    if ($HostName -match '^192\.168\.(\d{1,3})\.\d{1,3}$') {
        return $Matches[1]
    }

    return "000"
}

function Write-Step {
    param(
        [string]$TargetHost,
        [string]$Message,
        [ConsoleColor]$Color = [ConsoleColor]::Cyan
    )

    Write-Host "[$TargetHost] $Message" -ForegroundColor $Color
}

if ([string]::IsNullOrWhiteSpace($SourcePath)) {
    $SourcePath = Join-Path $PSScriptRoot "_temp_build\agent_hotfix_publish"
}

if (-not (Test-Path $SourcePath)) {
    throw "SourcePath bulunamadi: $SourcePath"
}

$sourceRoot = (Resolve-Path $SourcePath).Path
$filesToCopy = Get-ChildItem $sourceRoot -Recurse -File | Where-Object {
    $_.FullName -notmatch '\\deploy\\' -and
    $_.FullName -notmatch '\\publish\\' -and
    $_.Name -notin @('appsettings.json', 'appsettings.Development.json')
}

if ($filesToCopy.Count -eq 0) {
    throw "Kopyalanacak publish dosyasi bulunamadi: $sourceRoot"
}

Write-Host ""
Write-Host "================================================" -ForegroundColor Cyan
Write-Host "  MudoSoft Agent Emergency Hotfix Rollout" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host "  Source : $sourceRoot" -ForegroundColor Gray
Write-Host "  Targets: $($Targets -join ', ')" -ForegroundColor Gray
Write-Host ""

$results = @()

foreach ($target in $Targets) {
    $hostName = Resolve-TargetHost $target
    $storeCode = Resolve-StoreCode -Target $target -HostName $hostName
    $remoteInstallPath = "\\$hostName\$InstallDir"
    $remoteExePath = "C:\Program Files\MudoSoft\Agent\MudoSoft.Agent.exe"
    $remoteSettingsPath = Join-Path $remoteInstallPath "appsettings.json"

    try {
        Write-Step $hostName "Ping kontrolu..."
        if (-not (Test-Connection -ComputerName $hostName -Count 1 -Quiet)) {
            throw "Makineye ping yok"
        }

        Write-Step $hostName "Admin share kontrolu..."
        if (-not (Test-Path "\\$hostName\c$")) {
            throw "C$ erisimi yok. Gerekirse fix_admin_share.bat calistirin."
        }

        Write-Step $hostName "Servis durduruluyor ve prosesler temizleniyor..."
        sc.exe "\\$hostName" stop $ServiceName | Out-Null
        Start-Sleep -Seconds 2
        cmd /c "taskkill /s $hostName /f /im MudoSoft.Agent.exe /t" | Out-Null
        cmd /c "taskkill /s $hostName /f /im MudoSoft.Tray.exe /t" | Out-Null
        cmd /c "taskkill /s $hostName /f /im MudoSoft.RDHelper.exe /t" | Out-Null

        Write-Step $hostName "Klasor hazirlaniyor..."
        if (-not (Test-Path $remoteInstallPath)) {
            New-Item -ItemType Directory -Path $remoteInstallPath -Force | Out-Null
        }

        Write-Step $hostName "Hotfix dosyalari kopyalaniyor..."
        foreach ($file in $filesToCopy) {
            $relativePath = $file.FullName.Substring($sourceRoot.Length).TrimStart('\')
            $destination = Join-Path $remoteInstallPath $relativePath
            $destinationDir = Split-Path $destination -Parent

            if (-not (Test-Path $destinationDir)) {
                New-Item -ItemType Directory -Path $destinationDir -Force | Out-Null
            }

            Copy-Item $file.FullName -Destination $destination -Force
        }

        if (-not (Test-Path $remoteSettingsPath)) {
            Write-Step $hostName "appsettings.json yok, guvenli varsayilan config olusturuluyor..."
            $settings = @"
{
  "Agent": {
    "DeviceId": "$hostName",
    "BackendUrl": "http://10.0.210.99:5102",
    "StoreCode": "$storeCode",
    "HeartbeatIntervalSeconds": 15,
    "CommandPollIntervalSeconds": 5,
    "IpAddress": ""
  }
}
"@
            Set-Content -Path $remoteSettingsPath -Value $settings -Encoding UTF8
        }

        Write-Step $hostName "Boot ayarlari ve servis config uygulanıyor..."
        reg.exe add "\\$hostName\HKLM\SYSTEM\CurrentControlSet\Control" /v ServicesPipeTimeout /t REG_DWORD /d 120000 /f | Out-Null

        sc.exe "\\$hostName" query $ServiceName | Out-Null
        if ($LASTEXITCODE -ne 0) {
            sc.exe "\\$hostName" create $ServiceName binPath= "\"$remoteExePath\" --service" start= delayed-auto DisplayName= "MudoSoft Agent Service" | Out-Null
        }
        else {
            sc.exe "\\$hostName" config $ServiceName binPath= "\"$remoteExePath\" --service" start= delayed-auto | Out-Null
        }

        sc.exe "\\$hostName" description $ServiceName "MudoSoft Remote Management Agent" | Out-Null
        sc.exe "\\$hostName" failure $ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null

        Write-Step $hostName "Servis baslatiliyor..."
        sc.exe "\\$hostName" start $ServiceName | Out-Null
        Start-Sleep -Seconds 3

        $serviceOutput = sc.exe "\\$hostName" query $ServiceName
        $isRunning = $serviceOutput -match "RUNNING"
        if (-not $isRunning) {
            throw "Servis RUNNING durumuna gecmedi"
        }

        Write-Step $hostName "HOTFIX OK" Green
        $results += [pscustomobject]@{
            Target = $target
            Host = $hostName
            Status = "OK"
            Note = "Service running"
        }
    }
    catch {
        Write-Step $hostName "HATA: $($_.Exception.Message)" Red
        $results += [pscustomobject]@{
            Target = $target
            Host = $hostName
            Status = "FAIL"
            Note = $_.Exception.Message
        }
    }

    Write-Host ""
}

Write-Host "================ Summary ================" -ForegroundColor Cyan
$results | Format-Table -AutoSize
Write-Host "=========================================" -ForegroundColor Cyan
