<#
    DesTinGOOD PC Optimizer - Reduction de l'input lag pour Windows 10/11
    ==================================================================
    Outil a cases a cocher : chaque optimisation est optionnelle. Rien
    n'est modifie tant que vous ne lancez pas "Appliquer".

    Deux modes automatiques :
      - Mode GRAPHIQUE (cases + boutons) quand .NET/WinForms est dispo.
      - Mode CONSOLE interactif (menu numerote) en repli, notamment sous
        PowerShell "ConstrainedLanguage" ou WinForms est bloque.
      - Forcer la console : parametre -Console

    Securite :
      - Sauvegarde automatique des cles de registre (.reg) sur le Bureau
      - Point de restauration systeme optionnel
      - "Retablir les valeurs Windows" pour revenir en arriere
      - "Restaurer une sauvegarde" pour reimporter un dossier .reg

    Usage : double-clic sur Lancer-Optimiseur.bat, ou clic droit sur ce
    fichier > Executer avec PowerShell. L'elevation admin est automatique.
#>

param(
    [string]$BtUserSid,
    [switch]$Console
)

$ErrorActionPreference = 'Stop'
$script:AppName = 'DesTinGOOD PC Optimizer'
$script:Version = '2.0'

# --- Resolution du Bureau (compatible ConstrainedLanguage) --------------------
function Get-BtDesktop {
    param([string]$Sid)
    if ($Sid) {
        try {
            $pp = (Get-ItemProperty ("HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\{0}" -f $Sid) -Name ProfileImagePath -ErrorAction Stop).ProfileImagePath
            if ($pp) { return (Join-Path $pp 'Desktop') }
        } catch { }
    }
    return (Join-Path $env:USERPROFILE 'Desktop')
}

# --- Detection admin + SID (compatible ConstrainedLanguage) -------------------
function Test-BtAdmin {
    try {
        $id = [Security.Principal.WindowsIdentity]::GetCurrent()
        return (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    } catch {
        # ConstrainedLanguage : appels .NET bloques -> repli natif.
        # S-1-16-12288 = niveau d'integrite "High" = processus eleve. Ne depend
        # d'aucun service (contrairement a "net session").
        $exe = Join-Path $env:SystemRoot 'System32\whoami.exe'
        $groups = @(& $exe /groups) -join "`n"
        return ($groups -match 'S-1-16-12288')
    }
}

function Get-BtCurrentSid {
    try {
        return [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    } catch {
        # Chemin complet : evite qu'un whoami tiers dans le PATH soit choisi.
        $exe = Join-Path $env:SystemRoot 'System32\whoami.exe'
        $csv = @(& $exe /user /fo csv) | Select-Object -Last 1
        if ("$csv" -match 'S-1-[0-9\-]+') { return $Matches[0] }
        return ''
    }
}

# Chemin du script (vide si lance via Invoke-Expression -> repli sur BT_SCRIPT)
$script:SelfPath = $PSCommandPath
if (-not $script:SelfPath) { $script:SelfPath = $env:BT_SCRIPT }
if (-not $BtUserSid)       { $BtUserSid = $env:BT_USERSID }

# --- Elevation administrateur (policy-proof : relance via Invoke-Expression) ---
if (-not (Test-BtAdmin)) {
    if (-not $script:SelfPath) {
        Write-Warning "Chemin du script inconnu. Lancez plutot Lancer-Optimiseur.bat."
        Start-Sleep -Seconds 5
        exit
    }
    try {
        # La fenetre doit rester visible si on va tomber en mode console.
        $lm = "$($ExecutionContext.SessionState.LanguageMode)"
        $useConsole = $Console.IsPresent -or ($lm -ne 'FullLanguage')
        $winStyle = 'Hidden'
        if ($useConsole) { $winStyle = 'Normal' }
        $mySid = Get-BtCurrentSid
        # -Command + Invoke-Expression : contourne une strategie d'execution
        # "Restricted" (GPO) que -ExecutionPolicy Bypass ne peut PAS outrepasser
        # pour un -File. On transmet chemin et SID via variables d'environnement.
        $inner = "`$env:BT_SCRIPT = '{0}'; `$env:BT_USERSID = '{1}'; Invoke-Expression (Get-Content -LiteralPath '{0}' -Raw)" -f $script:SelfPath, $mySid
        $relArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-WindowStyle', $winStyle, '-Command', $inner)
        Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $relArgs
    } catch {
        Write-Warning "Elevation refusee. Le script doit etre lance en administrateur."
        Start-Sleep -Seconds 4
    }
    exit
}

# --- Ruche de l'utilisateur connecte (pas celle du compte administrateur) ------
# Elevation "over-the-shoulder" : si UAC a demande un AUTRE compte admin, HKCU
# pointerait sur le profil de l'admin. On remappe HKCU: vers la ruche de
# l'utilisateur d'origine, dont le SID a ete transmis par l'instance non elevee.
$script:HkcuExportRoot = 'HKCU'
$currentSid = Get-BtCurrentSid
if ($BtUserSid -and $BtUserSid -ne $currentSid) {
    if (Test-Path ("Registry::HKEY_USERS\{0}" -f $BtUserSid)) {
        Remove-PSDrive -Name HKCU -Force -ErrorAction SilentlyContinue
        [void](New-PSDrive -Name HKCU -PSProvider Registry -Root ("HKEY_USERS\{0}" -f $BtUserSid) -Scope Global)
        $script:HkcuExportRoot = "HKU\$BtUserSid"
    } else {
        Write-Warning "Profil de l'utilisateur d'origine introuvable ; les reglages par-utilisateur viseront le compte administrateur."
    }
}
$script:BackupDesktop = Get-BtDesktop -Sid $BtUserSid

# --- Detection des capacites (.NET / WinForms / P-Invoke) ----------------------
$script:LanguageMode = "$($ExecutionContext.SessionState.LanguageMode)"
$script:GuiAvailable = $false
if (-not $Console.IsPresent) {
    try {
        Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop
        Add-Type -AssemblyName System.Drawing -ErrorAction Stop
        [System.Windows.Forms.Application]::EnableVisualStyles()
        $script:GuiAvailable = $true
    } catch {
        $script:GuiAvailable = $false
    }
}

$script:SpiAvailable = $false
try {
    Add-Type -Namespace BtNative -Name User32 -MemberDefinition @'
[DllImport("user32.dll", SetLastError = true)]
public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, int[] pvParam, uint fWinIni);
'@ -ErrorAction Stop
    $script:SpiAvailable = $true
} catch {
    $script:SpiAvailable = $false
}

# --- Journalisation (routee vers l'IHM ou la console) -------------------------
function Write-BtLog {
    param([string]$Message, [string]$Level = 'INFO')
    $line = "[{0}] [{1}] {2}" -f (Get-Date -Format 'HH:mm:ss'), $Level, $Message
    if ($script:LogBox) {
        $script:LogBox.AppendText($line + [Environment]::NewLine)
    } else {
        $color = 'Gray'
        switch ($Level) {
            'OK'     { $color = 'Green' }
            'WARN'   { $color = 'Yellow' }
            'ERREUR' { $color = 'Red' }
            default  { $color = 'Gray' }
        }
        Write-Host $line -ForegroundColor $color
    }
}

# --- Helpers registre / natif -------------------------------------------------
function Set-BtRegValue {
    param([string]$Path, [string]$Name, $Value, [string]$Type = 'DWord')
    if (-not (Test-Path -Path $Path)) {
        [void](New-Item -Path $Path -Force)
    }
    [void](New-ItemProperty -Path $Path -Name $Name -Value $Value -PropertyType $Type -Force)
}

function Remove-BtRegValue {
    param([string]$Path, [string]$Name)
    if (Test-Path -Path $Path) {
        Remove-ItemProperty -Path $Path -Name $Name -ErrorAction SilentlyContinue
    }
}

function Get-BtRegValue {
    param([string]$Path, [string]$Name)
    try {
        (Get-ItemProperty -Path $Path -Name $Name -ErrorAction Stop).$Name
    } catch {
        $null
    }
}

function Invoke-BtNative {
    param([string]$Exe, [string[]]$ArgumentList, [string]$Action)
    $out = & $Exe @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw ("{0} a echoue (code {1})." -f $Action, $LASTEXITCODE)
    }
    return $out
}

