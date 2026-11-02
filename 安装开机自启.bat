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

powershell -NoProfile -ExecutionPolicy Bypass -Command "$exe='%~dp0BatteryMonitor.exe'; $a=New-ScheduledTaskAction -Execute $exe; $t=New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME; $p=New-ScheduledTaskPrincipal -UserId ($env:USERDOMAIN+'\'+$env:USERNAME) -LogonType Interactive -RunLevel Highest; $s=New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero); Register-ScheduledTask -TaskName 'BatteryMonitor' -Action $a -Trigger $t -Principal $p -Settings $s -Force | Out-Null; Start-ScheduledTask -TaskName 'BatteryMonitor'"

echo.
echo Done. It will start automatically at every logon.
echo.
pause
