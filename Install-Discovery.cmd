@echo off
setlocal
set "compiler=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%compiler%" set "compiler=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%compiler%" (
  echo The Windows .NET Framework compiler was not found. Manual proxy setup is available.
  pause
  exit /b 1
)
set "staging=%TEMP%\site-proxy-discovery-%RANDOM%-%RANDOM%.exe"
if exist "%staging%" exit /b 1
"%compiler%" /nologo /target:exe /optimize+ /r:System.Web.Extensions.dll /out:"%staging%" "%~dp0native\Discovery.cs" "%~dp0native\Program.cs"
if errorlevel 1 (
  pause
  exit /b 1
)
"%staging%" --install "%~dp0extension\manifest.json"
set "result=%errorlevel%"
del /q "%staging%"
pause
exit /b %result%
