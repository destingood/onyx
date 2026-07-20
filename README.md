# DesTinGOOD Optimizer

*(anciennement « BT Optimizer » — v10.3 : rebranding. Les fichiers techniques gardent leurs
noms — `BTOptimizer.exe/.dll`, `Lancer-BTOptimizer.bat`, `bt-*.txt`, sauvegardes — pour que
lanceurs, licences, profils et gardien continuent de fonctionner sans rien casser.)*

Application Windows 10/11 pour le **gaming** : viser les **500 FPS**, réduire l'**input lag**
et la **latence**, avec **cases à cocher** : chaque optimisation est optionnelle et
réversible. Rien n'est modifié tant que tu ne cliques pas sur **Appliquer**. Thème
sombre par défaut (basculable dans le menu ☰).

## En un coup d'œil

- **173 optimisations** réversibles + bouton **⚡ TOUT OPTIMISER** (1 clic adapté au matériel).
- **Deux portes d'entrée** : 🏥 **Santé de mon PC** (bilan /100 avec graphique de tendance) et
  🧭 **J'ai un problème…** (assistant symptôme → bon outil).
- **Suite de diagnostic** (menu ☰, rangée en 6 sous-menus) — chaque symptôme a son panneau
  avec un verdict actionnable :

| Domaine | Outils |
|---|---|
| ⚡ Performance & FPS | 🎯 Objectif 500 FPS · 📈 FPS en direct · ⏱ Latence en direct · 🧪 Benchmark · 🔍 Qui ralentit mon PC · 🏁 Checklist match |
| 🩺 Crashs & stabilité | 🩺 Stabilité (14 j) · 🌡️ Températures/throttling · 🛒 Boutiques/crashs · 🧹 Réglages néfastes · 🔧 Réparer Windows · 🛡 Gardien en fond |
| 📡 Réseau | 📶 Qualité réseau · 🛰️ Trajet (traceroute) · ⚙️ Réglages TCP/IP · 📡 Carte réseau · 🌐 DNS |
| 🎮 Jeux & écran | 📦 Bibliothèques (winget) · 🎮 Priorité par jeu · 🔐 Exclusions antivirus · 🖥️ Écran · 🖱️ Souris |
| 💾 Disque & entretien | 💾 Jeux & disques · 🖴 Optimiser lecteurs · 🧹 Nettoyage · 🔁 Points de restauration |

- **Honnête et sûr** : aucune injection (compatible anticheat), tout réversible, sauvegardes
  .reg + point de restauration, et un panneau qui **annule les dégâts** des mauvais optimiseurs.
- **Rapport HTML** exportable (audit avant/après) — livrable client.

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

### 🔒 Revue QA (2ᵉ passe) — panneau Thermique fluidifié (v13.9)

Extension de la revue aux panneaux en lecture seule : le panneau **🌡️ Températures &
throttling** lançait jusqu'à **6 appels `nvidia-smi` sur le thread de l'interface** à chaque
tick → il se figeait toutes les 2 s. Les mesures passent en **arrière-plan** (garde
anti-réentrance, application UI marshallée) — le panneau reste fluide. Vérification faite sur
tous les panneaux à timer : c'était le dernier concerné.

### 🔁 Boucle optimise → vérifie + fenêtres à jour (v13.8)

- Après **⚡ TOUT OPTIMISER**, l'app propose d'ouvrir directement le **bilan Santé** : tu vois
  ton nouveau score et sa progression sur le graphique, juste après avoir optimisé.
- **À propos** et **premier lancement** rafraîchis : compteur d'optimisations dynamique et
  positionnement « optimiseur **+ diagnostic** » au lieu du seul input lag.

### 📈 Bilan santé plus complet (v13.6)

Le tableau de bord **🏥 Santé** intègre désormais un contrôle **écran** : il détecte un
moniteur resté **sous sa fréquence max** (le classique 60 Hz sur un 144/240 Hz — une grosse
perte de fluidité invisible) et route directement vers le panneau 🖥️ Réglages d'écran pour
le corriger. Le score reflète mieux l'expérience réelle de jeu.

### 🔒 Revue QA — 3 correctifs (v13.5)

Passe de qualité sur les panneaux qui modifient le système (revue de code dédiée) :

- **Gardien en fond** : les mesures (nvidia-smi, journaux) tournaient sur le thread de
  l'interface → micro-freezes de la fenêtre en pleine partie. Passées **en arrière-plan**
  avec garde anti-réentrance ; la bulle d'alerte est marshallée proprement vers l'UI.
- **Filet de sécurité DNS** : la photo des DNS était indexée par le nom de la carte (pas
  unique sur les cartes double-port) → risque de restaurer le mauvais adaptateur. Désormais
  indexée par **GUID unique** (`SettingID`).
