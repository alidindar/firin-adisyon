@echo off
rem Sag tikla > Yonetici olarak calistir. Paket kokunde ya da scripts klasorunde calisir.
set "S=%~dp0scripts\uninstall-service.ps1"
if not exist "%S%" set "S=%~dp0uninstall-service.ps1"
powershell -NoProfile -ExecutionPolicy Bypass -File "%S%"
echo.
if errorlevel 1 (echo HATA: islem tamamlanmadi. Yukaridaki kirmizi mesaji okuyun.) else (echo Kaldirildi. Veriler C:\FirinAdisyon\data altinda duruyor.)
echo.
pause
