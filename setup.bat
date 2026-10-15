@echo off
rem First-time setup: double-click this after cloning MindAttic.Launcher.
rem Runs setup.ps1, which asks where the workspace lives, whether to add a
rem desktop icon, and which project to seed the roster with, then deploys.
setlocal
title MindAttic.Launcher Setup
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup.ps1"
if errorlevel 1 (
    echo.
    echo Setup did not finish - see the messages above.
)
echo.
pause
