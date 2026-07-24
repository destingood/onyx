@echo off
title Fluide - Generateur de licences
cd /d "%~dp0gui"
echo Ouverture du generateur de cles Fluide...
echo (garde ce dossier "seller" PRIVE : il contient ta cle-maitresse private.xml)
echo.
dotnet run -c Release
if %errorlevel% neq 0 (
    echo.
    echo Echec. Verifie que le SDK .NET est installe et que private.xml est dans "seller\".
    pause
)
