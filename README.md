# BT Optimizer

Application Windows 10/11 pour réduire l'**input lag**, la **latence** et accélérer le PC, avec **cases à cocher** : chaque optimisation est optionnelle et réversible. Rien n'est modifié tant que tu ne cliques pas sur **Appliquer**.

## ⭐ Lancement : `Lancer-BTOptimizer.bat`  (compatible Smart App Control)

**Double-clique sur `Lancer-BTOptimizer.bat`** → UAC (Oui) → la fenêtre s'ouvre.

> **Pourquoi ce lanceur ?** Cette machine a **Smart App Control (contrôle intelligent
> des applications)** activé. SAC **bloque les .exe compilés localement** (sans
> réputation dans le cloud Microsoft), c'est pourquoi un `BTOptimizer.exe` maison se
> fait refuser. La solution, **sans désactiver ta sécurité** : l'app est compilée en
> **.NET 10** et lancée par **`dotnet.exe`, qui est signé Microsoft et donc autorisé
> par SAC**. Le lanceur s'élève en admin puis exécute `dist\BTOptimizer.dll` via cet
> hôte de confiance. (Compilation : double-clic sur `Build.bat` — nécessite le .NET
> SDK, déjà présent ici.)

### Ancienne voie (`BTOptimizer.exe`)

Un `dist\BTOptimizer.exe` est aussi produit (double-clic direct possible, il s'élève
seul), mais selon l'humeur de SAC il peut être bloqué : **préfère le `.bat`**.

- **Vraie application native** (fenêtre, cases, boutons) — fonctionne même sur les
  machines où PowerShell est verrouillé (comme celle-ci) car elle ne dépend pas de PowerShell.
- **46 optimisations** groupées en 7 catégories, détection *[déjà actif]* en vert :
  input lag (souris, files d'attente, timer 1 ms global, MPO, plein écran exclusif,
  flip fenêtré Win11, HAGS…), CPU/alimentation (Performances ultimes, throttling,
  ASPM PCIe, USB…), rapidité (effets visuels, délais, NTFS, Edge/Widgets/Bing en
  fond…), services (SysMain, télémétrie, indexation, WER, applis sponsorisées…),
  réseau (Nagle, RSC, veille de la carte réseau…). Y compris un **nettoyage HPET**
  qui retire les réglages néfastes laissés par d'anciens guides.
- **Presets** : `Recommandé` (sûr), **`Auto (adapté à mon PC)`** (détecte SSD/HDD, GPU,
  RAM → coche intelligemment : pas de SysMain/Prefetch off sur disque mécanique, sécurité
  et expérimental laissés à ton choix), `eSport` (agressif), **`Benchmark`** (tout sauf
  sécurité), `Tout`, `Rien`.
- **Barre de recherche** : filtre les 62 optimisations par nom, catégorie ou description.
- **Détection matérielle** affichée au démarrage (CPU, RAM, GPU, type de disque).
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
- **Menu (☰, en haut à droite)** : À propos, Conditions d'utilisation, **Réinitialiser
  TOUTES les optimisations** (retour valeurs Windows + retrait gardien/OC + reset GPU),
  ouvrir le dossier de sauvegardes, ouvrir le journal.
- **Conditions d'utilisation** affichées et à accepter au **premier lancement**
  (avertissement risques/sécurité, aucune garantie, non affilié aux constructeurs).

### Latence EN DIRECT — précision LatencyMon (v9.5)

Menu ☰ → **Latence EN DIRECT (DPC/ISR par pilote)**. Contrairement à la mesure
rapide (compteurs) et à la capture xperf (différée, histogrammes), cette fenêtre
consomme la **session ETW noyau en temps réel** (NT Kernel Logger, moteur
TraceEvent de Microsoft — celui de PerfView) : chaque **DPC** et chaque **ISR**
arrive avec sa **durée exacte** (horloge QPC) et est attribué à son pilote via la
table des modules noyau (`NtQuerySystemInformation`). Pas d'échantillonnage, pas
d'approximation : validé à 22 000+ événements / 2,5 s, 0 perdu.

- **Verdict coloré en direct** (mêmes seuils que l'analyse : < 500 µs vert,
  \> 1000 µs rouge) avec le pilote fautif nommé.
