# Reduction maximale de la latence - A EXECUTER EN ADMINISTRATEUR
#   powershell -ExecutionPolicy Bypass -File .\scripts\latence-min-ADMIN.ps1            (etat + plan)
#   powershell -ExecutionPolicy Bypass -File .\scripts\latence-min-ADMIN.ps1 -Etape 1   (applique UNE etape)
#   powershell -ExecutionPolicy Bypass -File .\scripts\latence-min-ADMIN.ps1 -Retablir  (marche arriere totale)
#
# POURQUOI UNE ETAPE A LA FOIS
#   Tout appliquer d'un coup rend le resultat ininterpretable : si la latence baisse, on ne sait
#   pas laquelle a paye, et on garde les couts (RGB, courbes de ventilation) des reglages inutiles.
#   Chaque etape se mesure : releve LatencyMon de MEME DUREE avant et apres, meme etat de machine.
#
# CE QUE CE SCRIPT NE FAIT PAS
#   L'hyperviseur. Le couper globalement tue WSL2 et Docker. Utilise plutot
#   scripts\boot-gaming-ADMIN.ps1 : il ajoute un choix au demarrage, les deux modes restent dispo.
#
# TOUT EST SAUVEGARDE avant modification dans %USERPROFILE%\Desktop\ONYX-latence-sauvegarde.
# -Retablir remet l'etat d'origine a partir de cette sauvegarde.

param(
    [ValidateRange(0, 3)] [int] $Etape = 0,
    [switch] $Retablir
)

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { Write-Host "ERREUR : lance en administrateur." -ForegroundColor Red; exit 1 }

$Sauvegarde = Join-Path $env:USERPROFILE 'Desktop\ONYX-latence-sauvegarde'
$FicServices = Join-Path $Sauvegarde 'services-avant.csv'
$FicDwm      = Join-Path $Sauvegarde 'dwm-avant.reg'
$FicGraph    = Join-Path $Sauvegarde 'graphicsdrivers-avant.reg'

$CleDwm   = 'HKLM:\SOFTWARE\Microsoft\Windows\Dwm'
$CleGraph = 'HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers'

# Liste EXPLICITE, jamais un motif large. « MSI » attraperait MSiSCSI (initiateur iSCSI de
# Windows) et casserait le stockage reseau : un service ne se coupe que si on l'a nomme.
$ServicesCapteurs = @(
    'CorsairCpuIdService', 'iCUEUpdateService', 'CorsairGamingAudioConfigService',
    'LGHUBUpdaterService', 'logi_lamparray_service', 'LogiRegistryService',
    'RzActionSvc', 'Razer Game Scanner Service',
    'LightingService', 'AsusCertService',
    'Mystic_Light_Service',
    'HWiNFO', 'RTSSSmallServer', 'OpenRGBService', 'SignalRgbLedService'
)

function Prepare {
    if (-not (Test-Path $Sauvegarde)) { New-Item -ItemType Directory -Path $Sauvegarde -Force | Out-Null }
}

