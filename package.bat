@echo off
setlocal
set "HERE=%~dp0"

rem 版本号必须传：package.bat v1.0.1（避免默认值把新版本打成旧号）
if "%~1"=="" (
  echo.
  echo Usage: package.bat v1.0.1
  echo.
  exit /b 1
)
set "VER=%~1"

set "DIST=%HERE%dist\BatteryMonitor"
set "ZIP=%HERE%dist\BatteryMonitor-%VER%.zip"

call "%HERE%build.bat"
if errorlevel 1 exit /b 1

if exist "%HERE%dist" rmdir /s /q "%HERE%dist"
mkdir "%DIST%\devices"
mkdir "%DIST%\docs"

copy /y "%HERE%BatteryMonitor.exe"        "%DIST%" >nul
copy /y "%HERE%USAGE-zh.txt"              "%DIST%" >nul
copy /y "%HERE%README.md"                 "%DIST%" >nul
copy /y "%HERE%DISCLAIMER.md"             "%DIST%" >nul
copy /y "%HERE%LICENSE"                   "%DIST%" >nul
copy /y "%HERE%install-autostart.bat"     "%DIST%" >nul
copy /y "%HERE%uninstall-autostart.bat"   "%DIST%" >nul
copy /y "%HERE%devices\*.json"            "%DIST%\devices" >nul
copy /y "%HERE%docs\*.*"                  "%DIST%\docs" >nul

powershell -NoProfile -Command "Compress-Archive -Path '%DIST%' -DestinationPath '%ZIP%' -Force"
if errorlevel 1 (
  echo.
  echo ZIP FAILED
  exit /b 1
)

echo.
echo Done -^> dist\BatteryMonitor-%VER%.zip
