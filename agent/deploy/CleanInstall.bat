@echo off
title Orchestra Agent - Temiz Kurulum
color 0C
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
echo   Orchestra Agent - TEMIZ KURULUM
echo   Tum MudoSoft dosyalari silinip
echo   sifirdan kurulum yapilacak.
echo  ==========================================
echo.

set INSTALL_DIR=C:\Program Files\MudoSoft\Agent
set UPDATE_DIR=C:\Users\Public\MudoSoftUpdate
set SERVICE_NAME=MudosoftAgentService
set BACKEND_URL=http://10.75.1.109

:: ==========================================
:: 1. KOMPLE TEMIZLIK
:: ==========================================
echo  [1/6] Servis durduruluyor ve siliniyor...
sc stop %SERVICE_NAME% >nul 2>&1
timeout /t 3 /nobreak >nul
taskkill /F /IM MudoSoft.Agent.exe >nul 2>&1
taskkill /F /IM MudoSoft.Tray.exe >nul 2>&1
timeout /t 2 /nobreak >nul
sc delete %SERVICE_NAME% >nul 2>&1
timeout /t 2 /nobreak >nul

echo  [2/6] Tum MudoSoft dosyalari siliniyor...
if exist "%INSTALL_DIR%" rmdir /S /Q "%INSTALL_DIR%"
if exist "%UPDATE_DIR%" rmdir /S /Q "%UPDATE_DIR%"
echo        Temizlik tamamlandi.

:: ==========================================
:: 2. MAGAZA KODU TESPIT
:: ==========================================
echo  [3/6] Magaza kodu tespit ediliyor...

set STORE_CODE=
for /f "tokens=2 delims=:" %%a in ('ipconfig ^| findstr /c:"IPv4"') do (
    for /f "tokens=1-4 delims=." %%i in ("%%a") do (
        set OCTET3=%%k
        if not "!OCTET3!"=="" if not "!OCTET3!"=="0" if not "!OCTET3!"=="1" (
            if "!STORE_CODE!"=="" (
                set STORE_CODE=!OCTET3!
                set LOCAL_IP=%%i.%%j.%%k.%%l
            )
        )
    )
)

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

:: ==========================================
:: 3. GUNCEL AGENT INDIR
:: ==========================================
echo.
echo  [4/6] Guncel agent indiriliyor...
mkdir "%INSTALL_DIR%" 2>nul
mkdir "%UPDATE_DIR%" 2>nul

powershell -NoProfile -Command "(New-Object Net.WebClient).DownloadFile('%BACKEND_URL%/api/updates/download','%UPDATE_DIR%\agent.zip')"

if not exist "%UPDATE_DIR%\agent.zip" (
    echo.
    echo  HATA: Agent indirilemedi! Backend erisilebildiginden emin olun.
    echo  URL: %BACKEND_URL%/api/updates/download
    pause
    exit /b 1
)

:: ==========================================
:: 4. ZIP CIKAR
:: ==========================================
echo  [5/6] Dosyalar cikartiliyor...
powershell -NoProfile -Command "if (Get-Command Expand-Archive -ErrorAction SilentlyContinue) { Expand-Archive -Path '%UPDATE_DIR%\agent.zip' -DestinationPath '%INSTALL_DIR%' -Force } else { $s = New-Object -ComObject Shell.Application; $z = $s.NameSpace('%UPDATE_DIR%\agent.zip'); $d = $s.NameSpace('%INSTALL_DIR%'); $d.CopyHere($z.Items(), 256) }"

dir "%INSTALL_DIR%\MudoSoft.Agent.exe" >nul 2>&1
if %errorlevel% neq 0 (
    echo.
    echo  HATA: Agent dosyasi cikartilamadi!
    pause
    exit /b 1
)

:: ==========================================
:: 5. AYARLAR + SERVIS KURULUMU
:: ==========================================
echo  [6/6] Servis kuruluyor...

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

reg add "HKLM\SYSTEM\CurrentControlSet\Control" /v ServicesPipeTimeout /t REG_DWORD /d 120000 /f >nul 2>&1
sc create %SERVICE_NAME% binPath= "\"%INSTALL_DIR%\MudoSoft.Agent.exe\" --service" start= delayed-auto DisplayName= "Orchestra Agent" >nul 2>&1
sc description %SERVICE_NAME% "MudoSoft RMM Agent" >nul 2>&1
sc failure %SERVICE_NAME% reset= 60 actions= restart/5000/restart/10000/restart/30000 >nul 2>&1
sc start %SERVICE_NAME% >nul 2>&1
timeout /t 3 /nobreak >nul

:: ==========================================
:: KONTROL
:: ==========================================
sc query %SERVICE_NAME% | find "RUNNING" >nul 2>&1
if %errorLevel% equ 0 (
    echo.
    echo  ==========================================
    echo   TEMIZ KURULUM BASARILI!
    echo  ==========================================
    echo   Magaza: %STORE_CODE%  ^|  Servis: CALISIYOR
    echo   Cihaz ID: Otomatik (donanim hash)
    echo  ==========================================
) else (
    echo.
    echo   UYARI: Servis baslatilamadi.
    echo   Kontrol: sc query %SERVICE_NAME%
)

:: Temizlik
if exist "%UPDATE_DIR%\agent.zip" del /Q "%UPDATE_DIR%\agent.zip"

echo.
pause
