# Publier ONYX sur winget

But : les utilisateurs installent par `winget install DesTinGOOD.ONYX`, ce qui **évite
l'avertissement « fichier peu téléchargé »** du navigateur — le téléchargement passe par
le client winget, pas par Edge ou Chrome.

C'est gratuit, et winget accepte les logiciels propriétaires et payants.

## Ce que ça règle, et ce que ça ne règle pas

RÈGLE     l'avertissement de téléchargement du navigateur (celui de la capture).
NE RÈGLE PAS  l'avertissement SmartScreen au LANCEMENT de l'installeur non signé.
              Celui-là ne disparaît qu'avec un certificat de signature, ou avec le temps.

## Soumettre

1. Vérifier le manifeste (déjà fait, il passe) :

       winget validate --manifest winget\DesTinGOOD.ONYX\15.73.0.0

2. Tester l'installation localement :

       winget install --manifest winget\DesTinGOOD.ONYX\15.73.0.0

3. Ouvrir une pull request sur https://github.com/microsoft/winget-pkgs
   en copiant le dossier vers :

       manifests/d/DesTinGOOD/ONYX/15.73.0.0/

   Un robot valide automatiquement ; un humain relit ensuite.

## À chaque nouvelle version

Recopier le dossier sous le nouveau numéro, puis mettre à jour dans les trois fichiers :
`PackageVersion`, `InstallerUrl` et `InstallerSha256`.

L'empreinte se calcule ainsi :

    Get-FileHash .\installer\Output\ONYX-Setup-<version>.exe -Algorithm SHA256
