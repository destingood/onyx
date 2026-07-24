@echo off
title Fluide - Generateur de licences
cd /d "%~dp0"

rem  Via l'hote dotnet SIGNE (Smart App Control autorise dotnet.exe, pas un .exe local non signe).
set "DLL=%~dp0gui\bin\Release\net10.0-windows\FluideKeygen.dll"

if not exist "%DLL%" (
    echo Compilation du generateur...
    dotnet build "%~dp0gui\Keygen.csproj" -c Release --nologo -v q
    if errorlevel 1 (
        echo.
        echo Echec de compilation. Le SDK .NET 10 est-il installe ?
        pause
        exit /b 1
    )
)

echo Ouverture du generateur de cles Fluide...
echo (garde ce dossier "seller" PRIVE : il contient ta cle-maitresse private.xml)
echo.
dotnet "%DLL%"
if errorlevel 1 (
    echo.
    echo L'application s'est terminee avec une erreur.
    pause
)
