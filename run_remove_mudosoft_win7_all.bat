@echo off
cd /d E:\Mudosoft
echo.
echo ========================================
echo  Win7 MudoSoft Full Removal
echo ========================================
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "E:\Mudosoft\remove_mudosoft_win7_all.ps1"
echo.
echo ========================================
echo  Islem tamamlandi.
echo  Cikmak icin bir tusa basin.
echo ========================================
pause >nul