function Set-BtMouseParams {
    # pvParam = [seuil X, seuil Y, acceleration]. Ignore si P-Invoke indispo.
    param([int[]]$Values)
    if (-not $script:SpiAvailable) { return }
    [void][BtNative.User32]::SystemParametersInfo(0x0004, 0, $Values, 0x03)
}

function Enable-BtUltimatePlan {
    # Detection par GUID (independant de la langue de Windows) et duplication vers
    # un GUID de destination FIXE, pour reconnaitre la copie aux runs suivants et
    # ne pas accumuler de plans en double.
    $srcGuid   = 'e9a42b02-d5df-448d-aa00-03f14749eb61'   # plan integre "Performances ultimes" (masque)
    $cloneGuid = 'e9a42b02-d5df-448d-aa00-03f14749eb62'   # copie a GUID fixe creee par ce script
    $lines  = @(powercfg /list)
    $target = $lines | Where-Object { $_ -match "$srcGuid|$cloneGuid|Performances ultimes|Ultimate Performance" } | Select-Object -First 1
    if (-not $target) {
        [void](Invoke-BtNative 'powercfg' @('/duplicatescheme', $srcGuid, $cloneGuid) "Creation du plan Performances ultimes")
        $target = $cloneGuid
    }
    if ("$target" -notmatch '([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})') {
        throw "GUID du plan d'alimentation introuvable."
    }
    [void](Invoke-BtNative 'powercfg' @('/setactive', $Matches[1]) "Activation du plan Performances ultimes")
}

function Set-BtNagle {
    param([bool]$Disable)
    $root = 'HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces'
    foreach ($iface in (Get-ChildItem -Path $root)) {
        if ($Disable) {
            Set-BtRegValue -Path $iface.PSPath -Name 'TcpAckFrequency' -Value 1 -Type DWord
            Set-BtRegValue -Path $iface.PSPath -Name 'TCPNoDelay'      -Value 1 -Type DWord
        } else {
            Remove-BtRegValue -Path $iface.PSPath -Name 'TcpAckFrequency'
            Remove-BtRegValue -Path $iface.PSPath -Name 'TCPNoDelay'
        }
    }
}

function Backup-BtRegistry {
    param([array]$Tweaks)
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $dir   = Join-Path -Path $script:BackupDesktop -ChildPath ("dtg-optimizer-backup-" + $stamp)
    [void](New-Item -ItemType Directory -Path $dir -Force)
    $keys = @($Tweaks | ForEach-Object { $_.BackupKeys } | Where-Object { $_ } | Sort-Object -Unique)
    foreach ($key in $keys) {
        # reg.exe ne connait pas les PSDrive : on cible la vraie ruche utilisateur.
        $exportKey = $key -replace '^HKCU', $script:HkcuExportRoot
        $file = Join-Path -Path $dir -ChildPath (($exportKey -replace '[\\: ]', '_') + '.reg')
        # PS 5.1 : sous EAP=Stop, rediriger stderr (2>) d'un exe natif transforme la
        # 1re ligne d'erreur en exception terminale. On relaxe EAP le temps de l'appel
        # pour que l'avertissement "cle absente" reste atteignable.
        $eapBackup = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        & reg.exe export $exportKey $file /y 2>$null | Out-Null
        $code = $LASTEXITCODE
        $ErrorActionPreference = $eapBackup
        if ($code -ne 0) {
            Write-BtLog ("Cle absente (rien a sauvegarder) : {0}" -f $exportKey) 'WARN'
        }
    }
    (powercfg /getactivescheme) | Out-File -FilePath (Join-Path $dir 'plan-alimentation.txt') -Encoding UTF8
    (bcdedit /enum '{current}') | Out-File -FilePath (Join-Path $dir 'bcdedit.txt') -Encoding UTF8
    return $dir
}

function Import-BtBackup {
    param([string]$Folder)
    if (-not $Folder) { return }
    if (-not (Test-Path -Path $Folder)) {
        Write-BtLog ("Dossier introuvable : {0}" -f $Folder) 'ERREUR'
        return
    }
    $files = @(Get-ChildItem -Path $Folder -Filter '*.reg' -File -ErrorAction SilentlyContinue)
    if ($files.Count -eq 0) {
        Write-BtLog ("Aucun fichier .reg dans : {0}" -f $Folder) 'WARN'
        return
    }
    Write-BtLog ("Restauration depuis : {0}" -f $Folder)
    foreach ($f in $files) {
        $eapBackup = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        & reg.exe import $f.FullName 2>$null | Out-Null
        $code = $LASTEXITCODE
        $ErrorActionPreference = $eapBackup
        if ($code -eq 0) {
            Write-BtLog ("Importe : {0}" -f $f.Name) 'OK'
        } else {
            Write-BtLog ("Echec import : {0}" -f $f.Name) 'ERREUR'
        }
    }
    Write-BtLog "Restauration terminee. Un redemarrage peut etre necessaire." 'WARN'
}

