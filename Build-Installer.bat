@echo off
title Construction de l'installateur BT Optimizer
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
taskkill /IM BTOptimizer.exe /F >nul 2>&1
taskkill /IM dotnet.exe /FI "WINDOWTITLE eq BT Optimizer*" /F >nul 2>&1

echo === 2/4  Publication (.NET 10, dependant du runtime) ===
rem Repart d'un dist propre : retire l'etat/symboles ET tout reste d'une publication
rem AUTONOME precedente (coreclr.dll) qui ferait sauter a tort la verif .NET du .iss.
rem MAIS on PRESERVE les donnees utilisateur (tout ce qui commence par "bt-") :
rem bt-license.txt (la cle Pro !), bt-trial.txt, bt-gamecache, reglages...
if exist dist (
    for /d %%D in (dist\*) do echo %%~nxD| findstr /b /i "bt-" >nul || rd /s /q "%%D"
    for %%F in (dist\*) do echo %%~nxF| findstr /b /i "bt-" >nul || del /q "%%F"
)
dotnet publish BTOptimizer.csproj -c Release -o dist --nologo -p:DebugType=none
if %errorlevel% neq 0 (
    echo [X] Echec de la publication.
    pause & exit /b 1
)

rem Signature du binaire de l'app (sans effet si aucun certificat configure).
call "%~dp0Sign.bat" "dist\BTOptimizer.exe"

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
"%ISCC%" "installer\BTOptimizer.iss"
if %errorlevel% neq 0 (
    echo [X] Echec de la compilation de l'installateur.
    pause & exit /b 1
)

rem Signature de l'installateur genere (sans effet si aucun certificat configure).
for %%F in ("installer\Output\*.exe") do call "%~dp0Sign.bat" "%%~fF"

echo.
echo [OK] Installateur cree dans  installer\Output\
explorer "installer\Output"
pause
