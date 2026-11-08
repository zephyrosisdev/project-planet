@echo off
setlocal
set "SERVER_EXE=%~dp0Builds\WindowsServer\SyntheticFrontServer.exe"
if not exist "%SERVER_EXE%" (
  echo Server build missing. Run BuildServer.bat first.
  exit /b 2
)
if not exist "%~dp0Logs" mkdir "%~dp0Logs"
pushd "%~dp0"
"%SERVER_EXE%" -batchmode -nographics -survival-simulation-checks -logFile "%~dp0Logs\ServerSimulationChecks.log"
set "CHECK_RESULT=%errorlevel%"
popd
exit /b %CHECK_RESULT%
