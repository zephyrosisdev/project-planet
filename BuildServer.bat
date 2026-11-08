@echo off
setlocal
call "%~dp0Tools\BuildSurvival.bat" Server
exit /b %errorlevel%