function Etat {
    Write-Host ""
    Write-Host "=== ETAT ACTUEL ===" -ForegroundColor Cyan

    # powercfg sort CINQ valeurs hexa pour un reglage, toujours dans cet ordre :
    #   min possible, max possible, increment, SECTEUR, batterie
    # Le secteur est donc l'AVANT-DERNIERE. On compte les positions, jamais les libelles : ils
    # sont traduits, et deux pieges s'y cachent. En francais la ligne batterie s'appelle
    # « courant continu » - filtrer sur « courant » attrapait aussi le secteur, et la batterie
    # ecrasait la bonne valeur. Et prendre la premiere valeur ne marche pas non plus : c'est le
    # minimum possible, soit 0. Les deux erreurs affichaient un chiffre faux sans rien signaler.
    $vals = @()
    foreach ($l in (powercfg /q SCHEME_CURRENT SUB_PROCESSOR 893dee8e-2bef-41e0-89c6-b55d0929964c)) {
        if ($l -match '0x([0-9a-fA-F]{8})') { $vals += [Convert]::ToInt32($Matches[1], 16) }
    }
    if ($vals.Count -ge 2) { $procMin = $vals[$vals.Count - 2] } else { $procMin = 'inconnu' }
    Write-Host ("  Etat processeur MIN      : {0} %   (secteur ; 100 = pas de descente au repos)" -f $procMin)

    $mpo = (Get-ItemProperty $CleDwm -Name OverlayTestMode -ErrorAction SilentlyContinue).OverlayTestMode
    if ($null -eq $mpo) { Write-Host "  MPO                      : actif (defaut)" }
    else { Write-Host ("  MPO                      : desactive (OverlayTestMode = {0})" -f $mpo) }

    $hags = (Get-ItemProperty $CleGraph -Name HwSchMode -ErrorAction SilentlyContinue).HwSchMode
    if ($hags -eq 2) { Write-Host "  HAGS                     : active" }
    elseif ($hags -eq 1) { Write-Host "  HAGS                     : desactive" }
    else { Write-Host "  HAGS                     : non defini" }

    Write-Host ""
    Write-Host "  Polleurs de capteurs en marche :"
    $trouves = 0
    foreach ($n in $ServicesCapteurs) {
        $s = Get-Service -Name $n -ErrorAction SilentlyContinue
        if ($s -and $s.Status -eq 'Running') { Write-Host ("    - {0}  ({1})" -f $s.Name, $s.DisplayName) -ForegroundColor Yellow; $trouves++ }
    }
    if ($trouves -eq 0) { Write-Host "    (aucun)" -ForegroundColor Green }

    # Signale sans y toucher ce qui ressemble a un polleur mais n'est pas dans la liste nommee.
    $suspects = Get-Service -ErrorAction SilentlyContinue | Where-Object {
        $_.Status -eq 'Running' -and $_.Name -notin $ServicesCapteurs -and
        ($_.DisplayName -match 'Corsair|Logitech|Razer|ASUS|Aura|Armoury|Mystic|NZXT|OpenRGB|SignalRGB|HWiNFO|AIDA')
    }
    if ($suspects) {
        Write-Host ""
        Write-Host "  Non traites par ce script (arrete-les a la main si besoin) :" -ForegroundColor DarkYellow
        foreach ($s in $suspects) { Write-Host ("    - {0}  ({1})" -f $s.Name, $s.DisplayName) }
    }
}

function Plan {
    Write-Host ""
    Write-Host "=== PLAN ===" -ForegroundColor Cyan
    Write-Host "  -Etape 1  Arreter et desactiver les polleurs de capteurs (iCUE, G HUB...)"
    Write-Host "            Cause n1 d'une latence qui revient PAR CYCLES : lire un capteur passe"
    Write-Host "            par le SMBus, ce qui declenche un SMI qui gele tous les coeurs."
    Write-Host "            COUT : plus de RGB ni de courbes de ventilation logicielles."
    Write-Host "            VERIFIE TES TEMPERATURES : les ventilateurs repassent sur la courbe du BIOS."
    Write-Host ""
    Write-Host "  -Etape 2  Reactiver le MPO (annule mpo_off)"
    Write-Host "            Desactive, le compositeur fait tout passer par le GPU. Reduit le VOLUME"
    Write-Host "            de DPC graphiques, pas leur cout unitaire."
    Write-Host "            COUT : scintillements possibles sur certains ecrans. Redemarrage."
    Write-Host ""
    Write-Host "  -Etape 3  Desactiver HAGS"
    Write-Host "            Effet variable selon les machines - a mesurer, pas a croire."
    Write-Host "            COUT : aucun. Redemarrage."
    Write-Host ""
    Write-Host "  Hyperviseur : PAS ici. Utilise scripts\boot-gaming-ADMIN.ps1 (choix au demarrage)."
    Write-Host ""
    Write-Host "  Entre chaque etape : releve LatencyMon de MEME DUREE, meme etat de machine." -ForegroundColor Cyan
    Write-Host "  Sans ca, ONYX refusera de conclure - et il aura raison."
}

