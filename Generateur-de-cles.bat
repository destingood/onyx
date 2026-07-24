@echo off
title ONYX - Generateur de licences
cd /d "%~dp0"

rem  Lance le generateur de cles GRAPHIQUE via l'hote dotnet (signe Microsoft).
rem  IMPORTANT : Smart App Control autorise dotnet.exe (signe) mais BLOQUE un .exe
rem  compile localement non signe. On execute donc la DLL, pas l'apphost.

set "DLL=%~dp0seller\gui\bin\Release\net10.0-windows\ONYXKeygen.dll"

if not exist "%DLL%" (
    echo Premiere utilisation : compilation du generateur...
    dotnet build "%~dp0seller\gui\Keygen.csproj" -c Release --nologo -v q
    if errorlevel 1 (
        echo.
        echo Echec de compilation. Le SDK .NET 10 est-il installe ?
        pause
        exit /b 1
    )
)

echo Ouverture du generateur de cles ONYX...
echo (garde le dossier "seller" PRIVE : il contient ta cle-maitresse private.xml)
echo.
dotnet "%DLL%"
if errorlevel 1 (
    echo.
    echo L'application s'est terminee avec une erreur.
    pause
)