- **Sauvegardes de réparation** (hosts, OC GPU) : le `.bak` n'est plus **écrasé** aux
  réparations suivantes → l'original d'avant DesTinGOOD est préservé.

### ✨ Finition « ultra pro » — menu réorganisé (v13.4)

Le menu ☰ passe d'une longue liste à plat à une organisation **claire et professionnelle** :

- En haut, les **deux portes d'entrée** : 🧭 J'ai un problème… et 🏥 Santé de mon PC.
- Puis **6 sous-menus thématiques** : ⚡ Performance & FPS · 🩺 Crashs & stabilité ·
  📡 Réseau · 🎮 Jeux & écran · 💾 Disque & entretien · 🔧 Système & matériel.
- Un sous-menu **🗂 Mon profil d'optimisations** (re-scan, anti-régression, export/import,
  réinitialiser, sauvegardes, journal).
- En bas, la section **application** : guide, à propos, activation Pro, conditions, thème.

Tout est thémé (clair/sombre) et rangé par intention — on trouve le bon outil en 2 clics au
lieu de dérouler 30 lignes. La puce de version de l'en-tête est dynamique (suit l'app).

### 🧭 Assistant « J'ai un problème… » (v13.3)

☰ → **J'AI UN PROBLÈME…** — la porte d'entrée de toute la suite. Avec 25+ panneaux, ce
navigateur part du **symptôme** et ouvre directement le bon outil : « mes jeux crashent »,
« ça rame », « ça lag en ligne », « un jeu ne démarre pas », « boutique infinie »,
« téléchargements lents », « écran bloqué à 60 Hz », « souris pas à 1000 Hz », « PC lent au
démarrage »… Les problèmes sont **groupés par thème** (crashs, performance, réseau, jeux,
écran/périphériques, entretien) ; double-clic = ouverture du panneau qui traite ça. C'est ce
qui rend l'app utilisable par un non-expert — ou par un client à qui tu la confies.

### ⚙️ Réglages TCP/IP — jeu + téléchargements (v13.2)

☰ → **Réglages TCP/IP** — boucle avec le problème d'origine (Steam qui charge/télécharge à
l'infini). Le panneau affiche l'**état réel** de ta pile TCP et applique en un clic les bons
réglages **jeu + téléchargements** :

- **Réglage automatique de la fenêtre TCP → « normal »** : le point crucial. Désactivé par de
  mauvais guides « boost », il **bride les téléchargements** (Steam, MAJ Windows) et fait
  **caler des connexions**. On le remet à sa valeur correcte.
- **RSS activé** (réseau réparti sur plusieurs cœurs), **ECN / horodatages / RSC** ajustés
  pour la latence.

Commandes `netsh` **indépendantes de la langue**, et bouton **« Rétablir les valeurs
Windows »**. À faire tourner si tes téléchargements sont anormalement lents.

### 🛰️ Analyse du trajet réseau — traceroute (v13.1)

☰ → **Analyse du trajet réseau** — complète « Qualité réseau » (box vs internet) en montrant
**chaque saut** entre ton PC et une destination (1.1.1.1 par défaut, ou l'IP d'un serveur de
jeu), avec la latence à chaque étape et le **saut où le ping bondit**. Verdict clair : bond
dès le 1er saut = ta box/ton Wi-Fi ; au 2e = sortie FAI ; plus loin = peering/hébergeur (hors
de ton contrôle). Implémenté en ICMP pur (TTL croissant) — **indépendant de la langue** de
Windows, contrairement à `tracert`.

### 📡 Optimiser la carte réseau — latence (v13.0)

☰ → **Optimiser la carte réseau** — les cartes réseau ont des réglages qui économisent
l'énergie **au prix de la latence** : modération d'interruptions, contrôle de flux, Ethernet
écoénergétique (EEE), Green Ethernet. Le panneau lit **uniquement les réglages réellement
exposés par ta carte** (souvent en Ethernet ; le Wi-Fi en expose peu), montre leur état, et
les passe en « latence minimale » en un clic. **Valeurs d'origine sauvegardées** → bouton
« Rétablir » ; l'effet s'applique après un **redémarrage de la carte** (bref, proposé et
confirmé) ou du PC. Utile surtout en Ethernet pour gagner quelques ms de ping/gigue.

### 📉 Graphique de tendance santé (v12.9)

Le tableau de bord **🏥 Santé de mon PC** affiche maintenant un vrai **mini-graphique** de
l'évolution de tes scores (les 20 derniers bilans) à côté de la jauge : courbe colorée selon
le niveau, dernier point mis en avant, grille de repère 0-100. Ta progression se lit d'un
coup d'œil — un bel argument visuel à montrer avant/après une session d'optimisation.

### 🧹 Nettoyage disque étendu (v12.8)

