@echo off
title Compilation DesTinGOOD (.NET 10)
setlocal
cd /d "%~dp0"

rem --- Elevation (pour pouvoir fermer l'app en cours si besoin) ------------------
whoami /groups | find "S-1-16-12288" >nul 2>&1
if %errorlevel% neq 0 (
    echo Demande des droits administrateur...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

where dotnet >nul 2>&1
if %errorlevel% neq 0 (
    echo .NET SDK introuvable.
    echo Installe-le depuis https://dotnet.microsoft.com/download  ^(ou utilise Build-Framework.bat^).
    pause
    exit /b 1
)

echo Fermeture de l'app si elle tourne...
taskkill /IM BTOptimizer.exe /F >nul 2>&1
taskkill /IM dotnet.exe /FI "WINDOWTITLE eq DesTinGOOD*" /F >nul 2>&1

echo.
echo Compilation .NET 10 (compatible Smart App Control)...
dotnet publish BTOptimizer.csproj -c Release -o dist --nologo
if %errorlevel% neq 0 (
    echo.
    echo ECHEC de la compilation.
    pause
    exit /b 1
)

echo.
echo OK : dist\BTOptimizer.exe cree (v5.2).
echo Lance l'application avec  Lancer-BTOptimizer.bat  (passe par l'hote dotnet signe).
pause
