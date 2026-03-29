@echo off
cd /d E:\Mudosoft
echo.
echo ========================================
echo  Win7 Uninstall BAT Copy
echo ========================================
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "E:\Mudosoft\copy_uninstall_to_win7_all.ps1"
echo.
echo ========================================
echo  Islem tamamlandi.
echo  Cikmak icin bir tusa basin.
echo ========================================
pause >nul
