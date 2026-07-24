@echo off
title ONYX
setlocal
cd /d "%~dp0"

rem --- Elevation administrateur (test par niveau d'integrite) --------------------
whoami /groups | find "S-1-16-12288" >nul 2>&1
if %errorlevel% neq 0 (
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

rem --- Lancement du build mono-fichier ------------------------------------------
rem  Depuis PublishSingleFile (v14.45), il n'y a PLUS de BTOptimizer.dll : tout est
rem  dans dist\BTOptimizer.exe, qui passe Smart App Control meme non signe
rem  (verifie sur cette machine). On le lance directement ; il herite des droits
rem  admin de ce script, donc pas de second UAC.
set "EXE=%~dp0dist\BTOptimizer.exe"
if not exist "%EXE%" (
    echo Build introuvable : "%EXE%"
    echo Lance d'abord Build.bat pour compiler l'application.
    pause
    exit /b 1
)

"%EXE%"
if %errorlevel% neq 0 (
    echo.
    echo L'application s'est terminee avec le code %errorlevel%.
    pause
)
endlocal