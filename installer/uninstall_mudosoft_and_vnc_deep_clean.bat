@echo off
setlocal EnableExtensions EnableDelayedExpansion
title MudoSoft + VNC Deep Clean
color 4F

set "LOG=C:\mudosoft_vnc_deep_clean.log"
set "CONFIRM_TEXT=TEMIZLE"

echo ================================================================ > "%LOG%"
echo MudoSoft + VNC Deep Clean - %DATE% %TIME% >> "%LOG%"
echo ================================================================ >> "%LOG%"

echo.
echo ======================================================================
echo   MUDOSOFT + VNC DERIN TEMIZLIK
echo   Bu islem MudoSoft ve VNC ile ilgili servis, dosya, registry,
echo   scheduled task, firewall ve uninstall kayitlarini siler.
echo ======================================================================
echo.
echo   DIKKAT:
echo   - Geri alinamaz.
echo   - MudoSoft Agent / Tray / Helper kaldirilir.
echo   - TightVNC / UltraVNC / RealVNC kalintilari da temizlenir.
echo   - Karsilastirma testi icin "temiz makine" hedeflenir.
echo.

net session >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo HATA: Bu dosyayi YONETICI olarak calistirin!
    echo HATA: Admin yetkisi yok >> "%LOG%"
    pause
    exit /b 1
)

set /p USERCONFIRM=Devam etmek icin %CONFIRM_TEXT% yazin: 
if /I not "%USERCONFIRM%"=="%CONFIRM_TEXT%" (
    echo Iptal edildi.
    echo Kullanici islemi iptal etti >> "%LOG%"
    pause
    exit /b 1
)

call :Step "1/12" "MudoSoft ve VNC processleri sonlandiriliyor"
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

call :Step "2/12" "Servisler durdurulup siliniyor"
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

call :Step "3/12" "MSI ve uninstall kayitlari uzerinden kaldirma deneniyor"
for %%N in ("MudoSoft" "TightVNC" "UltraVNC" "RealVNC" "VNC Server") do (
    wmic product where "Name like '%%%~N%%'" call uninstall /nointeractive >nul 2>&1
)
call :DeleteUninstallKeys "MudoSoft"
call :DeleteUninstallKeys "TightVNC"
call :DeleteUninstallKeys "UltraVNC"
call :DeleteUninstallKeys "RealVNC"
call :DeleteUninstallKeys "VNC"

call :Step "4/12" "Scheduled task kalintilari temizleniyor"
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

call :Step "5/12" "Startup ve registry anahtarlari temizleniyor"
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

call :DeleteInstallerKeys "MudoSoft"
call :DeleteInstallerKeys "TightVNC"
call :DeleteInstallerKeys "UltraVNC"
call :DeleteInstallerKeys "RealVNC"
call :DeleteInstallerKeys "VNC"

call :Step "6/12" "Guvenlik duvari kurallari temizleniyor"
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
for %%P in (
    "C:\Program Files\MudoSoft\Agent\MudoSoft.Agent.exe"
    "C:\Program Files\MudoSoft\Agent\MudoSoft.Tray.exe"
    "C:\Program Files\MudoSoft\Agent\MudoSoft.RDHelper.exe"
    "C:\Program Files\TightVNC\tvnserver.exe"
    "C:\Program Files (x86)\TightVNC\tvnserver.exe"
    "C:\Program Files\UltraVNC\winvnc.exe"
    "C:\Program Files (x86)\UltraVNC\winvnc.exe"
    "C:\Program Files\RealVNC\VNC Server\vncserver.exe"
    "C:\Program Files (x86)\RealVNC\VNC Server\vncserver.exe"
) do (
    netsh advfirewall firewall delete rule program="%%~P" >nul 2>&1
)