- **Tuiles** : pire DPC / pire ISR (+ module), événements/s, durée, et **sonde de
  réveil 1 ms** (un thread haute priorité mesure de combien Windows le réveille en
  retard : l'équivalent utilisateur de l'« interrupt to process latency » de
  LatencyMon — ce que subit réellement un jeu).
- **Tableau par pilote** rafraîchi chaque seconde, triable : DPC (nb, max µs,
  moyenne µs), ISR (nb, max µs), CPU total ms, description lisible.
- **Remise à zéro** sans couper la session, **export** `bt-latence-live.txt`.
- Reprend la main proprement si une trace noyau (WPR/xperf) traînait, timer 1 ms
  forcé pendant la mesure puis rendu, session fermée au dernier octet à la fermeture.

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
- Bouton **`Comparer avec (AVANT)…`** dans l'analyse (ou **`Comparer les 2 dernières
  mesures`** sur la fenêtre principale, qui choisit tout seul les deux rapports les plus
  récents de `tools\`) : **comparaison AVANT/APRÈS** — bannière amélioration/dégradation,
  tuiles de deltas (pire DPC/ISR, DPC/s) et tableau par pilote avec Δ en µs coloré.

### DNS rapide (v5.4)

Bouton **`DNS rapide`** — panneau dédié : affiche le DNS actuel de chaque carte
réseau, propose un **résolveur rapide** (Cloudflare 1.1.1.1, Cloudflare anti-malware,
Google, Quad9, AdGuard) appliqué **à toutes les cartes actives** via WMI, avec **vidage
automatique du cache DNS**. Retour en **automatique (DHCP)** en un clic. Un DNS rapide
réduit la latence de résolution (temps de connexion aux serveurs de jeu / sites).

### Overclock automatique (v5.1)

Bouton **`Overclock automatique`** — panneau dédié :

- **GPU (overclock réel via `nvidia-smi`)** : trois presets **Sûr / Équilibré / Maximum**,
  plus réglage manuel du **power limit** (curseur en %, borné par le max constructeur)
  et d'un **verrou de fréquence cœur** (min/max MHz). Toutes les valeurs sont **bornées
  par le pilote NVIDIA** (pas de survoltage sauvage), **réversibles** en un clic
  (« Réinitialiser » remet le défaut constructeur : `-rgc` + power limit d'origine), et
  **persistables** au démarrage via une tâche planifiée (`BTOptimizerOC`).
  En ligne de commande : `BTOptimizer.exe -gpuoc` ré-applique l'OC sauvegardé.
- **RAM & CPU (diagnostic)** : l'overclock mémoire (XMP/EXPO) et CPU (multiplicateur/PBO)
  **ne peut pas se faire depuis Windows** — c'est le BIOS. Le panneau **détecte** si ta
  RAM tourne en-dessous de sa vitesse notée (→ active XMP) et si ton CPU est débloqué,
  avec le conseil correspondant. Honnête plutôt que faux.

> ⚠️ Un overclock, même borné, se **valide par un test de stabilité** (jeu prolongé ou
> stress test). En cas d'artefacts ou de plantage : bouton **Réinitialiser**.

- **Profil pilote NVIDIA « faible latence »** (bouton dans le panneau Overclock) :
  applique en un clic **Ultra Low Latency = Ultra**, **1 frame pré-rendue**, **mode
  performances maximales** via `nvidiaProfileInspector -silentImport` (l'outil du dossier
  `tools\npi\`). Utilise ton profil `input-lag-reapply.nip` s'il est présent, sinon un
  profil de secours embarqué. Le bouton est grisé si l'outil est absent.

### Système autonome (v5)

- **GARDIEN au démarrage** : case à cocher qui enregistre ta sélection comme *profil*
  (`bt-profile.txt`) et crée une **tâche planifiée** (`BTOptimizerGuard`, élévation
  automatique) qui ré-applique silencieusement le profil à **chaque ouverture de
  session** — les réglages écrasés par une mise à jour Windows reviennent tout seuls.
  Décocher la case supprime la tâche. Journal silencieux : `bt-optimizer-log.txt`.
- **Timer 1 ms AUTO** : case dédiée — dès qu'un **jeu plein écran / borderless** passe
  au premier plan, le timer 1 ms s'active tout seul (et se relâche au retour bureau).
- **Mode ligne de commande** (automatisation) :
  `BTOptimizer.exe -apply reco|esport|all|profile [-backup] [-restorepoint]`
  et `BTOptimizer.exe -revert profile|all` — sans interface, code retour 0 = succès.
- **Enregistrement CSV du moniteur** : bouton `Enregistrer CSV` — une ligne par seconde
  (CPU %, RAM, temp CPU, timer, temp/charge/fréquence/watts GPU, VRAM) dans
  `bt-monitor-<date>.csv` sur le Bureau. Parfait pour tracer une session de jeu.

### Moniteur matériel en direct

Bouton **`Moniteur matériel`** : fenêtre qui rafraîchit chaque seconde —
tuiles **charge CPU** (PDH), **RAM utilisée/totale**, **température CPU** (zone ACPI, si
exposée), **timer système**, et pour le **GPU NVIDIA** (via `nvidia-smi`) : nom,
température, charge, fréquences cœur/mémoire, consommation en watts et **VRAM**.
Deux **sparklines** tracent l'historique charge CPU (cyan) et charge GPU (vert).
Aucune dépendance externe : PDH + `GlobalMemoryStatusEx` + WMI + l'outil NVIDIA déjà
présent (la lib LibreHardwareMonitor du dossier cible .NET 10, incompatible avec le
runtime .NET Framework de l'exe, donc non liée).

### Samsung CoreSync — anti-saccades Odyssey (v7.9)

Le **diagnostic santé** (fenêtre « Composants & diagnostic ») détecte le logiciel
**Samsung CoreSync**, l'appli compagnon qui synchronise l'éclairage *Core Lighting*
des moniteurs **Odyssey** (G6/G7/G8/G9, Neo, Ark) avec l'image. Pour teinter les LED
arrière, elle **capture l'écran en continu** : cause connue de micro-saccades, de
pertes de FPS et d'input lag en jeu.

- CoreSync **tourne en fond** ou **démarre avec Windows** → bouton **`Désactiver`** :
  ferme l'appli et coupe son lancement automatique (entrées Run + tâche de démarrage
  de l'appli Store, même mécanisme que Gestionnaire des tâches → Démarrage). Réversible :
  relance l'appli ou réactive-la dans le Gestionnaire des tâches.
- Un **écran Samsung/Odyssey est branché** sans CoreSync → simple rappel : l'éclairage
  se règle aussi directement dans le menu du moniteur (Jeu → Éclairage Core), en couleur
  fixe de préférence.

Le `CoreSync.exe` d'**Adobe** Creative Cloud (synchro cloud, sans rapport) est reconnu
via son chemin d'installation et laissé tranquille.

Le diagnostic couvre aussi **Samsung Display Manager** (l'appli compagnon des moniteurs
Samsung récents, inutile pour CoreSync puisque l'éclairage est calculé par l'écran) et
son service **MAPT** (pont réseau B2B via la prise LAN du moniteur, sans intérêt à la
maison) : bouton **`Désactiver`** → fermeture de l'appli, retrait du démarrage
automatique (Run, raccourcis, tâches planifiées) et arrêt + désactivation du service.

### 🎯 Objectif 500 FPS — écran 500 Hz (v9.3)

Bouton **`🎯 500 FPS`** (fenêtre principale) ou menu ☰ → *Objectif 500 FPS*. Principe
honnête : **Windows ne « fabrique » pas des FPS, il enlève les freins** — les 500 FPS se
débloquent DANS chaque jeu (limite de FPS, V-Sync). Le panneau fait donc trois choses :

1. **L'écran d'abord** : liste chaque écran avec **Hz actuel vs Hz max** ; si l'écran
   500 Hz tourne à 240, bouton **`⬆ Passer à 500 Hz`** (API `ChangeDisplaySettingsEx`,
   testée avant application, **retour automatique en 12 s** si l'image disparaît —
   même sécurité que Windows). Rappels câble DisplayPort/OSD et fréquence FIXE (pas DRR).
2. **Freins Windows vérifiés en direct** (vert/orange) : plan d'alimentation, Power
   Throttling, HAGS, Mode Jeu, Game DVR, optimisations plein écran, MPO, flip fenêtré,
   SystemResponsiveness, **NoLazyMode** (nouveau tweak MMCSS v9.3), timer 1 ms — avec,
   pour chaque point orange, la case exacte à cocher. Bouton **`⚡ Appliquer le pack
   500 FPS`** : sélectionne et applique tout d'un coup (réversible, sauvegarde .reg).
3. **Jeu par jeu** : détecte les jeux installés (Steam multi-bibliothèques, Riot, Epic,
   Battle.net, clés de désinstallation — lecture seule, aucun fichier de jeu modifié)
   et affiche pour chacun **la manip exacte qui débloque la limite de FPS** : CS2
   (`fps_max 0`), VALORANT (Limiter les FPS : Non), Fortnite (Illimitée + mode
   Performance), Overwatch 2 (Personnalisée → 500), Apex (`+fps_max unlimited`),
   Call of Duty, LoL, Rocket League (`TASystemSettings.ini → MaxFPS=500`), R6 Siege,
   Minecraft. Plus les règles d'or (V-Sync OFF, Reflex ON, presets compétitifs) et les
   réglages pilote NVIDIA/AMD (V-Sync off, pas de limiteur pilote, perfs max, anti-lag).

**v9.4 — priorité CPU « Haute » pour les jeux compétitifs** : nouveau tweak
`games_cpu_priority_high` — Windows lance CS2, OW2, Valorant, Fortnite, Apex, COD, LoL,
Rocket League et R6 en priorité processeur Haute via **PerfOptions** (IFEO), le mécanisme
officiel de Windows : aucune injection, compatible anti-cheat, réversible. C'est le levier
qui compte quand le jeu est **limité par le CPU** (le jeu passe devant les tâches de fond
quand tous les cœurs saturent). Vérifié en direct dans le panneau 🎯.

## Dépôt git

Le dossier est un dépôt git local (branche `main`). Les traces `.etl` (volumineuses,
régénérables) et `BTOptimizer.exe` (recompilable via `Build.bat`) sont exclus par
`.gitignore`. Identité configurée localement pour ce dépôt : modifie-la avec
`git config user.name "..."` / `git config user.email "..."` si besoin.

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

**69 optimisations** au total, chacune avec détection de son état (vert « déjà actif »).
v6.3 ajoute : bande passante QoS libérée, plus de ports réseau, fermeture rapide des applis
figées, Explorateur en processus séparés, Storage Sense off, blocage du redémarrage auto de
Windows Update, désactivation du CEIP. Fonctions du menu ☰ : **Gestionnaire de démarrage** (activer/désactiver les programmes qui
se lancent au boot, comme le Gestionnaire des tâches, réversible) et **Nettoyage disque**
(fichiers temporaires, cache Windows Update, Prefetch, rapports d'erreurs et corbeille, avec
tailles et sélection). v6.4 ajoute aussi : suivi des applis off, animations barre des tâches
off, contenus dynamiques de recherche off, aperçus barre des tâches instantanés. Bloc « avancé » (réversible + sauvegardé, à cocher en connaissance de cause) :

| Tweak | Catégorie | Effet |
|-------|-----------|-------|
| **MSI mode GPU** | GPU | Interruptions par message → moins de latence/stutter d'interruption |
| État min. processeur 100 % | Alim | CPU à pleine fréquence, pas de latence de montée en régime |
| Désactiver les C-States CPU | Alim | Latence d'interruption minimale (chaleur/conso en hausse — exp.) |
| **Mitigations Spectre/Meltdown off** | Système | Gros gain CPU ancien — ⚠ réduit la protection (réversible) |
| **VBS / Intégrité mémoire (HVCI) off** | Système | Récupère les perfs mangées par la virtualisation — ⚠ sécurité |
| Prefetch/Superfetch off (SSD) | Système | Moins d'écritures disque |
| Horodatage « dernier accès » NTFS off | Système | Moins d'écritures disque |
| Catégorie **Confidentialité** | — | ID pub, télémétrie, historique d'activité, localisation, feedback, Cortana |
| Services Xbox / MapsBroker / Registre distant | Services | Moins de fond / surface d'attaque (réversibles) |

Les tweaks ⚠ **sécurité** ne sont ni recommandés ni dans les presets : opt-in explicite.

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
| `scripts/` | Scripts PowerShell autonomes (boot gaming, input lag, réseau TP-Link, overclock GPU, mesure DPC…) |
| `README.md` | Ce document |
