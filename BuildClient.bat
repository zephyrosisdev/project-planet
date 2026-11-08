@echo off
setlocal
call "%~dp0Tools\BuildSurvival.bat" Client
exit /b %errorlevel%
