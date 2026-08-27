@echo off
setlocal EnableExtensions
rem ============================================================================
rem  Build HebrewBooks from source (Release), NO obfuscation.
rem
rem  The CONTENTS of this file are pure ASCII on purpose. cmd.exe reads a .bat
rem  using the console codepage, not UTF-8: Hebrew text inside the body shifts
rem  its line parsing and it starts running fragments of its own commands. The
rem  Hebrew belongs in the FILE NAME, which cmd handles fine.
rem
rem  Double-click to build. Pass -publish to also produce the self-contained
rem  app folder (publish\app) that a release is packed from:
rem      build.bat            -> compile only
rem      build.bat -publish   -> compile + publish app folder
rem
rem  NOTHING here exits without going through :end - a double-clicked console
rem  closes the instant the script returns, so a bare exit flashes errors past.
rem ============================================================================
cd /d "%~dp0"
set "RC=1"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo.
  echo   .NET SDK not found on PATH. Install it from https://dotnet.microsoft.com
  goto end
)

set "PUBLISH="
if /i "%~1"=="-publish" set "PUBLISH=-Publish"

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %PUBLISH%
set "RC=%ERRORLEVEL%"

if not "%RC%"=="0" (
  echo.
  echo   BUILD FAILED - see the errors above.
  goto end
)

:end
echo.
pause
exit /b %RC%