function New-BtRestorePoint {
    Write-BtLog "Creation d'un point de restauration systeme (peut prendre une minute)..."
    try {
        $warn = @()
        Checkpoint-Computer -Description 'DesTinGOOD PC Optimizer' -RestorePointType 'MODIFY_SETTINGS' `
            -ErrorAction Stop -WarningVariable warn -WarningAction SilentlyContinue
        if ($warn.Count -gt 0) {
            # Throttle des 24 h : Checkpoint-Computer emet un WARNING (pas une erreur)
            # et ne cree rien. -ErrorAction Stop ne l'attrape pas, d'ou la verification.
            Write-BtLog ("Point de restauration NON cree : {0}" -f $warn[0].Message) 'WARN'
        } else {
            Write-BtLog "Point de restauration cree." 'OK'
        }
    } catch {
        Write-BtLog ("Point de restauration impossible : {0}" -f $_.Exception.Message) 'WARN'
    }
}

# --- Definition des optimisations ---------------------------------------------
$MMKey    = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile'
$GamesKey = "$MMKey\Tasks\Games"

$script:Tweaks = @(
    @{
        Id = 'mouse_accel'; Category = 'Souris & clavier'; Recommended = $true; Reboot = $false
        Name        = "Desactiver l'acceleration de la souris (precision du pointeur)"
        Description = "Deplacement 1:1 de la souris, indispensable pour la visee. Ne touche pas au curseur de vitesse. Effet immediat."
        BackupKeys  = @('HKCU\Control Panel\Mouse')
        Apply = {
            Set-BtRegValue -Path 'HKCU:\Control Panel\Mouse' -Name 'MouseSpeed'      -Value '0' -Type String
            Set-BtRegValue -Path 'HKCU:\Control Panel\Mouse' -Name 'MouseThreshold1' -Value '0' -Type String
            Set-BtRegValue -Path 'HKCU:\Control Panel\Mouse' -Name 'MouseThreshold2' -Value '0' -Type String
            # Effet direct uniquement dans le profil de l'utilisateur courant
            # (sinon SystemParametersInfo persisterait dans le profil admin).
            if ($script:HkcuExportRoot -eq 'HKCU') { Set-BtMouseParams -Values @(0, 0, 0) }
        }
        Revert = {
            Set-BtRegValue -Path 'HKCU:\Control Panel\Mouse' -Name 'MouseSpeed'      -Value '1'  -Type String
            Set-BtRegValue -Path 'HKCU:\Control Panel\Mouse' -Name 'MouseThreshold1' -Value '6'  -Type String
            Set-BtRegValue -Path 'HKCU:\Control Panel\Mouse' -Name 'MouseThreshold2' -Value '10' -Type String
            if ($script:HkcuExportRoot -eq 'HKCU') { Set-BtMouseParams -Values @(6, 10, 1) }
        }
        Check = { (Get-BtRegValue -Path 'HKCU:\Control Panel\Mouse' -Name 'MouseSpeed') -eq '0' }
    }
    @{
        Id = 'input_queues'; Category = 'Souris & clavier'; Recommended = $false; Reboot = $true
        Name        = "Reduire les files d'attente souris/clavier (32 au lieu de 100)"
        Description = "Buffers pilote plus petits = traitement plus direct des entrees. Experimental : sans effet mesurable sur certaines machines."
        BackupKeys  = @('HKLM\SYSTEM\CurrentControlSet\Services\mouclass\Parameters',
                        'HKLM\SYSTEM\CurrentControlSet\Services\kbdclass\Parameters')
        Apply = {
            Set-BtRegValue -Path 'HKLM:\SYSTEM\CurrentControlSet\Services\mouclass\Parameters' -Name 'MouseDataQueueSize'    -Value 32 -Type DWord
            Set-BtRegValue -Path 'HKLM:\SYSTEM\CurrentControlSet\Services\kbdclass\Parameters' -Name 'KeyboardDataQueueSize' -Value 32 -Type DWord
        }
        Revert = {
            Set-BtRegValue -Path 'HKLM:\SYSTEM\CurrentControlSet\Services\mouclass\Parameters' -Name 'MouseDataQueueSize'    -Value 100 -Type DWord
            Set-BtRegValue -Path 'HKLM:\SYSTEM\CurrentControlSet\Services\kbdclass\Parameters' -Name 'KeyboardDataQueueSize' -Value 100 -Type DWord
        }
        Check = { (Get-BtRegValue -Path 'HKLM:\SYSTEM\CurrentControlSet\Services\mouclass\Parameters' -Name 'MouseDataQueueSize') -eq 32 }
    }
    @{
        Id = 'power_ultimate'; Category = 'Alimentation & CPU'; Recommended = $true; Reboot = $false
        Name        = "Activer le plan d'alimentation Performances ultimes"
        Description = "Supprime les economies d'energie qui endorment le CPU (latence de reveil des coeurs). Le plan est cree s'il n'existe pas."
        BackupKeys  = @()
        Apply  = { Enable-BtUltimatePlan }
        Revert = { [void](Invoke-BtNative 'powercfg' @('/setactive', '381b4222-f694-41f0-9685-ff5bb260df2e') "Retour au plan Utilisation normale") }
        Check  = { "$(powercfg /getactivescheme)" -match 'e9a42b02-d5df-448d-aa00-03f14749eb6[12]|Performances ultimes|Ultimate Performance' }
    }
    @{
        Id = 'power_throttling'; Category = 'Alimentation & CPU'; Recommended = $true; Reboot = $false
        Name        = "Desactiver le Power Throttling (bridage energetique des processus)"
        Description = "Empeche Windows de brider les applications en arriere-plan/premier plan pour economiser l'energie."
        BackupKeys  = @('HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling')
        Apply  = { Set-BtRegValue -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling' -Name 'PowerThrottlingOff' -Value 1 -Type DWord }
        Revert = { Remove-BtRegValue -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling' -Name 'PowerThrottlingOff' }
        Check  = { (Get-BtRegValue -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling' -Name 'PowerThrottlingOff') -eq 1 }
    }
    @{
        Id = 'usb_suspend'; Category = 'Alimentation & CPU'; Recommended = $false; Reboot = $false
        Name        = "Desactiver la suspension selective USB (plan actif)"
        Description = "Evite que la souris/le clavier USB soient mis en veille par Windows. A appliquer apres le plan Performances ultimes."
        BackupKeys  = @()
        Apply = {
            [void](Invoke-BtNative 'powercfg' @('/setacvalueindex', 'SCHEME_CURRENT', '2a737441-1930-4402-8d77-b2bebba308a3', '48e6b7a6-50f5-4782-a5d4-53bb8f07e226', '0') "Reglage USB (secteur)")
            [void](Invoke-BtNative 'powercfg' @('/setdcvalueindex', 'SCHEME_CURRENT', '2a737441-1930-4402-8d77-b2bebba308a3', '48e6b7a6-50f5-4782-a5d4-53bb8f07e226', '0') "Reglage USB (batterie)")
            [void](Invoke-BtNative 'powercfg' @('/setactive', 'SCHEME_CURRENT') "Application du plan")
        }
        Revert = {
            [void](Invoke-BtNative 'powercfg' @('/setacvalueindex', 'SCHEME_CURRENT', '2a737441-1930-4402-8d77-b2bebba308a3', '48e6b7a6-50f5-4782-a5d4-53bb8f07e226', '1') "Reglage USB (secteur)")
            [void](Invoke-BtNative 'powercfg' @('/setdcvalueindex', 'SCHEME_CURRENT', '2a737441-1930-4402-8d77-b2bebba308a3', '48e6b7a6-50f5-4782-a5d4-53bb8f07e226', '1') "Reglage USB (batterie)")
            [void](Invoke-BtNative 'powercfg' @('/setactive', 'SCHEME_CURRENT') "Application du plan")
        }
        Check = { $null }
    }
    @{
        Id = 'hags'; Category = 'GPU & jeux'; Recommended = $false; Reboot = $true
        Name        = "Activer la planification GPU acceleree par materiel (HAGS)"
        Description = "Reduit la latence de la file de rendu sur GPU recents (NVIDIA GTX 10xx+/RTX, AMD RX 5000+). A eviter sur GPU anciens."
        BackupKeys  = @('HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers')
        Apply  = { Set-BtRegValue -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers' -Name 'HwSchMode' -Value 2 -Type DWord }
        Revert = { Remove-BtRegValue -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers' -Name 'HwSchMode' }
        Check  = { (Get-BtRegValue -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers' -Name 'HwSchMode') -eq 2 }
    }
    @{
        Id = 'fso_disable'; Category = 'GPU & jeux'; Recommended = $true; Reboot = $false
        Name        = "Desactiver les optimisations plein ecran (vrai plein ecran exclusif)"
        Description = "Force le comportement plein ecran classique : moins de latence de presentation dans les jeux plein ecran."
        BackupKeys  = @('HKCU\System\GameConfigStore')
        Apply = {
            Set-BtRegValue -Path 'HKCU:\System\GameConfigStore' -Name 'GameDVR_FSEBehaviorMode'              -Value 2 -Type DWord
            Set-BtRegValue -Path 'HKCU:\System\GameConfigStore' -Name 'GameDVR_HonorUserFSEBehaviorMode'     -Value 1 -Type DWord
            Set-BtRegValue -Path 'HKCU:\System\GameConfigStore' -Name 'GameDVR_DXGIHonorFSEWindowsCompatible' -Value 1 -Type DWord
            Set-BtRegValue -Path 'HKCU:\System\GameConfigStore' -Name 'GameDVR_EFSEFeatureFlags'             -Value 0 -Type DWord
        }
        Revert = {
            Remove-BtRegValue -Path 'HKCU:\System\GameConfigStore' -Name 'GameDVR_FSEBehaviorMode'
            Remove-BtRegValue -Path 'HKCU:\System\GameConfigStore' -Name 'GameDVR_HonorUserFSEBehaviorMode'
            Remove-BtRegValue -Path 'HKCU:\System\GameConfigStore' -Name 'GameDVR_DXGIHonorFSEWindowsCompatible'
            Remove-BtRegValue -Path 'HKCU:\System\GameConfigStore' -Name 'GameDVR_EFSEFeatureFlags'
        }
        Check = { (Get-BtRegValue -Path 'HKCU:\System\GameConfigStore' -Name 'GameDVR_FSEBehaviorMode') -eq 2 }
    }
    @{
        Id = 'game_mode'; Category = 'GPU & jeux'; Recommended = $true; Reboot = $false
        Name        = "Activer le Mode Jeu de Windows"
        Description = "Windows donne la priorite CPU/GPU au jeu au premier plan et suspend certaines taches de fond."
        BackupKeys  = @('HKCU\Software\Microsoft\GameBar')
        Apply = {
            Set-BtRegValue -Path 'HKCU:\Software\Microsoft\GameBar' -Name 'AutoGameModeEnabled' -Value 1 -Type DWord
            Set-BtRegValue -Path 'HKCU:\Software\Microsoft\GameBar' -Name 'AllowAutoGameMode'   -Value 1 -Type DWord
        }
        Revert = {
            Remove-BtRegValue -Path 'HKCU:\Software\Microsoft\GameBar' -Name 'AutoGameModeEnabled'
            Remove-BtRegValue -Path 'HKCU:\Software\Microsoft\GameBar' -Name 'AllowAutoGameMode'
        }
        Check = { (Get-BtRegValue -Path 'HKCU:\Software\Microsoft\GameBar' -Name 'AutoGameModeEnabled') -eq 1 }
    }
    @{
        Id = 'gamedvr_off'; Category = 'GPU & jeux'; Recommended = $true; Reboot = $false
        Name        = "Desactiver Game DVR / captures Xbox Game Bar"
        Description = "Supprime l'enregistrement d'ecran en arriere-plan (source classique de stutter et de latence)."
        BackupKeys  = @('HKCU\System\GameConfigStore',
                        'HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR',
                        'HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR')
        Apply = {
            Set-BtRegValue -Path 'HKCU:\System\GameConfigStore' -Name 'GameDVR_Enabled' -Value 0 -Type DWord
            Set-BtRegValue -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\GameDVR' -Name 'AppCaptureEnabled' -Value 0 -Type DWord
            Set-BtRegValue -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\GameDVR' -Name 'AllowGameDVR' -Value 0 -Type DWord
        }
        Revert = {
            Set-BtRegValue -Path 'HKCU:\System\GameConfigStore' -Name 'GameDVR_Enabled' -Value 1 -Type DWord
            Set-BtRegValue -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\GameDVR' -Name 'AppCaptureEnabled' -Value 1 -Type DWord
            Remove-BtRegValue -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\GameDVR' -Name 'AllowGameDVR'
        }
        Check = { (Get-BtRegValue -Path 'HKCU:\System\GameConfigStore' -Name 'GameDVR_Enabled') -eq 0 }
    }
    @{
        Id = 'sysresp'; Category = 'Systeme & planificateur'; Recommended = $true; Reboot = $false
        Name        = "SystemResponsiveness = 10 (priorite aux applications, pas au multimedia de fond)"
        Description = "Reserve moins de CPU aux taches multimedia de fond (20 % par defaut, 10 % ici)."
        BackupKeys  = @('HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile')
        Apply  = { Set-BtRegValue -Path $MMKey -Name 'SystemResponsiveness' -Value 10 -Type DWord }
        Revert = { Set-BtRegValue -Path $MMKey -Name 'SystemResponsiveness' -Value 20 -Type DWord }
        Check  = { (Get-BtRegValue -Path $MMKey -Name 'SystemResponsiveness') -eq 10 }
    }
    @{
        Id = 'w32ps'; Category = 'Systeme & planificateur'; Recommended = $false; Reboot = $false
        Name        = "Win32PrioritySeparation = 38 (quanta courts, priorite premier plan)"
        Description = "Le planificateur CPU reagit plus vite pour l'application au premier plan. Valeur prisee des joueurs (defaut Windows : 2)."
        BackupKeys  = @('HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl')
        Apply  = { Set-BtRegValue -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\PriorityControl' -Name 'Win32PrioritySeparation' -Value 38 -Type DWord }
        Revert = { Set-BtRegValue -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\PriorityControl' -Name 'Win32PrioritySeparation' -Value 2  -Type DWord }
        Check  = { (Get-BtRegValue -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\PriorityControl' -Name 'Win32PrioritySeparation') -eq 38 }
    }
    @{
        Id = 'games_task'; Category = 'Systeme & planificateur'; Recommended = $false; Reboot = $false
        Name        = "Profil MMCSS 'Games' : priorite CPU/GPU haute pour les jeux"
        Description = "Les jeux qui utilisent le profil multimedia Games obtiennent une priorite d'ordonnancement plus elevee."
        BackupKeys  = @('HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile')
        Apply = {
            Set-BtRegValue -Path $GamesKey -Name 'GPU Priority'        -Value 8 -Type DWord
            Set-BtRegValue -Path $GamesKey -Name 'Priority'            -Value 6 -Type DWord
            Set-BtRegValue -Path $GamesKey -Name 'Scheduling Category' -Value 'High' -Type String
            Set-BtRegValue -Path $GamesKey -Name 'SFIO Priority'       -Value 'High' -Type String
        }
        Revert = {
            Set-BtRegValue -Path $GamesKey -Name 'GPU Priority'        -Value 8 -Type DWord
            Set-BtRegValue -Path $GamesKey -Name 'Priority'            -Value 2 -Type DWord
            Set-BtRegValue -Path $GamesKey -Name 'Scheduling Category' -Value 'Medium' -Type String
            Set-BtRegValue -Path $GamesKey -Name 'SFIO Priority'       -Value 'Normal' -Type String
        }
        Check = { (Get-BtRegValue -Path $GamesKey -Name 'Scheduling Category') -eq 'High' }
    }
    @{
        Id = 'netthrottle'; Category = 'Systeme & planificateur'; Recommended = $false; Reboot = $false
        Name        = "Desactiver le NetworkThrottlingIndex (bridage reseau multimedia)"
        Description = "Supprime la limite de paquets reseau traites par milliseconde pendant la lecture multimedia."
        BackupKeys  = @('HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile')
        Apply  = { Set-BtRegValue -Path $MMKey -Name 'NetworkThrottlingIndex' -Value -1 -Type DWord }
        Revert = { Set-BtRegValue -Path $MMKey -Name 'NetworkThrottlingIndex' -Value 10 -Type DWord }
        Check  = { (Get-BtRegValue -Path $MMKey -Name 'NetworkThrottlingIndex') -eq -1 }
    }
    @{
        Id = 'dynamic_tick'; Category = 'Systeme & planificateur'; Recommended = $false; Reboot = $true
        Name        = "Desactiver le tick dynamique du noyau (bcdedit) - EXPERIMENTAL"
        Description = "Timer noyau a cadence fixe : peut lisser la latence sur certaines machines, augmente la consommation. A tester, reversible."
        BackupKeys  = @()
        Apply  = { [void](Invoke-BtNative 'bcdedit' @('/set', 'disabledynamictick', 'yes') "bcdedit disabledynamictick") }
        Revert = { [void](Invoke-BtNative 'bcdedit' @('/deletevalue', 'disabledynamictick') "bcdedit deletevalue") }
        Check  = { $null }
    }
    @{
        Id = 'nagle'; Category = 'Reseau (optionnel)'; Recommended = $false; Reboot = $false
        Name        = "Desactiver l'algorithme de Nagle (TcpAckFrequency / TCPNoDelay)"
        Description = "Envoi TCP immediat sans regroupement de paquets : utile pour les jeux en ligne TCP. Sans effet sur les jeux en UDP."
        BackupKeys  = @('HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces')
        Apply  = { Set-BtNagle -Disable $true }
        Revert = { Set-BtNagle -Disable $false }
        Check  = { $null }
    }
    @{
        Id = 'menu_delay'; Category = 'Confort (optionnel)'; Recommended = $false; Reboot = $false
        Name        = "Menus instantanes (MenuShowDelay = 0)"
        Description = "Ne change rien en jeu, mais l'interface Windows repond immediatement. Prend effet a la prochaine session."
        BackupKeys  = @('HKCU\Control Panel\Desktop')
        Apply  = { Set-BtRegValue -Path 'HKCU:\Control Panel\Desktop' -Name 'MenuShowDelay' -Value '0'   -Type String }
        Revert = { Set-BtRegValue -Path 'HKCU:\Control Panel\Desktop' -Name 'MenuShowDelay' -Value '400' -Type String }
        Check  = { (Get-BtRegValue -Path 'HKCU:\Control Panel\Desktop' -Name 'MenuShowDelay') -eq '0' }
    }
    @{
        Id = 'transparency'; Category = 'Confort (optionnel)'; Recommended = $false; Reboot = $false
        Name        = "Desactiver la transparence de l'interface"
        Description = "Moins de travail de composition pour le GPU sur le bureau. Effet purement cosmetique en jeu."
        BackupKeys  = @('HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize')
        Apply  = { Set-BtRegValue -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize' -Name 'EnableTransparency' -Value 0 -Type DWord }
        Revert = { Set-BtRegValue -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize' -Name 'EnableTransparency' -Value 1 -Type DWord }
        Check  = { (Get-BtRegValue -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize' -Name 'EnableTransparency') -eq 0 }
    }
)

$script:Categories = @(
    'Souris & clavier', 'Alimentation & CPU', 'GPU & jeux',
    'Systeme & planificateur', 'Reseau (optionnel)', 'Confort (optionnel)'
)

# --- Moteur d'application (partage IHM + console) -----------------------------
function Invoke-BtOperation {
    param([array]$Tweaks, [ValidateSet('Apply', 'Revert')][string]$Mode)
    if ($script:GuiAvailable -and $script:Form) {
        $script:Form.Cursor = [System.Windows.Forms.Cursors]::WaitCursor
    }
    $verb = 'Applique'
    if ($Mode -eq 'Revert') { $verb = 'Retabli' }
    try {
        if ($Mode -eq 'Apply' -and $script:DoBackup) {
            $dir = Backup-BtRegistry -Tweaks $Tweaks
            Write-BtLog ("Sauvegarde du registre creee : {0}" -f $dir) 'OK'
        }
        if ($Mode -eq 'Apply' -and $script:DoRestore) {
            New-BtRestorePoint
        }
        $ok = 0; $ko = 0; $rebootNeeded = $false
        # Retablir dans l'ordre inverse de l'application (ex : reactiver le plan par
        # defaut APRES avoir defait les reglages dependants du plan, comme l'USB).
        $ordered = @($Tweaks)
        if ($Mode -eq 'Revert' -and $ordered.Count -gt 1) {
            $ordered = $ordered[($ordered.Count - 1)..0]
        }
        foreach ($tweak in $ordered) {
            try {
                if ($Mode -eq 'Apply') { & $tweak.Apply } else { & $tweak.Revert }
                $ok++
                if ($tweak.Reboot) { $rebootNeeded = $true }
                Write-BtLog ("{0} : {1}" -f $verb, $tweak.Name) 'OK'
            } catch {
                $ko++
                Write-BtLog ("ECHEC : {0} -> {1}" -f $tweak.Name, $_.Exception.Message) 'ERREUR'
            }
        }
        if ($script:GuiAvailable) { Update-BtStates }
        Write-BtLog ("Termine : {0} reussite(s), {1} echec(s)." -f $ok, $ko)
        if ($rebootNeeded) {
            Write-BtLog "Un redemarrage est necessaire pour certaines optimisations." 'WARN'
            if ($script:GuiAvailable -and $script:Form) {
                [void][System.Windows.Forms.MessageBox]::Show($script:Form,
                    "Certaines modifications necessitent un redemarrage pour prendre effet.",
                    'Redemarrage conseille', 'OK', 'Information')
            }
        }
    } catch {
        # Erreur pendant la preparation (sauvegarde / point de restauration) : on la
        # journalise au lieu de laisser l'exception remonter et casser le programme.
        Write-BtLog ("ECHEC de la preparation : {0} - operation interrompue." -f $_.Exception.Message) 'ERREUR'
        if ($script:GuiAvailable -and $script:Form) {
            [void][System.Windows.Forms.MessageBox]::Show($script:Form,
                ("Une erreur est survenue avant l'application :`n`n{0}" -f $_.Exception.Message),
                $script:AppName, 'OK', 'Error')
        }
    } finally {
        if ($script:GuiAvailable -and $script:Form) {
            $script:Form.Cursor = [System.Windows.Forms.Cursors]::Default
        }
    }
}

# ==============================================================================
#  MODE GRAPHIQUE
# ==============================================================================
function Update-BtStates {
    if (-not $script:BoxList) { return }
    foreach ($cb in $script:BoxList) {
        $tweak = $cb.Tag
        $state = $null
        if ($tweak.Check) {
            try { $state = & $tweak.Check } catch { $state = $null }
        }
        if ($state -eq $true) {
            $cb.ForeColor = [System.Drawing.Color]::FromArgb(0, 130, 0)
            $cb.Text      = $script:BaseText[$tweak.Id] + '   [deja actif]'
        } else {
            $cb.ForeColor = [System.Drawing.SystemColors]::ControlText
            $cb.Text      = $script:BaseText[$tweak.Id]
        }
    }
}

function Get-BtGuiSelection {
    $selected = @()
    foreach ($cb in $script:BoxList) {
        if ($cb.Checked) { $selected += $cb.Tag }
    }
    return ,$selected
}

function Show-BtGui {
    trap {
        try {
            [void][System.Windows.Forms.MessageBox]::Show(
                ("{0} a rencontre une erreur et va se fermer :`n`n{1}" -f $script:AppName, $_.Exception.Message),
                "$script:AppName - erreur", 'OK', 'Error')
        } catch { }
        break
    }

    $script:Form = New-Object System.Windows.Forms.Form
    $script:Form.Text            = "$script:AppName $script:Version - Input lag minimal (Windows 10/11)"
    $script:Form.ClientSize      = New-Object System.Drawing.Size(764, 760)
    $script:Form.StartPosition   = 'CenterScreen'
    $script:Form.FormBorderStyle = 'FixedSingle'
    $script:Form.MaximizeBox     = $false
    $script:Form.Font            = New-Object System.Drawing.Font('Segoe UI', 9)
    $script:Form.BackColor       = [System.Drawing.Color]::FromArgb(245, 246, 248)

    $lblTitle = New-Object System.Windows.Forms.Label
    $lblTitle.Text     = "$script:AppName - reduction de l'input lag"
    $lblTitle.Location = New-Object System.Drawing.Point(16, 10)
    $lblTitle.Size     = New-Object System.Drawing.Size(730, 30)
    $lblTitle.Font     = New-Object System.Drawing.Font('Segoe UI Semibold', 14)

    $lblSub = New-Object System.Windows.Forms.Label
    $lblSub.Text      = 'Cochez les optimisations voulues puis cliquez sur Appliquer. Rien n''est modifie avant. Survolez une ligne pour les details.'
    $lblSub.Location  = New-Object System.Drawing.Point(18, 40)
    $lblSub.Size      = New-Object System.Drawing.Size(730, 18)
    $lblSub.ForeColor = [System.Drawing.Color]::FromArgb(90, 95, 105)

    $btnReco = New-Object System.Windows.Forms.Button
    $btnReco.Text     = 'Cocher les recommandes'
    $btnReco.Location = New-Object System.Drawing.Point(16, 64)
    $btnReco.Size     = New-Object System.Drawing.Size(170, 28)

    $btnAll = New-Object System.Windows.Forms.Button
    $btnAll.Text     = 'Tout cocher'
    $btnAll.Location = New-Object System.Drawing.Point(192, 64)
    $btnAll.Size     = New-Object System.Drawing.Size(110, 28)

    $btnNone = New-Object System.Windows.Forms.Button
    $btnNone.Text     = 'Tout decocher'
    $btnNone.Location = New-Object System.Drawing.Point(308, 64)
    $btnNone.Size     = New-Object System.Drawing.Size(110, 28)

    $panel = New-Object System.Windows.Forms.Panel
    $panel.Location    = New-Object System.Drawing.Point(16, 100)
    $panel.Size        = New-Object System.Drawing.Size(732, 402)
    $panel.AutoScroll  = $true
    $panel.BackColor   = [System.Drawing.Color]::White
    $panel.BorderStyle = 'FixedSingle'

    $toolTip = New-Object System.Windows.Forms.ToolTip
    $toolTip.AutoPopDelay = 20000
    $toolTip.InitialDelay = 400

    $script:BoxList  = @()
    $script:BaseText = @{}
    $yPos = 8
    foreach ($category in $script:Categories) {
        $items = @($script:Tweaks | Where-Object { $_.Category -eq $category })
        if ($items.Count -eq 0) { continue }

        $groupBox = New-Object System.Windows.Forms.GroupBox
        $groupBox.Text      = $category
        $groupBox.Location  = New-Object System.Drawing.Point(8, $yPos)
        $groupBox.Size      = New-Object System.Drawing.Size(692, (30 + $items.Count * 24))
        $groupBox.Font      = New-Object System.Drawing.Font('Segoe UI Semibold', 9)
        $groupBox.ForeColor = [System.Drawing.Color]::FromArgb(50, 70, 130)

        $cbIndex = 0
        foreach ($tweak in $items) {
            $text = $tweak.Name
            if ($tweak.Reboot) { $text += '  (redemarrage requis)' }

            $checkBox = New-Object System.Windows.Forms.CheckBox
            $checkBox.Text      = $text
            $checkBox.Location  = New-Object System.Drawing.Point(12, (20 + $cbIndex * 24))
            $checkBox.Size      = New-Object System.Drawing.Size(668, 22)
            $checkBox.Font      = New-Object System.Drawing.Font('Segoe UI', 9)
            $checkBox.ForeColor = [System.Drawing.SystemColors]::ControlText
            $checkBox.Tag       = $tweak
            $checkBox.Checked   = [bool]$tweak.Recommended
            $toolTip.SetToolTip($checkBox, $tweak.Description)

            $script:BaseText[$tweak.Id] = $text
            $script:BoxList += $checkBox
            $groupBox.Controls.Add($checkBox)
            $cbIndex++
        }
        $panel.Controls.Add($groupBox)
        $yPos += $groupBox.Height + 8
    }

    $script:ChkBackup = New-Object System.Windows.Forms.CheckBox
    $script:ChkBackup.Text     = 'Sauvegarder le registre avant modification (.reg sur le Bureau)'
    $script:ChkBackup.Location = New-Object System.Drawing.Point(16, 510)
    $script:ChkBackup.Size     = New-Object System.Drawing.Size(390, 22)
    $script:ChkBackup.Checked  = $true

    $script:ChkRestorePoint = New-Object System.Windows.Forms.CheckBox
    $script:ChkRestorePoint.Text     = 'Creer un point de restauration systeme'
    $script:ChkRestorePoint.Location = New-Object System.Drawing.Point(420, 510)
    $script:ChkRestorePoint.Size     = New-Object System.Drawing.Size(328, 22)
    $script:ChkRestorePoint.Checked  = $true

    $btnApply = New-Object System.Windows.Forms.Button
    $btnApply.Text      = 'APPLIQUER LA SELECTION'
    $btnApply.Location  = New-Object System.Drawing.Point(16, 540)
    $btnApply.Size      = New-Object System.Drawing.Size(300, 38)
    $btnApply.Font      = New-Object System.Drawing.Font('Segoe UI Semibold', 10)
    $btnApply.BackColor = [System.Drawing.Color]::FromArgb(0, 122, 60)
    $btnApply.ForeColor = [System.Drawing.Color]::White
    $btnApply.FlatStyle = 'Flat'

    $btnRevert = New-Object System.Windows.Forms.Button
    $btnRevert.Text      = 'Retablir Windows (selection)'
    $btnRevert.Location  = New-Object System.Drawing.Point(322, 540)
    $btnRevert.Size      = New-Object System.Drawing.Size(250, 38)
    $btnRevert.Font      = New-Object System.Drawing.Font('Segoe UI', 9.5)
    $btnRevert.BackColor = [System.Drawing.Color]::FromArgb(230, 232, 236)
    $btnRevert.FlatStyle = 'Flat'

    $btnRestore = New-Object System.Windows.Forms.Button
    $btnRestore.Text      = 'Restaurer une sauvegarde...'
    $btnRestore.Location  = New-Object System.Drawing.Point(578, 540)
    $btnRestore.Size      = New-Object System.Drawing.Size(170, 38)
    $btnRestore.Font      = New-Object System.Drawing.Font('Segoe UI', 9)
    $btnRestore.BackColor = [System.Drawing.Color]::FromArgb(230, 232, 236)
    $btnRestore.FlatStyle = 'Flat'

    $script:LogBox = New-Object System.Windows.Forms.TextBox
    $script:LogBox.Location   = New-Object System.Drawing.Point(16, 588)
    $script:LogBox.Size       = New-Object System.Drawing.Size(732, 156)
    $script:LogBox.Multiline  = $true
    $script:LogBox.ReadOnly   = $true
    $script:LogBox.ScrollBars = 'Vertical'
    $script:LogBox.BackColor  = [System.Drawing.Color]::White
    $script:LogBox.Font       = New-Object System.Drawing.Font('Consolas', 8.5)

    $btnReco.Add_Click({ foreach ($cb in $script:BoxList) { $cb.Checked = [bool]$cb.Tag.Recommended } })
    $btnAll.Add_Click({ foreach ($cb in $script:BoxList) { $cb.Checked = $true } })
    $btnNone.Add_Click({ foreach ($cb in $script:BoxList) { $cb.Checked = $false } })

    $btnApply.Add_Click({
        $selected = Get-BtGuiSelection
        if ($selected.Count -eq 0) {
            [void][System.Windows.Forms.MessageBox]::Show($script:Form, 'Aucune optimisation cochee.', $script:AppName, 'OK', 'Information')
            return
        }
        $message = "Appliquer {0} optimisation(s) ?" -f $selected.Count
        if ($script:ChkBackup.Checked) { $message += "`n`nUne sauvegarde du registre sera creee sur le Bureau." }
        if ([System.Windows.Forms.MessageBox]::Show($script:Form, $message, 'Confirmation', 'YesNo', 'Question') -eq 'Yes') {
            $script:DoBackup  = $script:ChkBackup.Checked
            $script:DoRestore = $script:ChkRestorePoint.Checked
            Invoke-BtOperation -Tweaks $selected -Mode 'Apply'
        }
    })

    $btnRevert.Add_Click({
        $selected = Get-BtGuiSelection
        if ($selected.Count -eq 0) {
            [void][System.Windows.Forms.MessageBox]::Show($script:Form, 'Cochez les optimisations a retablir.', $script:AppName, 'OK', 'Information')
            return
        }
        $message = "Remettre les valeurs par defaut de Windows pour {0} reglage(s) coche(s) ?" -f $selected.Count
        if ([System.Windows.Forms.MessageBox]::Show($script:Form, $message, 'Retablir', 'YesNo', 'Warning') -eq 'Yes') {
            $script:DoBackup  = $false
            $script:DoRestore = $false
            Invoke-BtOperation -Tweaks $selected -Mode 'Revert'
        }
    })

    $btnRestore.Add_Click({
        $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
        $dlg.Description  = 'Choisissez un dossier de sauvegarde dtg-optimizer-backup-...'
        $dlg.SelectedPath = $script:BackupDesktop
        if ($dlg.ShowDialog() -eq 'OK') {
            if ([System.Windows.Forms.MessageBox]::Show($script:Form,
                    ("Reimporter toutes les cles .reg depuis :`n{0} ?" -f $dlg.SelectedPath),
                    'Restaurer une sauvegarde', 'YesNo', 'Warning') -eq 'Yes') {
                Import-BtBackup -Folder $dlg.SelectedPath
                Update-BtStates
            }
        }
    })

    $script:Form.Controls.AddRange(@(
        $lblTitle, $lblSub, $btnReco, $btnAll, $btnNone, $panel,
        $script:ChkBackup, $script:ChkRestorePoint, $btnApply, $btnRevert, $btnRestore, $script:LogBox
    ))

    $osName = 'Windows'
    try { $osName = (Get-CimInstance -ClassName Win32_OperatingSystem).Caption } catch { }
    Write-BtLog ("Systeme : {0}" -f $osName)
    Write-BtLog "Pret. Aucune modification n'est faite avant de cliquer sur APPLIQUER."
    Update-BtStates

    [void]$script:Form.ShowDialog()
}

