@echo off
REM ============================================================
REM  Mesure de latence DPC/ISR (remplace LatencyMon)
REM  Double-clique ce fichier -> accepte l'UAC -> joue -> reviens
REM ============================================================

net session >nul 2>&1
if %errorlevel% neq 0 (
  echo Elevation administrateur...
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)

set "TOOLS=C:\Users\User\Desktop\bt\tools"
set "XPERF=C:\Program Files (x86)\Windows Kits\10\Windows Performance Toolkit\xperf.exe"

if not exist "%XPERF%" (
  echo ERREUR : xperf introuvable. Reinstalle le Windows Performance Toolkit :
  echo winget install Microsoft.WindowsADK --override "/features OptionId.WindowsPerformanceToolkit /quiet /norestart"
  pause
  exit /b 1
)

del "%TOOLS%\trace.etl" 2>nul
wpr -cancel >nul 2>&1

echo Demarrage de la capture noyau...
wpr -start "%TOOLS%\dpc-trace.wprp!DPC" -filemode
if errorlevel 1 (
  echo Echec du profil personnalise, tentative avec le profil CPU integre...
  wpr -start CPU -filemode
  if errorlevel 1 ( echo ERREUR : impossible de demarrer la capture. & pause & exit /b 1 )
)

echo.
echo ============================================================
echo   CAPTURE EN COURS
echo   Va jouer 3-5 minutes, puis reviens et appuie sur une touche
echo ============================================================
pause >nul

echo Arret de la capture...
wpr -stop "%TOOLS%\trace.etl"

echo Analyse xperf en cours...
"%XPERF%" -i "%TOOLS%\trace.etl" -a dpcisr > "%TOOLS%\dpcisr-analysis.txt"

echo.
echo ============================================================
echo   TERMINE - rapport : %TOOLS%\dpcisr-analysis.txt
echo   Repere-toi : la section "Total = N for module X.sys"
echo   liste chaque driver. Les buckets ^> 1000 usecs = probleme.
echo   Ou demande simplement a Claude de l'interpreter.
echo ============================================================
start notepad "%TOOLS%\dpcisr-analysis.txt"
pause
