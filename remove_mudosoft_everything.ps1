param(
    [Parameter(Mandatory = $true)]
    [string[]]$Targets
)

$ErrorActionPreference = "Stop"

function Resolve-TargetHost {
    param([string]$Target)

    if ($Target -match '^\d{1,3}$') {
        return "192.168.$Target.2"
    }

    return $Target
}

function Write-Step {
    param(
        [string]$TargetHost,
        [string]$Message,
        [ConsoleColor]$Color = [ConsoleColor]::Cyan
    )

    Write-Host "[$TargetHost] $Message" -ForegroundColor $Color
}

$cleanupScript = @'
@echo off
setlocal EnableExtensions EnableDelayedExpansion
set "LOG=C:\mudosoft_vnc_deep_clean.log"

echo ================================================================ > "%LOG%"
echo MudoSoft + VNC Deep Clean - %DATE% %TIME% >> "%LOG%"
echo ================================================================ >> "%LOG%"

for %%P in (
    MudoSoft.Agent.exe
    MudoSoft.Tray.exe
    MudoSoft.RDHelper.exe
    MudoSoftAgent.exe
    tvnserver.exe
    tvnviewer.exe
    winvnc.exe
    vncviewer.exe
    uvnc_service.exe
    vncserver.exe
) do (
    taskkill /F /IM "%%~P" /T >nul 2>&1
)
wmic process where "Name like '%%tvn%%' or Name like '%%vnc%%' or ExecutablePath like '%%MudoSoft%%' or ExecutablePath like '%%TightVNC%%' or ExecutablePath like '%%UltraVNC%%' or ExecutablePath like '%%RealVNC%%'" call terminate >nul 2>&1
timeout /t 2 /nobreak >nul

for %%S in (
    MudosoftAgentService
    MudosoftAgent
    MudoSoft.Agent
    MudoSoftService
    tvnserver
    uvnc_service
    WinVNC4
    WinVNC
    VNCServer
    VncServer
) do (
    sc stop "%%~S" >nul 2>&1
    sc delete "%%~S" >nul 2>&1
)
timeout /t 2 /nobreak >nul

for %%N in ("MudoSoft" "TightVNC" "UltraVNC" "RealVNC" "VNC Server") do (
    wmic product where "Name like '%%%~N%%'" call uninstall /nointeractive >nul 2>&1
)

for %%T in (
    MudoSoftInstall
    MudoSoft_RDHelper
    MudoSoftRDHelper
    MudoVncSetup
    MudoVncSetPwd
    MudoVncInstall
    MudoAgentFix
    MudoRunAgent
    MudoStartAgent
    TightVNC*
    UltraVNC*
    RealVNC*
    VNC*
    MudoSoft*
) do (
    schtasks /Delete /TN "%%~T" /F >nul 2>&1
    schtasks /Delete /TN "\%%~T" /F >nul 2>&1
)

for %%K in (
    "HKLM\SOFTWARE\MudoSoft"
    "HKCU\SOFTWARE\MudoSoft"
    "HKLM\SOFTWARE\WOW6432Node\MudoSoft"
    "HKLM\SYSTEM\CurrentControlSet\Services\MudosoftAgentService"
    "HKLM\SYSTEM\CurrentControlSet\Services\MudosoftAgent"
    "HKLM\SYSTEM\CurrentControlSet\Services\MudoSoft.Agent"
    "HKLM\SOFTWARE\TightVNC"
    "HKLM\SOFTWARE\WOW6432Node\TightVNC"
    "HKCU\SOFTWARE\TightVNC"
    "HKLM\SOFTWARE\UltraVNC"
    "HKLM\SOFTWARE\WOW6432Node\UltraVNC"
    "HKCU\SOFTWARE\UltraVNC"
    "HKLM\SOFTWARE\RealVNC"
    "HKLM\SOFTWARE\WOW6432Node\RealVNC"
    "HKCU\SOFTWARE\RealVNC"
    "HKLM\SYSTEM\CurrentControlSet\Services\tvnserver"
    "HKLM\SYSTEM\CurrentControlSet\Services\uvnc_service"
    "HKLM\SYSTEM\CurrentControlSet\Services\WinVNC4"
    "HKLM\SYSTEM\CurrentControlSet\Services\WinVNC"
    "HKLM\SYSTEM\CurrentControlSet\Services\VNCServer"
    "HKLM\SYSTEM\CurrentControlSet\Services\VncServer"
    "HKCR\.vnc"
    "HKCR\vncfile"
) do (
    reg delete "%%~K" /f >nul 2>&1
)

for %%R in (
    "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"
    "HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"
    "HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"
) do (
    reg delete "%%~R" /v "MudoSoftTray" /f >nul 2>&1
    reg delete "%%~R" /v "MudoSoftAgent" /f >nul 2>&1
    reg delete "%%~R" /v "MudoSoft" /f >nul 2>&1
    reg delete "%%~R" /v "tvnserver" /f >nul 2>&1
    reg delete "%%~R" /v "TightVNC Server" /f >nul 2>&1
    reg delete "%%~R" /v "UltraVNC" /f >nul 2>&1
    reg delete "%%~R" /v "RealVNC" /f >nul 2>&1
)

for %%F in (
    "MudoSoft Agent"
    "MudoSoft Tray"
    "MudoSoft RDHelper"
    "MudoSoft"
    "TightVNC"
    "TightVNC Server"
    "UltraVNC"
    "UltraVNC Server"
    "RealVNC"
    "VNC Server"
    "VNC"
    "tvnserver"
) do (
    netsh advfirewall firewall delete rule name="%%~F" >nul 2>&1
)

