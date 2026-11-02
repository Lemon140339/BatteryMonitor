@echo off
setlocal
set "HERE=%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo.
  echo csc.exe not found. Install .NET Framework 4.x first.
  echo.
  exit /b 1
)

echo Building with:
echo   %CSC%
echo.

"%CSC%" /nologo /target:winexe /out:"%HERE%BatteryMonitor.exe" ^
  "%HERE%src\*.cs" ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll

if errorlevel 1 (
  echo.
  echo BUILD FAILED
  exit /b 1
)

echo.
echo OK -^> BatteryMonitor.exe