function Etape1 {
    Prepare
    if (-not (Test-Path $FicServices)) {
        $avant = @()
        foreach ($n in $ServicesCapteurs) {
            $s = Get-Service -Name $n -ErrorAction SilentlyContinue
            if ($s) { $avant += [PSCustomObject]@{ Nom = $s.Name; Demarrage = $s.StartType; Etat = $s.Status } }
        }
        if ($avant.Count -eq 0) { Write-Host "Aucun polleur connu installe. Rien a faire." -ForegroundColor Green; return }
        $avant | Export-Csv -Path $FicServices -NoTypeInformation -Encoding UTF8
        Write-Host ("[OK] Etat des services sauvegarde : {0}" -f $FicServices) -ForegroundColor Green
    }
    else { Write-Host "[i] Sauvegarde deja presente, conservee (etat d'origine)." -ForegroundColor DarkGray }

    foreach ($n in $ServicesCapteurs) {
        $s = Get-Service -Name $n -ErrorAction SilentlyContinue
        if (-not $s) { continue }
        if ($s.Status -eq 'Running') {
            try { Stop-Service -Name $n -Force -ErrorAction Stop; Write-Host ("[OK] arrete   : {0}" -f $n) -ForegroundColor Green }
            catch { Write-Host ("[!] arret impossible : {0} - {1}" -f $n, $_.Exception.Message) -ForegroundColor Yellow }
        }
        try { Set-Service -Name $n -StartupType Disabled -ErrorAction Stop; Write-Host ("[OK] desactive: {0}" -f $n) -ForegroundColor Green }
        catch { Write-Host ("[!] desactivation impossible : {0}" -f $n) -ForegroundColor Yellow }
    }
    Write-Host ""
    Write-Host "Ferme aussi les fenetres/icones iCUE et G HUB pres de l'horloge, puis ONYX." -ForegroundColor Cyan
    Write-Host "Releve LatencyMon de 10 min, bureau au repos. VERIFIE TES TEMPERATURES." -ForegroundColor Cyan
}

function Etape2 {
    Prepare
    if (-not (Test-Path $FicDwm)) {
        reg export 'HKLM\SOFTWARE\Microsoft\Windows\Dwm' $FicDwm /y | Out-Null
        Write-Host ("[OK] Cle DWM sauvegardee : {0}" -f $FicDwm) -ForegroundColor Green
    }
    $mpo = (Get-ItemProperty $CleDwm -Name OverlayTestMode -ErrorAction SilentlyContinue).OverlayTestMode
    if ($null -eq $mpo) { Write-Host "Le MPO est deja actif. Rien a faire." -ForegroundColor Green; return }
    Remove-ItemProperty -Path $CleDwm -Name OverlayTestMode -ErrorAction SilentlyContinue
    Write-Host "[OK] MPO reactive (OverlayTestMode retire)." -ForegroundColor Green
    Write-Host "Redemarre, puis releve de MEME DUREE. Si des scintillements apparaissent : -Retablir." -ForegroundColor Cyan
}

function Etape3 {
    Prepare
    if (-not (Test-Path $FicGraph)) {
        reg export 'HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers' $FicGraph /y | Out-Null
        Write-Host ("[OK] Cle GraphicsDrivers sauvegardee : {0}" -f $FicGraph) -ForegroundColor Green
    }
    Set-ItemProperty -Path $CleGraph -Name HwSchMode -Value 1 -Type DWord
    Write-Host "[OK] HAGS desactive (HwSchMode = 1)." -ForegroundColor Green
    Write-Host "Redemarre, puis releve de MEME DUREE." -ForegroundColor Cyan
}

function Retablir {
    if (-not (Test-Path $Sauvegarde)) { Write-Host "Aucune sauvegarde trouvee. Rien a retablir." -ForegroundColor Yellow; return }

    if (Test-Path $FicServices) {
        foreach ($r in (Import-Csv -Path $FicServices)) {
            try {
                Set-Service -Name $r.Nom -StartupType $r.Demarrage -ErrorAction Stop
                if ($r.Etat -eq 'Running') { Start-Service -Name $r.Nom -ErrorAction SilentlyContinue }
                Write-Host ("[OK] retabli : {0} ({1})" -f $r.Nom, $r.Demarrage) -ForegroundColor Green
            }
            catch { Write-Host ("[!] retablissement impossible : {0}" -f $r.Nom) -ForegroundColor Yellow }
        }
    }
    if (Test-Path $FicDwm)   { reg import $FicDwm   | Out-Null; Write-Host "[OK] cle DWM restauree." -ForegroundColor Green }
    if (Test-Path $FicGraph) { reg import $FicGraph | Out-Null; Write-Host "[OK] cle GraphicsDrivers restauree." -ForegroundColor Green }
    Write-Host ""
    Write-Host "Redemarre pour que tout reprenne son etat d'origine." -ForegroundColor Cyan
}

# ---------------------------------------------------------------------------

if ($Retablir) { Retablir; exit 0 }

switch ($Etape) {
    1 { Etape1 }
    2 { Etape2 }
    3 { Etape3 }
    default { Etat; Plan }
}
