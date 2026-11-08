@echo off
setlocal
set "SERVER_EXE=%~dp0Builds\WindowsServer\SyntheticFrontServer.exe"
set "SERVER_CONFIG=%~1"
if not defined SERVER_CONFIG set "SERVER_CONFIG=%~dp0Builds\WindowsServer\server.json"
if not exist "%SERVER_EXE%" (
  echo Server build missing. Run BuildServer.bat first.
  exit /b 2
)
if not exist "%SERVER_CONFIG%" (
  echo Server config missing: %SERVER_CONFIG%
  exit /b 2
)
"%SERVER_EXE%" -batchmode -nographics -server-config "%SERVER_CONFIG%" -logFile -
exit /b %errorlevel%
