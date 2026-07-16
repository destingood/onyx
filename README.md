# BT Optimizer

Application Windows 10/11 pour réduire l'**input lag**, la **latence** et accélérer le PC, avec **cases à cocher** : chaque optimisation est optionnelle et réversible. Rien n'est modifié tant que tu ne cliques pas sur **Appliquer**.

## ⭐ L'application : `BTOptimizer.exe`

**Double-clique sur `BTOptimizer.exe`** → UAC (Oui) → la fenêtre s'ouvre.

- **Vraie application native** (fenêtre, cases, boutons) — fonctionne même sur les
  machines où PowerShell est verrouillé (comme celle-ci) car elle ne dépend pas de PowerShell.
- **46 optimisations** groupées en 7 catégories, détection *[déjà actif]* en vert :
  input lag (souris, files d'attente, timer 1 ms global, MPO, plein écran exclusif,
  flip fenêtré Win11, HAGS…), CPU/alimentation (Performances ultimes, throttling,
  ASPM PCIe, USB…), rapidité (effets visuels, délais, NTFS, Edge/Widgets/Bing en
  fond…), services (SysMain, télémétrie, indexation, WER, applis sponsorisées…),
  réseau (Nagle, RSC, veille de la carte réseau…). Y compris un **nettoyage HPET**
  qui retire les réglages néfastes laissés par d'anciens guides.
