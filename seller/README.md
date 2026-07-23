# Dossier VENDEUR — ne pas livrer aux clients

Ce dossier contient les outils de commercialisation de Fluide. **Rien ici ne doit
être inclus dans le paquet distribué aux clients**, en particulier `private.xml`.

## 1. Générer une clé de licence Pro

```
cd seller
dotnet run -- "Nom du client"          →  licence À VIE (127 €)
dotnet run -- "Nom du client" 365      →  ABONNEMENT annuel (49 €/an, expire dans N jours)
```

La commande affiche une **clé** à envoyer à l'acheteur. Le client la colle dans
l'app (menu ☰ → « Activer la version Pro »). La clé encode le nom du client
(+ la date d'expiration pour un abonnement, incluse dans la partie signée donc
infalsifiable) + une signature RSA-2048 ; l'app la vérifie avec la clé **publique**
embarquée. Les clés sans date (générées avant l'abonnement) restent valides à vie.

**Renouvellement** : à chaque échéance annuelle (email Gumroad), génère une nouvelle
clé `365` et envoie-la au client — l'ancienne expire d'elle-même.

- `private.xml` = clé **privée** : elle seule permet de créer des clés valides.
  **Garde-la secrète et sauvegardée.** Si elle fuite, n'importe qui peut générer des
  licences. Si tu la perds, tu ne peux plus émettre de clés (il faudrait regénérer une
  paire et rebâtir l'app avec la nouvelle clé publique).
- Ce n'est pas un DRM incassable (aucun DRM hors-ligne ne l'est) : c'est un contrôle
  de licence standard, suffisant pour un produit indépendant.

## 2. Signer le binaire (indispensable pour vendre)

Sans signature, Windows SmartScreen / Smart App Control bloqueront l'app chez beaucoup
de clients. Il te faut un **certificat de signature de code** (Sectigo, DigiCert…),
idéalement **EV** pour une réputation immédiate.

```
powershell -ExecutionPolicy Bypass -File .\sign.ps1 -Pfx "C:\cert.pfx" -Password "..."
```

Test local seulement (auto-signé, non reconnu ailleurs) : `.\sign.ps1 -SelfSignedTest`.

## 3. Construire l'installeur

1. À la racine du projet : lance **`Build.bat`** (produit `dist\`).
2. (Optionnel) signe `dist\BTOptimizer.exe` et `.dll` (étape 2).
3. Ouvre `installer\BTOptimizer.iss` dans **Inno Setup 6+** et compile
   (ou `ISCC.exe installer\BTOptimizer.iss`). Renseigne d'abord `AppPublisher` et
   `AppURL` dans le `.iss`.
4. L'installeur final est dans `installer\Output\`.

## 4. Checklist avant mise en vente

- [ ] EULA relue par un juriste ; entité (auto-entreprise/société) qui porte la responsabilité.
- [ ] Certificat de signature de code obtenu, exe + dll signés.
- [ ] Droits de redistribution vérifiés pour les outils tiers bundlés (nvidiaProfileInspector, etc.).
- [ ] Édition gratuite testée (fonctions Pro bien verrouillées) + clé Pro testée.
- [ ] Argumentaire basé sur des gains **mesurés** (l'app mesure la latence avant/après).
- [ ] Pas de fausses promesses ni de messages alarmistes.

## Split Gratuit / Pro (actuel)

- **Gratuit** : les 173 optimisations en manuel, preset Recommandé, toute la suite de
  diagnostic (bilan Santé /100, assistant « J'ai un problème… », crashs/stabilité, réseau,
  disque, mesure FPS/latence, benchmark, rapport HTML), sauvegarde, point de restauration,
  réinitialisation.
- **Pro** : bouton ⚡ TOUT OPTIMISER (auto-tune matériel), presets eSport/Benchmark, MODE JEU
  auto, overclock GPU + profil pilote NVIDIA, gardien de démarrage & surveillance en fond,
  DNS rapide & réglages réseau avancés.

Pour changer ce partage : voir les appels `RequirePro(...)` dans `src\MainForm.cs`.
