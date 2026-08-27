@echo off
setlocal EnableExtensions EnableDelayedExpansion
rem ============================================================================
rem  Back the working folder up to the source repo on GitHub
rem  (yossi-computers/HebrewBooks-2026). This is a BACKUP, not a release: it
rem  commits whatever is in C:\Dev\HebrewBooks and pushes the current branch.
rem  Publishing a version to installed copies is the separate release script.
rem
rem  The CONTENTS of this file are pure ASCII on purpose, and it must keep CRLF
rem  line endings - with LF alone cmd mis-reads the :labels and every goto lands
rem  in the wrong place. The Hebrew belongs in the FILE NAME, not the body.
rem
rem  NOTHING here exits without going through :end.
rem ============================================================================
cd /d "%~dp0"
set "RC=1"
set "BRANCH="

echo.
echo   ==========================================================
echo    HebrewBooks - back the project up to GitHub
echo   ==========================================================
echo.

where git >nul 2>nul
if errorlevel 1 (
  echo   git is not installed, or not on PATH. Get it from https://git-scm.com
  goto end
)

for /f "delims=" %%B in ('git rev-parse --abbrev-ref HEAD 2^>nul') do set "BRANCH=%%B"
if not defined BRANCH (
  echo   This folder is not a git repository - nothing to back up.
  goto end
)
if /i "%BRANCH%"=="HEAD" (
  echo   No branch is checked out (detached HEAD). Check out a branch first:
  echo       git switch -c main
  goto end
)

rem --- Refuse while a merge or rebase is half-finished (conflict markers would
rem     otherwise be committed as if they were source). --------------------------
for /f "delims=" %%G in ('git rev-parse --git-dir 2^>nul') do set "GITDIR=%%G"
if not defined GITDIR set "GITDIR=.git"
if exist "%GITDIR%\MERGE_HEAD"        goto midmerge
if exist "%GITDIR%\rebase-merge"      goto midmerge
if exist "%GITDIR%\rebase-apply"      goto midmerge
if exist "%GITDIR%\CHERRY_PICK_HEAD"  goto midmerge

git status --porcelain >"%TEMP%\hb_status.txt" 2>nul
for %%A in ("%TEMP%\hb_status.txt") do set "DIRTY=%%~zA"
del "%TEMP%\hb_status.txt" >nul 2>nul

if "%DIRTY%"=="0" (
  echo   Nothing changed since the last backup on branch "%BRANCH%".
  echo   Pushing anyway in case a previous commit was never sent...
  git push origin "%BRANCH%"
  set "RC=%ERRORLEVEL%"
  goto done
)

echo   Branch: %BRANCH%
echo.
echo   Describe what changed (Enter = "backup <date> <time>").
set /p "MSG=  Message: "
if "!MSG!"=="" set "MSG=backup %DATE% %TIME%"

echo.
echo   Staging and committing all changes...
git add -A
git commit -m "!MSG!"
if errorlevel 1 (
  echo.
  echo   Commit failed - see above.
  goto end
)

echo.
echo   Pushing to origin/%BRANCH% ...
git push origin "%BRANCH%"
set "RC=%ERRORLEVEL%"

:done
if not "%RC%"=="0" (
  echo.
  echo   BACKUP FAILED - see the errors above.
  echo   If this is the first push, set the remote once:
  echo       git remote add origin https://github.com/yossi-computers/HebrewBooks-2026
  echo       git push -u origin %BRANCH%
  goto end
)
echo.
echo   Backed up to https://github.com/yossi-computers/HebrewBooks-2026 (branch %BRANCH%).
goto end

:midmerge
echo   A merge or rebase is half-finished. Resolve it first, then run this again.
goto end

:end
echo.
pause
exit /b %RC%
