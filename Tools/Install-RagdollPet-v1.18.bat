@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Extract-RagdollPet-v1.18.ps1"
if errorlevel 1 (
  echo.
  echo Installation did not finish. Keep all downloaded parts in this folder and try again.
  pause
)
endlocal
