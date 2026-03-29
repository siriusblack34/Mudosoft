@echo off
:: ============================================================
::  Admin Share (C$) Erisimini Ac
::  Uzaktan kurulum yapilamayan makinelerde calistir
::  Yonetici olarak calistir
:: ============================================================
echo.
echo  ============================================================
echo   Admin Share (C$) Erisimini Ac
echo  ============================================================
echo.

net session >nul 2>&1
if %errorlevel% neq 0 (
    echo  [!] YONETICI OLARAK CALISTIRIN!
    pause
    exit /b 1
)

echo  [1/4] LocalAccountTokenFilterPolicy ayarlaniyor...
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" /v LocalAccountTokenFilterPolicy /t REG_DWORD /d 1 /f
echo.

echo  [2/4] Admin share'ler yeniden olusturuluyor...
reg add "HKLM\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters" /v AutoShareWks /t REG_DWORD /d 1 /f
reg add "HKLM\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters" /v AutoShareServer /t REG_DWORD /d 1 /f
echo.

echo  [3/4] Server servisi yeniden baslatiliyor...
net stop LanmanServer /y >nul 2>&1
timeout /t 2 /nobreak >nul
net start LanmanServer >nul 2>&1
echo        OK
echo.

echo  [4/4] Dogrulama...
net share C$ >nul 2>&1
if %errorlevel% equ 0 (
    echo   [+] C$ paylasimi AKTIF
) else (
    echo   [!] C$ hala aktif degil - reboot gerekebilir
)
net share ADMIN$ >nul 2>&1
if %errorlevel% equ 0 (
    echo   [+] ADMIN$ paylasimi AKTIF
) else (
    echo   [!] ADMIN$ hala aktif degil - reboot gerekebilir
)

echo.
echo  ============================================================
echo   Tamamlandi. Reboot sonrasi uzaktan kurulum yapilabilir.
echo  ============================================================
timeout /t 5 /nobreak >nul