call :Step "7/12" "Program Files ve kok klasorler temizleniyor"
for %%D in (
    "C:\Program Files\MudoSoft"
    "C:\Program Files (x86)\MudoSoft"
    "C:\MudoSoft"
    "C:\MudoSoftAgent"
    "C:\AgentDeploy"
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
for /L %%V in (1,1,99) do (
    if exist "C:\AgentDeploy_v%%V" rmdir /S /Q "C:\AgentDeploy_v%%V" >nul 2>&1
)

call :Step "8/12" "Temp, log ve kalan dosyalar temizleniyor"
del /F /Q "C:\mudosoft*.log" >nul 2>&1
del /F /Q "C:\vnc*.log" >nul 2>&1
del /F /Q "C:\tightvnc*.log" >nul 2>&1
del /F /Q "C:\Users\Public\mudosoft*.log" >nul 2>&1
del /F /Q "C:\Users\Public\MudoSoft*.flag" >nul 2>&1
del /F /Q "C:\Users\Public\MudoSoftHelper.flag" >nul 2>&1
del /F /Q "C:\Users\Public\Desktop\MudoSoft*.lnk" >nul 2>&1
del /F /Q "C:\Users\Public\Desktop\*VNC*.lnk" >nul 2>&1
del /F /Q "C:\temp\mudoinstall.bat" >nul 2>&1
del /F /Q "C:\temp\mudo_start_agent_service.bat" >nul 2>&1
del /F /Q "C:\temp\mudo_start_agent_service.log" >nul 2>&1
del /F /Q "C:\temp\tightvnc.msi" >nul 2>&1
del /F /Q "C:\temp\tightvnc_mudosoft.msi" >nul 2>&1
del /F /Q "C:\temp\vnc_*.*" >nul 2>&1
del /F /Q "C:\temp\mudosoft*.*" >nul 2>&1
del /F /Q "C:\Windows\Temp\mudosoft*" >nul 2>&1
del /F /Q "C:\Windows\Temp\tightvnc*" >nul 2>&1
for /D %%U in ("C:\Users\*") do (
    del /F /Q "%%~fU\AppData\Local\Temp\mudosoft*" >nul 2>&1
    del /F /Q "%%~fU\AppData\Local\Temp\tightvnc*" >nul 2>&1
    del /F /Q "%%~fU\Desktop\MudoSoft*.lnk" >nul 2>&1
    del /F /Q "%%~fU\Desktop\*VNC*.lnk" >nul 2>&1
    del /F /Q "%%~fU\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\MudoSoft*.lnk" >nul 2>&1
    del /F /Q "%%~fU\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\*VNC*.lnk" >nul 2>&1
)

call :Step "9/12" "MudoSoft ve VNC ozel dosyalari temizleniyor"
del /F /Q "C:\Program Files\MudoSoft\Agent\device_id.txt" >nul 2>&1
del /F /Q "C:\Program Files\MudoSoft\Agent\vnc_password.dat" >nul 2>&1
del /F /Q "C:\MudoSoftAgent\device_id.txt" >nul 2>&1
del /F /Q "C:\MudoSoftAgent\vnc_password.dat" >nul 2>&1
del /F /Q "%ProgramData%\Microsoft\Windows\Start Menu\Programs\MudoSoft*.lnk" >nul 2>&1
del /F /Q "%ProgramData%\Microsoft\Windows\Start Menu\Programs\*VNC*.lnk" >nul 2>&1

call :Step "10/12" "WMI, event log ve ek kalintilar temizleniyor"
wevtutil cl "MudoSoft" >nul 2>&1
wevtutil cl "VNC Server" >nul 2>&1
rmdir /S /Q "%LOCALAPPDATA%\WixToolset" >nul 2>&1

call :Step "11/12" "Son temizlik - tekrar proses ve servis kontrolu"
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
for %%S in (
    MudosoftAgentService
    tvnserver
    uvnc_service
    WinVNC4
    VNCServer
) do (
    sc stop "%%~S" >nul 2>&1
    sc delete "%%~S" >nul 2>&1
)

call :Step "12/12" "Dogrulama"
call :CheckMissing "C:\Program Files\MudoSoft"
call :CheckMissing "C:\Program Files\TightVNC"
call :CheckMissing "C:\Program Files (x86)\TightVNC"
call :CheckServiceGone "MudosoftAgentService"
call :CheckServiceGone "tvnserver"

echo. >> "%LOG%"
echo Deep clean tamamlandi. %DATE% %TIME% >> "%LOG%"
echo.
echo ======================================================================
echo   TEMIZLIK TAMAMLANDI
echo   Log: %LOG%
echo.
echo   Not:
echo   - Kalan kilitli dosya olursa reboot sonrasi tekrar calistirabilirsiniz.
echo   - Bugunluk karsilastirma testi icin bu makine MudoSoft ve VNC'sizdir.
echo ======================================================================
echo.
pause
exit /b 0

:Step
echo.
echo ====================================================================== 
echo [%~1] %~2
echo ======================================================================
echo [%~1] %~2 >> "%LOG%"
exit /b 0

:DeleteUninstallKeys
for %%H in (
    "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"
    "HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    "HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"
) do (
    for /f "tokens=*" %%I in ('reg query "%%~H" /s /f "%~1" 2^>nul ^| findstr /B /C:"HKEY_"') do (
        reg delete "%%~I" /f >nul 2>&1
    )
)
exit /b 0

:DeleteInstallerKeys
for %%H in (
    "HKCR\Installer\Products"
    "HKLM\SOFTWARE\Classes\Installer\Products"
    "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\UserData\S-1-5-18\Products"
) do (
    for /f "tokens=*" %%I in ('reg query "%%~H" /s /f "%~1" 2^>nul ^| findstr /B /C:"HKEY_"') do (
        reg delete "%%~I" /f >nul 2>&1
    )
)
exit /b 0

:CheckMissing
if exist "%~1" (
    echo UYARI: Klasor kalmis -> %~1
    echo UYARI: Klasor kalmis -> %~1 >> "%LOG%"
) else (
    echo OK: Klasor temiz -> %~1
    echo OK: Klasor temiz -> %~1 >> "%LOG%"
)
exit /b 0

:CheckServiceGone
sc query "%~1" >nul 2>&1
if %ERRORLEVEL% EQU 1060 (
    echo OK: Servis temiz -> %~1
    echo OK: Servis temiz -> %~1 >> "%LOG%"
) else (
    echo UYARI: Servis kalmis olabilir -> %~1
    echo UYARI: Servis kalmis olabilir -> %~1 >> "%LOG%"
)
exit /b 0
