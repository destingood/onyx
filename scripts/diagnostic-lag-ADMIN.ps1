<#
    BT Diagnostic - Pourquoi ce PC lag dans les jeux ?
    ==================================================================
    Script de RELEVE uniquement : il ne modifie RIEN sur la machine.
    Il collecte tout ce qui peut expliquer des lags / freezes / FPS bas
    et ecrit un rapport texte sur le Bureau.

    Utilisation :
      Clic droit sur Lancer-Diagnostic.bat > Executer en tant qu'admin
      (ou clic droit sur ce fichier > Executer avec PowerShell)

    Parametres :
      -Complet         : ajoute le dump DXDIAG et l'analyse longue des journaux
      -SansReseau      : saute le test de ping (PC hors ligne)
      -Sortie <chemin> : force l'emplacement du rapport

    Vie privee : aucun numero de serie, aucune adresse MAC, aucun nom de
    reseau Wi-Fi n'est collecte. Le rapport reste en local.
#>

param(
    [switch]$Complet,
    [switch]$SansReseau,
    [string]$Sortie
)

$ErrorActionPreference = 'Continue'
$ProgressPreference    = 'SilentlyContinue'
$script:Version        = '1.0'
$script:Lignes         = New-Object System.Collections.ArrayList
$script:Alertes        = New-Object System.Collections.ArrayList

# --- Elevation admin ---------------------------------------------------------
$identite  = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identite)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "Elevation des droits administrateur..." -ForegroundColor Yellow
    $argus = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"{0}"' -f $PSCommandPath))
    if ($Complet)    { $argus += '-Complet' }
    if ($SansReseau) { $argus += '-SansReseau' }
    if ($Sortie)     { $argus += @('-Sortie', ('"{0}"' -f $Sortie)) }
    try {
        Start-Process -FilePath 'powershell.exe' -ArgumentList $argus -Verb RunAs
    } catch {
        Write-Host "Impossible d'elever les droits." -ForegroundColor Red
        Write-Host "Fais un clic droit sur le fichier > Executer en tant qu'administrateur." -ForegroundColor Red
        Read-Host "Appuie sur Entree pour fermer"
    }
    return
}

# --- Helpers -----------------------------------------------------------------
function W {
    param([string]$t = '')
    $null = $script:Lignes.Add($t)
}

function Titre {
    param([string]$t)
    W ''
    W ('=' * 78)
    W ('  ' + $t)
    W ('=' * 78)
}

function Champ {
    param([string]$Nom, $Valeur)
    if ($null -eq $Valeur -or "$Valeur" -eq '') { $Valeur = '(inconnu)' }
    W ("  {0,-32}: {1}" -f $Nom, $Valeur)
}

function Add-Alerte {
    param(
        [ValidateSet('CRITIQUE', 'IMPORTANT', 'INFO')][string]$Niveau,
        [string]$Sujet,
        [string]$Detail = '',
        [string]$Fix = ''
    )
    $null = $script:Alertes.Add([pscustomobject]@{
        Niveau = $Niveau
        Sujet  = $Sujet
        Detail = $Detail
        Fix    = $Fix
    })
}

function Sonde {
    param([string]$Nom, [scriptblock]$Bloc)
    Write-Host ('  . ' + $Nom) -ForegroundColor DarkGray
    try { & $Bloc } catch { W ("  [erreur pendant '" + $Nom + "'] " + $_.Exception.Message) }
}

function Get-Reg {
    param([string]$Chemin, [string]$Nom)
    try { return (Get-ItemProperty -Path $Chemin -Name $Nom -ErrorAction Stop).$Nom } catch { return $null }
}

function Go {
    param([double]$Octets)
    return [math]::Round($Octets / 1GB, 1)
}

# --- Demarrage ---------------------------------------------------------------
Clear-Host
Write-Host ''
Write-Host '  BT DIAGNOSTIC - analyse des causes de lag en jeu' -ForegroundColor Cyan
Write-Host '  Aucune modification n est faite sur ce PC.' -ForegroundColor DarkGray
Write-Host ''

$debut = Get-Date
$horo  = $debut.ToString('yyyyMMdd-HHmmss')

if (-not $Sortie) {
    $bureau = [Environment]::GetFolderPath('Desktop')
    if (-not (Test-Path $bureau)) { $bureau = $env:USERPROFILE }
    $Sortie = Join-Path $bureau ("BT-Diagnostic-{0}-{1}.txt" -f $env:COMPUTERNAME, $horo)
}

W ('RAPPORT DE DIAGNOSTIC BT - v' + $script:Version)
W ('Genere le ' + $debut.ToString('dd/MM/yyyy HH:mm:ss'))
W ('Machine   : ' + $env:COMPUTERNAME)
W ''
W 'A FAIRE : envoyer CE FICHIER en entier pour analyse.'
W 'Il ne contient ni numero de serie, ni mot de passe, ni adresse reseau.'

# =============================================================================
Titre '1. SYSTEME'
# =============================================================================
Sonde 'Systeme d exploitation' {
    $os = Get-CimInstance Win32_OperatingSystem
    $cs = Get-CimInstance Win32_ComputerSystem
    $ub = Get-Reg 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' 'DisplayVersion'
    $ur = Get-Reg 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' 'UBR'

    Champ 'Windows'           ("{0} (build {1}.{2})" -f $os.Caption, $os.BuildNumber, $ur)
    Champ 'Version affichee'  $ub
    Champ 'Architecture'      $os.OSArchitecture
    Champ 'Installe le'       $os.InstallDate
    Champ 'Dernier demarrage' $os.LastBootUpTime

    $uptime = (Get-Date) - $os.LastBootUpTime
    Champ 'Allume depuis'     ("{0} j {1} h {2} min" -f $uptime.Days, $uptime.Hours, $uptime.Minutes)
    Champ 'Fabricant'         $cs.Manufacturer
    Champ 'Modele'            $cs.Model

    $script:EstPortable = $false
    try {
        foreach ($c in (Get-CimInstance Win32_SystemEnclosure).ChassisTypes) {
            if (@(8, 9, 10, 11, 12, 14, 18, 21, 30, 31, 32) -contains [int]$c) { $script:EstPortable = $true }
        }
    } catch { }
    if ($script:EstPortable) { Champ 'Type de machine' 'Portable' } else { Champ 'Type de machine' 'Fixe / tour' }

    if ($uptime.TotalDays -gt 7) {
        Add-Alerte 'IMPORTANT' 'PC jamais redemarre' `
            ("Allume depuis {0} jours sans redemarrage complet." -f [int]$uptime.TotalDays) `
            'Redemarrer vraiment (pas fermer le capot / veille). Fuites memoire et pilotes fatigues = micro-freezes.'
    }
    try {
        $ageInstall = ((Get-Date) - $os.InstallDate).TotalDays
        if ($ageInstall -gt 1095) {
            Add-Alerte 'INFO' 'Installation Windows tres ancienne' `
                ("Windows installe depuis environ {0} ans." -f [math]::Round($ageInstall / 365, 1)) `
                'Une reinstallation propre reste la solution la plus efficace si tout le reste est sain.'
        }
    } catch { }
    if ([int]$os.BuildNumber -lt 19041) {
        Add-Alerte 'IMPORTANT' 'Version de Windows obsolete' ('Build ' + $os.BuildNumber) `
            'Mettre a jour Windows : les pilotes de jeu recents ne sont plus optimises pour ces builds.'
    }
}

Sonde 'Carte mere / BIOS' {
    $bios = Get-CimInstance Win32_BIOS
    $bb   = Get-CimInstance Win32_BaseBoard
    Champ 'Carte mere'   ("{0} {1}" -f $bb.Manufacturer, $bb.Product)
    Champ 'Version BIOS' $bios.SMBIOSBIOSVersion
    Champ 'Date BIOS'    $bios.ReleaseDate
    try {
        if (((Get-Date) - $bios.ReleaseDate).TotalDays -gt 1460) {
            Add-Alerte 'INFO' 'BIOS tres ancien' ('BIOS date de ' + $bios.ReleaseDate.ToString('MM/yyyy')) `
                'Une mise a jour BIOS corrige souvent les soucis de RAM/XMP et de gestion CPU. A faire avec prudence.'
        }
    } catch { }
    try { Champ 'Secure Boot' (Confirm-SecureBootUEFI -ErrorAction Stop) }
    catch { Champ 'Secure Boot' '(non lisible / mode BIOS legacy)' }
}

