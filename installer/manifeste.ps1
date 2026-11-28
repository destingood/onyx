# Écrit « onyx-derniere.ini », le manifeste lu par l'installateur en ligne (ONYX-Web.iss).
# À lancer après la compilation de l'installateur complet, puis à joindre à la release
# avec ONYX-Installation.exe.
#
#   powershell -ExecutionPolicy Bypass -File installer\manifeste.ps1 -Installateur installer\Output\ONYX-Setup-16.14.0.0.exe
#
# -Etiquette : étiquette git de la release (par défaut « v16.14 », tirée du nom du fichier).
# -Sortie    : fichier à écrire (par défaut onyx-derniere.ini, à côté de l'installateur).
param(
    [Parameter(Mandatory = $true)][string]$Installateur,
    [string]$Etiquette = '',
    [string]$Sortie = ''
)
$ErrorActionPreference = 'Stop'

$f = Get-Item -LiteralPath $Installateur
if ($f.Name -notmatch '^ONYX-Setup-(\d+)\.(\d+)\.(\d+)\.(\d+)\.exe$') {
    throw "Nom inattendu : $($f.Name) (attendu : ONYX-Setup-16.14.0.0.exe)."
}
$version = '{0}.{1}.{2}.{3}' -f $Matches[1], $Matches[2], $Matches[3], $Matches[4]
if (-not $Etiquette) { $Etiquette = 'v{0}.{1:00}' -f [int]$Matches[1], [int]$Matches[2] }
if ($Etiquette -notmatch '^[A-Za-z0-9.-]{1,30}$') { throw "Étiquette refusée : $Etiquette" }
if ($f.Length -le 1MB) { throw "Fichier trop petit pour être l'installateur complet : $($f.Length) octets." }

$sha = (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
if (-not $Sortie) { $Sortie = Join-Path $f.DirectoryName 'onyx-derniere.ini' }

$lignes = @(
    '[ONYX]'
    "Version=$version"
    "Etiquette=$Etiquette"
    "Fichier=$($f.Name)"
    "Taille=$($f.Length)"
    "Sha256=$sha"
)
[System.IO.File]::WriteAllLines($Sortie, $lignes, [System.Text.Encoding]::ASCII)
Write-Output "Manifeste écrit : $Sortie"
$lignes | Write-Output
