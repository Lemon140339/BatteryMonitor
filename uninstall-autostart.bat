@echo off
setlocal
net session >nul 2>&1
if errorlevel 1 (
  echo.
  echo Please right-click this file and choose "Run as administrator".
  echo.
  pause
  exit /b 1
)

schtasks /End /TN "BatteryMonitor" >nul 2>&1
schtasks /Delete /TN "BatteryMonitor" /F

echo.
echo Autostart removed.
echo.
pause
