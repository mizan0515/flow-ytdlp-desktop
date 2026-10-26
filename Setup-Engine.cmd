@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Setup-Engine.ps1"
if errorlevel 1 (
  echo Setup failed. Review the error above and run again.
  pause
  exit /b 1
)
echo Setup complete. You can now start Flow.exe.
pause
