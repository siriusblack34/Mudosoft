# MudoSoft + VNC Remote Deep Clean - Win7 PS 2.0 Compatible
# Non-interactive, no scheduled tasks, EDR-safe
# Start-Process ile detached calisir - agent dursa bile devam eder

$ErrorActionPreference = 'SilentlyContinue'

$batContent = @'
@echo off
setlocal EnableExtensions EnableDelayedExpansion
set "LOG=C:\mudosoft_vnc_deep_clean.log"
echo ================================================================ > "%LOG%"
echo MudoSoft + VNC Remote Deep Clean - %DATE% %TIME% >> "%LOG%"
echo ================================================================ >> "%LOG%"

echo [1/9] Processler sonlandiriliyor >> "%LOG%"
for %%P in (MudoSoft.Agent.exe MudoSoft.Tray.exe MudoSoft.RDHelper.exe MudoSoftAgent.exe tvnserver.exe tvnviewer.exe winvnc.exe vncviewer.exe uvnc_service.exe vncserver.exe) do (
    taskkill /F /IM "%%~P" /T >nul 2>&1
)
timeout /t 3 /nobreak >nul

echo [2/9] Servisler durduruluyor ve siliniyor >> "%LOG%"
for %%S in (MudosoftAgentService MudosoftAgent MudoSoft.Agent MudoSoftService tvnserver uvnc_service WinVNC4 WinVNC VNCServer VncServer) do (
    sc stop "%%~S" >nul 2>&1
    sc delete "%%~S" >nul 2>&1
)
timeout /t 2 /nobreak >nul

echo [3/9] MSI uzerinden kaldirma deneniyor >> "%LOG%"
for %%N in ("MudoSoft" "TightVNC" "UltraVNC" "RealVNC" "VNC Server") do (
    wmic product where "Name like '%%%~N%%'" call uninstall /nointeractive >nul 2>&1
)

echo [4/9] Registry temizleniyor >> "%LOG%"
for %%K in ("HKLM\SOFTWARE\MudoSoft" "HKCU\SOFTWARE\MudoSoft" "HKLM\SOFTWARE\WOW6432Node\MudoSoft" "HKLM\SYSTEM\CurrentControlSet\Services\MudosoftAgentService" "HKLM\SYSTEM\CurrentControlSet\Services\MudosoftAgent" "HKLM\SYSTEM\CurrentControlSet\Services\MudoSoft.Agent" "HKLM\SOFTWARE\TightVNC" "HKLM\SOFTWARE\WOW6432Node\TightVNC" "HKCU\SOFTWARE\TightVNC" "HKLM\SOFTWARE\UltraVNC" "HKLM\SOFTWARE\WOW6432Node\UltraVNC" "HKCU\SOFTWARE\UltraVNC" "HKLM\SOFTWARE\RealVNC" "HKLM\SOFTWARE\WOW6432Node\RealVNC" "HKCU\SOFTWARE\RealVNC" "HKLM\SYSTEM\CurrentControlSet\Services\tvnserver" "HKLM\SYSTEM\CurrentControlSet\Services\uvnc_service" "HKLM\SYSTEM\CurrentControlSet\Services\WinVNC4" "HKLM\SYSTEM\CurrentControlSet\Services\WinVNC" "HKLM\SYSTEM\CurrentControlSet\Services\VNCServer" "HKCR\.vnc" "HKCR\vncfile") do (
    reg delete "%%~K" /f >nul 2>&1
)
for %%R in ("HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" "HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" "HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run") do (
    reg delete "%%~R" /v "MudoSoftTray" /f >nul 2>&1
    reg delete "%%~R" /v "MudoSoftAgent" /f >nul 2>&1
    reg delete "%%~R" /v "MudoSoft" /f >nul 2>&1
    reg delete "%%~R" /v "tvnserver" /f >nul 2>&1
    reg delete "%%~R" /v "TightVNC Server" /f >nul 2>&1
    reg delete "%%~R" /v "UltraVNC" /f >nul 2>&1
    reg delete "%%~R" /v "RealVNC" /f >nul 2>&1
)