for %%D in (
    "C:\Program Files\MudoSoft"
    "C:\Program Files (x86)\MudoSoft"
    "C:\ProgramData\MudoSoft"
    "C:\MudoSoft"
    "C:\MudoSoftAgent"
    "C:\Users\Public\MudoSoftUpdate"
    "C:\Program Files\TightVNC"
    "C:\Program Files (x86)\TightVNC"
    "C:\Program Files\UltraVNC"
    "C:\Program Files (x86)\UltraVNC"
    "C:\Program Files\RealVNC"
    "C:\Program Files (x86)\RealVNC"
    "C:\ProgramData\TightVNC"
    "C:\ProgramData\UltraVNC"
    "C:\ProgramData\RealVNC"
    "C:\ProgramData\RealVNC Service"
) do (
    if exist %%~D (
        takeown /F "%%~D" /R /D Y >nul 2>&1
        icacls "%%~D" /grant Administrators:F /T /C >nul 2>&1
        rmdir /S /Q "%%~D" >nul 2>&1
    )
)

del /F /Q "C:\mudosoft*.log" >nul 2>&1
del /F /Q "C:\vnc*.log" >nul 2>&1
del /F /Q "C:\tightvnc*.log" >nul 2>&1
del /F /Q "C:\Users\Public\mudosoft*.log" >nul 2>&1
del /F /Q "C:\temp\mudoinstall.bat" >nul 2>&1
del /F /Q "C:\temp\mudo_start_agent_service.bat" >nul 2>&1
del /F /Q "C:\temp\mudo_start_agent_service.log" >nul 2>&1
del /F /Q "C:\temp\tightvnc.msi" >nul 2>&1
del /F /Q "C:\temp\tightvnc_mudosoft.msi" >nul 2>&1
del /F /Q "C:\temp\vnc_*.*" >nul 2>&1
del /F /Q "C:\temp\mudosoft*.*" >nul 2>&1

for %%P in (
    MudoSoft.Agent.exe
    MudoSoft.Tray.exe
    MudoSoft.RDHelper.exe
    tvnserver.exe
    winvnc.exe
    uvnc_service.exe
    vncserver.exe
) do (
    taskkill /F /IM "%%~P" /T >nul 2>&1
)

echo Deep clean tamamlandi. %DATE% %TIME% >> "%LOG%"
exit /b 0
'@

Write-Host ""
Write-Host "================================================" -ForegroundColor Cyan
Write-Host "  MudoSoft/VNC Full Removal - Win7 Fleet" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host "  Targets: $($Targets -join ', ')" -ForegroundColor Gray
Write-Host ""

$results = @()

foreach ($target in $Targets) {
    $hostName = Resolve-TargetHost $target
    $remoteBat = "\\$hostName\c$\Windows\Temp\mudosoft_win7_remove_all.bat"
    $taskName = "MudoSoftWin7FullRemove"

    try {
        Write-Step $hostName "Ping kontrolu..."
        if (-not (Test-Connection -ComputerName $hostName -Count 1 -Quiet)) {
            throw "Makineye ping yok"
        }

        Write-Step $hostName "Admin share kontrolu..."
        if (-not (Test-Path "\\$hostName\c$")) {
            throw "C$ erisimi yok"
        }

        Write-Step $hostName "Remote cleanup script kopyalaniyor..."
        Set-Content -Path $remoteBat -Value $cleanupScript -Encoding ASCII -Force

        $runTime = (Get-Date).AddMinutes(2).ToString("HH:mm")

        Write-Step $hostName "Scheduled task olusturuluyor..."
        $createOutput = & schtasks.exe /Create /S $hostName /RU SYSTEM /SC ONCE /ST $runTime /TN $taskName /TR "cmd /c C:\Windows\Temp\mudosoft_win7_remove_all.bat" /F 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "Scheduled task create failed: $($createOutput -join ' ')"
        }

        Write-Step $hostName "Scheduled task calistiriliyor..."
        $runOutput = & schtasks.exe /Run /S $hostName /TN $taskName 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "Scheduled task run failed: $($runOutput -join ' ')"
        }

        Start-Sleep -Seconds 20

        Write-Step $hostName "Dogrulama yapiliyor..."
        $serviceOutput = sc.exe "\\$hostName" query MudosoftAgentService 2>&1
        $agentServiceGone = ($LASTEXITCODE -eq 1060) -or ($serviceOutput -match "FAILED 1060")
        $agentDirExists = Test-Path "\\$hostName\c$\Program Files\MudoSoft"
        $vncDirExists = (Test-Path "\\$hostName\c$\Program Files\TightVNC") -or (Test-Path "\\$hostName\c$\Program Files (x86)\TightVNC")

        & schtasks.exe /Delete /S $hostName /TN $taskName /F 2>&1 | Out-Null

        if (-not $agentServiceGone -or $agentDirExists -or $vncDirExists) {
            throw "Kalinti olabilir. ServisGone=$agentServiceGone AgentDir=$agentDirExists VncDir=$vncDirExists"
        }

        Write-Step $hostName "REMOVE OK" Green
        $results += [pscustomobject]@{
            Target = $target
            Host = $hostName
            Status = "OK"
            Note = "Mudosoft/VNC removed"
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
