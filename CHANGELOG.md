# Journal des versions — DesTinGOOD

Toutes les optimisations sont **réversibles**, aucune n'utilise d'injection (compatible
anticheat), et rien n'est modifié sans ton action. Les versions suivent l'assembly
(`BTOptimizer.dll`) ; la puce de version de l'en-tête les affiche automatiquement.

## v14.29 — Rebrand : DesTinGOOD devient **Fluide**
- Nouveau nom (**Fluide**), nouveau logo (**la frametime qui devient plate** — le chaos
  à gauche, la ligne idéale à droite), nouvelle signature : **« Mesuré, pas promis. »**
  Décidés par interview, avec faits vérifiés : `fluide.gg` libre, « FrameLab » écarté
  (FrameLabFPS existe déjà), `fluide.fr/.app` pris.
- Portée : couche **visible** uniquement — titres, textes, logo/icône de fenêtre,
  installateur (« Fluide Setup », `Fluide-Setup-x.exe`), landing, kit de vente. La
  couche technique (`BTOptimizer.exe`, `bt-*.txt`, lanceurs, clés, gardien) est
  inchangée, comme au rebrand v10.3.
- Boutique : `fluide.gumroad.com` (repli `fluidegg`) ; domaine `fluide.gg` à acheter
  après les premières ventes. Landing : titre/description SEO, favicon frametime,
  données structurées SoftwareApplication (3 offres).

## v14.28 — Booster Dynamique + fusion de la branche vente
- **Affinité CPU sur mesure** : le Gardien détecte le lancement des jeux et assigne
  dynamiquement les processus aux meilleurs cœurs (automatique ou sélection manuelle),
  avec priorité Haute. Idéal pour exclure le Core 0 ou gérer les E-Cores.
- **Core parking dynamique** : bascule du profil d'alimentation en jeu, restauration
  du profil précédent en quittant.
- **Nettoyeur RAM automatique en fond** : purge de la liste Standby sous un seuil
  configurable (tableau de bord Système) — fini les stutters de RAM pleine.
- **Fusion de la branche vente** (v14.25 → v14.27 ci-dessous) : licences à expiration,
  funnel d'achat, correctif du viseur — le tout dans la branche interface.

## v14.27 — Correctif critique du viseur
- La fenêtre du viseur (crosshair) ne couvre plus tout l'écran : réduite à
  l'encombrement du réticule. Un overlay plein écran faisait perdre aux jeux sans
  bordure le flip indépendant DWM → grosse chute de FPS jusqu'au redémarrage du jeu.
  Bug diagnostiqué en conditions réelles (Overwatch) et corrigé le soir même.

## v14.26 — Tarifs alignés marché (−15 %) + clés à expiration
- Nouveaux paliers : Gratuit (inchangé, 173 optimisations) · Pro Annuel **49 €/an** ·
  Pro à Vie **127 €** (le marché : 59 €/an · 150 €).
- Clés d'abonnement à date d'expiration signée RSA (infalsifiable) ; les clés sans
  date restent valides à vie ; message clair et retour en édition gratuite à l'expiration.
- Keygen : `dotnet run -- "Nom" [jours]` ; fenêtre Pro : confirmation « à vie »/« jusqu'au … ».

## v14.25 — Funnel de vente branché
- Lien d'achat dans la fenêtre Pro (aucun chemin de paiement n'existait dans l'app).
- Landing reliée aux pages de vente ; kit de lancement dans `marketing/`
  (PLAN-LANCEMENT.md + posts-lancement.md).

*(Les notes v13.6 → v14.24 sont détaillées dans le README, sections par version.)*

## v13.5 — Revue QA
- **Gardien en fond** : les mesures (nvidia-smi, journaux) passent en arrière-plan avec
  garde anti-réentrance et bulle marshallée — fini les micro-freezes de la fenêtre en jeu.
- **Filet de sécurité DNS** : photo des DNS indexée par GUID unique (`SettingID`) au lieu du
  nom de carte — plus de risque sur les configurations double-port.
- **Sauvegardes de réparation** (hosts, OC GPU) : le `.bak` d'origine n'est plus écrasé.

## v13.4 — Finition « ultra pro »
- Menu ☰ réorganisé en **6 sous-menus thématiques** + 2 portes d'entrée + section application.
- Vue d'ensemble ajoutée en tête du README (tableau de la suite).

## v13.3 — Assistant « J'ai un problème… »
- Navigateur **symptôme → bon outil** (25+ panneaux groupés par thème), la porte d'entrée
  pour s'y retrouver sans être expert.

## v13.2 — Réglages TCP/IP
- Remet l'**autotuning à « normal »** (débloque les téléchargements type Steam), RSS/ECN/
  horodatages/RSC ajustés ; affiche l'état réel, réversible (commandes `netsh`).