# =============================================================================
Titre '2. PROCESSEUR'
# =============================================================================
Sonde 'CPU' {
    foreach ($cpu in (Get-CimInstance Win32_Processor)) {
        Champ 'Processeur'         $cpu.Name.Trim()
        Champ 'Coeurs / threads'   ("{0} / {1}" -f $cpu.NumberOfCores, $cpu.NumberOfLogicalProcessors)
        Champ 'Frequence nominale' ("{0} MHz" -f $cpu.MaxClockSpeed)
        Champ 'Frequence actuelle' ("{0} MHz" -f $cpu.CurrentClockSpeed)
        Champ 'Charge instantanee' ("{0} %" -f $cpu.LoadPercentage)
        Champ 'Socket'             $cpu.SocketDesignation

        if ($cpu.LoadPercentage -gt 35) {
            Add-Alerte 'IMPORTANT' 'CPU deja charge au repos' `
                ("Charge de {0} % sans jeu lance." -f $cpu.LoadPercentage) `
                'Voir la section PROCESSUS : quelque chose mange le CPU en permanence (maj, antivirus, indexation, mineur).'
        }
    }

    # Pourcentage de performance reel : detecte le bridage, independant de la langue de Windows
    try {
        $perf = Get-CimInstance Win32_PerfFormattedData_Counters_ProcessorInformation -ErrorAction Stop |
                Where-Object { $_.Name -eq '_Total' }
        if ($perf) {
            Champ 'Performance CPU reelle' ("{0} % de la frequence nominale" -f $perf.PercentProcessorPerformance)
            if ([int]$perf.PercentProcessorPerformance -lt 60) {
                Add-Alerte 'CRITIQUE' 'CPU bride (throttling)' `
                    ("Le CPU tourne a {0} % de sa frequence nominale, au repos." -f $perf.PercentProcessorPerformance) `
                    'Causes classiques : mode Economie d energie, PC sur batterie, surchauffe (pate thermique / ventilo bouche), limite de puissance BIOS.'
            }
        }
    } catch { }
}

# =============================================================================
Titre '3. MEMOIRE RAM'
# =============================================================================
Sonde 'RAM' {
    $os      = Get-CimInstance Win32_OperatingSystem
    $totalGo = [math]::Round($os.TotalVisibleMemorySize / 1MB, 1)
    $libreGo = [math]::Round($os.FreePhysicalMemory / 1MB, 1)
    $utilPct = [math]::Round((1 - ($os.FreePhysicalMemory / $os.TotalVisibleMemorySize)) * 100, 0)

    Champ 'RAM totale'  ("{0} Go" -f $totalGo)
    Champ 'RAM libre'   ("{0} Go" -f $libreGo)
    Champ 'Utilisation' ("{0} % (sans jeu lance)" -f $utilPct)

    $barrettes = @(Get-CimInstance Win32_PhysicalMemory)
    Champ 'Nombre de barrettes' $barrettes.Count
    W ''
    foreach ($b in $barrettes) {
        W ("    Slot {0,-14} {1,-4} Go   max {2} MHz   configure {3} MHz   {4}" -f `
            $b.DeviceLocator, [math]::Round($b.Capacity / 1GB, 0), $b.Speed, $b.ConfiguredClockSpeed, $b.PartNumber)
    }

    if ($totalGo -lt 12) {
        Add-Alerte 'CRITIQUE' 'Pas assez de RAM pour les jeux recents' ("{0} Go installes." -f $totalGo) `
            '16 Go est le minimum confortable. Avec 8 Go, Windows swappe sur le disque : freezes reguliers garantis.'
    } elseif ($totalGo -lt 16) {
        Add-Alerte 'IMPORTANT' 'RAM juste' ("{0} Go installes." -f $totalGo) `
            'Passer a 16 Go (2 barrettes identiques) donne un gain net sur les 1% low.'
    }

    if ($barrettes.Count -eq 1 -and $totalGo -ge 8) {
        Add-Alerte 'CRITIQUE' 'RAM en simple canal (single channel)' 'Une seule barrette installee.' `
            'Ajouter une deuxieme barrette identique : jusqu a +20/30 % de FPS, encore plus avec un GPU integre.'
    }

    $xmpOff = @($barrettes | Where-Object { $_.ConfiguredClockSpeed -and $_.Speed -and ($_.ConfiguredClockSpeed -lt ($_.Speed * 0.9)) })
    if ($xmpOff.Count -gt 0) {
        Add-Alerte 'IMPORTANT' 'RAM qui ne tourne pas a sa vitesse' `
            ("Configuree a {0} MHz alors que les barrettes montent a {1} MHz." -f $xmpOff[0].ConfiguredClockSpeed, $xmpOff[0].Speed) `
            'Activer le profil XMP / EXPO / DOCP dans le BIOS. Gain gratuit de 5 a 15 % de FPS.'
    }

    if ($utilPct -gt 70) {
        Add-Alerte 'IMPORTANT' 'RAM saturee avant meme de jouer' ("{0} % de la RAM utilisee au repos." -f $utilPct) `
            'Voir les sections PROCESSUS et DEMARRAGE : trop de choses tournent en fond.'
    }

    try {
        Champ 'Fichier d echange auto' (Get-CimInstance Win32_ComputerSystem).AutomaticManagedPagefile
        $pf = @(Get-CimInstance Win32_PageFileUsage)
        if ($pf.Count -eq 0) {
            Champ 'Fichier d echange' 'AUCUN'
            Add-Alerte 'CRITIQUE' 'Fichier d echange desactive' 'Aucun pagefile actif.' `
                'Le remettre en "gere automatiquement". Sans lui : crashs et freezes des que la RAM sature.'
        } else {
            foreach ($p in $pf) {
                Champ 'Fichier d echange' ("{0} - {1} Mo alloues, pic {2} Mo" -f $p.Name, $p.AllocatedBaseSize, $p.PeakUsage)
            }
        }
    } catch { }
}

