@echo off
setlocal
pushd "%~dp0"
if not exist "%~dp0SyntheticFront.exe" (
  echo RunWeatherTrace.bat must be beside the weather diagnostic SyntheticFront.exe.
  popd
  exit /b 2
)
if not exist "%~dp0Diagnostics" mkdir "%~dp0Diagnostics"
echo Weather recording is enabled. Play normally with your existing server.
echo After a flicker, press Ctrl+Shift+F10 to save the preceding render history.
echo JSON traces and screenshots are saved in the Diagnostics folder.
start "" /wait "%~dp0SyntheticFront.exe" -planet-weather-trace -planet-weather-trace-output "%~dp0Diagnostics" -logFile "%~dp0Diagnostics\Player.log" %*
set "WEATHER_TRACE_EXIT=%ERRORLEVEL%"
popd
exit /b %WEATHER_TRACE_EXIT%
