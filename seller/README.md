# Dossier VENDEUR — ne pas livrer aux clients

Ce dossier contient les outils de commercialisation de ONYX. **Rien ici ne doit
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

## Ce que le générateur refuse de faire (et pourquoi)

**Il ne livre plus une clé sans l'avoir relue.** Signer ne prouve rien : ce qui compte est que
l'APPLICATION sache vérifier. Après avoir signé, le générateur revérifie la clé avec la moitié
publique lue dans `src/License.cs`. Si les deux moitiés ne correspondent pas, rien n'est émis,
rien n'est journalisé, rien n'est copié — et le bandeau du bas passe au rouge.

Ce n'est pas théorique : le commit `ab25e7c` a remplacé la clé publique de l'application par celle
d'une paire dont la moitié privée n'existe nulle part. Le générateur a continué de produire des
clés impeccablement signées et systématiquement refusées. Sept licences vendues, aucune ne
fonctionnait, et le message affiché au client parlait de copier-coller.

**Le champ « ID du PC » doit rester VIDE.** L'application lie désormais la clé toute seule au
premier PC où elle est activée. Remplir ce champ ne protège de rien de plus, et **tue la clé à la
prochaine réinstallation de Windows** (le MachineGuid est régénéré). Il ne reste que pour les deux
clés historiques déjà verrouillées — à réémettre sans verrou.

**« Réémettre »** reprend le licencié et le type d'une ligne du journal et resigne. Le nom est dans
la partie signée : le retaper à la main, c'est risquer une majuscule d'écart et livrer une licence
à un autre nom. Un abonnement est réémis pour la durée RESTANTE, pas pour un an de plus.

**Le journal est écrit avant la livraison.** Si le CSV ne peut pas s'écrire, la clé n'est ni
affichée ni copiée : une licence sans trace ne se retrouve pas, ne se réémet pas, ne se conteste pas.

> Le keygen en ligne de commande (`dotnet run --project keygen.csproj`) vérifie lui aussi la paire,
> mais **n'écrit pas au journal**. Pour une vente, utilise le générateur graphique.
