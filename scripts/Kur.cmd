@echo off
rem Sag tikla > Yonetici olarak calistir. Paket kokunde ya da scripts klasorunde calisir.
set "S=%~dp0scripts\install-service.ps1"
if not exist "%S%" set "S=%~dp0install-service.ps1"
powershell -NoProfile -ExecutionPolicy Bypass -File "%S%"
echo.
if errorlevel 1 (echo HATA: islem tamamlanmadi. Yukaridaki kirmizi mesaji okuyun.) else (echo Kurulum tamam. Tarayicida http://localhost:5080 adresini acin.)
echo.
pause
