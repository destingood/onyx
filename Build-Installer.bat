@echo off
title Construction de l'installateur DesTinGOOD PC Optimizer
setlocal enabledelayedexpansion
cd /d "%~dp0"

rem --- Elevation (fermer l'app + ecrire dans dist) ------------------------------
whoami /groups | find "S-1-16-12288" >nul 2>&1
if %errorlevel% neq 0 (
    echo Demande des droits administrateur...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

where dotnet >nul 2>&1
if %errorlevel% neq 0 (
    echo [X] .NET SDK introuvable. Installe-le : https://dotnet.microsoft.com/download
    pause & exit /b 1
)

echo === 1/4  Fermeture de l'app si elle tourne ===
taskkill /IM DTGOptimizer.exe /F >nul 2>&1
taskkill /IM dotnet.exe /FI "WINDOWTITLE eq DesTinGOOD PC Optimizer*" /F >nul 2>&1

echo === 2/4  Publication (.NET 10, dependant du runtime) ===
rem Nettoie les fichiers d'etat/symboles qui ne doivent pas etre distribues.
del /q "dist\dtg-*.txt" >nul 2>&1
del /q "dist\dtg-*.csv" >nul 2>&1
del /q "dist\dtg-*.nip" >nul 2>&1
del /q "dist\bt-*.txt"  >nul 2>&1
del /q "dist\bt-*.csv"  >nul 2>&1
del /q "dist\bt-*.nip"  >nul 2>&1
del /q "dist\*.pdb"      >nul 2>&1
dotnet publish DTGOptimizer.csproj -c Release -o dist --nologo -p:DebugType=none
if %errorlevel% neq 0 (
    echo [X] Echec de la publication.
    pause & exit /b 1
)

echo === 3/4  Recherche d'Inno Setup (ISCC.exe) ===
set "ISCC="
for %%P in (
    "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
    "%ProgramFiles%\Inno Setup 6\ISCC.exe"
    "%LocalAppData%\Programs\Inno Setup 6\ISCC.exe"
) do if exist "%%~P" set "ISCC=%%~P"

if not defined ISCC (
    echo Inno Setup n'est pas installe.
    where winget >nul 2>&1
    if !errorlevel! equ 0 (
        choice /C ON /N /M "Installer Inno Setup maintenant via winget ? [O]ui / [N]on : "
        if !errorlevel! equ 1 (
            winget install -e --id JRSoftware.InnoSetup --accept-source-agreements --accept-package-agreements
            for %%P in (
                "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
                "%ProgramFiles%\Inno Setup 6\ISCC.exe"
                "%LocalAppData%\Programs\Inno Setup 6\ISCC.exe"
            ) do if exist "%%~P" set "ISCC=%%~P"
        )
    )
)

if not defined ISCC (
    echo.
    echo [!] Inno Setup introuvable. Installe-le puis relance ce script :
    echo        winget install -e --id JRSoftware.InnoSetup
    echo     ou telechargement : https://jrsoftware.org/isdl.php
    echo.
    echo Le binaire est pret dans  dist\  ^(tu peux aussi diffuser un ZIP portable^).
    pause & exit /b 1
)

echo === 4/4  Compilation de l'installateur ===
"%ISCC%" "installer\DTGOptimizer.iss"
if %errorlevel% neq 0 (
    echo [X] Echec de la compilation de l'installateur.
    pause & exit /b 1
)

echo.
echo [OK] Installateur cree dans  installer\Output\
explorer "installer\Output"
pause
