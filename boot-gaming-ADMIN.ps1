# Cree une entree de demarrage "GAMING" sans hyperviseur - A EXECUTER EN ADMIN
#   powershell -ExecutionPolicy Bypass -File .\boot-gaming-ADMIN.ps1
#
# Effet : au demarrage du PC, un menu propose 2 choix :
#   - "Windows 11"                  -> normal, Docker/WSL2 fonctionnent
#   - "Windows 11 - GAMING"         -> hyperviseur OFF = +2-5% perf/latence,
#                                      mais Docker Desktop/WSL2 indisponibles
#                                      (jusqu'au prochain boot en mode normal)
# Rien n'est supprime : les deux entrees pointent vers le meme Windows.

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { Write-Host "ERREUR : lance en administrateur." -ForegroundColor Red; exit 1 }

# Garde-fou : ne pas creer l'entree deux fois
$existing = bcdedit /enum | Select-String 'GAMING'
if ($existing) { Write-Host "L'entree GAMING existe deja. Rien a faire." -ForegroundColor Yellow; exit 0 }

# 1. Dupliquer l'entree de boot actuelle
$out = bcdedit /copy '{current}' /d "Windows 11 - GAMING (sans hyperviseur)"
if ($out -match '\{[0-9a-f-]+\}') {
    $guid = $Matches[0]
    Write-Host "[OK] Entree creee : $guid"
} else {
    Write-Host "ERREUR bcdedit : $out" -ForegroundColor Red; exit 1
}

# 2. Desactiver l'hyperviseur sur CETTE entree uniquement
bcdedit /set $guid hypervisorlaunchtype off
Write-Host "[OK] Hyperviseur OFF sur l'entree GAMING"

# 3. Menu de boot visible 5 secondes (entree par defaut = Windows normal)
bcdedit /timeout 5
Write-Host "[OK] Menu de boot : 5 s au demarrage"

Write-Host ""
Write-Host "Termine. Au prochain demarrage, choisis :" -ForegroundColor Green
Write-Host "  - 'Windows 11'          pour dev (Docker/WSL2 OK)"
Write-Host "  - 'Windows 11 - GAMING' pour jouer (perfs max)"

# --- Pour tout annuler ---
# bcdedit /enum   (repere le GUID de l'entree GAMING)
# bcdedit /delete {GUID}
# bcdedit /timeout 30