# =============================================================================
Titre '4. CARTE GRAPHIQUE ET ECRAN'
# =============================================================================
Sonde 'GPU' {
    $gpus       = @(Get-CimInstance Win32_VideoController)
    $script:Gpu = $gpus
    $iGpuActif  = $false
    $aUnDGpu    = $false

    foreach ($g in $gpus) {
        W ''
        Champ 'Carte graphique'  $g.Name
        Champ '  Pilote version' $g.DriverVersion
        Champ '  Pilote date'    $g.DriverDate
        Champ '  Etat'           $g.Status
        Champ '  Code erreur'    $g.ConfigManagerErrorCode

        if ($g.CurrentHorizontalResolution) {
            Champ '  ECRAN BRANCHE ICI' 'oui (cette carte affiche l image)'
            Champ '  Resolution'  ("{0} x {1}" -f $g.CurrentHorizontalResolution, $g.CurrentVerticalResolution)
            Champ '  Rafraichissement actuel' ("{0} Hz" -f $g.CurrentRefreshRate)
            Champ '  Rafraichissement max'    ("{0} Hz" -f $g.MaxRefreshRate)

            if ($g.MaxRefreshRate -gt $g.CurrentRefreshRate -and $g.CurrentRefreshRate -gt 0) {
                Add-Alerte 'IMPORTANT' 'Ecran pas regle a son taux max' `
                    ("Regle a {0} Hz alors qu il supporte {1} Hz." -f $g.CurrentRefreshRate, $g.MaxRefreshRate) `
                    'Parametres > Systeme > Affichage > Parametres avances > choisir la frequence la plus haute. Sensation de fluidite immediate.'
            }
            if ($g.Name -match 'Intel|UHD|Iris|Vega|Radeon\(TM\) Graphics|AMD Radeon Graphics') { $iGpuActif = $true }
        } else {
            Champ '  ECRAN BRANCHE ICI' 'non (carte inactive)'
        }

        if ($g.Name -match 'NVIDIA|GeForce|RTX|GTX|Radeon RX|Arc A') { $aUnDGpu = $true }

        try {
            $ageP = ((Get-Date) - $g.DriverDate).TotalDays
            if ($ageP -gt 400 -and $g.Name -match 'NVIDIA|GeForce|Radeon|Arc') {
                Add-Alerte 'IMPORTANT' 'Pilote graphique perime' `
                    ("Pilote de {0} ({1} jours)." -f $g.DriverDate.ToString('MM/yyyy'), [int]$ageP) `
                    'Installer le dernier pilote depuis nvidia.com / amd.com (pas Windows Update). Faire une installation propre.'
            }
        } catch { }

        if ($g.ConfigManagerErrorCode -ne 0) {
            Add-Alerte 'CRITIQUE' 'Carte graphique en erreur dans Windows' `
                ("{0} - code erreur {1}" -f $g.Name, $g.ConfigManagerErrorCode) `
                'Desinstaller le pilote avec DDU en mode sans echec, puis reinstaller le pilote officiel.'
        }
    }

    if ($aUnDGpu -and $iGpuActif -and -not $script:EstPortable) {
        Add-Alerte 'CRITIQUE' 'Ecran branche sur la carte mere, pas sur la carte graphique' `
            'Une carte graphique dediee est presente mais l image sort du chipset integre.' `
            'Debrancher le cable video de la carte mere et le brancher sur la carte graphique (en bas de la tour).'
    }

    # VRAM reelle depuis le registre (Win32_VideoController ment au dela de 4 Go)
    try {
        $cle = 'HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}'
        foreach ($sub in (Get-ChildItem $cle -ErrorAction Stop | Where-Object { $_.PSChildName -match '^\d{4}$' })) {
            $mem = Get-Reg $sub.PSPath 'HardwareInformation.qwMemorySize'
            $nom = Get-Reg $sub.PSPath 'DriverDesc'
            if ($mem) { Champ 'VRAM detectee' ("{0} : {1} Go" -f $nom, (Go $mem)) }
        }
    } catch { }
}

