# Dossier VENDEUR — ne pas livrer aux utilisateurs

Ce dossier contient les outils de commercialisation de ONYX. **Rien ici ne doit
être inclus dans le paquet distribué aux utilisateurs**.

ONYX est gratuit : il n'y a plus de clé de licence ni de générateur de clés. Si un ancien
`private.xml` traîne encore dans ce dossier, il ne sert plus à rien — supprime-le, il reste
ignoré par git.

## 1. Signer le binaire

Sans signature, Windows SmartScreen / Smart App Control bloqueront l'app chez beaucoup
d'utilisateurs. Il te faut un **certificat de signature de code** (Sectigo, DigiCert…),
idéalement **EV** pour une réputation immédiate.

```
powershell -ExecutionPolicy Bypass -File .\sign.ps1 -Pfx "C:\cert.pfx" -Password "..."
```

Test local seulement (auto-signé, non reconnu ailleurs) : `.\sign.ps1 -SelfSignedTest`.

## 2. Construire l'installeur

1. À la racine du projet : lance **`Build.bat`** (produit `dist\`).
2. (Optionnel) signe `dist\BTOptimizer.exe` et `.dll` (étape 1).
3. Ouvre `installer\BTOptimizer.iss` dans **Inno Setup 6+** et compile
   (ou `ISCC.exe installer\BTOptimizer.iss`). Renseigne d'abord `AppPublisher` et
   `AppURL` dans le `.iss`.
4. L'installeur final est dans `installer\Output\`.

## 3. Checklist avant diffusion

- [ ] EULA relue par un juriste ; entité (auto-entreprise/société) qui porte la responsabilité.
- [ ] Certificat de signature de code obtenu, exe + dll signés.
- [ ] Droits de redistribution vérifiés pour les outils tiers bundlés (nvidiaProfileInspector, etc.).
- [ ] Argumentaire basé sur des gains **mesurés** (l'app mesure la latence avant/après).
- [ ] Pas de fausses promesses ni de messages alarmistes.
