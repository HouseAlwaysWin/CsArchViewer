@echo off
setlocal EnableExtensions EnableDelayedExpansion

if "%~1"=="" (
  echo Usage: %~nx0 vX.Y.Z
  echo Example: %~nx0 v1.2.3
  exit /b 1
)

set "VERSION_TAG=%~1"
set "REMOTE_NAME=origin"

echo [release] Start release for %VERSION_TAG%

where git >nul 2>nul
if errorlevel 1 (
  echo [release] ERROR: git command not found.
  exit /b 1
)

git rev-parse --is-inside-work-tree >nul 2>nul
if errorlevel 1 (
  echo [release] ERROR: not inside a git repository.
  exit /b 1
)

echo %VERSION_TAG% | findstr /R /C:"^v[0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*$" >nul
if errorlevel 1 (
  echo [release] ERROR: invalid version format.
  echo [release] Expected format: vX.Y.Z ^(example: v1.2.3^)
  exit /b 1
)

for /f "tokens=* usebackq" %%b in (`git branch --show-current`) do set "CURRENT_BRANCH=%%b"
if "%CURRENT_BRANCH%"=="" (
  echo [release] ERROR: failed to detect current branch.
  exit /b 1
)

for /f "tokens=* usebackq" %%s in (`git status --porcelain`) do (
  echo [release] ERROR: working tree is not clean. Please commit or stash changes first.
  exit /b 1
)

git rev-parse --verify "refs/tags/%VERSION_TAG%" >nul 2>nul
if not errorlevel 1 (
  echo [release] Existing local tag found: %VERSION_TAG%, deleting...
  git tag -d "%VERSION_TAG%"
  if errorlevel 1 (
    echo [release] ERROR: failed to delete local tag %VERSION_TAG%.
    exit /b 1
  )
)

git ls-remote --tags %REMOTE_NAME% "refs/tags/%VERSION_TAG%" | findstr /R /C:".*" >nul
if not errorlevel 1 (
  echo [release] Existing remote tag found: %VERSION_TAG%, deleting...
  git push %REMOTE_NAME% ":refs/tags/%VERSION_TAG%"
  if errorlevel 1 (
    echo [release] ERROR: failed to delete remote tag %VERSION_TAG%.
    exit /b 1
  )
)

echo [release] Pushing branch %CURRENT_BRANCH% to %REMOTE_NAME%...
git push %REMOTE_NAME% "%CURRENT_BRANCH%"
if errorlevel 1 (
  echo [release] ERROR: failed to push branch.
  exit /b 1
)

echo [release] Creating annotated tag %VERSION_TAG%...
git tag -a "%VERSION_TAG%" -m "Release %VERSION_TAG%"
if errorlevel 1 (
  echo [release] ERROR: failed to create tag.
  exit /b 1
)

echo [release] Pushing tag %VERSION_TAG%...
git push %REMOTE_NAME% "%VERSION_TAG%"
if errorlevel 1 (
  echo [release] ERROR: failed to push tag.
  echo [release] You can delete local tag with: git tag -d %VERSION_TAG%
  exit /b 1
)

echo [release] Done. GitHub Action "release" should start automatically.
echo [release] Check Actions: https://github.com/martin951/CsArchViewer/actions
exit /b 0
