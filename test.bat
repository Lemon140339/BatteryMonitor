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

echo Running logic tests: shell windows...
"%CSC%" /nologo /target:exe /out:"%HERE%.tmp-test\ShellWindowRulesTest.exe" ^
  "%HERE%src\ShellWindowRules.cs" ^
  "%HERE%tests\ShellWindowRulesTest.cs"
if errorlevel 1 goto :buildfail
"%HERE%.tmp-test\ShellWindowRulesTest.exe"
if errorlevel 1 goto :testfail

echo.
echo Running logic tests: profiles and models...
"%CSC%" /nologo /target:exe /out:"%HERE%.tmp-test\ProfileModelTest.exe" ^
  "%HERE%src\Profile.cs" ^
  "%HERE%src\HidDevice.cs" ^
  "%HERE%src\ConfigManifest.cs" ^
  "%HERE%src\DebugLog.cs" ^
  "%HERE%src\ProtocolCodec.cs" ^
  "%HERE%src\Hidpp.cs" ^
  "%HERE%src\Autostart.cs" ^
  "%HERE%tests\ProfileModelTest.cs" ^
  /r:System.Web.Extensions.dll /r:System.dll
if errorlevel 1 goto :buildfail
"%HERE%.tmp-test\ProfileModelTest.exe"
if errorlevel 1 goto :testfail

echo.
echo OK - all tests passed
exit /b 0

:buildfail
echo.
echo TEST BUILD FAILED
exit /b 1

:testfail
echo.
echo TESTS FAILED
exit /b 1
