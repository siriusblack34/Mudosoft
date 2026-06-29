@echo off
title Orchestra Agent Kurulumu
color 0A
setlocal enabledelayedexpansion

:: Admin kontrolu
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo.
    echo  Bu dosyaya SAG TIKLA ^> "Yonetici olarak calistir" secin.
    echo.
    pause
    exit /b 1
)

echo.
echo  ==========================================
echo   Orchestra Agent - Tek Tusla Kurulum
echo  ==========================================
echo.

set INSTALL_DIR=C:\Program Files\MudoSoft\Agent
set SERVICE_NAME=MudosoftAgentService
set SCRIPT_DIR=%~dp0
set BACKEND_URL=http://10.75.1.109

:: ==========================================
:: IP'den magaza kodu tespit et (3. oktet)
:: ==========================================
echo  [*] Magaza kodu tespit ediliyor...

set STORE_CODE=
for /f "tokens=2 delims=:" %%a in ('ipconfig ^| findstr /c:"IPv4"') do (
    for /f "tokens=1-4 delims=." %%i in ("%%a") do (
        set OCTET3=%%k
        :: Bos degilse ve 0 degilse kullan
        if not "!OCTET3!"=="" if not "!OCTET3!"=="0" if not "!OCTET3!"=="1" (
            if "!STORE_CODE!"=="" (
                set STORE_CODE=!OCTET3!
                set LOCAL_IP=%%i.%%j.%%k.%%l
            )
        )
    )
)

:: Trim
if defined STORE_CODE set STORE_CODE=%STORE_CODE: =%
if defined LOCAL_IP set LOCAL_IP=%LOCAL_IP: =%

if "%STORE_CODE%"=="" (
    echo  [!] IP adresinden magaza kodu tespit edilemedi.
    set /p STORE_CODE="  Magaza kodunu manuel girin: "
) else (
    echo.
    echo   IP Adresi:   %LOCAL_IP%
    echo   Magaza Kodu: %STORE_CODE%
    echo   Backend:     %BACKEND_URL%
    echo.
    set /p CONFIRM="  Bilgiler dogru mu? (E/H): "
    if /i not "!CONFIRM!"=="E" (
        set /p STORE_CODE="  Magaza kodunu girin: "
    )
)

echo.
echo  Magaza Kodu: %STORE_CODE%
echo  Backend:     %BACKEND_URL%
echo.

:: ==========================================
:: Kurulum
:: ==========================================

:: Mevcut servisi durdur
echo  [1/4] Mevcut servis durduruluyor...
sc stop %SERVICE_NAME% >nul 2>&1
sc delete %SERVICE_NAME% >nul 2>&1
taskkill /F /IM MudoSoft.Agent.exe >nul 2>&1
timeout /t 2 /nobreak >nul

:: Dosyalari kopyala
echo  [2/4] Dosyalar kopyalaniyor...
if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%"
copy /Y "%SCRIPT_DIR%MudoSoft.Agent.exe" "%INSTALL_DIR%\" >nul

:: appsettings.json olustur
echo  [3/4] Yapilandirma olusturuluyor...
(
echo {
echo   "Agent": {
echo     "BackendUrl": "%BACKEND_URL%",
echo     "StoreCode": "%STORE_CODE%",
echo     "HeartbeatIntervalSeconds": 15,
echo     "CommandPollIntervalSeconds": 5,
echo     "IpAddress": "",
echo     "Collectors": {
echo       "PortMonitor": {
echo         "Enabled": true,
echo         "IntervalSeconds": 60,
echo         "Ports": [
echo           { "Port": 1433, "ServiceName": "SQL Server" },
echo           { "Port": 3389, "ServiceName": "RDP" }
echo         ],
echo         "TimeoutMs": 3000
echo       },
echo       "ProcessUsage": { "Enabled": false, "IntervalSeconds": 300, "TopCount": 10 },
echo       "ServiceMonitor": {
echo         "Enabled": false,
echo         "IntervalSeconds": 300,
echo         "MonitoredServices": [ "MSSQL$SQLEXPRESS", "SQLBrowser" ],
echo         "AutoRestart": true,
echo         "MaxRestartsPerHour": 3
echo       },
echo       "EventLog": { "Enabled": false, "IntervalSeconds": 300, "LogNames": [ "System", "Application" ], "MaxEventsPerCycle": 50 },
echo       "DiskHealth": { "Enabled": false, "IntervalSeconds": 3600 },
echo       "WindowsUpdate": { "Enabled": false, "IntervalSeconds": 3600 },
echo       "Temperature": { "Enabled": false, "IntervalSeconds": 300 },
echo       "UpsStatus": { "Enabled": false, "IntervalSeconds": 300 },
echo       "NetworkSpeed": { "Enabled": false, "IntervalSeconds": 3600, "TestUrl": "http://speedtest.tele2.net/10MB.zip", "TimeoutSeconds": 30 },
echo       "UptimeReport": { "Enabled": false, "IntervalSeconds": 600 },
echo       "ScheduledCleanup": { "Enabled": false, "IntervalSeconds": 86400, "Targets": [ { "Path": "%%TEMP%%", "MaxAgeDays": 7 }, { "Path": "C:\\Windows\\Prefetch", "MaxAgeDays": 30 }, { "Path": "C:\\Windows\\SoftwareDistribution\\Download", "MaxAgeDays": 7 } ] }
echo     }
echo   }
echo }
) > "%INSTALL_DIR%\appsettings.json"

:: Servis kur ve baslat
echo  [4/4] Servis kuruluyor ve baslatiliyor...
reg add "HKLM\SYSTEM\CurrentControlSet\Control" /v ServicesPipeTimeout /t REG_DWORD /d 120000 /f >nul 2>&1
sc create %SERVICE_NAME% binPath= "\"%INSTALL_DIR%\MudoSoft.Agent.exe\" --service" start= delayed-auto DisplayName= "Orchestra Agent Service" >nul 2>&1
sc description %SERVICE_NAME% "MudoSoft RMM Agent" >nul 2>&1
sc failure %SERVICE_NAME% reset= 60 actions= restart/5000/restart/10000/restart/30000 >nul 2>&1
sc start %SERVICE_NAME% >nul 2>&1
timeout /t 3 /nobreak >nul

:: Kontrol
sc query %SERVICE_NAME% | find "RUNNING" >nul 2>&1
if %errorLevel% equ 0 (
    echo.
    echo  ==========================================
    echo   KURULUM BASARILI!
    echo  ==========================================
    echo   Magaza: %STORE_CODE%  ^|  Servis: CALISIYOR
    echo   Cihaz ID: Otomatik (donanim hash)
    echo  ==========================================
) else (
    echo.
    echo   UYARI: Servis baslatilamadi.
    echo   Log: C:\mudosoft_helper.log
)

echo.
pause
