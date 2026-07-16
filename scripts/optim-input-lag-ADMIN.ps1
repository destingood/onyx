# Optimisations input lag - A EXECUTER EN ADMINISTRATEUR
# (clic droit > Executer avec PowerShell en admin, ou depuis un terminal admin :
#   powershell -ExecutionPolicy Bypass -File .\scripts\optim-input-lag-ADMIN.ps1)
# Un redemarrage est necessaire pour le HAGS et le timer global.

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "ERREUR : lance ce script en tant qu'administrateur." -ForegroundColor Red
    exit 1
}

# 1. HAGS (Hardware-Accelerated GPU Scheduling) - requis pour NVIDIA Reflex optimal / DLSS FG
Set-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers' -Name HwSchMode -Value 2 -Type DWord
Write-Host "[OK] HAGS active (HwSchMode=2) - effectif apres redemarrage"

# 2. Scheduler multimedia : plus de temps CPU pour le jeu au premier plan
$mm = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile'
Set-ItemProperty $mm -Name SystemResponsiveness -Value 10 -Type DWord
Write-Host "[OK] SystemResponsiveness 20 -> 10"

# 3. Categorie 'Games' du scheduler en priorite haute
$games = "$mm\Tasks\Games"
Set-ItemProperty $games -Name 'Scheduling Category' -Value 'High' -Type String
Set-ItemProperty $games -Name 'Priority' -Value 6 -Type DWord
Set-ItemProperty $games -Name 'GPU Priority' -Value 8 -Type DWord
Write-Host "[OK] Tache 'Games' : Scheduling Category=High, Priority=6"

# 4. Timer global 0,5 ms (Windows 11 : par defaut la resolution du timer est
#    par-processus ; cette cle restaure le comportement global quand un process
#    la demande - source : valleyofdoom/TimerResolution + PC-Tuning)
Set-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\kernel' -Name GlobalTimerResolutionRequests -Value 1 -Type DWord
Write-Host "[OK] GlobalTimerResolutionRequests=1 - effectif apres redemarrage"

# 5. Lancer SetTimerResolution (0,5 ms) a chaque ouverture de session
#    Outil : github.com/valleyofdoom/TimerResolution (SetTimerResolution.exe, deja
#    telecharge dans tools\). Cree une tache planifiee au logon, sans fenetre.
$exe = 'C:\Users\User\Desktop\bt\tools\SetTimerResolution.exe'
if (Test-Path $exe) {
    $action  = New-ScheduledTaskAction -Execute $exe -Argument '--resolution 5000 --no-console'
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero)
    Register-ScheduledTask -TaskName 'SetTimerResolution-0.5ms' -Action $action -Trigger $trigger -Settings $settings -Force | Out-Null
    Start-ScheduledTask -TaskName 'SetTimerResolution-0.5ms'
    Write-Host "[OK] Tache planifiee 'SetTimerResolution-0.5ms' creee et demarree"
} else {
    Write-Host "[!!] $exe introuvable - etape sautee" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Termine. REDEMARRE le PC pour activer HAGS + timer global." -ForegroundColor Green
Write-Host "Apres redemarrage, verifie le timer avec tools\MeasureSleep.exe (delta ~0.5ms attendu)."

# --- Pour tout annuler (valeurs d'origine) ---
# Set-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers' -Name HwSchMode -Value 1
# Set-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile' -Name SystemResponsiveness -Value 20
# Set-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games' -Name 'Scheduling Category' -Value 'Medium'
# Set-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games' -Name 'Priority' -Value 2
# Remove-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\kernel' -Name GlobalTimerResolutionRequests
# Unregister-ScheduledTask -TaskName 'SetTimerResolution-0.5ms' -Confirm:$false
