@echo off
setlocal
title BT Diagnostic - pourquoi ce PC lag en jeu
cd /d "%~dp0"

echo.
echo   ============================================================
echo    BT DIAGNOSTIC
echo   ============================================================
echo.
echo    Ce script ANALYSE le PC et ecrit un rapport sur le Bureau.
echo    Il ne modifie RIEN.
echo.
echo    Ferme tes jeux, mais laisse tourner ce qui tourne
echo    d'habitude (Discord, navigateur, etc.) : c'est justement
echo    ce qu'on veut mesurer.
echo.
pause

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\diagnostic-lag-ADMIN.ps1" %*

endlocal
