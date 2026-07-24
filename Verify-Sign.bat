@echo off
rem ============================================================================
rem  Verify-Sign.bat  [fichier]
rem
rem  Verifie la signature des binaires produits (par defaut : dist\BTOptimizer.dll
rem  et dist\BTOptimizer.exe), ou du fichier passe en parametre.
rem
rem  Pourquoi ce script : signtool.exe n'est PAS dans le PATH, il vit dans le SDK
rem  Windows ("signtool n'est pas reconnu..."). On le localise ici exactement comme
rem  le fait Sign.bat, pour ne pas avoir a retenir un chemin a rallonge.
rem
rem  RAPPEL : la DLL est le fichier CRITIQUE. C'est elle que Smart App Control
rem  refuse quand elle n'est pas signee ; en .NET le .exe n'est qu'un lanceur.
rem ============================================================================
setlocal enabledelayedexpansion
cd /d "%~dp0"

rem --- Localise signtool.exe (variante x64 du SDK Windows) ---
set "SIGNTOOL="
for /f "delims=" %%S in ('where /r "%ProgramFiles(x86)%\Windows Kits\10\bin" signtool.exe 2^>nul ^| findstr /i "\\x64\\"') do if not defined SIGNTOOL set "SIGNTOOL=%%S"
if not defined SIGNTOOL for /f "delims=" %%S in ('where /r "%ProgramFiles%\Windows Kits\10\bin" signtool.exe 2^>nul ^| findstr /i "\\x64\\"') do if not defined SIGNTOOL set "SIGNTOOL=%%S"
if not defined SIGNTOOL ( where signtool >nul 2>&1 && set "SIGNTOOL=signtool" )
if not defined SIGNTOOL (
    echo [Verify] signtool.exe introuvable.
    echo          Installe le SDK Windows ^(composant "Signing Tools for Desktop Apps"^).
    pause
    exit /b 1
)
echo [Verify] signtool : !SIGNTOOL!

set "FILES=dist\BTOptimizer.dll dist\BTOptimizer.exe"
if not "%~1"=="" set "FILES=%~1"

set "KO=0"
for %%F in (%FILES%) do (
    echo.
    if exist "%%F" (
        echo === %%F ===
        "!SIGNTOOL!" verify /pa /v "%%F"
        if !errorlevel! neq 0 set "KO=1"
    ) else (
        echo [Verify] absent : %%F  ^(compile d'abord avec Build.bat^)
        set "KO=1"
    )
)

echo.
if "!KO!"=="0" (
    echo [Verify] OK : tout est signe et la chaine de certification est valide.
) else (
    echo [Verify] Au moins un fichier n'est PAS signe ^(ou la verification a echoue^).
    echo          Tant que la DLL n'est pas signee, Smart App Control bloquera le lancement.
)
echo.
pause
