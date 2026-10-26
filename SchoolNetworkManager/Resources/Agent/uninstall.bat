@echo off
net session >nul 2>&1
if errorlevel 1 ( echo Please run as Administrator. & pause & exit /b 1 )
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0uninstall.ps1"
if /i not "%~1"=="/quiet" pause