Le **Nettoyage disque** récupère bien plus d'espace, en restant 100 % sûr (dossiers qui se
régénèrent) : **caches des navigateurs** (Chrome, Edge, Brave, Firefox — **tous les profils**,
détectés automatiquement ; le cache seul, pas tes mots de passe ni cookies), **rapports de
plantage** (CrashDumps + minidumps d'anciens écrans bleus), **cache de livraison des mises à
jour** (Delivery Optimization P2P) et **journaux d'installation Windows (CBS)** souvent
volumineux — en plus des temporaires, du cache Windows Update et des caches de shaders déjà
présents.

### 🛡 Gardien en fond — alertes GPU chaud / pilote (v12.7)

☰ → **Surveillance en fond** (case à cocher du menu, opt-in). Une fois activée, l'app veille
discrètement pendant que tu joues (app réduite en zone de notification) : si le **GPU dépasse
85 °C** ou si le **pilote GPU signale une erreur**, tu reçois une **bulle d'alerte** — avant
que ça ne tourne au throttling ou au crash « dispositif de rendu perdu ». Léger et
respectueux : la température n'est mesurée que lorsqu'un jeu tourne, avec un délai anti-spam
entre deux alertes. Ne prévient que des **nouvelles** erreurs pilote après activation.

### 📦 Bibliothèques & applis enrichies (v12.6)

Le panneau **Bibliothèques & applis de jeu** s'étoffe (identifiants winget tous vérifiés) :

- **+ runtimes** : **.NET Desktop Runtime 6** (essentiel — encore très répandu dans les
  lanceurs), **.NET Desktop Runtime 9** et **Java (Temurin 21 JRE)** en optionnels.
- **+ applis gamer** (optionnelles, détection locale) : **Steam**, **Epic Games Launcher**,
  **MSI Afterburner** (OC + overlay FPS), **HWiNFO** (capteurs), **CapFrameX** (frametimes
  façon labo), **CrystalDiskInfo** (santé S.M.A.R.T. des disques), **Display Driver
  Uninstaller / DDU** (pilote GPU propre après crashs), **PowerToys**, **Playnite**
  (bibliothèque de jeux unifiée). Installation en un clic via winget, comme le reste.

### 🔐 Exclusions antivirus pour les jeux (v12.5)

☰ → **Exclusions antivirus pour les jeux** — Windows Defender analyse les fichiers de jeu à
chaque accès, ce qui cause des **micro-saccades** et rallonge les chargements. Le panneau
liste tes **jeux détectés** (avec leur dossier), montre lesquels sont déjà exclus, et permet
d'**exclure/ré-inclure** chaque dossier en un clic via l'API officielle Defender (WMI).
Honnête sur le compromis : n'exclus que des installations de jeux **sûres**, et c'est
**entièrement réversible**.

### 🧪 Benchmark rapide (v12.4)

☰ → **Benchmark rapide** — mesure la **puissance** de ton PC en ~7 s (distinct de l'analyse
de latence) : **CPU** sur 1 cœur et sur tous les cœurs (Mops/s), **bande passante mémoire**
(Go/s), **disque système** en lecture/écriture (Mo/s). Verdict indicatif — détecte notamment
un **disque dur mécanique** (lecture < 150 Mo/s → conseille le passage sur SSD). Parfait pour
**comparer avant/après** une optimisation, ou deux PC entre eux. Résultats journalisés.

### 📄 Rapport HTML = audit complet (v12.3)

Le bouton **Rapport** produit désormais un vrai **audit** exportable (page HTML autonome,
thème sombre soigné, ouvrable dans le navigateur et imprimable en PDF) : en plus de l'état
des 173 optimisations et du matériel, il ajoute une section **Diagnostic santé** —
crashs pilote GPU (14 j), écrans bleus, réglages néfastes à corriger, bibliothèques de jeu
manquantes, points de restauration, espace disque — avec pastilles vertes/rouges. Le
livrable **avant/après** idéal pour montrer ton travail à un client (généré en arrière-plan
pour ne pas figer la fenêtre).

### 📈 Historique de santé — vois tes progrès (v12.2)

Le tableau de bord **🏥 Santé de mon PC** devient évolutif : chaque bilan est **mémorisé**
(date + score, fichier local), et le panneau affiche la **tendance** — « ▲ +7 depuis le
dernier bilan » et la suite de tes derniers scores (ex. `78 → 85 → 92`). Tu **vois
concrètement** l'effet de tes optimisations dans le temps : tu corriges ce qui est rouge,
tu refais le bilan, le score monte.

### ⏱️ MODE JEU AUTO précis + notification (v12.1)

- **MODE JEU AUTO plus intelligent** : au lieu de se déclencher sur n'importe quelle appli
  en plein écran (y compris une vidéo), il détecte maintenant **le processus d'un vrai jeu**
  (CS2, Valorant, Fortnite, Overwatch, Apex, CoD, LoL, R6…) — activation **immédiate et sans
  faux positif**, et coupure au retour au bureau. Le plein écran reste un repli pour les jeux
  non listés. Le journal indique quel jeu a déclenché le mode.
- **+1 optimisation** (opt-in) : « Désactiver les notifications (bulles) » — plus de toast
  qui vole le focus ou provoque une micro-saccade en pleine partie (réversible ; volontairement
  hors du 1 clic pour ne pas couper tes notifications par surprise).

### 🔁 Points de restauration (v12.0)

☰ → **Points de restauration** — le filet de sécurité ultime. Un point de restauration est
une **photo du système Windows** à un instant T (tes fichiers personnels ne sont pas
touchés). Le panneau **liste les points existants** (date, description), permet d'en
**créer un maintenant** en un clic (à faire avant de gros changements), ouvre la
**restauration Windows** (`rstrui`) pour revenir en arrière, et **active la restauration
système** si un outil l'avait désactivée. Parfait complément du ⚡ TOUT OPTIMISER qui, lui,
crée déjà un point automatiquement.

### 🖴 Optimiser les lecteurs (v11.9)

☰ → **Optimiser les lecteurs (TRIM SSD / défrag HDD)** — la maintenance disque que les
mauvais « optimiseurs » cassent souvent. Windows choisit automatiquement le **RE-TRIM**
sur SSD (préserve les performances dans la durée) ou la **défragmentation** sur disque dur
mécanique (chargements plus rapides), et l'app **réactive la tâche planifiée** si elle avait
été désactivée. Le panneau **🧹 Réglages néfastes** détecte aussi désormais cette tâche
coupée et la remet en place.

### 🎮 Priorité CPU par jeu (v11.8)

☰ → **Priorité CPU par jeu** — jusqu'ici la priorité « Haute » s'appliquait en bloc à tous
les jeux compétitifs ; ce panneau donne le **contrôle par jeu**. Coche uniquement ton jeu
principal (ou plusieurs) : quand le CPU sature, ce jeu passe devant les tâches de fond.
Mécanisme **officiel Windows** (Image File Execution Options) — aucune injection, compatible
anticheat. Les jeux **détectés** sur le PC sont marqués 🎮 ; l'état « priorité haute active »
est affiché par jeu ; entièrement **réversible** (décoche et applique). `javaw.exe`
volontairement exclu (partagé par toutes les apps Java).

### 🖥️ Réglages d'écran gaming (v11.7)

☰ → **Réglages d'écran** — le piège le plus courant : un écran 144/240 Hz resté à **60 Hz**
sans que le joueur le sache. Le panneau liste chaque écran (résolution, fréquence actuelle
vs max, principal), signale ceux **sous leur fréquence max** et les passe tous au maximum
en **un clic** (test avant application, réversible). Il rappelle aussi les réglages qui se
font dans Windows/NVIDIA — **VRR/G-Sync**, **HDR**, **mise à l'échelle** — avec les
raccourcis pour ouvrir les réglages d'affichage Windows et le panneau NVIDIA (honnête :
ces états ne se lisent pas de façon fiable depuis une app).