echo [5/9] Firewall kurallari temizleniyor >> "%LOG%"
for %%F in ("MudoSoft Agent" "MudoSoft Tray" "MudoSoft RDHelper" "MudoSoft" "TightVNC" "TightVNC Server" "UltraVNC" "RealVNC" "VNC Server" "VNC" "tvnserver") do (
    netsh advfirewall firewall delete rule name="%%~F" >nul 2>&1
)

echo [6/9] Klasorler siliniyor >> "%LOG%"
for %%D in ("C:\Program Files\MudoSoft" "C:\Program Files (x86)\MudoSoft" "C:\ProgramData\MudoSoft" "C:\MudoSoft" "C:\MudoSoftAgent" "C:\AgentDeploy" "C:\Users\Public\MudoSoftUpdate" "C:\Program Files\TightVNC" "C:\Program Files (x86)\TightVNC" "C:\Program Files\UltraVNC" "C:\Program Files (x86)\UltraVNC" "C:\Program Files\RealVNC" "C:\Program Files (x86)\RealVNC" "C:\ProgramData\TightVNC" "C:\ProgramData\UltraVNC" "C:\ProgramData\RealVNC") do (
    if exist %%~D (
        takeown /F "%%~D" /R /D Y >nul 2>&1
        icacls "%%~D" /grant Administrators:F /T /C >nul 2>&1
        rmdir /S /Q "%%~D" >nul 2>&1
        echo   Silindi: %%~D >> "%LOG%"
    )
)

echo [7/9] Temp ve log dosyalari temizleniyor >> "%LOG%"
del /F /Q "C:\Users\Public\mudosoft*.log" >nul 2>&1
del /F /Q "C:\Users\Public\MudoSoft*.flag" >nul 2>&1
del /F /Q "C:\Users\Public\Desktop\MudoSoft*.lnk" >nul 2>&1
del /F /Q "C:\Users\Public\Desktop\*VNC*.lnk" >nul 2>&1
del /F /Q "C:\temp\mudoinstall.bat" >nul 2>&1
del /F /Q "C:\temp\mudo_start_agent_service.*" >nul 2>&1
del /F /Q "C:\temp\tightvnc*.*" >nul 2>&1
del /F /Q "C:\temp\vnc_*.*" >nul 2>&1
del /F /Q "C:\temp\mudosoft*.*" >nul 2>&1
del /F /Q "C:\Windows\Temp\mudosoft*" >nul 2>&1

echo [8/9] Son temizlik >> "%LOG%"
for %%P in (MudoSoft.Agent.exe MudoSoft.Tray.exe MudoSoft.RDHelper.exe tvnserver.exe winvnc.exe) do (
    taskkill /F /IM "%%~P" /T >nul 2>&1
)
for %%S in (MudosoftAgentService tvnserver uvnc_service WinVNC4 VNCServer) do (
    sc stop "%%~S" >nul 2>&1
    sc delete "%%~S" >nul 2>&1
)

echo [9/9] Dogrulama >> "%LOG%"
set "CLEAN=1"
if exist "C:\Program Files\MudoSoft" (
    echo UYARI: MudoSoft klasoru kalmis >> "%LOG%"
    set "CLEAN=0"
)
if exist "C:\Program Files\TightVNC" (
    echo UYARI: TightVNC klasoru kalmis >> "%LOG%"
    set "CLEAN=0"
)
sc query MudosoftAgentService >nul 2>&1
if not %ERRORLEVEL% EQU 1060 (
    echo UYARI: MudosoftAgentService hala var >> "%LOG%"
    set "CLEAN=0"
)
if "%CLEAN%"=="1" (
    echo SONUC: TEMIZLIK BASARILI >> "%LOG%"
) else (
    echo SONUC: KISMI TEMIZLIK - bazi kalintilar var >> "%LOG%"
)
echo Tamamlandi: %DATE% %TIME% >> "%LOG%"
del /F /Q "%~f0" >nul 2>&1
exit /b 0
'@

# Batch dosyasini yaz
$batPath = Join-Path $env:SystemRoot 'Temp\mudosoft_deep_clean.bat'
Set-Content -Path $batPath -Value $batContent -Force

# Detached process olarak calistir (agent dursa bile devam eder)
Start-Process -FilePath 'cmd.exe' -ArgumentList "/c `"$batPath`"" -WindowStyle Hidden
Write-Output "Deep clean baslatildi (detached). Log: C:\mudosoft_vnc_deep_clean.log"