- **Presets** : `Recommandé` (sûr), `eSport` (agressif), `Tout`, `Rien`.
- **Timer Windows 1 ms** en direct + **affichage temps réel de la résolution du
  timer système** (passe en vert quand ≤ 1 ms : tu VOIS l'effet).
- **Réduction en zone de notification** : la fenêtre réduite garde le timer 1 ms
  actif pendant que tu joues (double-clic sur l'icône pour rouvrir).
- **Compteur « Actives : X / 46 »** dans l'en-tête.
- Boutons : `APPLIQUER LA SÉLECTION`, `Rétablir Windows (sélection)`,
  `Restaurer une sauvegarde…`, `Sauvegardes`, **`Rapport`** (état complet des 46
  réglages dans un .txt sur le Bureau — pratique pour comparer avant/après).
- Journal coloré, icône d'application, instance unique, infos de version.
- `Build.bat` ferme automatiquement l'app avant de recompiler.

### Mesure & analyse de latence (façon LatencyMon)

- Bouton **`Mesurer latence`** : mesure rapide 12 s (timer système, gigue réelle de
  `Sleep(1)`, % DPC/ISR, DPC/s) + comparaison automatique avec la mesure précédente,
  historisée dans `bt-optimizer-mesures.txt`. Propose en option la **capture ETW
  DPC/ISR de 30 s** (WPR + `xperf`, même méthode que les scripts du dossier `tools\`).
- Bouton **`Analyse latence`** : ouvre n'importe quel rapport `xperf dpcisr` dans une
  **vraie interface façon LatencyMon** — bannière de verdict colorée (vert/orange/rouge),
  tuiles (pire DPC, pire ISR, totaux, durée), et **tableau des pilotes triable** :
  nombre de DPC/ISR, pire latence (µs) et temps total par pilote, avec description
  lisible (NVIDIA, TCP/IP, audio HD, hyperviseur…). Les pilotes lents sont surlignés.
  La capture ETW ouvre cette fenêtre automatiquement.

Pour recompiler après modification du code source (`src/`) : double-clic sur
**`Build.bat`** (utilise le compilateur C# intégré à Windows, rien à installer).

## Alternative PowerShell : `Lancer-Optimiseur.bat`

Ancienne version script (`bt-optimizer.ps1`) : interface graphique PowerShell, ou
menu console en repli automatique si PowerShell est verrouillé. Conservée en secours.

## Sécurité (avant toute modification)

- ☑ **Sauvegarde du registre** en `.reg` dans un dossier `bt-optimizer-backup-…` sur le Bureau
- ☑ **Point de restauration système** (optionnel)
- **Retablir les valeurs Windows** : annule les réglages cochés
- **Restaurer une sauvegarde** : réimporte un dossier `.reg` créé précédemment

Toutes les valeurs par défaut de Windows sont rétablies proprement (suppression de la clé quand le défaut est « valeur absente », comme pour HAGS, Power Throttling, Game Mode).

## Les optimisations

Légende : ✅ = coché par défaut (recommandé) · 🔁 = redémarrage requis

### Souris & clavier
| Tweak | Effet sur l'input lag | |
|-------|-----------------------|---|
| Désactiver l'accélération souris | Mouvement **1:1**, la souris ne « dérive » plus selon la vitesse. Le plus gros gain de ressenti pour la visée. | ✅ |
| Réduire les files d'attente souris/clavier (32) | Buffers pilote plus courts → entrées traitées plus directement. Expérimental. | 🔁 |

### Alimentation & CPU
| Tweak | Effet sur l'input lag | |
|-------|-----------------------|---|
| Plan **Performances ultimes** | Empêche le CPU de s'endormir (supprime la latence de réveil des cœurs). Créé s'il n'existe pas. | ✅ |
| Désactiver le **Power Throttling** | Windows ne bride plus les applis pour économiser l'énergie. | ✅ |
| Désactiver la **suspension sélective USB** | La souris/clavier USB ne sont plus mis en veille. À appliquer après le plan Perf. ultimes. | |

### GPU & jeux
| Tweak | Effet sur l'input lag | |
|-------|-----------------------|---|
| **HAGS** (planification GPU matérielle) | Réduit la latence de la file de rendu sur GPU récents (NVIDIA GTX 10xx+/RTX, AMD RX 5000+). Éviter sur GPU ancien. | 🔁 |
| Désactiver les **optimisations plein écran** | Vrai plein écran exclusif → moins de latence de présentation. | ✅ |
| **Mode Jeu** Windows | Priorité CPU/GPU au jeu au premier plan. | ✅ |
| Désactiver **Game DVR** / captures Xbox | Supprime l'enregistrement de fond (cause classique de stutter/latence). | ✅ |

### Système & planificateur
| Tweak | Effet sur l'input lag | |
|-------|-----------------------|---|
| `SystemResponsiveness = 10` | Moins de CPU réservé au multimédia de fond (20 %→10 %). | ✅ |
| `Win32PrioritySeparation = 38` | Le planificateur réagit plus vite pour l'appli au premier plan. | |
| Profil MMCSS **Games** (priorité haute) | Les jeux via le profil multimédia Games passent en priorité d'ordonnancement élevée. | |
| Désactiver `NetworkThrottlingIndex` | Lève la limite de paquets réseau/ms pendant le multimédia. | |
| Désactiver le **tick dynamique** (bcdedit) | Timer noyau à cadence fixe : peut lisser la latence. Expérimental, consomme plus. | 🔁 |

### Réseau (optionnel)
| Tweak | Effet sur l'input lag | |
|-------|-----------------------|---|
| Désactiver l'**algorithme de Nagle** | Envoi TCP immédiat sans regroupement. Utile pour les jeux en ligne **TCP** ; sans effet en UDP. | |

### Confort (optionnel)
| Tweak | Effet | |
|-------|-------|---|
| Menus instantanés (`MenuShowDelay = 0`) | Interface Windows plus réactive (pas d'effet en jeu). | |
| Désactiver la transparence | Moins de composition GPU sur le bureau. | |

## Bon à savoir

- Pour un vrai gain d'input lag, pense aussi à ce qui est **hors du système** : rafraîchissement de l'écran, **V-Sync désactivé** ou mode *Low Latency* du pilote GPU, mode *jeu* du moniteur, souris à fort polling (1000 Hz+).
- Les tweaks marqués 🔁 ne prennent effet **qu'après redémarrage**.
- Après un **retour Windows Update** majeur, certaines clés peuvent être réinitialisées : relance l'outil, les lignes déjà actives sont marquées *(déjà actif)*.

## ⚠️ Verrou PowerShell détecté sur cette machine

Cette machine a une **politique de verrouillage PowerShell au niveau machine**
(`HKLM\…\Session Manager\Environment\__PSLockdownPolicy = 4`) qui force le mode
**ConstrainedLanguage**. Conséquences :

- L'**interface graphique** (WinForms) ne peut pas se charger → l'outil bascule
  automatiquement en **mode console**, qui applique quand même **toutes** les
  optimisations (le seul effet « souris en direct » est reporté à la prochaine
  ouverture de session ; la valeur est bien écrite dans le registre).
- Sur un PC de jeu **sans** ce verrou, l'interface graphique s'affiche normalement.

Cette machine a aussi **`MachinePolicy = Restricted`** (politique d'exécution posée
par GPO). Le lanceur contourne ce point en exécutant le script via
`Invoke-Expression` (et non `-File`), donc **`Lancer-Optimiseur.bat` fonctionne
quand même ici**, en **mode console** (le seul mode possible sous ConstrainedLanguage).

En résumé sur cette machine :

| | État |
|---|---|
| Lancement via `.bat` | ✅ fonctionne (contournement `Invoke-Expression`) |
| Interface **console** | ✅ fonctionne — applique **toutes** les optimisations |
| Interface **graphique** | ❌ nécessite la levée du ConstrainedLanguage |

Pour retrouver l'interface graphique, il faut lever le ConstrainedLanguage
(`__PSLockdownPolicy`) — c'est un **paramètre de sécurité admin**, l'outil n'y
touche pas. Pour vérifier l'état :

```powershell
reg query "HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment" /v __PSLockdownPolicy
Get-ExecutionPolicy -List
```

## Nouvelles optimisations (v3, application .exe)

En plus des tweaks input lag historiques :

| Tweak | Catégorie | Effet |
|-------|-----------|-------|
| Effets visuels « Meilleures performances » | Rapidité | Interface plus rapide, moins de GPU pour le bureau |
| Suppression du délai de démarrage des applis | Rapidité | Les applis du démarrage se lancent sans attente artificielle |
| Désactiver le démarrage rapide (Fast Startup) | Rapidité | Boot plus « propre » (optionnel, boot un peu plus long) |
| Désactiver les applis en arrière-plan (UWP) | Services | Moins d'activité de fond |
| Désactiver SysMain / Superfetch | Services | ⚠ seulement sur SSD ; à éviter sur disque mécanique |
| Désactiver la télémétrie (DiagTrack) | Services | Moins d'activité disque/réseau en fond |
| **Timer Windows 1 ms** (case en direct) | — | Résolution du planificateur au max tant que l'app est ouverte |

## Fichiers

| Fichier | Rôle |
|---------|------|
| **`BTOptimizer.exe`** | **L'application** (recommandé) |
| `Build.bat` | Recompile l'application depuis `src/` |
| `src/` | Code source C# de l'application |
| `bt-optimizer.ps1` | Version script PowerShell (secours) |
| `Lancer-Optimiseur.bat` | Lanceur de la version PowerShell |
| `README.md` | Ce document |