Sonde 'nvidia-smi (si carte NVIDIA)' {
    $smi = Get-Command nvidia-smi.exe -ErrorAction SilentlyContinue
    if (-not $smi) {
        $chemins = @(
            "$env:ProgramFiles\NVIDIA Corporation\NVSMI\nvidia-smi.exe",
            "$env:SystemRoot\System32\nvidia-smi.exe"
        )
        foreach ($c in $chemins) { if (Test-Path $c) { $smi = $c; break } }
    } else { $smi = $smi.Source }

    if ($smi) {
        W ''
        W '  Etat NVIDIA (nom, pilote, temp C, clock actuel/max MHz, conso W, limite W, charge %, VRAM Mo) :'
        $q = 'name,driver_version,temperature.gpu,clocks.current.graphics,clocks.max.graphics,power.draw,power.limit,utilization.gpu,memory.total'
        $res = & $smi "--query-gpu=$q" '--format=csv,noheader' 2>&1
        foreach ($l in $res) { W ('    ' + $l) }

        $thr = & $smi '--query-gpu=clocks_throttle_reasons.active,clocks_throttle_reasons.hw_thermal_slowdown,clocks_throttle_reasons.sw_power_cap' '--format=csv,noheader' 2>&1
        W ('  Raisons de bridage GPU  : ' + ($thr -join ' | '))

        try {
            $t = [int]((& $smi '--query-gpu=temperature.gpu' '--format=csv,noheader' 2>&1) | Select-Object -First 1)
            if ($t -gt 80) {
                Add-Alerte 'CRITIQUE' 'GPU chaud au repos' ("{0} C sans jeu lance." -f $t) `
                    'Nettoyer les ventilateurs et le radiateur a l air sec, verifier le flux d air du boitier. Un GPU qui chauffe se bride tout seul.'
            }
        } catch { }
    } else {
        W '  (pas de carte NVIDIA detectee ou nvidia-smi absent)'
    }
}

# =============================================================================
Titre '5. STOCKAGE'
# =============================================================================
Sonde 'Disques physiques' {
    try {
        foreach ($d in (Get-PhysicalDisk -ErrorAction Stop | Sort-Object DeviceId)) {
            W ''
            Champ 'Disque'      $d.FriendlyName
            Champ '  Type'      $d.MediaType
            Champ '  Bus'       $d.BusType
            Champ '  Taille'    ("{0} Go" -f (Go $d.Size))
            Champ '  Sante'     $d.HealthStatus
            Champ '  Etat'      $d.OperationalStatus

            if ($d.HealthStatus -ne 'Healthy') {
                Add-Alerte 'CRITIQUE' 'Disque en mauvaise sante' `
                    ("{0} : etat {1}" -f $d.FriendlyName, $d.HealthStatus) `
                    'Sauvegarder immediatement. Un disque mourant provoque des freezes de plusieurs secondes.'
            }

            try {
                $rc = $d | Get-StorageReliabilityCounter -ErrorAction Stop
                if ($rc.Temperature)        { Champ '  Temperature' ("{0} C" -f $rc.Temperature) }
                if ($rc.Wear -ne $null)     { Champ '  Usure SSD'   ("{0} %" -f $rc.Wear) }
                if ($rc.ReadErrorsTotal)    { Champ '  Erreurs lecture' $rc.ReadErrorsTotal }
                if ($rc.PowerOnHours)       { Champ '  Heures allume' $rc.PowerOnHours }
                if ($rc.Wear -gt 80) {
                    Add-Alerte 'IMPORTANT' 'SSD en fin de vie' ("Usure a {0} %." -f $rc.Wear) 'Prevoir le remplacement du SSD.'
                }
            } catch { }
        }
    } catch {
        foreach ($d in (Get-CimInstance Win32_DiskDrive)) {
            Champ 'Disque' ("{0} - {1} Go - {2}" -f $d.Model, (Go $d.Size), $d.InterfaceType)
        }
    }
}

Sonde 'Volumes et espace libre' {
    W ''
    foreach ($v in (Get-Volume | Where-Object { $_.DriveLetter -and $_.FileSystemType -ne 'Unknown' } | Sort-Object DriveLetter)) {
        $pct = 0
        if ($v.Size -gt 0) { $pct = [math]::Round(($v.SizeRemaining / $v.Size) * 100, 0) }
        W ("    {0}: {1,-14} {2,6} Go libres sur {3,6} Go  ({4} % libre)  {5}" -f `
            $v.DriveLetter, $v.FileSystemLabel, (Go $v.SizeRemaining), (Go $v.Size), $pct, $v.FileSystemType)

        if ($v.DriveLetter -eq 'C' -and ($pct -lt 10 -or (Go $v.SizeRemaining) -lt 20)) {
            Add-Alerte 'CRITIQUE' 'Disque systeme presque plein' `
                ("Il reste {0} Go ({1} %) sur C:." -f (Go $v.SizeRemaining), $pct) `
                'Liberer au moins 15 a 20 % du disque. Un SSD plein s ecroule en performance et Windows n a plus de place pour le cache.'
        } elseif ($pct -lt 10) {
            Add-Alerte 'IMPORTANT' ("Disque {0}: presque plein" -f $v.DriveLetter) `
                ("{0} % libres." -f $pct) 'Faire de la place, surtout si les jeux sont installes dessus.'
        }
    }
}

Sonde 'Ou sont installes les jeux' {
    $dossiers = @(
        "$env:ProgramFiles(x86)\Steam\steamapps\common",
        "$env:ProgramFiles\Epic Games",
        "$env:ProgramFiles(x86)\Riot Games",
        "$env:ProgramFiles(x86)\Battle.net",
        'C:\Games', 'D:\Games', 'D:\SteamLibrary', 'E:\SteamLibrary'
    )
    W ''
    foreach ($d in $dossiers) {
        if ($d -and (Test-Path $d)) {
            $lettre = $d.Substring(0, 1)
            $type = '(type inconnu)'
            try {
                $part = Get-Partition -DriveLetter $lettre -ErrorAction Stop
                $disk = Get-PhysicalDisk -ErrorAction Stop | Where-Object { $_.DeviceId -eq $part.DiskNumber }
                if ($disk) { $type = ("{0} / {1}" -f $disk.MediaType, $disk.BusType) }
            } catch { }
            W ("    {0}   -> {1}" -f $d, $type)
            if ($type -match 'HDD') {
                Add-Alerte 'IMPORTANT' 'Jeux installes sur un disque dur mecanique' `
                    ($d + ' est sur un HDD.') `
                    'Deplacer les jeux joues souvent sur le SSD : c est LA cause n1 des freezes de chargement de textures.'
            }
        }
    }
}

# =============================================================================
Titre '6. ALIMENTATION ET BRIDAGE'
# =============================================================================
Sonde 'Mode d alimentation' {
    $actif = (powercfg /getactivescheme) 2>&1
    W ('  Schema actif : ' + ($actif -join ' '))
    W ''
    W '  Schemas disponibles :'
    foreach ($l in ((powercfg /list) 2>&1)) { W ('    ' + $l) }

    $txt = "$actif"
    if ($txt -match 'conomie|Power saver|Economiseur') {
        Add-Alerte 'CRITIQUE' 'Mode Economie d energie actif' 'Le schema d alimentation bride volontairement le CPU.' `
            'Passer en "Performances elevees" ou "Utilisation normale". Panneau de configuration > Options d alimentation.'
    } elseif ($txt -match 'quilibr|Balanced|normale') {
        Add-Alerte 'INFO' 'Mode equilibre' 'Schema Equilibre actif.' `
            'Acceptable, mais "Performances elevees" evite les micro-baisses de frequence sur les jeux nerveux.'
    }

    try {
        $minCpu = ((powercfg /q SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMIN) 2>&1 | Select-String 'Index actuel|Current AC Power Setting') -join ' '
        W ('  Etat CPU minimum (secteur) : ' + $minCpu)
    } catch { }
}

Sonde 'Batterie / secteur' {
    $bats = @(Get-CimInstance Win32_Battery -ErrorAction SilentlyContinue)
    if ($bats.Count -eq 0) {
        W '  Pas de batterie (PC fixe).'
    } else {
        foreach ($b in $bats) {
            Champ 'Batterie'       $b.Name
            Champ '  Charge'       ("{0} %" -f $b.EstimatedChargeRemaining)
            Champ '  Statut'       $b.BatteryStatus
            if ($b.BatteryStatus -eq 1) {
                Add-Alerte 'CRITIQUE' 'PC portable sur batterie' 'Le PC n est pas branche au secteur.' `
                    'TOUJOURS jouer branche. Sur batterie, un portable perd facilement 50 a 70 % de ses performances.'
            }
        }
        try {
            $bs = Get-Reg 'HKLM:\SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes' 'ActiveOverlayAcPowerScheme'
            Champ 'Curseur de performance (secteur)' $bs
        } catch { }
    }
}

