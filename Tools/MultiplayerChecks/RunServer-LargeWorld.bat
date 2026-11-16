@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0RunServerLargeWorld.ps1" %*
exit /b %errorlevel%
