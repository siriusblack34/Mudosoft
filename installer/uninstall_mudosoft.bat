@echo off
title MudoSoft - Tam Temizlik Araci
color 4F
echo.
echo ===================================================================
echo    MUDOSOFT TAM TEMIZLIK ARACI - Tum Kalintilari Siler
echo    Bilgisayarda MudoSoft'a dair HICBIR SEY birakmaz!
echo ===================================================================
echo.
echo    DIKKAT: Bu islem geri alinamaz!
echo.

:: Admin kontrolu
net session >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo HATA: Bu dosyayi YONETICI olarak calistirin!
    echo Sag tikla ^> Yonetici olarak calistir
    echo.
    pause
    exit /b 1
)

echo ===================================================================
echo [1/10] SERVISLER DURDURULUYOR...
echo ===================================================================
net stop "MudosoftAgentService" 2>nul
net stop "MudoSoftAgent" 2>nul
net stop "MudoSoftService" 2>nul
timeout /t 3 /nobreak > nul

echo ===================================================================
echo [2/10] TUM MUDOSOFT ISLEMLERI SONLANDIRILIYOR...
echo ===================================================================
taskkill /F /IM MudoSoft.Agent.exe /T 2>nul
taskkill /F /IM MudoSoft.Tray.exe /T 2>nul
taskkill /F /IM MudoSoft.RDHelper.exe /T 2>nul
taskkill /F /IM RDHelper.exe /T 2>nul
taskkill /F /IM MudoSoftAgent.exe /T 2>nul
timeout /t 2 /nobreak > nul

echo ===================================================================
echo [3/10] WINDOWS SERVISLERI SILINIYOR...
echo ===================================================================
sc delete "MudosoftAgentService" 2>nul
sc delete "MudoSoftAgent" 2>nul
sc delete "MudoSoftService" 2>nul

echo ===================================================================
echo [4/10] MSI KURULUM KAYITLARI KALDIRILIYOR...
echo ===================================================================
:: MSI uzerinden temiz kaldirma (sessiz mod)
wmic product where "Name like '%%MudoSoft%%'" call uninstall /nointeractive 2>nul
:: Alternatif yontem
for /f "tokens=*" %%i in ('reg query "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall" /s /f "MudoSoft" 2^>nul ^| findstr "HKEY_"') do (
    for /f "tokens=2*" %%a in ('reg query "%%i" /v "UninstallString" 2^>nul ^| findstr "UninstallString"') do (
        echo   MSI Kaldirma: %%b
        %%b /qn 2>nul
    )
)

echo ===================================================================
echo [5/10] KURULUM KLASORLERI SILINIYOR...
echo ===================================================================
:: Ana kurulum dizinleri
rmdir /S /Q "C:\Program Files\MudoSoft" 2>nul
rmdir /S /Q "C:\Program Files (x86)\MudoSoft" 2>nul
rmdir /S /Q "C:\MudoSoftAgent" 2>nul
rmdir /S /Q "C:\MudoSoft" 2>nul

:: Eski deploy klasorleri
for /L %%v in (20,1,50) do (
    rmdir /S /Q "C:\AgentDeploy_v%%v" 2>nul
)
rmdir /S /Q "C:\AgentDeploy" 2>nul

echo ===================================================================
echo [6/10] LOG VE TEMP DOSYALARI TEMIZLENIYOR...
echo ===================================================================
:: C: root log dosyalari
del /F /Q "C:\mudosoft_*.log" 2>nul
del /F /Q "C:\mudosoft*.log" 2>nul

:: Public log dosyalari
del /F /Q "C:\Users\Public\mudosoft_*.log" 2>nul
del /F /Q "C:\Users\Public\MudoSoft*.flag" 2>nul
del /F /Q "C:\Users\Public\MudoSoftHelper.flag" 2>nul

:: Session log
del /F /Q "C:\mudosoft_session.log" 2>nul
del /F /Q "C:\mudosoft_helper.log" 2>nul

:: Windows Temp
rmdir /S /Q "C:\Windows\Temp\MudoSoftUpdate" 2>nul
rmdir /S /Q "C:\Windows\Temp\MudoSoft_MSI_Source" 2>nul
del /F /S /Q "C:\Windows\Temp\mudosoft*" 2>nul

:: Kullanici Temp (tum kullanicilar icin)
for /d %%u in (C:\Users\*) do (
    rmdir /S /Q "%%u\AppData\Local\Temp\MudoSoftUpdate" 2>nul
    rmdir /S /Q "%%u\AppData\Local\Temp\MudoSoft_MSI_Source" 2>nul
    del /F /Q "%%u\AppData\Local\Temp\mudosoft*" 2>nul
    del /F /Q "%%u\AppData\Local\Temp\update.log" 2>nul
)

