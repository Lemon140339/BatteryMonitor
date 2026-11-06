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

if not exist "%HERE%.tmp-test" mkdir "%HERE%.tmp-test"

echo Running logic tests...
"%CSC%" /nologo /target:exe /out:"%HERE%.tmp-test\ShellWindowRulesTest.exe" ^
  "%HERE%src\ShellWindowRules.cs" ^
  "%HERE%tests\ShellWindowRulesTest.cs"
if errorlevel 1 (
  echo.
  echo TEST BUILD FAILED
  exit /b 1
)

"%HERE%.tmp-test\ShellWindowRulesTest.exe"
if errorlevel 1 (
  echo.
  echo TESTS FAILED
  exit /b 1
)

echo.
echo OK - all tests passed
