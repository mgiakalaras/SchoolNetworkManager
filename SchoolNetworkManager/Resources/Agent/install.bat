@echo off
rem School Network Manager - agent install. Right click > Run as administrator (or /quiet for silent use).
net session >nul 2>&1
if errorlevel 1 (
  echo.
  echo  Please run this file as Administrator ^(right click ^> Run as administrator^).
  if /i not "%~1"=="/quiet" pause
  exit /b 1
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"
if errorlevel 1 (
  echo  Installation FAILED.
  if /i not "%~1"=="/quiet" pause
  exit /b 1
)
echo.
echo  Agent installed. The computer will now appear in School Network Manager.
if /i not "%~1"=="/quiet" pause