echo ===================================================================
echo [7/10] REGISTRY KAYITLARI TEMIZLENIYOR...
echo ===================================================================
:: Startup (Run) kayitlari - HKLM
reg delete "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v "MudoSoftTray" /f 2>nul
reg delete "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v "MudoSoftAgent" /f 2>nul
reg delete "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v "MudoSoft" /f 2>nul

:: Startup (Run) kayitlari - HKCU (tum kullanicilar)
reg delete "HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v "MudoSoftTray" /f 2>nul
reg delete "HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v "MudoSoftAgent" /f 2>nul
reg delete "HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v "MudoSoft" /f 2>nul

:: MudoSoft'a ait registry anahtarlari
reg delete "HKLM\SOFTWARE\MudoSoft" /f 2>nul
reg delete "HKCU\SOFTWARE\MudoSoft" /f 2>nul
reg delete "HKLM\SOFTWARE\WOW6432Node\MudoSoft" /f 2>nul

:: Uninstall kayitlari (Program Ekle/Kaldir'dan temizle)
for /f "tokens=*" %%i in ('reg query "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall" /s /f "MudoSoft" 2^>nul ^| findstr "HKEY_"') do (
    echo   Siliniyor: %%i
    reg delete "%%i" /f 2>nul
)
for /f "tokens=*" %%i in ('reg query "HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" /s /f "MudoSoft" 2^>nul ^| findstr "HKEY_"') do (
    echo   Siliniyor: %%i
    reg delete "%%i" /f 2>nul
)

:: Windows Installer kayitlari (MSI izleri)
for /f "tokens=*" %%i in ('reg query "HKCR\Installer\Products" /s /f "MudoSoft" 2^>nul ^| findstr "HKEY_"') do (
    echo   Siliniyor: %%i
    reg delete "%%i" /f 2>nul
)

echo ===================================================================
echo [8/10] ZAMANLANMIS GOREVLER TEMIZLENIYOR...
echo ===================================================================
schtasks /delete /tn "MudoSoft*" /f 2>nul
schtasks /delete /tn "\MudoSoft*" /f 2>nul

echo ===================================================================
echo [9/10] GUVENLIK DUVARI KURALLARI TEMIZLENIYOR...
echo ===================================================================
netsh advfirewall firewall delete rule name="MudoSoft Agent" 2>nul
netsh advfirewall firewall delete rule name="MudoSoft Tray" 2>nul
netsh advfirewall firewall delete rule name="MudoSoft RDHelper" 2>nul
netsh advfirewall firewall delete rule name="MudoSoft" 2>nul
:: Program yolu ile eşleşen kurallar
netsh advfirewall firewall delete rule program="C:\Program Files\MudoSoft\Agent\MudoSoft.Agent.exe" 2>nul
netsh advfirewall firewall delete rule program="C:\Program Files\MudoSoft\Agent\MudoSoft.Tray.exe" 2>nul
netsh advfirewall firewall delete rule program="C:\Program Files\MudoSoft\Agent\MudoSoft.RDHelper.exe" 2>nul

echo ===================================================================
echo [10/10] DEVICE ID VE DIGER KALINTI DOSYALAR...
echo ===================================================================
:: Device ID dosyalari
del /F /Q "C:\Program Files\MudoSoft\Agent\device_id.txt" 2>nul
del /F /Q "C:\MudoSoftAgent\device_id.txt" 2>nul

:: WIX installer cache
rmdir /S /Q "%LOCALAPPDATA%\WixToolset" 2>nul

:: Event Log kaynaklari (opsiyonel)
wevtutil cl "MudoSoft" 2>nul

echo.
echo ===================================================================
echo.
echo    [OK] TEMIZLIK TAMAMLANDI!
echo.
echo    Kaldirilan ogeler:
echo      - Windows Servisleri (MudosoftAgentService)
echo      - Tum MudoSoft islemleri
echo      - MSI kurulum kayitlari
echo      - Kurulum klasorleri (Program Files, AgentDeploy)
echo      - Log ve temp dosyalari
echo      - Registry kayitlari (Run, Uninstall, Software)
echo      - Zamanlanmis gorevler
echo      - Guvenlik duvari kurallari
echo      - Device ID ve diger kalinti dosyalar
echo.
echo    Bilgisayarda MudoSoft'a dair HICBIR SEY kalmadi!
echo.
echo ===================================================================
echo.
pause
