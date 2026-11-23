@echo off
setlocal
set "install=%LOCALAPPDATA%\SiteProxySwitcher"
if not exist "%install%\SiteProxyDiscovery.exe" (
  echo The discovery helper was not found.
  pause
  exit /b 1
)
"%install%\SiteProxyDiscovery.exe" --unregister
if errorlevel 1 (
  pause
  exit /b 1
)
del /q "%install%\SiteProxyDiscovery.exe"
del /q "%install%\host.json"
rmdir "%install%" 2>nul
echo Discovery files removed.
pause