Sonde 'Temperatures (zones thermiques ACPI)' {
    try {
        $tz = @(Get-CimInstance -Namespace 'root/wmi' -ClassName MSAcpi_ThermalZoneTemperature -ErrorAction Stop)
        if ($tz.Count -eq 0) { W '  (aucune sonde ACPI exposee - normal sur beaucoup de cartes meres)' }
        foreach ($t in $tz) {
            $c = [math]::Round(($t.CurrentTemperature / 10) - 273.15, 1)
            W ("    {0} : {1} C" -f $t.InstanceName, $c)
            if ($c -gt 85) {
                Add-Alerte 'CRITIQUE' 'Temperature systeme elevee' ("{0} C au repos." -f $c) `
                    'Nettoyage complet (poussiere), verification des ventilateurs, changement de pate thermique si le PC a plus de 3 ans.'
            }
        }
    } catch {
        W '  (sondes ACPI non disponibles - utiliser HWiNFO64 pour les vraies temperatures)'
    }
}

# =============================================================================
Titre '7. REGLAGES WINDOWS QUI COUTENT DES FPS'
# =============================================================================
Sonde 'Securite basee sur la virtualisation (VBS / HVCI)' {
    try {
        $dg = Get-CimInstance -Namespace 'root\Microsoft\Windows\DeviceGuard' -ClassName Win32_DeviceGuard -ErrorAction Stop
        Champ 'VBS statut'            $dg.VirtualizationBasedSecurityStatus
        Champ 'Services en cours'     ($dg.SecurityServicesRunning -join ', ')
        Champ 'Services configures'   ($dg.SecurityServicesConfigured -join ', ')

        if ($dg.VirtualizationBasedSecurityStatus -eq 2) {
            Add-Alerte 'IMPORTANT' 'VBS actif (perte de FPS)' 'La securite basee sur la virtualisation tourne.' `
                'Desactiver "Integrite de la memoire" dans Securite Windows > Securite des appareils > Isolation du noyau. Gain typique : 5 a 15 % de FPS.'
        }
        if ($dg.SecurityServicesRunning -contains 2) {
            Add-Alerte 'IMPORTANT' 'Integrite de la memoire (HVCI) active' 'HVCI en cours d execution.' `
                'Meme reglage que ci-dessus. A desactiver seulement si le PC sert surtout au jeu.'
        }
    } catch { W '  (non lisible sur cette machine)' }

    $hyperv = Get-Reg 'HKLM:\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity' 'Enabled'
    Champ 'HVCI registre' $hyperv
}

Sonde 'Reglages jeu Windows' {
    $hags   = Get-Reg 'HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers' 'HwSchMode'
    $gameDvr = Get-Reg 'HKCU:\System\GameConfigStore' 'GameDVR_Enabled'
    $gameDvrPol = Get-Reg 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\GameDVR' 'AllowGameDVR'
    $gameMode = Get-Reg 'HKCU:\Software\Microsoft\GameBar' 'AutoGameModeEnabled'
    $mpo    = Get-Reg 'HKLM:\SOFTWARE\Microsoft\Windows\Dwm' 'OverlayTestMode'
    $fso    = Get-Reg 'HKCU:\System\GameConfigStore' 'GameDVR_FSEBehaviorMode'

    Champ 'Planification GPU (HAGS)' $hags
    Champ 'Game DVR (utilisateur)'   $gameDvr
    Champ 'Game DVR (strategie)'     $gameDvrPol
    Champ 'Mode Jeu auto'            $gameMode
    Champ 'MPO OverlayTestMode'      $mpo
    Champ 'Optimisations plein ecran' $fso

    if ($gameDvr -eq 1) {
        Add-Alerte 'IMPORTANT' 'Enregistrement en arriere-plan Xbox actif' 'Game DVR / capture en arriere-plan active.' `
            'Parametres > Jeux > Captures > desactiver "Enregistrer ce qui s est passe". Cout constant en FPS et en 1% low.'
    }
    if ($gameMode -eq 0) {
        Add-Alerte 'INFO' 'Mode Jeu desactive' 'Le Mode Jeu Windows est off.' 'Le reactiver aide sur les configs modestes.'
    }
    if ($null -eq $hags) {
        Add-Alerte 'INFO' 'Planification GPU accelere non configuree' 'HwSchMode absent du registre.' `
            'Essayer de l activer (Parametres > Affichage > Graphiques) et comparer : selon la config, cela aide ou nuit.'
    }
}

Sonde 'Preferences GPU par application' {
    $cle = 'HKCU:\Software\Microsoft\DirectX\UserGpuPreferences'
    if (Test-Path $cle) {
        $p = Get-ItemProperty $cle
        foreach ($n in $p.PSObject.Properties.Name) {
            if ($n -notmatch '^PS') { W ("    {0} = {1}" -f $n, $p.$n) }
        }
    } else { W '  (aucune preference GPU par application definie)' }
}

Sonde 'Effets visuels et transparence' {
    Champ 'Effets visuels'  (Get-Reg 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects' 'VisualFXSetting')
    Champ 'Transparence'    (Get-Reg 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize' 'EnableTransparency')
    Champ 'Animations'      (Get-Reg 'HKCU:\Control Panel\Desktop\WindowMetrics' 'MinAnimate')
}

# =============================================================================
Titre '8. PILOTES ET PERIPHERIQUES EN ERREUR'
# =============================================================================
Sonde 'Peripheriques en probleme' {
    try {
        $ko = @(Get-PnpDevice -ErrorAction Stop | Where-Object { $_.Status -ne 'OK' -and $_.Status -ne 'Unknown' })
        if ($ko.Count -eq 0) {
            W '  Aucun peripherique en erreur.'
        } else {
            W ("  {0} peripherique(s) en erreur :" -f $ko.Count)
            foreach ($d in $ko) {
                W ("    [{0}] {1}  (classe {2})" -f $d.Status, $d.FriendlyName, $d.Class)
            }
            $critiques = @($ko | Where-Object { $_.Class -match 'Display|System|Net|HIDClass|USB|Media' })
            if ($critiques.Count -gt 0) {
                Add-Alerte 'IMPORTANT' 'Pilotes manquants ou en erreur' `
                    (($critiques | Select-Object -First 5 | ForEach-Object { $_.FriendlyName }) -join ' | ') `
                    'Installer les pilotes manquants depuis le site du fabricant de la carte mere / du portable.'
            }
        }
    } catch { W '  (Get-PnpDevice indisponible)' }
}

Sonde 'Pilotes tiers recents' {
    try {
        $drv = Get-CimInstance Win32_PnPSignedDriver -ErrorAction Stop |
               Where-Object { $_.DriverProviderName -and $_.DriverProviderName -notmatch 'Microsoft' } |
               Sort-Object DriverDate -Descending | Select-Object -First 20
        W ''
        foreach ($d in $drv) {
            W ("    {0,-12} {1,-40} {2}" -f $d.DriverVersion, ("$($d.DeviceName)").Substring(0, [Math]::Min(40, "$($d.DeviceName)".Length)), $d.DriverDate)
        }
    } catch { }
}

# =============================================================================
Titre '9. CE QUI TOURNE EN FOND'
# =============================================================================
Sonde 'Top 20 processus par RAM' {
    W ''
    W '    RAM(Mo)   CPU(s)   Nom'
    foreach ($p in (Get-Process | Sort-Object WorkingSet64 -Descending | Select-Object -First 20)) {
        $cpuT = 0
        try { $cpuT = [math]::Round($p.CPU, 0) } catch { }
        W ("    {0,7}   {1,6}   {2}" -f [math]::Round($p.WorkingSet64 / 1MB, 0), $cpuT, $p.ProcessName)
    }
}

Sonde 'Logiciels connus pour couter des FPS' {
    $connus = @{
        'RTSS'                  = 'RivaTuner Statistics Server - overlay, peut causer du stutter'
        'MSIAfterburner'        = 'MSI Afterburner - overlay de monitoring'
        'Discord'               = 'Discord - overlay in-game souvent responsable de freezes'
        'NVIDIA Share'          = 'Overlay GeForce Experience / ShadowPlay'
        'RazerSynapse'          = 'Razer Synapse - lourd et connu pour du stutter'
        'iCUE'                  = 'Corsair iCUE - tres gourmand'
        'ArmouryCrate'          = 'Asus Armoury Crate - tres gourmand'
        'LGHUB'                 = 'Logitech G HUB - fuites memoire connues'
        'wallpaper32'           = 'Wallpaper Engine - consomme GPU en permanence'
        'wallpaper64'           = 'Wallpaper Engine - consomme GPU en permanence'
        'MSIDragonCenter'       = 'MSI Dragon Center'
        'OneDrive'              = 'OneDrive - synchro pendant le jeu = a-coups disque'
        'Dropbox'               = 'Dropbox - synchro pendant le jeu'
        'CCleaner'              = 'CCleaner en tache de fond'
        'qbittorrent'           = 'Torrent actif - sature le reseau et le disque'
        'uTorrent'              = 'Torrent actif - sature le reseau et le disque'
        'BitTorrent'            = 'Torrent actif'
        'Rainmeter'             = 'Rainmeter'
        'GameBar'               = 'Xbox Game Bar'
        'SearchIndexer'         = 'Indexation Windows - pics disque'
        'MsMpEng'               = 'Antivirus Defender - analyse en cours ?'
        'avastsvc'              = 'Avast - antivirus tiers lourd'
        'avgsvc'                = 'AVG - antivirus tiers lourd'
        'mcshield'              = 'McAfee - antivirus tiers tres lourd'
        'NortonSecurity'        = 'Norton - antivirus tiers lourd'
        'ekrn'                  = 'ESET'
        'MBAMService'           = 'Malwarebytes'
        'EpicWebHelper'         = 'Epic Games Launcher en fond'
        'SteamWebHelper'        = 'Steam - onglets du client en fond'
    }
    W ''
    $procs = Get-Process | Select-Object -ExpandProperty ProcessName -Unique
    $trouve = $false
    foreach ($k in $connus.Keys) {
        $match = @($procs | Where-Object { $_ -like ('*' + $k + '*') })
        if ($match.Count -gt 0) {
            $trouve = $true
            W ("    [PRESENT] {0,-20} {1}" -f $match[0], $connus[$k])
        }
    }
    if (-not $trouve) { W '    Aucun logiciel connu problematique detecte.' }

    # Navigateurs : RAM cumulee
    $nav = @(Get-Process chrome, msedge, firefox, opera, brave -ErrorAction SilentlyContinue)
    if ($nav.Count -gt 0) {
        $ramNav = [math]::Round((($nav | Measure-Object WorkingSet64 -Sum).Sum) / 1GB, 1)
        W ''
        Champ 'Processus navigateur' $nav.Count
        Champ 'RAM navigateurs'      ("{0} Go" -f $ramNav)
        if ($ramNav -gt 3) {
            Add-Alerte 'IMPORTANT' 'Navigateur qui mange la RAM' `
                ("{0} Go de RAM pris par {1} processus de navigateur." -f $ramNav, $nav.Count) `
                'Fermer le navigateur avant de jouer, ou au moins les onglets YouTube/Twitch (accelaration materielle = GPU pris en otage).'
        }
    }
}

Sonde 'Programmes au demarrage' {
    $items = @()
    try { $items += Get-CimInstance Win32_StartupCommand | Select-Object Name, Command, Location } catch { }
    W ''
    W ("  {0} entree(s) de demarrage :" -f $items.Count)
    foreach ($i in $items) {
        W ("    {0,-30} [{1}]" -f $i.Name, $i.Location)
    }
    if ($items.Count -gt 12) {
        Add-Alerte 'IMPORTANT' 'Trop de programmes au demarrage' ("{0} entrees." -f $items.Count) `
            'Gestionnaire des taches > Demarrage : desactiver tout ce qui n est pas indispensable. Le PC demarre plus vite et garde de la RAM pour les jeux.'
    }
}

Sonde 'Services en cours' {
    $svc = @(Get-Service | Where-Object { $_.Status -eq 'Running' })
    Champ 'Services actifs' $svc.Count
    $lourds = @('SysMain', 'WSearch', 'DiagTrack', 'dmwappushservice', 'MapsBroker', 'WerSvc')
    W ''
    foreach ($n in $lourds) {
        $s = Get-Service -Name $n -ErrorAction SilentlyContinue
        if ($s) { W ("    {0,-20} {1,-10} demarrage: {2}" -f $s.Name, $s.Status, $s.StartType) }
    }
    if ($svc.Count -gt 180) {
        Add-Alerte 'INFO' 'Beaucoup de services actifs' ("{0} services en cours." -f $svc.Count) `
            'Signe d une machine chargee en logiciels. A croiser avec la liste de demarrage.'
    }
}

Sonde 'Antivirus installes' {
    try {
        $av = @(Get-CimInstance -Namespace 'root\SecurityCenter2' -ClassName AntiVirusProduct -ErrorAction Stop)
        foreach ($a in $av) { Champ 'Antivirus' $a.displayName }
        $tiers = @($av | Where-Object { $_.displayName -notmatch 'Defender' })
        if ($tiers.Count -gt 0) {
            Add-Alerte 'IMPORTANT' 'Antivirus tiers installe' (($tiers | ForEach-Object { $_.displayName }) -join ', ') `
                'Les antivirus tiers scannent les fichiers de jeu en temps reel = stutter. Windows Defender seul suffit, avec une exclusion sur les dossiers de jeux.'
        }
        if ($av.Count -gt 1) {
            Add-Alerte 'CRITIQUE' 'Plusieurs antivirus en meme temps' ("{0} produits detectes." -f $av.Count) `
                'Deux antivirus se scannent mutuellement : ralentissement massif garanti. N en garder qu un.'
        }
    } catch { W '  (SecurityCenter2 non lisible)' }

    try {
        $mp = Get-MpComputerStatus -ErrorAction Stop
        Champ 'Defender temps reel' $mp.RealTimeProtectionEnabled
        Champ 'Defender analyse en cours' $mp.QuickScanInProgress
        $ex = (Get-MpPreference -ErrorAction Stop).ExclusionPath
        if ($ex) {
            W '  Exclusions Defender :'
            foreach ($e in $ex) { W ('    ' + $e) }
        } else {
            Add-Alerte 'INFO' 'Aucune exclusion antivirus sur les jeux' 'Defender scanne aussi les dossiers de jeux.' `
                'Ajouter le dossier steamapps et les dossiers de jeux en exclusion : moins d a-coups au chargement.'
        }
    } catch { }
}

# =============================================================================
Titre '10. JOURNAUX D EVENEMENTS (30 DERNIERS JOURS)'
# =============================================================================
Sonde 'Erreurs materielles et crashs' {
    $depuis = (Get-Date).AddDays(-30)

    $recherches = @(
        @{ Nom = 'WHEA (erreur materielle CPU/RAM/PCIe)'; Log = 'System'; Provider = 'Microsoft-Windows-WHEA-Logger'; Id = $null; Grave = 'CRITIQUE'
           Fix = 'Erreur materielle reelle : RAM instable (XMP trop agressif), overclock, alimentation faible ou composant defaillant.' },
        @{ Nom = 'Kernel-Power 41 (arret brutal)'; Log = 'System'; Provider = 'Microsoft-Windows-Kernel-Power'; Id = 41; Grave = 'CRITIQUE'
           Fix = 'Le PC s eteint sans prevenir : alimentation insuffisante, surchauffe, ou instabilite RAM/CPU.' },
        @{ Nom = 'Ecran bleu / BugCheck'; Log = 'System'; Provider = $null; Id = 1001; Grave = 'CRITIQUE'
           Fix = 'Analyser les minidumps dans C:\Windows\Minidump.' },
        @{ Nom = 'Plantage du pilote graphique (TDR 4101)'; Log = 'System'; Provider = $null; Id = 4101; Grave = 'CRITIQUE'
           Fix = 'Le pilote graphique plante et redemarre : reinstallation propre du pilote avec DDU, ou GPU instable/surchauffe.' },
        @{ Nom = 'Erreurs disque (disk / Ntfs)'; Log = 'System'; Provider = $null; Id = 7; Grave = 'CRITIQUE'
           Fix = 'Secteurs illisibles : sauvegarder et tester le disque (CrystalDiskInfo).' },
        @{ Nom = 'Timeout disque (Id 129)'; Log = 'System'; Provider = $null; Id = 129; Grave = 'IMPORTANT'
           Fix = 'Le controleur disque ne repond pas a temps : cable SATA, pilote de stockage, ou SSD en souffrance. Cause classique de freezes de 2-5 secondes.' },
        @{ Nom = 'Disque en erreur (Id 153)'; Log = 'System'; Provider = $null; Id = 153; Grave = 'IMPORTANT'
           Fix = 'Idem : verifier le cable et la sante du disque.' }
    )

    foreach ($r in $recherches) {
        try {
            $filtre = @{ LogName = $r.Log; StartTime = $depuis }
            if ($r.Provider) { $filtre['ProviderName'] = $r.Provider }
            if ($r.Id)       { $filtre['Id'] = $r.Id }

            $ev = @(Get-WinEvent -FilterHashtable $filtre -MaxEvents 200 -ErrorAction Stop)
            W ''
            Champ $r.Nom ("{0} evenement(s)" -f $ev.Count)
            foreach ($e in ($ev | Select-Object -First 3)) {
                $msg = ("$($e.Message)" -replace '\s+', ' ')
                if ($msg.Length -gt 160) { $msg = $msg.Substring(0, 160) + '...' }
                W ("      {0}  {1}" -f $e.TimeCreated.ToString('dd/MM HH:mm'), $msg)
            }
            if ($ev.Count -gt 0) {
                Add-Alerte $r.Grave $r.Nom ("{0} occurrence(s) en 30 jours, derniere le {1}." -f $ev.Count, $ev[0].TimeCreated.ToString('dd/MM/yyyy HH:mm')) $r.Fix
            }
        } catch {
            W ''
            Champ $r.Nom '0 evenement'
        }
    }
}

Sonde 'Applications qui plantent' {
    try {
        $depuis = (Get-Date).AddDays(-30)
        $ev = @(Get-WinEvent -FilterHashtable @{ LogName = 'Application'; Id = 1000, 1002; StartTime = $depuis } -MaxEvents 100 -ErrorAction Stop)
        W ''
        Champ 'Plantages applicatifs' ("{0} en 30 jours" -f $ev.Count)
        $groupes = $ev | Group-Object { ($_.Properties[0].Value) } | Sort-Object Count -Descending | Select-Object -First 8
        foreach ($g in $groupes) { W ("      {0,-40} {1} fois" -f $g.Name, $g.Count) }
    } catch { W '  (aucun plantage applicatif recent)' }
}

# =============================================================================
Titre '11. RESEAU'
# =============================================================================
Sonde 'Cartes reseau' {
    try {
        foreach ($a in (Get-NetAdapter -ErrorAction Stop | Where-Object { $_.Status -eq 'Up' })) {
            W ''
            Champ 'Carte'         $a.InterfaceDescription
            Champ '  Type'        $a.MediaType
            Champ '  Debit lien'  $a.LinkSpeed
            Champ '  Pilote'      ("{0} du {1}" -f $a.DriverVersion, $a.DriverDate)

            if ($a.InterfaceDescription -match 'Wi-?Fi|Wireless|802\.11' -or $a.MediaType -match 'Native 802.11') {
                Add-Alerte 'IMPORTANT' 'Connexion en Wi-Fi' ('Carte active : ' + $a.InterfaceDescription) `
                    'Pour les jeux en ligne, passer en cable Ethernet elimine les pics de ping et les micro-coupures. C est la cause la plus frequente de "lag" ressenti en multijoueur.'
            }
            if ($a.LinkSpeed -match '^(10|100) Mbps') {
                Add-Alerte 'IMPORTANT' 'Lien reseau limite' ('Debit negocie : ' + $a.LinkSpeed) `
                    'Cable Ethernet de mauvaise qualite ou port limite : essayer un autre cable (Cat 5e minimum) ou un autre port.'
            }
        }
    } catch { W '  (Get-NetAdapter indisponible)' }

    try {
        $dns = (Get-DnsClientServerAddress -AddressFamily IPv4 -ErrorAction Stop | Where-Object { $_.ServerAddresses.Count -gt 0 })
        W ''
        foreach ($d in $dns) { W ("    DNS {0,-30} {1}" -f $d.InterfaceAlias, ($d.ServerAddresses -join ', ')) }
    } catch { }
}

if (-not $SansReseau) {
    Sonde 'Test de latence (20 pings)' {
        foreach ($cible in @('1.1.1.1', '8.8.8.8')) {
            try {
                $r = @(Test-Connection -ComputerName $cible -Count 20 -ErrorAction Stop)
                $temps = @($r | ForEach-Object { $_.ResponseTime })
                $moy = [math]::Round(($temps | Measure-Object -Average).Average, 1)
                $max = ($temps | Measure-Object -Maximum).Maximum
                $min = ($temps | Measure-Object -Minimum).Minimum
                $perte = 20 - $r.Count
                $gigue = $max - $min
                W ''
                Champ ('Ping ' + $cible) ("moyenne {0} ms | min {1} | max {2} | gigue {3} ms | perte {4}/20" -f $moy, $min, $max, $gigue, $perte)

                if ($perte -gt 0) {
                    Add-Alerte 'CRITIQUE' 'Perte de paquets' ("{0} paquets perdus sur 20 vers {1}." -f $perte, $cible) `
                        'Connexion instable : tester en Ethernet, changer de cable, verifier la box. C est ce qui donne les teleportations en jeu.'
                }
                if ($gigue -gt 30) {
                    Add-Alerte 'IMPORTANT' 'Gigue reseau elevee' ("Variation de {0} ms sur la latence." -f $gigue) `
                        'Quelque chose sature la connexion (telechargement, streaming, torrent, maj Steam) ou le Wi-Fi est perturbe.'
                }
                if ($moy -gt 60) {
                    Add-Alerte 'IMPORTANT' 'Latence elevee' ("Ping moyen de {0} ms vers {1}." -f $moy, $cible) `
                        'Verifier qu aucun telechargement ne tourne et tester en Ethernet.'
                }
            } catch {
                W ('  Ping ' + $cible + ' : echec (' + $_.Exception.Message + ')')
            }
        }
    }
} else {
    W ''
    W '  (test reseau saute : parametre -SansReseau)'
}

# =============================================================================
Titre '12. LOGICIELS INSTALLES'
# =============================================================================
Sonde 'Inventaire' {
    $cles = @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*'
    )
    $apps = @()
    foreach ($c in $cles) {
        try {
            $apps += Get-ItemProperty $c -ErrorAction SilentlyContinue |
                     Where-Object { $_.DisplayName } |
                     Select-Object DisplayName, DisplayVersion, Publisher, InstallDate
        } catch { }
    }
    $apps = $apps | Sort-Object DisplayName -Unique
    Champ 'Logiciels installes' $apps.Count
    W ''
    foreach ($a in $apps) {
        W ("    {0,-52} {1}" -f ("$($a.DisplayName)").Substring(0, [Math]::Min(52, "$($a.DisplayName)".Length)), $a.DisplayVersion)
    }

    $suspects = @($apps | Where-Object { $_.DisplayName -match 'Driver Booster|DriverPack|Advanced SystemCare|PC ?Optimizer|Registry Cleaner|WinZip|Search Protect|Web Companion|MyPC|SpeedUp|Ask Toolbar|Baidu|Yandex' })
    if ($suspects.Count -gt 0) {
        Add-Alerte 'CRITIQUE' 'Logiciels "optimiseurs" douteux installes' `
            (($suspects | ForEach-Object { $_.DisplayName }) -join ', ') `
            'Ces outils cassent plus qu ils ne reparent (pilotes non officiels, services en fond, pubs). A desinstaller.'
    }
}

if ($Complet) {
    Sonde 'DXDIAG (dump complet, peut prendre 30 s)' {
        $dx = [System.IO.Path]::ChangeExtension($Sortie, $null) + 'dxdiag.txt'
        try {
            Start-Process -FilePath 'dxdiag.exe' -ArgumentList @('/t', $dx) -Wait -NoNewWindow
            W ('  Dump DXDIAG genere : ' + $dx)
            W '  (a envoyer aussi)'
        } catch { W ('  Echec DXDIAG : ' + $_.Exception.Message) }
    }
}

# =============================================================================
# VERDICT
# =============================================================================
$verdict = New-Object System.Collections.ArrayList
$null = $verdict.Add('')
$null = $verdict.Add('#' * 78)
$null = $verdict.Add('#  VERDICT - CE QUI EXPLIQUE PROBABLEMENT LES LAGS')
$null = $verdict.Add('#' * 78)
$null = $verdict.Add('')

if ($script:Alertes.Count -eq 0) {
    $null = $verdict.Add('  Aucun probleme evident detecte par le script.')
    $null = $verdict.Add('  Si ca lag quand meme : le materiel est probablement juste pour les jeux vises,')
    $null = $verdict.Add('  ou le probleme est thermique (a verifier avec HWiNFO64 pendant une partie).')
} else {
    $n = 0
    foreach ($niveau in @('CRITIQUE', 'IMPORTANT', 'INFO')) {
        $lot = @($script:Alertes | Where-Object { $_.Niveau -eq $niveau })
        if ($lot.Count -eq 0) { continue }
        $null = $verdict.Add('')
        $null = $verdict.Add('  --- ' + $niveau + ' (' + $lot.Count + ') ' + ('-' * (60 - $niveau.Length)))
        foreach ($a in $lot) {
            $n++
            $null = $verdict.Add('')
            $null = $verdict.Add(("  {0}. {1}" -f $n, $a.Sujet))
            if ($a.Detail) { $null = $verdict.Add('     Constat : ' + $a.Detail) }
            if ($a.Fix)    { $null = $verdict.Add('     A faire : ' + $a.Fix) }
        }
    }
}

$null = $verdict.Add('')
$null = $verdict.Add(('  Total : {0} point(s) d attention.' -f $script:Alertes.Count))
$null = $verdict.Add('')
$null = $verdict.Add('  Le detail complet de chaque mesure se trouve dans les sections ci-dessous.')
$null = $verdict.Add('')

# Le verdict est insere juste apres l en-tete pour etre lu en premier
$entete = $script:Lignes.GetRange(0, 6)
$corps  = $script:Lignes.GetRange(6, $script:Lignes.Count - 6)

$final = New-Object System.Collections.ArrayList
$null = $final.AddRange($entete)
$null = $final.AddRange($verdict)
$null = $final.AddRange($corps)
$null = $final.Add('')
$null = $final.Add(('Fin du rapport - genere en {0} secondes.' -f [math]::Round(((Get-Date) - $debut).TotalSeconds, 1)))

try {
    $final | Out-File -FilePath $Sortie -Encoding UTF8 -Force
} catch {
    $Sortie = Join-Path $env:USERPROFILE ("BT-Diagnostic-{0}.txt" -f $horo)
    $final | Out-File -FilePath $Sortie -Encoding UTF8 -Force
}

# --- Resume console ----------------------------------------------------------
Write-Host ''
Write-Host ('  ' + ('=' * 60)) -ForegroundColor Cyan
Write-Host '  DIAGNOSTIC TERMINE' -ForegroundColor Cyan
Write-Host ('  ' + ('=' * 60)) -ForegroundColor Cyan
Write-Host ''

$crit = @($script:Alertes | Where-Object { $_.Niveau -eq 'CRITIQUE' })
$imp  = @($script:Alertes | Where-Object { $_.Niveau -eq 'IMPORTANT' })

Write-Host ('  Problemes critiques  : ' + $crit.Count) -ForegroundColor Red
Write-Host ('  Points importants    : ' + $imp.Count)  -ForegroundColor Yellow
Write-Host ''
foreach ($a in $crit) { Write-Host ('   [!] ' + $a.Sujet) -ForegroundColor Red }
foreach ($a in $imp)  { Write-Host ('   [-] ' + $a.Sujet) -ForegroundColor Yellow }

Write-Host ''
Write-Host '  Rapport complet :' -ForegroundColor Green
Write-Host ('  ' + $Sortie) -ForegroundColor White
Write-Host ''
Write-Host '  >>> Envoie CE FICHIER pour une analyse detaillee. <<<' -ForegroundColor Cyan
Write-Host ''

try { Start-Process notepad.exe -ArgumentList ('"' + $Sortie + '"') } catch { }
Read-Host '  Appuie sur Entree pour fermer'
