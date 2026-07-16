@echo off
title BT Optimizer
setlocal
set "BT_SCRIPT=%~dp0bt-optimizer.ps1"

if not exist "%BT_SCRIPT%" (
    echo Fichier introuvable : "%BT_SCRIPT%"
    pause
    exit /b 1
)

rem --- Elevation administrateur -------------------------------------------------
rem Test d'elevation via le niveau d'integrite (S-1-16-12288 = High = eleve).
rem Independant du service Serveur, contrairement a "net session".
whoami /groups | find "S-1-16-12288" >nul 2>&1
if %errorlevel% neq 0 (
    echo Demande des droits administrateur...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

rem --- Lancement ----------------------------------------------------------------
rem On execute le script via Invoke-Expression au lieu de -File : cela contourne
rem une strategie d'execution PowerShell "Restricted"/"AllSigned" (souvent posee
rem par GPO) que -ExecutionPolicy Bypass ne peut pas outrepasser pour un fichier.
powershell -NoProfile -ExecutionPolicy Bypass -Command "$env:BT_SCRIPT='%BT_SCRIPT%'; Invoke-Expression (Get-Content -LiteralPath $env:BT_SCRIPT -Raw)"

if %errorlevel% neq 0 (
    echo.
    echo Une erreur est survenue. Details ci-dessus.
    pause
)
endlocal
