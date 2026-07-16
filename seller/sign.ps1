<#
    Signature du binaire BT Optimizer.

    Signature avec un VRAI certificat (recommandé pour la vente) :
        powershell -ExecutionPolicy Bypass -File .\sign.ps1 -Pfx "C:\chemin\moncert.pfx" -Password "motdepasse"

    Signature de TEST (auto-signé, local uniquement — NE convient PAS à la distribution) :
        powershell -ExecutionPolicy Bypass -File .\sign.ps1 -SelfSignedTest

    NB : un certificat auto-signé n'est PAS reconnu par les autres PC ni par Smart App
    Control. Pour vendre, il faut un certificat de signature de code d'une autorité
    (idéalement EV pour une réputation immédiate).
#>
param(
    [string]$Pfx,
    [string]$Password,
    [switch]$SelfSignedTest,
    [string]$TimestampServer = "http://timestamp.digicert.com"
)

$ErrorActionPreference = 'Stop'
$dist = Join-Path $PSScriptRoot "..\dist"
$targets = @("BTOptimizer.exe", "BTOptimizer.dll") | ForEach-Object { Join-Path $dist $_ } | Where-Object { Test-Path $_ }
if ($targets.Count -eq 0) { Write-Error "Aucun binaire dans ..\dist. Lance Build.bat d'abord."; exit 1 }

if ($SelfSignedTest) {
    Write-Host "Certificat AUTO-SIGNE (test local uniquement)..." -ForegroundColor Yellow
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject "CN=BT Optimizer (TEST)" `
        -CertStoreLocation "Cert:\CurrentUser\My" -KeyUsage DigitalSignature -KeySpec Signature
}
elseif ($Pfx) {
    if (-not (Test-Path $Pfx)) { Write-Error "PFX introuvable : $Pfx"; exit 1 }
    $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($Pfx, $Password)
}
else {
    Write-Error "Fournis -Pfx <cert.pfx> [-Password ...] ou -SelfSignedTest."
    exit 1
}

foreach ($t in $targets) {
    $r = Set-AuthenticodeSignature -FilePath $t -Certificate $cert -TimestampServer $TimestampServer -HashAlgorithm SHA256
    Write-Host ("{0} -> {1}" -f (Split-Path $t -Leaf), $r.Status)
}
Write-Host "Terminé." -ForegroundColor Green
