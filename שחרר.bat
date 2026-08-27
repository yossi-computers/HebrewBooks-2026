@echo off
setlocal EnableExtensions
rem ============================================================================
rem  Publish a new HebrewBooks version to GitHub Releases on
rem  yossi-computers/HebrewBooks-2026 . The installed app's auto-updater reads
rem  that repo and the 'stable' channel, so a full release rolls out to it.
rem
rem  The CONTENTS of this file are pure ASCII on purpose (see build.bat). The
rem  Hebrew belongs in the FILE NAME, not the body.
rem
rem  Usage:  (double-click)  -> asks for the version, notes and channel
rem          release.bat 3.0.120 "what changed"              -> public release
rem          release.bat 3.0.120 "what changed" -prerelease  -> pre-release
rem          release.bat 3.0.120 "what changed" -draft       -> draft
rem          release.bat 3.0.120 -draft                      -> draft, default notes
rem
rem  A full public release reaches installed copies. -prerelease is on the page
rem  for a manual install but no copy updates itself to it; -draft is invisible
rem  to everyone but you. Re-running the SAME version merges into that release,
rem  so a run that died mid-upload is finished by running it again.
rem
rem  NOTHING here exits without going through :end.
rem ============================================================================
cd /d "%~dp0"

set "VER=%~1"
set "NOTES=%~2"
set "CHANNEL="
set "RC=1"

if /i "%~2"=="-draft"      ( set "CHANNEL=draft"      & set "NOTES=" )
if /i "%~2"=="-prerelease" ( set "CHANNEL=prerelease" & set "NOTES=" )
if /i "%~3"=="-draft"      set "CHANNEL=draft"
if /i "%~3"=="-prerelease" set "CHANNEL=prerelease"

if not "%VER%"=="" goto validate

rem --- interactive (double-click) ---------------------------------------------
echo.
echo   ==========================================================
echo    HebrewBooks - publish a new version to GitHub Releases
echo   ==========================================================
echo.
call :showlatest
echo.
set /p "VER=  New version X.Y.Z (Enter = cancel): "
if "%VER%"=="" goto cancelled
echo.
echo   Release notes - what changed. Enter = "HebrewBooks %VER%".
set /p "NOTES=  Notes: "
echo.
echo   Which channel? Only a full release reaches installed copies.
echo.
echo     1 = release       installed copies update to it automatically
echo     2 = pre-release   on the page for a MANUAL install, no auto-update
echo     3 = draft         nobody can see or download it except you
echo.
set /p "ANS=  Choose [1/2/3, Enter = 1]: "
if "%ANS%"=="2" set "CHANNEL=prerelease"
if "%ANS%"=="3" set "CHANNEL=draft"
if "%ANS%"=="" goto validate
if "%ANS%"=="1" goto validate
if not defined CHANNEL (
  echo.
  echo   "%ANS%" is not one of 1, 2 or 3.
  goto end
)

:validate
echo(%VER%| findstr /r /x "[0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*" >nul
if errorlevel 1 (
  echo.
  echo   "%VER%" is not a version number. It must be X.Y.Z, for example 3.0.120,
  echo   and higher than the last release or installed copies will not see it.
  goto end
)
if "%NOTES%"=="" set "NOTES=HebrewBooks %VER%"

echo.
if "%CHANNEL%"=="draft" (
  echo   About to publish v%VER% as a DRAFT. Nobody else can see it.
) else if "%CHANNEL%"=="prerelease" (
  echo   About to publish v%VER% as a PRE-RELEASE. On the page, no auto-update.
) else (
  echo   About to publish v%VER% - a full PUBLIC release. Installed copies update.
)
echo   Notes: %NOTES%
echo.
set /p "ANS=  Type Y to build and release: "
if /i not "%ANS%"=="y" goto cancelled

if "%CHANNEL%"=="draft"      goto draft
if "%CHANNEL%"=="prerelease" goto prerelease

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0release.ps1" -Version "%VER%" -Notes "%NOTES%"
set "RC=%ERRORLEVEL%"
goto done

:draft
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0release.ps1" -Version "%VER%" -Notes "%NOTES%" -Draft
set "RC=%ERRORLEVEL%"
goto done

:prerelease
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0release.ps1" -Version "%VER%" -Notes "%NOTES%" -Prerelease
set "RC=%ERRORLEVEL%"

:done
if not "%RC%"=="0" (
  echo.
  echo   RELEASE FAILED - see the errors above.
  goto end
)
goto end

:cancelled
echo.
echo   Cancelled - nothing was built and nothing was published.
set "RC=1"
goto end

:showlatest
for /f "delims=" %%V in ('gh release view -R yossi-computers/HebrewBooks-2026 --json tagName -q .tagName 2^>nul') do set "LATEST=%%V"
if defined LATEST (
  echo   Latest published release: %LATEST%
) else (
  echo   [no releases yet, or gh is not logged in]
)
goto :eof

:end
echo.
pause
exit /b %RC%
