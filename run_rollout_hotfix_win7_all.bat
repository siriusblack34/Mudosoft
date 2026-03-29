@echo off
cd /d E:\Mudosoft
echo.
echo ========================================
echo  MudoSoft Win7 Hotfix Rollout
echo ========================================
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "E:\Mudosoft\rollout_hotfix_win7_all.ps1"
echo.
echo ========================================
echo  Islem tamamlandi.
echo  Cikmak icin bir tusa basin.
echo ========================================
pause >nul
