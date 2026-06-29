@echo off
title Orchestra Agent Kurulumu
echo.
echo ============================================
echo   Orchestra Agent Kurulumu
echo ============================================
echo.

:: Admin kontrolu
net session >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo HATA: Bu dosyayi Yonetici olarak calistirin!
    echo Sag tikla ^> Yonetici olarak calistir
    echo.
    pause
    exit /b 1
)

set /p STORECODE="Magaza Kodu (ornek: 150): "
if "%STORECODE%"=="" (
    echo HATA: Magaza kodu bos birakilamaz!
    pause
    exit /b 1
)

set BACKENDURL=http://10.75.1.109
set /p NEWURL="Backend URL [%BACKENDURL%]: "
if not "%NEWURL%"=="" set BACKENDURL=%NEWURL%

echo.
echo   Magaza Kodu : %STORECODE%
echo   Backend URL : %BACKENDURL%
echo   Cihaz Adi   : %COMPUTERNAME%
echo.

echo [1/5] Eski servis temizleniyor...
net stop MudosoftAgentService >nul 2>&1
sc delete MudosoftAgentService >nul 2>&1
taskkill /F /IM MudoSoft.Agent.exe /T >nul 2>&1
taskkill /F /IM MudoSoft.Tray.exe /T >nul 2>&1
taskkill /F /IM MudoSoft.RDHelper.exe /T >nul 2>&1
timeout /t 2 /nobreak >nul

echo [2/5] MSI kurulumu baslatiliyor...
echo.

msiexec /i "%~dp0MudoSoftAgent.msi" /qb

if %ERRORLEVEL% NEQ 0 (
    echo HATA: MSI kurulumu basarisiz! Kod: %ERRORLEVEL%
    pause
    exit /b 1
)

echo.
echo [3/5] Yapilandirma dosyasi olusturuluyor...

:: appsettings.json yaz
set SETTINGSFILE=C:\Program Files\MudoSoft\Agent\appsettings.json
(
echo {
echo   "Agent": {
echo     "DeviceId": "%COMPUTERNAME%",
echo     "BackendUrl": "%BACKENDURL%",
echo     "StoreCode": "%STORECODE%",
echo     "HeartbeatIntervalSeconds": 15,
echo     "CommandPollIntervalSeconds": 5,
echo     "IpAddress": ""
echo   }
echo }
) > "%SETTINGSFILE%"

echo   appsettings.json olusturuldu.

echo.
echo [4/5] Windows servisi olusturuluyor...

reg add "HKLM\SYSTEM\CurrentControlSet\Control" /v ServicesPipeTimeout /t REG_DWORD /d 120000 /f >nul 2>&1

:: Servisi sc create ile kur (MSI yerine - Win7 uyumlu)
sc create MudosoftAgentService binPath= "\"C:\Program Files\MudoSoft\Agent\MudoSoft.Agent.exe\" --service" start= delayed-auto obj= LocalSystem DisplayName= "Orchestra Agent"
sc description MudosoftAgentService "Orchestra Agent - Uzaktan Yonetim"
sc failure MudosoftAgentService reset= 86400 actions= restart/5000/restart/10000/restart/30000

if %ERRORLEVEL% NEQ 0 (
    echo UYARI: Servis olusturulamadi! Kod: %ERRORLEVEL%
    echo Manuel olarak olusturmayi deneyin.
)

echo.
echo [5/5] Servis baslatiliyor...
timeout /t 2 /nobreak >nul
net start MudosoftAgentService
if %ERRORLEVEL% NEQ 0 (
    echo   Ilk deneme basarisiz, 5 saniye bekleniyor...
    timeout /t 5 /nobreak >nul
    net start MudosoftAgentService
)

echo.
echo ============================================
echo   KURULUM BASARILI!
echo   Cihaz: %COMPUTERNAME%
echo   Magaza: %STORECODE%
echo ============================================
echo.
pause
