# Fiabilisation du dongle Bluetooth TP-Link UB500 - A EXECUTER EN ADMINISTRATEUR
#   powershell -ExecutionPolicy Bypass -File .\scripts\fix-bt-tplink-ADMIN.ps1
#
# Probleme constate : erreurs BTHUSB 5 (paquets HCI corrompus) -> Windows
# reinitialise la radio (evenement 18) -> la manette Xbox se deconnecte
# uniquement quand elle est utilisee (trafic BT continu).
# Le registre montre DeviceSelectiveSuspended=1 : Windows suspend le dongle
# malgre le plan d'alimentation. Ce script coupe toute gestion d'alimentation
# sur le dongle et les hubs USB qu'il traverse.

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { Write-Host "ERREUR : lance en administrateur." -ForegroundColor Red; exit 1 }

# 1. Decocher "Autoriser l'ordinateur a eteindre ce peripherique pour
#    economiser l'energie" sur la radio Bluetooth du dongle TP-Link.
#    (WMI MSPower_DeviceEnable est casse sur cette machine -> on ecrit
#    directement l'equivalent registre du pilote WDF :
#    IdleInWorkingState=0 + interdiction de la veille profonde D3cold)
$found = 0
Get-ChildItem 'HKLM:\SYSTEM\CurrentControlSet\Enum\USB\VID_0489&PID_E116&MI_00' -ErrorAction SilentlyContinue | ForEach-Object {
    $dp = Join-Path $_.PSPath 'Device Parameters'
    $wdf = Join-Path $dp 'WDF'
    if (Test-Path $wdf) {
        Set-ItemProperty $wdf -Name IdleInWorkingState -Value 0 -Type DWord
        Write-Host "[OK] IdleInWorkingState=0 (radio $($_.PSChildName)) - plus de mise en veille du dongle"
        $found++
    }
    $d3 = Join-Path $dp 'e5b3b5ac-9725-4f78-963f-03dfb1d828c7'
    if (Test-Path $d3) {
        Set-ItemProperty $d3 -Name D3ColdSupported -Value 0 -Type DWord
        Write-Host "[OK] D3ColdSupported=0 (radio $($_.PSChildName)) - veille profonde interdite"
    }
}
if ($found -eq 0) { Write-Host "[!!] Radio TP-Link introuvable dans le registre (dongle debranche ?)" -ForegroundColor Yellow }

# 2. EnhancedPowerManagementEnabled=0 sur l'instance USB du dongle
#    (fix Microsoft connu pour les coupures des peripheriques USB audio/BT)
Get-ChildItem 'HKLM:\SYSTEM\CurrentControlSet\Enum\USB\VID_0489&PID_E116' -ErrorAction SilentlyContinue | ForEach-Object {
    $dp = Join-Path $_.PSPath 'Device Parameters'
    if (Test-Path $dp) {
        Set-ItemProperty $dp -Name EnhancedPowerManagementEnabled -Value 0 -Type DWord
        Write-Host "[OK] EnhancedPowerManagementEnabled=0 sur $($_.PSChildName)"
    }
}

# 3. Forcer la suspension selective USB a "Desactivee" dans le plan actif
#    (deja le cas dans le plan Bitsum, on verrouille par securite)
powercfg /setacvalueindex SCHEME_CURRENT 2a737441-1930-4402-8d77-b2bebba308a3 48e6b7a6-50f5-4782-a5d4-53bb8f07e226 0 | Out-Null
powercfg /setdcvalueindex SCHEME_CURRENT 2a737441-1930-4402-8d77-b2bebba308a3 48e6b7a6-50f5-4782-a5d4-53bb8f07e226 0 | Out-Null
powercfg /setactive SCHEME_CURRENT | Out-Null
Write-Host "[OK] Suspension selective USB : Desactivee (AC + DC)"

Write-Host ""
Write-Host "Termine. Maintenant :" -ForegroundColor Green
Write-Host "  1. DEBRANCHE le dongle du hub USB et branche-le DIRECT sur un port"
Write-Host "     USB 2.0 a l'ARRIERE du PC (loin des ports/peripheriques USB 3.0)."
Write-Host "  2. Redemarre le PC."
Write-Host "  3. Verifie apres une session de jeu :"
Write-Host "     Get-WinEvent -FilterHashtable @{LogName='System'; ProviderName='BTHUSB'; Id=5} -MaxEvents 5"
Write-Host "     -> plus aucune erreur recente = probleme regle."

# --- Pour tout annuler ---
# Get-ChildItem 'HKLM:\SYSTEM\CurrentControlSet\Enum\USB\VID_0489&PID_E116&MI_00' | ForEach-Object { Set-ItemProperty (Join-Path $_.PSPath 'Device Parameters\WDF') -Name IdleInWorkingState -Value 1; Set-ItemProperty (Join-Path $_.PSPath 'Device Parameters\e5b3b5ac-9725-4f78-963f-03dfb1d828c7') -Name D3ColdSupported -Value 1 }
# Get-ChildItem 'HKLM:\SYSTEM\CurrentControlSet\Enum\USB\VID_0489&PID_E116' | ForEach-Object { Remove-ItemProperty (Join-Path $_.PSPath 'Device Parameters') -Name EnhancedPowerManagementEnabled -ErrorAction SilentlyContinue }
