@echo off
rem ============================================================================
rem  Sign.bat  <fichier-a-signer>
rem
rem  Signe un binaire avec signtool (SDK Windows) SI une configuration existe.
rem  Sinon : n'echoue PAS, previent juste que le binaire n'est pas signe.
rem  -> le build fonctionne avec ou sans certificat.
rem
rem  Configuration : cree "signing.local.cfg" a cote de ce fichier (gitignore) a
rem  partir de "signing.cfg.example". Il definit soit un .pfx + mot de passe,
rem  soit une empreinte (thumbprint) d'un certificat du magasin Windows.
rem ============================================================================
setlocal enabledelayedexpansion
set "TARGET=%~1"
if "%TARGET%"=="" ( echo [Sign] aucun fichier fourni. & exit /b 0 )
if not exist "%TARGET%" ( echo [Sign] introuvable : %TARGET% & exit /b 0 )

rem --- Charge la config locale si presente ---
if exist "%~dp0signing.local.cfg" call "%~dp0signing.local.cfg"
if not defined SIGN_TS set "SIGN_TS=http://timestamp.digicert.com"

if not defined SIGN_PFX if not defined SIGN_THUMB (
    echo [Sign] Non configure ^(signing.local.cfg absent^) : "%~nx1" NON signe.
    echo        SmartScreen affichera "editeur inconnu". Voir signing.cfg.example.
    exit /b 0
)

rem --- Localise signtool.exe (variante x64 du SDK Windows) ---
set "SIGNTOOL="
for /f "delims=" %%S in ('where /r "%ProgramFiles(x86)%\Windows Kits\10\bin" signtool.exe 2^>nul ^| findstr /i "\\x64\\"') do if not defined SIGNTOOL set "SIGNTOOL=%%S"
if not defined SIGNTOOL for /f "delims=" %%S in ('where /r "%ProgramFiles%\Windows Kits\10\bin" signtool.exe 2^>nul ^| findstr /i "\\x64\\"') do if not defined SIGNTOOL set "SIGNTOOL=%%S"
if not defined SIGNTOOL ( where signtool >nul 2>&1 && set "SIGNTOOL=signtool" )
if not defined SIGNTOOL (
    echo [Sign] signtool.exe introuvable. Installe le SDK Windows ^(composant "Signing Tools"^).
    echo        "%~nx1" NON signe.
    exit /b 0
)

echo [Sign] Signature de "%~nx1"...
if defined SIGN_THUMB (
    "!SIGNTOOL!" sign /fd SHA256 /tr "%SIGN_TS%" /td SHA256 /sha1 "%SIGN_THUMB%" "%TARGET%"
) else (
    "!SIGNTOOL!" sign /fd SHA256 /tr "%SIGN_TS%" /td SHA256 /f "%SIGN_PFX%" /p "%SIGN_PASS%" "%TARGET%"
)
if !errorlevel! neq 0 (
    echo [Sign] ECHEC de la signature ^(certificat/mot de passe/serveur d'horodatage ?^).
    exit /b 1
)
echo [Sign] OK : "%~nx1" signe et horodate.
exit /b 0
