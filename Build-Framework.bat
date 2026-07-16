@echo off
title Compilation DesTinGOOD PC Optimizer
setlocal
cd /d "%~dp0"

rem S'elever pour pouvoir fermer l'app en cours (qui tourne en administrateur).
whoami /groups | find "S-1-16-12288" >nul 2>&1
if %errorlevel% neq 0 (
    echo Demande des droits administrateur pour la compilation...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

rem Compilateur C# integre a Windows (.NET Framework 4.x) - rien a installer.
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo Compilateur C# introuvable ^(.NET Framework 4.x requis^).
    pause
    exit /b 1
)

rem Si l'application tourne encore, on la ferme proprement (sinon fichier verrouille).
tasklist /FI "IMAGENAME eq DTGOptimizer.exe" 2>nul | find /I "DTGOptimizer.exe" >nul
if %errorlevel% equ 0 (
    echo Fermeture de DesTinGOOD PC Optimizer en cours d'execution...
    taskkill /IM DTGOptimizer.exe >nul 2>&1
    timeout /t 2 /nobreak >nul
    tasklist /FI "IMAGENAME eq DTGOptimizer.exe" 2>nul | find /I "DTGOptimizer.exe" >nul
    if %errorlevel% equ 0 taskkill /F /IM DTGOptimizer.exe >nul 2>&1
    timeout /t 1 /nobreak >nul
)

echo Compilation de DTGOptimizer.exe ...
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 ^
  /win32manifest:src\app.manifest /win32icon:src\app.ico ^
  /r:System.dll /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Management.dll ^
  /out:DTGOptimizer.exe src\Model.cs src\Native.cs src\Sys.cs src\Tweaks.cs src\Engine.cs src\Bench.cs src\DpcIsr.cs src\HwMonitor.cs src\LatencyForm.cs src\CompareForm.cs src\MonitorForm.cs src\OverclockForm.cs src\Cli.cs src\MainForm.cs src\Program.cs

if %errorlevel% neq 0 (
    echo.
    echo ECHEC de la compilation.
    pause
    exit /b 1
)
echo.
echo OK : DTGOptimizer.exe cree. Double-cliquez dessus pour lancer l'application.
pause