### 🏥 Santé de mon PC — le bilan en un score (v11.6)

☰ → **SANTÉ DE MON PC** — le tableau de bord qui unifie tout. Un clic lance les contrôles
rapides de tous les panneaux et rend un **score global sur 100** (Excellent / Bon / Moyen /
À corriger) :

- Crashs & erreurs pilote GPU (14 j), signes matériels (BSOD/WHEA/coupures)
- Réglages néfastes d'un ancien optimiseur
- Causes de boutiques infinies / crashs (HAGS, services, OC…)
- Bibliothèques de jeu manquantes (vcruntime, DirectX…)
- Espace disque système
- Stabilité réseau (gigue/perte)
- Optimisations recommandées appliquées

Chaque point à corriger est listé (les graves en haut) et **ouvre le panneau concerné en
un double-clic**. C'est le point de départ idéal : on lit le score, on corrige ce qui est
rouge, on rejoue.

### 💾🔧 Jeux & disques + Réparation de Windows (v11.5)

- **💾 Jeux & disques** — ☰ → type (**SSD/HDD**) et espace libre de chaque disque, et sur
  **quel disque** sont installés tes jeux détectés. Un jeu sur disque dur mécanique =
  chargements lents et saccades de streaming de textures → conseil de le déplacer sur SSD
  (avec la marche à suivre Steam). Alerte aussi si un disque système est presque plein.
- **🔧 Réparer l'intégrité de Windows** — ☰ → lance **DISM /RestoreHealth** puis
  **SFC /scannow** en arrière-plan (10-20 min, suivi dans le journal) : répare les
  fichiers système corrompus, cause fréquente de crashs qui persistent **malgré** toutes
  les optimisations. Le dernier recours, propre et officiel.

### 🧹 Réglages néfastes d'autres optimiseurs (v11.4)

