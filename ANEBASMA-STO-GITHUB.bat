@echo off
chcp 65001 >nul
setlocal EnableExtensions
title School Network Manager - ανέβασμα στο GitHub

rem Ανεβάζει το School Network Manager στο https://github.com/mgiakalaras/SchoolNetworkManager με ένα διπλό κλικ.
rem Δεν ανεβαίνουν ποτέ η βάση, τα αντίγραφα ασφαλείας ή τα ανεβασμένα αρχεία (.gitignore).
rem Μετά το push, το GitHub Action φτιάχνει μόνο του το Docker image (καρτέλα Actions).

set "OWNER=mgiakalaras"
set "NAME=SchoolNetworkManager"
set "REPO=https://github.com/%OWNER%/%NAME%.git"
set "HERE=%~dp0"
set "WORK=%TEMP%\SNM-github"

where git >nul 2>&1
if errorlevel 1 goto nogit

rem ---- η έκδοση, από το SchoolNetworkManager.csproj (<Version>2.17.0</Version>)
set "VER="
for /f "tokens=3 delims=<>" %%v in ('findstr /c:"<Version>" "%HERE%SchoolNetworkManager\SchoolNetworkManager.csproj"') do set "VER=%%v"
if not defined VER goto noversion
echo.
echo  School Network Manager %VER%  -^>  %REPO%
echo.

if exist "%WORK%" rmdir /s /q "%WORK%"
git clone --quiet "%REPO%" "%WORK%"
if errorlevel 1 goto failed

robocopy "%HERE%." "%WORK%" /MIR /XD .git .github bin obj .vs publish /XF *.db *.db-shm *.db-wal *.zip *.tar /NFL /NDL /NJH /NJS /NP >nul
if errorlevel 8 goto failed

pushd "%WORK%"
git config user.name >nul 2>&1 || git config user.name "%OWNER%"
git config user.email >nul 2>&1 || git config user.email "%OWNER%@users.noreply.github.com"
git add -A
git diff --cached --quiet
if errorlevel 1 (
    git commit --quiet -m "School Network Manager %VER%"
    echo  Οι αλλαγές καταχωρήθηκαν.
) else (
    echo  Καμία αλλαγή από το τελευταίο ανέβασμα.
)
git push --quiet origin HEAD:main
if errorlevel 1 goto failedpop
popd

echo.
echo  Ανέβηκε. Σε λίγα λεπτά το Docker image είναι έτοιμο:
echo  https://github.com/%OWNER%/%NAME%/actions  (πράσινο = εντάξει)
echo  Μετά: Portainer - Stacks - schoolnetworkmanager - Update the stack, με Re-pull image.
echo.
echo  Έτοιμο!
pause
exit /b 0

:nogit
echo  Δεν βρέθηκε το Git. Εγκατάστησέ το από https://git-scm.com/download/win
echo  ή από PowerShell:  winget install Git.Git   και ξανατρέξε αυτό το αρχείο.
pause
exit /b 1

:noversion
echo  Δεν βρέθηκε η έκδοση στο SchoolNetworkManager\SchoolNetworkManager.csproj.
pause
exit /b 1

:failedpop
popd
:failed
echo.
echo  Κάτι απέτυχε (δες τα μηνύματα πιο πάνω). Τίποτα δεν χάθηκε από τον φάκελό σου.
pause
exit /b 1
