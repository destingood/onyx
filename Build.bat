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
rem On PRESERVE les donnees utilisateur : tout ce qui commence par "bt-" (bt-gamecache
rem = jaquettes + icones extraites, bt-*.txt = reglages). Un "rd /s /q dist" brutal les
rem effacait a CHAQUE compilation, obligeant a tout re-telecharger ensuite.
if exist dist (
    for /d %%D in (dist\*) do echo %%~nxD| findstr /b /i "bt-" >nul || rd /s /q "%%D"
    for %%F in (dist\*) do echo %%~nxF| findstr /b /i "bt-" >nul || del /q "%%F"
)

echo.
echo Compilation .NET 10 AUTONOME MONO-FICHIER (tout dans BTOptimizer.exe)...
rem  MONO-FICHIER (PublishSingleFile) : le code applicatif est bundle DANS l'exe, il n'y a
rem  donc PLUS de BTOptimizer.dll separee sur le disque. C'est LA cle : Smart App Control
rem  bloquait la DLL au chargement ("strategie de controle d'application", 0x800711C7).
rem  Sans DLL separee, l'exe autonome se lance meme NON signe (verifie sur cette machine).
dotnet publish BTOptimizer.csproj -c Release -o dist -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true --nologo
if %errorlevel% neq 0 (
    echo.
    echo ECHEC de la compilation.
    pause
    exit /b 1
)

rem --- Signature (sans effet tant qu'aucun certificat n'est configure) -----------
rem  Mono-fichier : tout est dans l'exe, on signe donc l'exe. Sans certificat, Sign.bat
rem  ne fait rien et l'exe passe SAC quand meme (pas de DLL separee a bloquer). Avec un
rem  certificat, la signature enleve en plus l'avertissement "editeur inconnu" chez les clients.
call "%~dp0Sign.bat" "dist\BTOptimizer.exe"

echo.
echo OK : dist\BTOptimizer.exe cree (mono-fichier autonome, passe Smart App Control).
echo Double-clique dist\BTOptimizer.exe : il marche sans installer .NET.
echo (Lancer-BTOptimizer.bat reste utile seulement pour lancer en admin.)
pause
