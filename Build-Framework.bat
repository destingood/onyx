@echo off
title Compilation DesTinGOOD
setlocal
cd /d "%~dp0"

rem ---------------------------------------------------------------------------
rem  NOTE : la compilation "sans SDK" (csc.exe de .NET Framework 4.x) n'est plus
rem  possible. L'app cible desormais .NET 10 et utilise TraceEvent (netstandard2.0)
rem  dans EtwLive.cs / FpsEtw.cs, que le vieux compilateur ne sait pas referencer.
rem  Ce script passe donc par le SDK .NET (dotnet), comme Build.bat.
rem ---------------------------------------------------------------------------

rem S'elever pour pouvoir fermer l'app en cours (qui tourne en administrateur).
whoami /groups | find "S-1-16-12288" >nul 2>&1
if %errorlevel% neq 0 (
    echo Demande des droits administrateur pour la compilation...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

where dotnet >nul 2>&1
if %errorlevel% neq 0 (
    echo.
    echo .NET SDK introuvable. Il est desormais REQUIS ^(.NET 10^).
    echo Installe-le depuis https://dotnet.microsoft.com/download
    pause
    exit /b 1
)

rem Si l'application tourne encore, on la ferme (sinon fichier verrouille).
tasklist /FI "IMAGENAME eq BTOptimizer.exe" 2>nul | find /I "BTOptimizer.exe" >nul
if %errorlevel% equ 0 (
    echo Fermeture de DesTinGOOD en cours d'execution...
    taskkill /IM BTOptimizer.exe >nul 2>&1
    timeout /t 2 /nobreak >nul
    tasklist /FI "IMAGENAME eq BTOptimizer.exe" 2>nul | find /I "BTOptimizer.exe" >nul
    if %errorlevel% equ 0 taskkill /F /IM BTOptimizer.exe >nul 2>&1
    timeout /t 1 /nobreak >nul
)

echo.
echo Compilation .NET 10 (dotnet publish, tous les fichiers de src\) ...
dotnet publish BTOptimizer.csproj -c Release -o dist --nologo
if %errorlevel% neq 0 (
    echo.
    echo ECHEC de la compilation.
    pause
    exit /b 1
)

echo.
echo OK : dist\BTOptimizer.exe cree.
echo Lance l'application avec  Lancer-BTOptimizer.bat  ^(passe par l'hote dotnet signe^).
pause
