@echo off
title BT Optimizer
setlocal
cd /d "%~dp0"

rem --- Elevation administrateur (test par niveau d'integrite) --------------------
whoami /groups | find "S-1-16-12288" >nul 2>&1
if %errorlevel% neq 0 (
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

rem --- Lancement via l'hote dotnet (signe Microsoft) ----------------------------
rem  Smart App Control autorise dotnet.exe (signe) : l'app tourne donc meme si un
rem  .exe compile localement serait bloque. Le code s'execute avec les droits admin
rem  herites de ce script.
set "DLL=%~dp0dist\BTOptimizer.dll"
if not exist "%DLL%" (
    echo Build introuvable : "%DLL%"
    echo Lance d'abord Build.bat pour compiler l'application.
    pause
    exit /b 1
)

set "DOTNET=dotnet"
where dotnet >nul 2>&1 || set "DOTNET=%ProgramFiles%\dotnet\dotnet.exe"

"%DOTNET%" "%DLL%"
if %errorlevel% neq 0 (
    echo.
    echo L'application s'est terminee avec le code %errorlevel%.
    pause
)
endlocal