# ==============================================================================
#  MODE CONSOLE (repli, compatible ConstrainedLanguage)
# ==============================================================================
function Read-BtYesNo {
    param([string]$Prompt)
    $a = (Read-Host ($Prompt + ' (o/n)')).Trim().ToLower()
    return ($a -eq 'o' -or $a -eq 'oui' -or $a -eq 'y')
}

function Show-BtConsoleMenu {
    param([hashtable]$Checked, [bool]$DoBackup, [bool]$DoRestore, [hashtable]$IndexMap)
    Clear-Host
    Write-Host ""
    Write-Host ("  ===== {0} {1} - reduction de l'input lag =====" -f $script:AppName, $script:Version) -ForegroundColor Cyan
    Write-Host "  Mode console (interface graphique indisponible)." -ForegroundColor DarkGray
    Write-Host "  Aucune modification tant que vous ne lancez pas [G]." -ForegroundColor DarkGray
    $i = 0
    foreach ($category in $script:Categories) {
        $items = @($script:Tweaks | Where-Object { $_.Category -eq $category })
        if ($items.Count -eq 0) { continue }
        Write-Host ""
        Write-Host ("  -- {0} --" -f $category) -ForegroundColor Cyan
        foreach ($tweak in $items) {
            $i++
            $IndexMap[$i] = $tweak
            $mark = '[ ]'
            if ($Checked[$tweak.Id]) { $mark = '[X]' }
            $suffix = ''
            if ($tweak.Reboot) { $suffix = ' *redemarrage*' }
            $active = ''
            if ($tweak.Check) {
                try { if (& $tweak.Check) { $active = ' (deja actif)' } } catch { }
            }
            $lineColor = 'Gray'
            if ($Checked[$tweak.Id]) { $lineColor = 'White' }
            Write-Host ("   {0,2}. {1} {2}{3}" -f $i, $mark, $tweak.Name, $suffix) -ForegroundColor $lineColor -NoNewline
            if ($active) { Write-Host $active -ForegroundColor Green } else { Write-Host "" }
        }
    }
    Write-Host ""
    $bkp = 'NON'; if ($DoBackup)  { $bkp = 'OUI' }
    $rst = 'NON'; if ($DoRestore) { $rst = 'OUI' }
    Write-Host ("  Sauvegarde .reg : {0}    Point de restauration : {1}" -f $bkp, $rst) -ForegroundColor DarkGray
    Write-Host "  [n] cocher/decocher   [R] recommandes   [A] tout   [N] rien" -ForegroundColor DarkGray
    Write-Host "  [B] sauvegarde on/off   [P] point on/off" -ForegroundColor DarkGray
    Write-Host "  [G] APPLIQUER   [U] retablir Windows   [I] importer une sauvegarde   [Q] quitter" -ForegroundColor DarkGray
    Write-Host ""
}

