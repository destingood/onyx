# Optimisation Windows (gaming) - A EXECUTER EN ADMINISTRATEUR
#   powershell -ExecutionPolicy Bypass -File .\optimise-windows-ADMIN.ps1

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { Write-Host "ERREUR : lance en administrateur." -ForegroundColor Red; exit 1 }

# 1. Service de telemetrie DiagTrack (la telemetrie est deja a 0 dans ta config,
#    ce service tourne pour rien)
Stop-Service DiagTrack -Force -ErrorAction SilentlyContinue
Set-Service DiagTrack -StartupType Disabled
Write-Host "[OK] DiagTrack (telemetrie) stoppe et desactive"

# 2. SysMain (ex-Superfetch) : avec 64 Go de RAM et des jeux sur NVMe, il cause
#    plus de stutter qu'il n'aide
Stop-Service SysMain -Force -ErrorAction SilentlyContinue
Set-Service SysMain -StartupType Disabled
Write-Host "[OK] SysMain desactive"

# 3. Delivery Optimization : telechargement Windows Update en LAN seulement
#    (ton PC ne servira plus de seed P2P pour les updates d'inconnus)
$do = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization'
if (-not (Test-Path $do)) { New-Item $do -Force | Out-Null }
Set-ItemProperty $do -Name DODownloadMode -Value 1 -Type DWord
Write-Host "[OK] Delivery Optimization -> LAN uniquement"

# 4. Taches planifiees CEIP (programme d'amelioration de l'experience)
foreach ($task in @(
    '\Microsoft\Windows\Customer Experience Improvement Program\Consolidator',
    '\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip',
    '\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser',
    '\Microsoft\Windows\Application Experience\ProgramDataUpdater'
)) {
    schtasks /Change /TN $task /Disable 2>$null | Out-Null
}
Write-Host "[OK] Taches CEIP / Compatibility Appraiser desactivees"

# 5. Nettoyage du magasin de composants Windows (WinSxS) - LONG (5-15 min),
#    libere plusieurs Go sur C:
Write-Host "Nettoyage WinSxS en cours (5-15 min, ne ferme pas la fenetre)..."
Dism.exe /Online /Cleanup-Image /StartComponentCleanup
Write-Host "[OK] WinSxS nettoye"

Write-Host ""
Write-Host "Termine." -ForegroundColor Green

# --- Pour annuler ---
# Set-Service DiagTrack -StartupType Automatic; Start-Service DiagTrack
# Set-Service SysMain -StartupType Automatic; Start-Service SysMain
# Remove-ItemProperty 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization' -Name DODownloadMode
# schtasks /Change /TN "\Microsoft\Windows\Customer Experience Improvement Program\Consolidator" /Enable
