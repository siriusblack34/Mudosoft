@echo off
:: ============================================================
::  MudoSoft Agent + TightVNC - Tam Kaldirim
::  Yonetici olarak calistir
:: ============================================================
echo.
echo  ============================================================
echo   MudoSoft Agent + TightVNC - Tam Kaldirim
echo  ============================================================
echo.

net session >nul 2>&1
if %errorlevel% neq 0 (
    echo  [!] YONETICI OLARAK CALISTIRIN!
    pause
    exit /b 1
)

echo  [1/9] Servisler durduruluyor...
for %%s in (MudosoftAgentService MudosoftAgent MudoSoft.Agent tvnserver) do (
    net stop %%s >nul 2>&1
    sc stop %%s >nul 2>&1
)
timeout /t 2 /nobreak >nul
echo        OK

echo  [2/9] Prosesler sonlandiriliyor...
taskkill /f /im MudoSoft.Agent.exe >nul 2>&1
taskkill /f /im tvnserver.exe >nul 2>&1
taskkill /f /im tvnviewer.exe >nul 2>&1
wmic process where "ExecutablePath like '%%MudoSoft%%'" call terminate >nul 2>&1
timeout /t 2 /nobreak >nul
echo        OK

echo  [3/9] Servisler siliniyor...
for %%s in (MudosoftAgentService MudosoftAgent MudoSoft.Agent tvnserver) do (
    sc delete %%s >nul 2>&1
)
echo        OK

echo  [4/9] TightVNC MSI ile kaldiriliyor...
wmic product where "name like '%%TightVNC%%'" call uninstall /nointeractive >nul 2>&1
echo        OK

echo  [5/9] Zamanli gorevler temizleniyor...
for /f "tokens=2 delims=\" %%t in ('schtasks /query /fo csv /nh 2^>nul ^| findstr /i "Mudo"') do (
    schtasks /delete /tn "%%~t" /f >nul 2>&1
)
for %%t in (MudoSoftInstall MudoSoft_Install MudoSoft_VNC_Install MudoVncSetup MudoVncSetPwd MudoVncInstall MudoAgentFix MudoRunAgent MudoKillAgent MudoStartAgent MudoTryStart MudoScStart) do (
    schtasks /delete /tn "%%t" /f >nul 2>&1
)
echo        OK

echo  [6/9] Registry temizleniyor...
:: Agent
reg delete "HKLM\SYSTEM\CurrentControlSet\Services\MudosoftAgentService" /f >nul 2>&1
reg delete "HKLM\SYSTEM\CurrentControlSet\Services\MudosoftAgent" /f >nul 2>&1
reg delete "HKLM\SYSTEM\CurrentControlSet\Services\MudoSoft.Agent" /f >nul 2>&1
reg delete "HKLM\SOFTWARE\MudoSoft" /f >nul 2>&1
:: TightVNC
reg delete "HKLM\SOFTWARE\TightVNC" /f >nul 2>&1
reg delete "HKLM\SOFTWARE\WOW6432Node\TightVNC" /f >nul 2>&1
reg delete "HKLM\SYSTEM\CurrentControlSet\Services\tvnserver" /f >nul 2>&1
:: TightVNC Uninstall kaydi
for /f "tokens=*" %%k in ('reg query "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall" /s /f "TightVNC" 2^>nul ^| findstr "HKEY_"') do (
    reg delete "%%k" /f >nul 2>&1
)
for /f "tokens=*" %%k in ('reg query "HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" /s /f "TightVNC" 2^>nul ^| findstr "HKEY_"') do (
    reg delete "%%k" /f >nul 2>&1
)
echo        OK

echo  [7/9] Firewall kurallari temizleniyor...
netsh advfirewall firewall delete rule name="TightVNC" >nul 2>&1
netsh advfirewall firewall delete rule name="MudoSoft Agent" >nul 2>&1
netsh advfirewall firewall delete rule name="MudoSoft" >nul 2>&1
echo        OK

echo  [8/9] Dosya ve klasorler siliniyor...
:: Agent
if exist "C:\Program Files\MudoSoft" (
    takeown /F "C:\Program Files\MudoSoft" /R /D Y >nul 2>&1
    icacls "C:\Program Files\MudoSoft" /grant Administrators:F /T >nul 2>&1
    rd /s /q "C:\Program Files\MudoSoft" >nul 2>&1
)
rd /s /q "C:\Program Files (x86)\MudoSoft" >nul 2>&1
rd /s /q "C:\ProgramData\MudoSoft" >nul 2>&1
rd /s /q "C:\MudoSoft" >nul 2>&1
:: TightVNC
if exist "C:\Program Files\TightVNC" (
    takeown /F "C:\Program Files\TightVNC" /R /D Y >nul 2>&1
    icacls "C:\Program Files\TightVNC" /grant Administrators:F /T >nul 2>&1
    rd /s /q "C:\Program Files\TightVNC" >nul 2>&1
)
rd /s /q "C:\Program Files (x86)\TightVNC" >nul 2>&1
:: Temp
del /f /q "C:\temp\tightvnc.msi" >nul 2>&1
del /f /q "C:\temp\tightvnc_mudosoft.msi" >nul 2>&1
del /f /q "C:\temp\agent.zip" >nul 2>&1
del /f /q "C:\temp\mudoinstall.bat" >nul 2>&1
for /d %%d in (C:\temp\MudoInstall_*) do rd /s /q "%%d" >nul 2>&1
del /f /q C:\mudosoft_helper.log >nul 2>&1
del /f /q "C:\Users\Public\mudosoft_helper_debug.log" >nul 2>&1
echo        OK

echo  [9/9] Dogrulama...
echo.
set OK=1
sc query MudosoftAgentService >nul 2>&1 && (echo   [!] Agent servisi hala var - reboot gerekebilir & set OK=0) || echo   [+] Agent servisi   : temiz
sc query tvnserver >nul 2>&1             && (echo   [!] VNC servisi hala var - reboot gerekebilir & set OK=0)   || echo   [+] VNC servisi      : temiz
if exist "C:\Program Files\MudoSoft"     (echo   [!] Agent klasoru hala var & set OK=0)                        else echo   [+] Agent klasoru   : temiz
if exist "C:\Program Files\TightVNC"     (echo   [!] VNC klasoru hala var & set OK=0)                          else echo   [+] VNC klasoru     : temiz

echo.
echo  ============================================================
if "%OK%"=="1" (
    echo   TAMAMLANDI - Her sey temizlendi
) else (
    echo   TAMAMLANDI - Bazi kalemler reboot sonrasi silinecek
)
echo  ============================================================
timeout /t 5 /nobreak >nul
