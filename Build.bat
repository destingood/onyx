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
echo Nettoyage de l'ancien build (evite un dist hybride autonome/framework)...
if exist dist rd /s /q dist

echo.
echo Compilation .NET 10 AUTONOME (runtime embarque : marche sans installer .NET)...
dotnet publish BTOptimizer.csproj -c Release -o dist -r win-x64 --self-contained true --nologo
if %errorlevel% neq 0 (
    echo.
    echo ECHEC de la compilation.
    pause
    exit /b 1
)

echo.
echo OK : dist\BTOptimizer.exe cree (autonome, runtime .NET embarque).
echo Double-clique dist\BTOptimizer.exe : il marche sans installer .NET.
echo (Lancer-BTOptimizer.bat reste utile seulement pour lancer en admin.)
pause
