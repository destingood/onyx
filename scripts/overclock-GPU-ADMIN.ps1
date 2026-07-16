# Overclock GPU (RTX 4080 SUPER) - A EXECUTER EN ADMINISTRATEUR
#   powershell -ExecutionPolicy Bypass -File .\scripts\overclock-GPU-ADMIN.ps1
#
# Utilise nvidia-smi (outil OFFICIEL NVIDIA livre avec le driver).
# Monte le power limit de 320 W (defaut) a 400 W (max autorise par ta
# Gigabyte 4080 Super = 125%). Effet : le GPU garde ses clocks boost dans
# les scenes lourdes au lieu de throttle. Chaleur en hausse dans ces scenes.

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { Write-Host "ERREUR : lance en administrateur." -ForegroundColor Red; exit 1 }

# 1. Power limit 400 W maintenant
nvidia-smi -pl 400
Write-Host "[OK] Power limit -> 400 W"

# 2. Persistance : le power limit revient a 320 W a chaque reboot.
#    Cette tache le re-applique a chaque ouverture de session.
$smi = "$env:SystemRoot\System32\nvidia-smi.exe"
if (-not (Test-Path $smi)) { $smi = (Get-Command nvidia-smi).Source }
$action  = New-ScheduledTaskAction -Execute $smi -Argument '-pl 400'
$trigger = New-ScheduledTaskTrigger -AtLogOn
$principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
Register-ScheduledTask -TaskName 'GPU-PowerLimit-400W' -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
Write-Host "[OK] Tache 'GPU-PowerLimit-400W' creee (re-applique a chaque logon)"

Write-Host ""
nvidia-smi --query-gpu=power.limit,power.max_limit --format=csv
Write-Host "Termine." -ForegroundColor Green

# --- Pour annuler ---
# nvidia-smi -pl 320
# Unregister-ScheduledTask -TaskName 'GPU-PowerLimit-400W' -Confirm:$false