☰ → **Réglages néfastes d'autres « optimiseurs »** — l'anti-poison. Beaucoup de guides
« boost FPS » et d'outils douteux laissent des réglages **dangereux** ; ce panneau les
détecte (lecture locale) et remet les valeurs saines de Windows, chacun réversible :

- **Timer HPET forcé** (`bcdedit useplatformclock`) — ajoute de la latence, mythe tenace.
- **Récupération GPU désactivée** (`TdrLevel=0`) — un simple accroc GPU **fige tout** au
  lieu de récupérer : cause directe de freezes/crashs.
- **Windows Defender coupé par politique** (scripts « debloat ») — PC sans antivirus.
- **Fichier d'échange désactivé** — plantages et « out of memory » en jeu.
- **TRIM SSD désactivé** — use le SSD et ralentit les écritures.
- **Effacer le pagefile à l'arrêt** — arrêt du PC très lent, aucun gain.
- **Grand cache système** (`LargeSystemCache=1`) — vole de la RAM aux jeux.

Les problèmes sont pré-cochés ⚠ ; « aucun réglage néfaste détecté » est aussi un bon
résultat (ton PC n'a pas été abîmé).

### 🌡️🔍 Températures/throttling & « Qui ralentit mon PC ? » (v11.3)

- **🌡️ Températures & throttling** — ☰ → surveillance EN DIRECT : température/fréquence/
  puissance GPU (nvidia-smi), charge/température CPU, et surtout les **RAISONS de bridage
  signalées par le pilote** — ralentissement **thermique** ou **frein d'alimentation**.
  Verdict clair : GPU trop chaud (nettoyage/flux d'air/power limit) vs alim insuffisante
  (câbles/PSU) vs sous contrôle. C'est LA cause fréquente des « dispositif de rendu perdu »
  et des chutes de FPS soudaines — teste en lançant un jeu à côté.
- **🔍 Qui ralentit mon PC ?** — ☰ → top des processus par **CPU (mesuré sur 1 s)** et RAM,
  avec repérage des **logiciels de fond connus** étiquetés par catégorie : 🎨 RGB (iCUE,
  G HUB, Armoury Crate, Synapse, SignalRGB…), 🚀 lanceurs (Epic, EA, Ubisoft, Battle.net,
  GOG…), 🌐 navigateurs, 💬 overlays (Overwolf, Discord), ☁️ cloud (OneDrive, Dropbox).
  Fermeture optionnelle, confirmée, **jamais sur un process système**.

### 📶🖱️ Qualité réseau, fréquence souris, MODE JEU AUTO (v11.2)

- **📶 Qualité réseau en jeu** — ☰ → mesure la latence, la **gigue** (variation du
  ping, ce qui compte vraiment en jeu) et la **perte de paquets** vers **ta box** ET
  vers internet (20 pings par segment). L'écart entre les deux **désigne le
  coupable** : gigue dès la box → c'est ton Wi-Fi/câble (passe en Ethernet) ;
  box saine mais internet instable → FAI ou hébergeur du jeu. Bouton de réparation
  réseau intégré.
- **🖱️ Fréquence réelle de la souris** — ☰ → mesure EN DIRECT le taux de rapport
  (polling rate) via l'entrée brute Windows (raw input, aucune injection) : bouge la
  souris en cercles, l'app affiche les Hz réels et compare au palier gaming (1000 Hz).
  Vérifie que ton « 1000 Hz » n'est pas resté à 125 Hz par défaut.
- **MODE JEU AUTO** (case sous la liste, opt-in) — active le mode jeu (RAM libérée,
  services de fond suspendus, timer 1 ms) **tout seul** quand un jeu passe en plein
  écran, et le **coupe seul** au retour au bureau. Ne touche jamais à une activation
  manuelle du bouton MODE JEU.

### 🌐 DNS v2 — benchmark jeux, IPv6 et filet de sécurité (v11.1)

Le panneau **DNS rapide** passe en v2 :

- **Benchmark parallèle « spécial jeux »** : chaque fournisseur est testé sur ses
  **2 serveurs** × 3 domaines (google + steampowered + riotgames), en parallèle
  (~5 s au total). Tableau trié **médiane / pire cas / filtre**, clic sur une ligne
  = sélection ; le conseillé (le plus rapide **sans filtre**) est fléché en gras.
- **9 résolveurs** dont les variantes **SANS filtre** que personne ne connaît :
  Quad9 9.9.9.10, AdGuard 94.140.14.140, OpenDNS — et les filtrants clairement
  marqués « (filtre) » avec avertissement boutiques.
- **IPv4 + IPv6 appliqués ensemble** (netsh pour l'IPv6, échecs tolérés) — fini le
  DNS moderne à moitié appliqué. « Automatique » remet les DEUX en DHCP.
- **FILET DE SÉCURITÉ** : après application, l'app interroge réellement le nouveau
  résolveur ; s'il ne répond pas depuis ton réseau, **retour automatique** aux
  réglages précédents (photo par carte) — impossible de se retrouver sans internet.

### 📦 Bibliothèques & applis de jeu (v11.0)

☰ → **Bibliothèques de jeu manquantes (vcruntime, DirectX…) & applis** — un jeu qui
refuse de se lancer, c'est presque toujours une bibliothèque manquante :

- **Détection locale instantanée** (fichiers/registre, sans réseau) des runtimes que
  les jeux réclament : **Visual C++ 2010/2012/2013/2015-2022** (64 et 32 bits —
  le fameux `vcruntime140.dll manquant`), **DirectX runtime juin 2010**
  (`d3dx9_43.dll`, `xaudio2_7.dll`, `xinput1_3.dll` — tous les jeux DX9-DX11
  d'avant 2015), **.NET Desktop Runtime 8**, **OpenAL**. Les manquantes sont
  pré-cochées ⚠.
- **Installation en un clic via WINGET**, le gestionnaire de paquets **officiel
  Microsoft** (aucun téléchargement douteux, licences acceptées proprement,
  journalisé). Si winget est absent, l'app explique comment l'obtenir (App
  Installer du Store).
- **Applis utiles en option** (jamais pré-cochées) : 7-Zip (mods/archives),
  **OBS Studio** (clips/stream — remplace le Game DVR que l'optimiseur coupe),
  Discord.

### 🩺🏁🛡 Stabilité, checklist match, anti-régression (v10.9)

Quatre fonctions nées du terrain :

- **🩺 Stabilité (14 jours)** — ☰ → *Stabilité : qu'est-ce qui a planté sur ce PC ?*
  Lit les journaux Windows (lecture seule) : crashs/blocages d'applis **taggés 🎮
  quand c'est un jeu connu**, erreurs pilote GPU (`nvlddmkm`), écrans bleus
  (BugCheck), coupures brutales (Kernel-Power 41), erreurs matérielles (WHEA) —
  puis rend un **verdict** : pilote GPU → bouton réparation ; signes matériels →
  températures/XMP/alim ; crashs isolés → intégrité des fichiers du jeu.
- **🏁 Prêt pour le match ?** — ☰ → checklist de 30 s avant de jouer : **ping/gigue/
  perte** (10 pings), timer 1 ms, MODE JEU (activable direct), erreurs pilote 24 h,
  téléchargements Windows en fond, espace disque, optimisations clés appliquées.
  Verdict « ✔ PRÊT POUR LE MATCH » quand tout est vert.
- **🛡 Anti-régression Windows Update** — au lancement, l'app vérifie en fond si des
  optimisations de ton profil ont été **annulées par une mise à jour** (journal +
  bulle de notification), et ☰ → *Mon profil a-t-il été annulé ?* les ré-applique
  en un clic (seulement celles qui ont dérivé, sauvegarde .reg).
- **📤 Export / import de profil** — partage ta sélection entre PC (fichier .txt
  d'identifiants) ; les identifiants inconnus sont ignorés proprement.
- **🧹 Caches de shaders dans le Nettoyage disque** (NVIDIA DXCache/GLCache,
  D3DSCache, AMD) — LE réflexe anti-stutters après une mise à jour de pilote
  (recompilation au prochain lancement, c'est normal).

### ⚡ TOUT OPTIMISER — 1 clic (v10.8)

Le gros bouton vert sous les presets : **un seul clic** et l'app fait tout —

1. **Détection du matériel** (SSD/HDD, RAM, GPU, portable/fixe, Windows 10/11,
   écran haute fréquence, imprimante, tactile…).
2. **Niveau choisi automatiquement** : PC fixe → Agressif (max sûr), portable →
   Équilibré (batterie/chaleur préservées).
3. **Sélection auto-tune cochée puis appliquée** avec **sauvegarde .reg forcée**
   (+ point de restauration si coché), **timer 1 ms activé** et **RAM libérée**.
4. **Réglages à risque écartés d'office** (retour d'expérience des réparations) :
   HAGS (« dispositif de rendu perdu »), applis UWP en fond (boutiques Store),
   tunnels IPv6 — cochables à la main ; sécurité (Spectre/VBS) et OC jamais inclus.

Tout est réversible : « Rétablir (sélection) » ou ☰ → Réinitialiser TOUTES les
optimisations.

**+6 optimisations sûres au catalogue (v10.8)** : tâches planifiées de télémétrie
coupées (Compatibility Appraiser, CEIP, DiskDiagnostic — gros pics disque au repos
en moins), pas de relance des applis à l'ouverture de session, **Windows Recall
désactivé** (Win11 24H2+), suggestions de frappe analysées coupées, OneDrive hors
démarrage (optionnel), Assistant Compatibilité PcaSvc (optionnel).

### 🛒 Boutiques à l'infini / jeux qui crashent (v10.7)

Menu ☰ → **Boutiques qui chargent à l'infini / jeux qui crashent (Steam / Game Pass)…** —
quand la boutique Steam reste vide, que l'inventaire/les skins tournent sans fin, que la
boutique intégrée d'un jeu ne s'ouvre plus **ou que les jeux crashent/figent**, ce panneau
**diagnostique les causes connues côté Windows** et répare les points cochés en un clic
(tout est journalisé) :

- **Overclock GPU / erreurs pilote NVIDIA** : power limit monté, anciens verrous de
  fréquence (`lgc`), tâches de ré-application au démarrage (`BTOptimizerOC`,
  `GPU-PowerLimit-400W`) — croisés avec les **erreurs pilote `nvlddmkm` des 7 derniers
  jours** (journal Système). Un OC instable fait crasher les jeux ET les vues web
  accélérées GPU (boutique Steam/overlay = « chargement infini »). Réparer = fréquences
  et power limit **constructeur** (`nvidia-smi -rgc` / `-pl` défaut), fichiers OC purgés
  (sauvegarde `.bak`), tâches retirées. Pré-coché seulement si des erreurs pilote
  récentes prouvent l'instabilité.
- **HAGS — « Votre dispositif de rendu a été perdu »** : la planification GPU
  matérielle (activée par le tweak `hags` des presets eSport) est la cause classique
  de ce message (Overwatch & co). Quand HAGS est actif **et** que le journal montre
  des erreurs pilote récentes, le point est pré-coché : réparer le désactive
  explicitement (`HwSchMode=1`, redémarrage requis).
- **DNS filtrant** (AdGuard anti-pub, Cloudflare anti-malware, Quad9…) : un domaine de
  boutique/CDN bloqué par le résolveur = page qui tourne à l'infini → retour au DNS
  automatique. Le panneau **DNS rapide** marque désormais ces résolveurs « (filtre) »,
  l'auto-sélection du test de latence ne choisit plus qu'un résolveur **sans filtre**,
  et un avertissement s'affiche avant d'appliquer un résolveur filtrant.
- **Fichier hosts** : lignes `0.0.0.0/127.0.0.1` héritées d'anciens guides anti-pub qui
  bloquent Steam/Epic/Xbox/EA… → neutralisées avec sauvegarde (`hosts.destingood.bak`).
- **Services Boutique/licences** (ClipSVC, LicenseManager, wlidsvc, TokenBroker,
  InstallService, AppXSvc, BITS, wuauserv, DoSvc) et **services Xbox** désactivés par un
  optimiseur → retour au démarrage Windows par défaut.
- **Applications UWP en arrière-plan coupées**, **politiques** DODownloadMode /
  AutoDownload du Store → retirées (jeux Store/Game Pass qui se connectent à nouveau).
- **IPv6 bridé** (DisabledComponents), **proxy fantôme** (WinINET + WinHTTP),
  **heure Windows** (certificats TLS) → remis d'aplomb.
- **Mur pare-feu tiers (SysHardener & co)** : des dizaines de règles « Bloquer » en
  sortie, programme par programme — le jeu tourne mais ses processus web (boutique,
  launcher, monnaies en « Connexion… ») sont coupés dans TOUS les jeux. Détection par
  groupe de règles (jamais les règles système), **désactivation d'un clic sans rien
  supprimer** (réactivable à l'identique). Jamais pré-coché : retirer un durcissement
  volontaire reste ton choix.
- **Cache web Steam** (boutique, overlay, inventaire — `htmlcache`/`httpcache`) : LE
  remède classique ; Steam est fermé proprement (`-shutdown`), le cache vidé, et il se
  reconstruit au lancement suivant. + **vidage du cache DNS** en un clic.

Chaque point affiche ✔/⚠ après analyse ; seuls les points détectés sont pré-cochés.
Les réparations sont **idempotentes et sans danger** — elles annulent au besoin les
optimisations agressives correspondantes (visibles « non actives » au re-scan).

### Habillage pro (v10.6)

Interface au niveau d'un produit commercial, sans changer une seule habitude :

- **Barre de titre sombre** fusionnée avec le bandeau (API DWM, teinte exacte sur
  Windows 11) — la fenêtre est d'un seul tenant.
- **Bandeau repensé** : wordmark **DesTinGOOD** en dégradé vert→bleu, puces
  `v10.6` / `PRO` / `ESSAI n J`, et **liseré dégradé signature** repris sous le
  bandeau de toutes les fenêtres.
- **Boutons arrondis** redessinés partout (survol, pression, focus, désactivé) —
  les couleurs accent (vert, orange, bleu) sont conservées.
- **Catégories en cartes arrondies** avec pastille accent (fini la bordure gravée
  des GroupBox) ; journal posé dans une carte.
- **Menus assortis au thème** (☰, zone de notification, presets Auto), coins
  arrondis sur Windows 11.
- **Tableaux** : en-têtes de colonnes plats assortis, bordure enfoncée retirée.
- **Bascule clair/sombre fiabilisée** : chaque contrôle garde son rôle (bandeaux,
  tuiles, boutons accent) d'une bascule à l'autre ; champs de saisie affinés.

### FPS EN DIRECT — façon PresentMon (v10.2)

Menu ☰ → **FPS EN DIRECT (par jeu)**, ou bouton **Compteur FPS…** du panneau 🎯.
Session ETW abonnée aux fournisseurs **Microsoft-Windows-DXGI / D3D9** (les mêmes
événements que PresentMon) : chaque `Present()` de chaque application est compté,
**sans overlay, sans injection, zéro impact** — compatible anti-cheat puisqu'on ne
touche pas au jeu. La fenêtre montre : bannière **verdict face au Hz de l'écran**
(vert ≥ 95 % : « ton 500 Hz est exploité »), **FPS**, **1% low** (moyenne du pire
1 % des frametimes — l'indicateur de fluidité réel), frametime moyen, pire frame,
**graphique des frametimes** (une barre = une image, ligne verte = cible de
l'écran, barres orange/rouge = micro-saccades), et le tableau de toutes les
applications qui présentent (clic = suivre ; dwm/explorer grisés, un jeu est
sélectionné automatiquement). Idéal pour vérifier qu'un déblocage de limite de
FPS (panneau 🎯) a fonctionné — et voir les vrais 500 FPS s'afficher.

**v10.6 — compteur amélioré** : **gros affichage du FPS** dans la bannière,
nouvelle métrique **0,1 % low** (les pires micro-saccades, comme dans les
benchmarks) en tuile et en colonne, tuiles arrondies mises à jour sans
clignotement, graphique sur carte avec **grille graduée en ms** et échelle
lissée, **export CSV des frametimes** (~20 s de frames, ouvrable dans Excel),
et **mode compact 📌** : petite fenêtre épinglée au-dessus du jeu (nom, gros
FPS, verdict), réversible d'un clic.

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
- **CPU — boost maximal (v10.7)** : tout ce que Windows peut réellement donner, en un
  clic — plan **Performances ultimes**, **turbo boost Agressif** (fréquence max immédiate),
  **état minimal 100 %**, **déparcage de tous les cœurs**, et levée de tout **plafond de
  fréquence** posé dans le plan. **Fréquence effective affichée EN DIRECT** (compteur
  `% Processor Performance` × base) pour VOIR le turbo tenir, état ✓/○ de chaque levier,
  rétablissement des défauts Windows d'un clic. Les deux nouveaux leviers (turbo Agressif,
  déparcage) existent aussi en cases dans la liste principale (presets eSport/LATENCE MIN).
  Aucune tension ni multiplicateur touchés — honnête : le vrai OC (PBO, multiplicateur)
  reste au BIOS, le panneau détecte si ton CPU est débloqué et te dit quoi activer.
- **RAM (diagnostic)** : détecte si la RAM tourne en-dessous de sa vitesse notée
  (→ active XMP/EXPO au BIOS). Honnête plutôt que faux.

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

**v10.1 — mode Auto conseiller & un clic** : le bouton Auto **retient ton dernier niveau**
(marqué « ← dernier choix » en gras dans le menu), propose d'**appliquer immédiatement**
la sélection (Oui/Non — Non laisse juste coché comme avant), et son journal devient un
vrai conseiller : **RAM sous sa vitesse XMP** (« active XMP dans le BIOS » — gain gratuit),
**écran sous sa fréquence max** (renvoie vers 🎯), **hyperviseur/VBS actif** (coût CPU,
désactivable via le diagnostic — ton choix), **jeux compétitifs détectés** (priorité CPU
Haute). Nouveaux signaux matériels : `HypervisorActive`, `RamRatedMTs/RamRunningMTs`,
`ScreensBelowMax`.

**v10.0 — le preset `Auto` connaît ton écran** : la détection matérielle lit désormais la
**fréquence max des écrans** (affichée dans le résumé). Sur un écran **240 Hz+**, les
niveaux **Équilibré/Agressif** d'`Auto (adapté à mon PC)` ajoutent le pack très hauts FPS
(files d'entrée courtes `input_queues`, tick noyau fixe `dynamic_tick` sur PC fixe, et à
partir de **360 Hz** les C-States off `cpu_idle_disable` — réveil CPU instantané au prix
de la consommation). Le niveau Prudent et les portables gardent leurs protections.
Sécurité (Spectre/VBS) et `msi_storage` (avancé) restent TOUJOURS un choix explicite.
Le journal explique ce que l'écran a déclenché.

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