## v13.1 — Trajet réseau (traceroute)
- Latence **par saut** (ICMP TTL, indépendant de la langue) et verdict sur **où** le lag
  apparaît : ta box, ton FAI, ou plus loin.

## v13.0 — Carte réseau (latence)
- Désactive modération d'interruptions / contrôle de flux / EEE / Green Ethernet **exposés
  par la carte** ; valeurs d'origine sauvegardées, redémarrage carte proposé, réversible.

## v12.9 — Graphique de tendance santé
- Courbe des 20 derniers scores dans le tableau de bord Santé (colorée par niveau).

## v12.8 — Nettoyage disque étendu
- Caches navigateurs (Chrome/Edge/Brave/Firefox, tous profils), crash dumps/minidumps,
  cache Delivery Optimization, journaux CBS — en plus des cibles existantes.

## v12.7 — Gardien en fond (opt-in)
- Alerte par bulle si le GPU dépasse 85 °C ou si le pilote signale une erreur pendant le
  jeu (mesure uniquement en jeu, anti-spam).

## v12.6 — Bibliothèques & applis enrichies
- + .NET 6/9, Java (Temurin) ; + Steam, Epic, MSI Afterburner, HWiNFO, CapFrameX,
  CrystalDiskInfo, DDU, PowerToys, Playnite (IDs winget vérifiés).

## v12.5 — Exclusions antivirus pour les jeux
- Exclut/réinclut les dossiers de jeux détectés (API Defender WMI) — moins de saccades,
  compromis assumé et réversible.

## v12.4 — Benchmark rapide
- Puissance CPU (1 cœur / tous cœurs), bande passante mémoire, disque L/E ; détecte un HDD.

## v12.3 — Rapport HTML = audit
- Le rapport ajoute une section **Diagnostic santé** (crashs, réglages néfastes, biblio-
  thèques, restauration, disque) avec pastilles vert/rouge.

## v12.2 — Historique de santé
- Chaque bilan mémorisé, tendance affichée (delta + scores récents).

## v12.1 — MODE JEU AUTO précis
- Détection par **processus de jeu** (plus de faux positif sur une vidéo) ; + désactivation
  des notifications toast (opt-in).

## v12.0 — Points de restauration
- Liste, création nommée, ouverture de `rstrui`, activation de la restauration système.

## v11.9 — Optimiser les lecteurs
- RE-TRIM SSD / défrag HDD (`defrag /O`) + réactivation de la maintenance planifiée.

## v11.8 — Priorité CPU par jeu
- Contrôle granulaire via IFEO officiel (détection installée, `javaw` exclu), réversible.

## v11.7 — Réglages d'écran gaming
- Fréquence max multi-écran corrigée en 1 clic ; checklist VRR/G-Sync/HDR/échelle.

## v11.6 — Tableau de bord Santé /100
- Score global agrégeant crashs, thermique, réglages néfastes, boutiques, bibliothèques,
  disque, réseau, optimisations — accès direct au panneau de chaque point.

## v11.5 — Jeux & disques + Réparer Windows
- Type SSD/HDD par lecteur, jeux sur quel disque, alerte espace ; action DISM + SFC.

## v11.4 — Réglages néfastes (anti-poison)
- Détecte et annule les tweaks dangereux d'autres « optimiseurs » (HPET forcé, TdrLevel=0,
  Defender coupé, pagefile off, TRIM off, ClearPageFile, LargeSystemCache).

## v11.3 — Thermique & « Qui ralentit mon PC »
- Surveillance températures/throttling (raisons de bridage pilote) ; top des processus de
  fond avec repérage RGB/lanceurs/navigateurs.

## v11.2 — Qualité réseau, souris, MODE JEU AUTO
- Gigue/perte box vs internet ; fréquence réelle de la souris (raw input) ; mode jeu auto.

## v11.1 — DNS v2
- Benchmark parallèle « spécial jeux », 9 résolveurs (dont variantes sans filtre), IPv4+IPv6,
  filet de sécurité avec retour automatique. Installations winget fiabilisées.

## v11.0 — Bibliothèques de jeu
- Détection locale + installation winget (VC++ 2010-2022, DirectX, .NET, OpenAL) + applis.

## v10.9 — Stabilité, checklist, anti-régression
- Moniteur de stabilité (crashs 14 j + verdict), checklist avant match, anti-régression
  Windows Update, export/import de profil, caches shaders au nettoyage.

## v10.8 — ⚡ TOUT OPTIMISER
- Bouton 1 clic (niveau auto, exclusions compat, sauvegarde forcée) + 6 optimisations sûres.

## v10.7 — Boutiques infinies / jeux qui crashent
- Panneau de diagnostic & réparation (Steam/Game Pass) : DNS filtrant, hosts, services,
  IPv6, proxy, heure, cache Steam ; détection HAGS + OC GPU instable (« dispositif de rendu
  perdu ») ; mur pare-feu tiers ; boost CPU maximal.
