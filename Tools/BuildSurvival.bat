@echo off
setlocal
set "PROJECT=%~dp0.."
if /I not "%~1"=="Client" if /I not "%~1"=="Server" exit /b 2
if not defined UNITY_EDITOR (
  for /f "tokens=2" %%V in ('findstr /b "m_EditorVersion:" "%PROJECT%\ProjectSettings\ProjectVersion.txt"') do set "UNITY_VERSION=%%V"
)
if not defined UNITY_EDITOR set "UNITY_EDITOR=%ProgramFiles%\Unity\Hub\Editor\%UNITY_VERSION%\Editor\Unity.com"
if not exist "%UNITY_EDITOR%" (
  echo Unity editor not found. Set UNITY_EDITOR to the pinned editor's Unity.com or Unity.exe.
  exit /b 2
)
if not exist "%PROJECT%\Logs" mkdir "%PROJECT%\Logs"
set "MEMORY_ARGS=-job-worker-count 2"
if "%UNITY_BUILD_LOW_MEMORY%"=="1" set "MEMORY_ARGS=-job-worker-count 1"
"%UNITY_EDITOR%" -batchmode -nographics -quit %MEMORY_ARGS% -projectPath "%PROJECT%" -executeMethod MassiveArmy.EditorTools.SurvivalBuild.%~1 -logFile "%PROJECT%\Logs\Build%~1.log"
set "BUILD_RESULT=%errorlevel%"
if not "%BUILD_RESULT%"=="0" echo Build failed. See Logs\Build%~1.log. Close this project in Unity before batch building.
exit /b %BUILD_RESULT%