function Get-BtConsoleSelection {
    param([hashtable]$Checked)
    $sel = @()
    foreach ($tweak in $script:Tweaks) {
        if ($Checked[$tweak.Id]) { $sel += $tweak }
    }
    return ,$sel
}

function Start-BtConsole {
    if ($script:LanguageMode -ne 'FullLanguage') {
        Write-Host ""
        Write-Host "  Info : PowerShell est en mode '$($script:LanguageMode)' (verrou de securite)." -ForegroundColor Yellow
        Write-Host "  L'interface graphique est indisponible ; utilisation du menu console." -ForegroundColor Yellow
        if (-not $script:SpiAvailable) {
            Write-Host "  (L'effet 'souris' en direct est desactive ; il s'appliquera a la prochaine ouverture de session.)" -ForegroundColor DarkYellow
        }
        Start-Sleep -Seconds 2
    }

    $checked = @{}
    foreach ($tweak in $script:Tweaks) { $checked[$tweak.Id] = [bool]$tweak.Recommended }
    $doBackup  = $true
    $doRestore = $true

    while ($true) {
        $indexMap = @{}
        Show-BtConsoleMenu -Checked $checked -DoBackup $doBackup -DoRestore $doRestore -IndexMap $indexMap
        $ans = (Read-Host "  Choix").Trim()
        if ($ans -match '^\d+$') {
            $n = [int]$ans
            if ($indexMap.ContainsKey($n)) {
                $id = $indexMap[$n].Id
                $checked[$id] = -not $checked[$id]
            }
            continue
        }
        switch ($ans.ToUpper()) {
            'R' { foreach ($tweak in $script:Tweaks) { $checked[$tweak.Id] = [bool]$tweak.Recommended } }
            'A' { foreach ($tweak in $script:Tweaks) { $checked[$tweak.Id] = $true } }
            'N' { foreach ($tweak in $script:Tweaks) { $checked[$tweak.Id] = $false } }
            'B' { $doBackup  = -not $doBackup }
            'P' { $doRestore = -not $doRestore }
            'G' {
                $sel = Get-BtConsoleSelection -Checked $checked
                if ($sel.Count -eq 0) { Write-Host "  Aucune optimisation cochee." -ForegroundColor Yellow; Start-Sleep -Seconds 2; continue }
                Write-Host ""
                if (Read-BtYesNo ("  Appliquer {0} optimisation(s) ?" -f $sel.Count)) {
                    $script:DoBackup  = $doBackup
                    $script:DoRestore = $doRestore
                    Invoke-BtOperation -Tweaks $sel -Mode 'Apply'
                    [void](Read-Host "`n  Entree pour continuer")
                }
            }
            'U' {
                $sel = Get-BtConsoleSelection -Checked $checked
                if ($sel.Count -eq 0) { Write-Host "  Cochez les reglages a retablir." -ForegroundColor Yellow; Start-Sleep -Seconds 2; continue }
                Write-Host ""
                if (Read-BtYesNo ("  Remettre les valeurs Windows pour {0} reglage(s) ?" -f $sel.Count)) {
                    $script:DoBackup  = $false
                    $script:DoRestore = $false
                    Invoke-BtOperation -Tweaks $sel -Mode 'Revert'
                    [void](Read-Host "`n  Entree pour continuer")
                }
            }
            'I' {
                Write-Host ""
                $folder = (Read-Host "  Chemin du dossier de sauvegarde (dtg-optimizer-backup-...)").Trim('"').Trim()
                if ($folder) {
                    Import-BtBackup -Folder $folder
                    [void](Read-Host "`n  Entree pour continuer")
                }
            }
            'Q' { return }
            default { }
        }
    }
}

# ==============================================================================
#  LANCEMENT
# ==============================================================================
if ($script:GuiAvailable) {
    Show-BtGui
} else {
    Start-BtConsole
}
