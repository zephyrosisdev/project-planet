@echo off
setlocal
pushd "%~dp0"
"%~dp0SyntheticFront.exe" -survival-world large-test-100 -planet-size-multiplier 100 %*
set "RESULT=%errorlevel%"
popd
exit /b %RESULT%
