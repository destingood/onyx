# Journal des versions — DesTinGOOD

Toutes les optimisations sont **réversibles**, aucune n'utilise d'injection (compatible
anticheat), et rien n'est modifié sans ton action. Les versions suivent l'assembly
(`BTOptimizer.dll`) ; la puce de version de l'en-tête les affiche automatiquement.

## v14.62 — Il comprend le langage texto (abréviations + fautes)
- **Couche d'expansion des abréviations**, appliquée AVANT tout le routage : « slt g un pb
  mon pc ram bcp pk » devient « salut j'ai un problème mon pc rame beaucoup pourquoi » et part
  droit à l'enquête. ~90 abréviations courantes : pk/koi/cmt/ki/kan (mots interrogatifs),
  g/chui/ta/ya/jv (pronoms-verbes), bcp/tjs/tt/mtn/pcq, pb/pblm/prob (problème), maj, ordi→pc,
  pa/pu/plu (négations), slt/cc/wsh/stp/svp/mrc/dsl, wi/ui/nn/nan (oui-non)…
- **Se combine avec la tolérance aux fautes déjà en place** (distance d'édition) : abréviation
  développée PUIS faute corrigée → « pk mon pc ram » compris même mal orthographié.
- **« ça va pas / ça marche pas »** (négatif) n'est plus pris pour un « ça va ! » : il demande
  ce qui cloche.
- Tout profite de la couche : règles, lexique, conseiller d'outils, détection des doutes.

## v14.61 — Il comprend le langage de tous les jours
- **« ça va ? » compris**, y compris les formes familières et sans accent : cava, sava, cv,
  ça roule, quoi de neuf, tu vas bien… (avant, « cava » partait à l'IA qui répondait sur le
  vin espagnol !). Réservé aux messages courts : « comment va mon PC » reste une question de
  santé.
- **Salutations et au revoir** élargis (yo, wesh, slt, à plus, ciao, bonne journée…) et
  « qui es-tu ? / tu es une IA ? » répondu directement.
- Ces échanges du quotidien sont traités par les règles (instantané, juste) au lieu d'être
  pris au premier degré par le modèle.

## v14.60 — Un outil pour chaque besoin, avec les risques
- **Conseiller d'outils** (`ToolAdvisor`) : pour un besoin que l'app ne couvre pas nativement,
  le Copilote propose le BON outil — le sien en 1 clic quand il existe, sinon une
  **recommandation gratuite externe**, toujours accompagnée de **son risque/précaution** et de
  « prends-le sur le site officiel ». Couvre : récupérer un fichier supprimé (Recuva), tester
  la RAM (MemTest86), cloner/partitionner un disque, créer une clé USB Windows, scan malware
  (Malwarebytes Free), désinstaller proprement (BCUninstaller), contrôle à distance, mot de
  passe Windows oublié, éditeur du registre…
- **Mises en garde intégrées** : les « driver updaters »/« PC boosters », CCleaner et les
  **arnaques au faux support** sont explicitement déconseillés (pourquoi + quoi faire à la
  place) — le Copilote protège l'utilisateur, il ne se contente pas de proposer.
- **L'IA locale suit la même règle** : quand elle conseille un outil, elle doit dire qu'il est
  gratuit, énoncer les risques et renvoyer au site officiel.

## v14.59 — Dépanneur PC universel : bien au-delà du gaming
Le Copilote ne se limite plus aux soucis de jeu — il aide à réparer un PC quel que soit le
problème, avec les grandes réparations gratuites et officielles :
- **« répare Windows »** → DISM /RestoreHealth + SFC /scannow : LE remède aux corruptions
  système (crashs qui persistent, MAJ qui échoue, apps qui ne s'ouvrent plus). Gratuit, sans
  risque.
- **« plus d'internet »** → réinitialisation de la pile réseau (Winsock + TCP/IP + DNS/ARP,
  commandes officielles Windows) : répare la plupart des connexions coupées par un VPN ou un
  antivirus, avec redémarrage proposé pour finaliser.
- **« plus de son »** → relance du moteur audio (services Windows) : le son revient sans
  redémarrer.
- **écran bleu / BSOD** → réparation Windows + relevé des plantages datés, et lecture du
  « code d'arrêt » si tu le donnes.
- **Ce que le logiciel NE PEUT PAS faire depuis Windows** (PC qui ne démarre pas, écran noir,
  périphérique USB/Bluetooth/imprimante mort) → **guides sûrs pas-à-pas** : câble/entrée écran,
  reset d'alimentation, mode sans échec, réinstallation de pilote, ré-appairage… puis « dis-moi
  où ça bloque et je continue avec toi ».
- 3 pastilles d'accueil (Réparer Windows · Plus de son · Plus d'internet) ; `Sys.RestartService`.
- Et pour tout le reste, l'IA locale prend le relais (si activée).

## v14.58 — Il répond à TOUT, et il assume ses doutes
- **Répond à n'importe quelle question** : dès que l'IA locale est active, tout ce que les
  règles ne traitent pas AVEC CERTITUDE part vers le modèle — y compris un signal PC faible
  (avant, un mot ambigu déclenchait un « tu veux dire… ? » ; maintenant il répond vraiment).
  Les vraies commandes PC (mesures, enquête, réparations) gardent la priorité, elles restent
  imbattables.
- **Il dit ses doutes** : le modèle a désormais pour consigne stricte de signaler l'incertitude
  (« Je ne suis pas certain, mais… », « à vérifier »), de ne JAMAIS inventer un fait/chiffre/
  date/mesure, et d'admettre qu'il n'a ni internet, ni l'heure réelle, ni l'actualité du jour —
  plutôt que d'affirmer du faux. Il signale aussi quand ses connaissances peuvent être datées.
- Ton ajusté : sans IA, le signal faible propose « active l'ia pour que je réponde à tout ».

## v14.57 — Détection d'Ollama fiabilisée + vraie configuration
- **Détecte Ollama où qu'il soit** : dossier utilisateur, Program Files (x86/x64) ET le PATH
  (« where ollama ») — un Ollama déjà présent ailleurs n'est plus réinstallé par erreur.
- **Installe s'il manque, configure toujours** : après installation (ou détection), l'app
  **configure** Ollama pour qu'il soit toujours prêt :
  - démarrage AUTOMATIQUE avec Windows (entrée Run « Ollama » posée si l'installeur ne l'a
    pas fait — le moteur est là à chaque session sans rien lancer) ;
  - modèle gardé en mémoire 30 min entre deux questions (OLLAMA_KEEP_ALIVE) → après la
    première réponse, les suivantes sont quasi instantanées.
- Journal explicite à chaque étape (« Ollama absent → installation », « déjà présent — pas de
  réinstallation », « configuré : démarrage auto + modèle gardé en mémoire »).
- `Sys.SetUserEnv` : variable d'environnement utilisateur persistante (n'écrase pas si identique).

## v14.56 — IA locale : modèle adapté à CHAQUE machine + consentement Oui/Non
- **Le modèle est choisi selon la config du client** (VRAM du GPU + RAM détectées, sans
  pilote noyau — registre `qwMemorySize` tous constructeurs, repli capteurs/RAM). Barème
  prudent, optimal ET optimisé quelle que soit la machine :
  - grosse carte (≈ 11 Go VRAM, 24 Go RAM) → **qwen2.5:7b** (le plus malin) ;
  - config équilibrée (6 Go VRAM, 12 Go RAM) → **llama3.2:3b** (le sweet spot) ;
  - PC modeste (3,5 Go VRAM, 8 Go RAM) → **qwen2.5:1.5b** (léger et vif) ;
  - petite config / sans vrai GPU → **qwen2.5:0.5b** (ultra-léger, tourne partout).
- **Question au premier lancement** : « Installer le cerveau IA local ? Oui / Non » —
  posée UNE seule fois, et **seulement quand une installation/un téléchargement serait
  réellement nécessaire** (si Ollama + un modèle sont déjà là, activation silencieuse, zéro
  question). Non → plus jamais reproposé ; Oui → installe le modèle adapté.
- La garde d'espace disque s'ajuste à la taille du modèle choisi ; installation guidée et
  auto utilisent toutes deux le même choix matériel.

## v14.55 — L'IA locale s'installe TOUTE SEULE pour chaque installation
- **Zéro action requise** : au premier lancement (20 s après l'ouverture, en arrière-plan),
  l'app installe Ollama (winget, silencieux), démarre le moteur, télécharge le petit modèle
  (≈ 2 Go, une fois) et active le cerveau — chaque personne qui installe l'app a un Copilote
  qui répond à tout, sans rien configurer.
- **Garde-fous** : jamais sans winget ; jamais sous 6 Go libres ; 3 tentatives lourdes
  maximum (compteur persisté) ; téléchargement interrompu = REPRIS au lancement suivant
  (Ollama reprend où il en était) ; jamais dans le harnais de test.
- **Le choix de l'utilisateur reste roi** : « désactive l'ia » coupe ET bloque définitivement
  l'installation automatique (fichier bt-ia-off.txt) ; « active l'ia » lève ce blocage.
- **Transparence** : l'accueil du Copilote affiche l'étape en cours (« installation d'Ollama »,
  « téléchargement du modèle… ») et rappelle comment annuler ; une question posée pendant
  l'installation reçoit une réponse honnête (« repose-la dans quelques minutes »).

## v14.54 — Le cerveau IA 100 % LOCAL (optionnel, gratuit) : il répond à tout
- **« active l'ia »** → le Copilote se branche sur **Ollama** (gratuit, open source) : un
  modèle d'IA qui tourne **sur TA machine** (ta carte graphique fait le travail). Aucune
  donnée envoyée, aucun abonnement, aucune clé — la promesse « 100 % local » tient.
- **Répartition intelligente** : les règles répondent d'abord (mesures réelles, réparations,
  lexique — imbattables sur le PC) ; tout ce qu'elles ne comprennent pas part vers l'IA
  locale, qui connaît l'état réel du PC (santé, optimisations, jeux) et les commandes de
  l'app — et qui a interdiction d'inventer des mesures ou de recommander du payant.
- **Installation guidée depuis le chat** : Ollama absent → installation winget en un clic ;
  aucun modèle → téléchargement de llama3.2:3b (≈ 2 Go, une fois) en un clic, progression
  en direct. « désactive l'ia » coupe tout ; sans activation, RIEN ne change.
- Ollama rejoint le catalogue 📦 Bibliothèques ; « c'est quoi ollama ? » au lexique.
- Strictement OPT-IN : le seul trafic est vers 127.0.0.1 (ta propre machine).

## v14.53 — Le rapport d'audit depuis le chat + « Prépare ma partie »
- **« génère le rapport » / « fais un audit »** → le Copilote produit le **rapport HTML
  complet** (matériel, toutes les optimisations, diagnostic santé) sur le Bureau et
  l'ouvre — un clic, rien n'est modifié au système. Le livrable avant/après en une phrase.
- **« prépare ma partie » / « je vais jouer »** → repérage des applis de fond CONNUES
  (RGB, lanceurs, fonds animés, overlays, capture, cloud, navigateurs) et **fermeture
  groupée en un clic**. Discord/Spotify (comms/musique) et Riot (nécessaire pour
  LoL/Valorant) sont épargnés volontairement. Nouvelle pastille dédiée.
- BloatForm expose son catalogue d'applis de fond au Copilote (RunningBloat) ; la
  fermeture douce/forcée est factorisée (CloseByName).

## v14.52 — La doc CAPET digérée : le bon intégré, les pièges détectés
Analyse du kit de tutos fourni (TUTO 1-3, ISLC, filtres NVIDIA, scripts d'inversion). La
moitié était DÉJÀ native (accél. souris, GameDVR, services risqués en opt-in, ISLC/Autoruns/
DDU au catalogue, Ultimate Performance, HAGS, MSI…). Le reste est intégré — et les pièges
sont désormais détectés :
- **2 nouvelles détections « Réglages néfastes »** (avec réparation 1 clic, héritées
  automatiquement par l'enquête du Copilote et « TOUT réparer ») :
  - **Windows Update bloqué** (outils type Wub) — plus aucune mise à jour, même de
    sécurité ; réactivation à la valeur normale de Windows ;
  - **Maintenance automatique désactivée** (MaintenanceDisabled) — re-TRIM SSD, défrag et
    nettoyages nocturnes ne tournent plus.
- **3 optimisations réversibles de plus** (hors presets, descriptions honnêtes) :
  - **Fermetures et menus plus rapides** (MenuShowDelay 8 ms, applis bloquées fermées en
    1-2 s — avertit du risque AutoEndTasks sur travail non enregistré) ;
  - **Empêcher Windows Update d'écraser tes pilotes** (SearchOrderConfig) — le complément
    naturel d'un nettoyage DDU ;
  - **NVIDIA : ancien filtre de netteté** (EnableGR535) — le réglage communautaire des
    filtres sharpness fournis.
- **4 notions au lexique du Copilote** : ISLC/standby list, MarkC, netteté NVIDIA, Wub.
- Non retenu, en conscience : couper la maintenance auto et bloquer Windows Update (les
  2 détections ci-dessus font l'inverse), Spooler/DPS/RmSvc déjà en opt-in ⚠, le pack
  « SystemProfile\Tasks\Games » (étiqueté placebo par l'auteur lui-même).

## v14.51 — Les finitions auxquelles on ne pense pas (mais qui changent tout)
- **Progression EN DIRECT** : pendant une action longue (installation winget, « TOUT
  réparer »), la bulle « écrit… » s'élargit et relaie **chaque ligne du journal en temps
  réel** — fini les trois points muets pendant deux minutes.
- **Boucle fermée** : après « 🚀 TOUT réparer », le Copilote **relance tout seul une
  vérification complète** (mesure, lecture seule) → grâce à sa mémoire, il écrit noir sur
  blanc « Depuis la dernière enquête — réglé ✔ : … ».
- **Cockpit vivant** : les tuiles (Écrans / Ping / GPU) se **rafraîchissent après chaque
  correction** — tu passes l'écran à 240 Hz, la tuile suit — + bouton ↻ discret.
- **📄 Diagnostic exportable** : sous chaque enquête, « Enregistrer ce diagnostic » écrit un
  .txt propre et daté sur le Bureau (causes avec impact, points sains, raisonnement complet)
  — le livrable à remettre à un client ou à garder comme trace avant/après.
- **Copier un message** : clic droit sur n'importe quelle bulle → texte (cartes et pied
  compris) dans le presse-papiers.

## v14.50 — Le Copilote RÉPARE (presque) tout — et d'un seul clic
- **🚀 « TOUT réparer »** : après l'enquête, UN bouton enchaîne toutes les corrections dans
  l'ordre d'impact — point de restauration d'abord (filet), chaque étape isolée (une qui
  échoue n'arrête pas les autres), compte-rendu ligne par ligne. Un seul clic explicite :
  la promesse fondatrice (jamais de changement sans ton accord) reste intacte.
- **Presque chaque piste a maintenant SA réparation en un clic** :
  - bibliothèques manquantes → **installation directe** des runtimes Microsoft (winget) ;
  - DNS lent → **bascule vérifiée** (test réel après changement, retour arrière AUTOMATIQUE
    s'il ne répond pas — même filet que le panneau) ;
  - optimisations inactives → **preset « Recommandé » appliqué par le moteur complet**
    (sauvegarde registre + point de restauration AVANT, application isolée, réversible) ;
  - processus gourmand → **fermeture propre** (douce puis forcée, jamais le cœur de Windows) ;
  - démarrage chargé → **coupe les lanceurs connus** (liste blanche stricte : Steam, Epic,
    Discord, Spotify, Wallpaper Engine… — réversible, rien n'est désinstallé) ;
  - crashs pilote GPU → **installation de DDU** (gratuit) directement depuis le chat ;
  - redémarrage en retard → **redémarrage programmé (60 s) annulable d'un clic** — jamais
    inclus dans « TOUT réparer » (il garde son propre bouton).
- Les mesures individuelles enchaînent aussi sur leur réparation (mesure DNS → bascule,
  processus → fermeture, bibliothèques → installation…).

## v14.49 — La boîte à outils du Copilote : 4 mesures, 5 pistes et un lexique de plus
- **4 nouvelles mesures dans le chat** :
  - **DNS** — chronomètre TON serveur DNS contre Cloudflare/Google (gratuits) sur les mêmes
    requêtes ; verdict et bascule gratuite via le panneau DNS rapide.
  - **Démarrage** — inventaire réel de ce qui se lance à chaque allumage, avec verdict.
  - **Crashs (14 j)** — relevé pilote GPU + crashs/blocages d'applications (jeux marqués 🎮),
    et le bon remède gratuit selon le motif (DDU, bibliothèques, stress-test).
  - **Latence (~5 s)** — timer système, régularité réelle (Sleep 1 ms), pics DPC des pilotes ;
    la mesure qui départage « machine réactive » et « pilote qui micro-coupe ».
- **L'enquête passe à ~15 pistes et S'ADAPTE au symptôme** : + redémarrage en retard (uptime,
  le « démarrage rapide » endort au lieu d'éteindre), + **RAM sous sa vitesse vendue**
  (XMP/EXPO coupé — des FPS déjà payés), + pilote GPU très âgé, + démarrage chargé, + **DNS
  lent mesuré UNIQUEMENT quand la plainte est réseau**. Le Wi-Fi est détecté et change les
  conseils (câble = gratuit).
- **Lexique pédagogique** : « c'est quoi le DLSS ? », « à quoi sert XMP ? », « ddu » → 20
  notions expliquées simplement, avec l'outil lié tendu à chaque fois qu'il existe.
- Nouvelles intentions comprises : DNS, démarrage/boot, crash/écran bleu, input lag/latence —
  toutes tolérantes aux fautes de frappe, toutes comptées dans le routage raisonné.
- 2 pastilles d'accueil de plus : « PC lent à s'allumer » et « Ça crash / écran bleu ».

## v14.48 — Page Copilote « pro » (cockpit) + intelligence proactive
- **Cockpit en tête de page** : 4 tuiles d'état EN DIRECT — Santé, Écrans (au max ✓ / N sous
  le max), Ping réel (échos), Température GPU — colorées selon les seuils. La page se lit
  comme un poste de pilotage, pas comme un simple chat.
- **Diagnostic en CARTES d'impact** : chaque cause de l'enquête devient une carte (barre
  d'accent + pastille « IMPACT 90 · CRITIQUE » rouge/orange/jaune) avec **son bouton de
  correction intégré** — fini le mur de texte numéroté.
- **Accueil proactif** : en arrivant sur la page, le Copilote a déjà jeté un œil (mesures
  légères : écrans, disque, bibliothèques) et fait UNE proposition utile s'il y a lieu —
  sinon il se tait.
- **Mémoire d'une enquête à l'autre** (`bt-copilote.txt`, pistes stables uniquement) :
  « Depuis la dernière enquête — réglé ✔ : disque · toujours là : crashs du pilote GPU ·
  nouveau : … ». Les pistes volatiles (ping, processus) ne polluent pas le suivi.
- **Question de clarification** : sur un signal faible, il demande « Tu veux dire “X” ? »
  au lieu de balayer d'un « pas compris ».
- **Barre de saisie pro** : carte arrondie + envoi compact, et **raccourcis permanents**
  au-dessus (Enquête complète · Solutions gratuites · Test ping · Processus gourmands ·
  Pourquoi ?) — plus besoin de remonter au message d'accueil.

## v14.47 — Le Copilote comprend les fautes de frappe, raisonne et ne propose QUE du gratuit
- **Tolérance aux fautes de frappe** : « mon ecrqn est bloquer a 60 herz », « grqtuit »,
  « conexion »… sont compris (distance d'édition bornée par mot : 1 faute dès 5 lettres,
  2 dès 8 ; mots courts exacts pour éviter les contresens). Appliqué aussi au catalogue
  symptôme → outil.
- **Il raisonne comme un technicien** :
  - plusieurs pistes dans une phrase (« ça chauffe et mon écran est bloqué ») → il préfère
    TOUT vérifier d'un coup (enquête) au lieu de répondre à moitié ;
  - une intention précise noyée dans un symptôme large → enquête aussi ;
  - une demande précise seule → sa mesure dédiée, plus rapide (le réseau reste prioritaire).
- **« Pourquoi ? » / « explique »** : il justifie son dernier diagnostic **mesure par mesure**
  (constaté / seuil / conséquence). Chaque enquête garde son raisonnement complet.
- **Il ne propose QUE du gratuit, et le dit** : nouvelle intention « gratuit / sans payer »
  (règle de la maison + enquête), chaque cause porte sa **solution gratuite** concrète
  (dépoussiérage, Fan Control, DDU, runtimes Microsoft, fermer les gourmands…), et jamais
  d'achat conseillé avant d'avoir tout tenté à 0 €.
- **L'enquête passe à 10 pistes** : + **réglages néfastes** laissés par d'anciens
  « optimiseurs » (HPET forcé, TdrLevel=0, Defender coupé, pagefile désactivé, TRIM off…)
  avec **réparation groupée en un clic** — remet les valeurs par défaut de Windows,
  gratuit et réversible.
- Harnais : `BT_UISHOT_MSG` permet de capturer la conversation de démo avec un message
  arbitraire (vérification réelle de la tolérance aux fautes).

## v14.46 — Le Copilote SUIT la conversation et mesure plus loin
- **Il comprend « oui », « ok », « vas-y », « non »** : la réponse se rapporte à sa dernière
  proposition. « J'ouvre le bilan ? » → « oui » → il l'ouvre (avant, ça tombait dans
  « Pas sûr d'avoir bien compris 🤔 »). Un « oui » sur une correction re-présente son bouton —
  le changement reste TOUJOURS sur clic explicite (promesse fondatrice inchangée).
- **2 nouvelles mesures en direct dans le chat** :
  - **Connexion** : 6 échos réels (1.1.1.1) → ping moyen, gigue, perte de paquets, verdict
    clair (perte = tirs annulés, gigue = jeu irrégulier), et rien d'inventé si ICMP est muet.
  - **Processus gourmands** : top CPU/RAM mesuré sur ~1 s, agrégé par application (les 20
    processus d'un navigateur comptent ensemble), cœur de Windows et jeux en cours exclus.
- **Routage réparé** : « Ping / lag en ligne » lançait l'enquête générique (le mot « lag »)
  au lieu de tester le réseau ; les intentions précises (écran, chauffe, disque…) passent
  désormais AVANT le mot « problème », et « bilan complet » va bien à l'enquête (le mot
  « bilan » était intercepté par la réponse de score santé).
- **L'enquête vérifie 3 pistes de plus** (9 au total) : **crashs du pilote GPU (14 j)** — LE
  signal du « dispositif de rendu perdu » —, **stabilité de la connexion** et **programme
  gourmand en fond**. Toujours classées par impact, et le sain est dit aussi.
- **Finitions** : le bouton d'une correction affiche « ✓ Terminé » quand elle a abouti (il
  restait « en cours… » pour toujours) et se ré-arme en cas d'échec ; Flux réfléchit pendant
  que le Copilote travaille ; le champ de saisie a le focus en arrivant sur la page.
- Toujours 100 % local : le seul trafic est l'écho ICMP du test réseau — aucune donnée ne
  quitte la machine.

## v14.39 — Le Copilote ENQUÊTE (il raisonne comme un technicien)
- Sur un symptôme large (« ça rame », « FPS bas », « bilan », « diagnostic »…), il ne renvoie
  plus vers un panneau : il lance **toutes les mesures**, **croise** les résultats, **classe
  les causes par impact réel** et rend un **plan d'action ordonné**.
- Six pistes vérifiées : fréquence des écrans · température GPU · saturation mémoire ·
  disque système et espace récupérable · bibliothèques de jeu · optimisations inactives.
- **Il dit aussi ce qui va bien** (« Vérifié et sain : écrans à leur fréquence maximale ·
  températures GPU sous contrôle… ») et, s'il ne trouve rien, il le dit franchement au lieu
  d'inventer un problème.
- Chaque cause corrigeable porte **son bouton** dans la même bulle ; une réponse peut donc
  désormais contenir **plusieurs corrections** (`Reply.Plan`), toujours sur clic explicite.
- Réponses des actions typées (`Reply` au lieu d'une chaîne) : une mesure enchaîne d'elle-même
  sur la correction qui en découle, sans logique de suivi codée en dur.
- 100 % local : aucune requête réseau, aucune donnée ne quitte la machine.
- Vérifié en capture sur cette machine : 2 causes réelles trouvées (disque plein à 95 %,
  1 bibliothèque manquante), 3 points sains listés, correction proposée en un clic.

## v14.37 — Animations d'ouverture (version prudente, sans bug)
- **Fondu d'ouverture des fenêtres** : les fenêtres d'outil (menu ⋯) apparaissent en fondu
  doux (~150 ms), via un moteur d'animation transitoire (`Anim`) — **un seul timer ~60 fps
  actif UNIQUEMENT pendant l'effet**, puis à l'arrêt : zéro coût au repos (cohérent optimiseur).
- **Toast « nouveau badge »** : apparition / disparition en fondu.
- **Interrupteur « Animations de l'interface »** (Réglages système, activé par défaut) : se
  coupe seul quand un jeu tourne, respecte le réglage Windows « Afficher les animations »,
  choix persisté (`bt-anim.txt`).
- *Retiré volontairement (instables sur du WinForms peint à la main)* : le **cross-fade entre
  pages** — la capture `PrintWindow` ressortait **noire** sur certains GPU (flash noir) ; et
  l'**easing des survols / interrupteurs** — repeindre 8×/transition des boutons non
  double-bufferisés provoquait un **clignotement**. Survol et bascule restent donc
  **instantanés** (comportement stable connu). Une reprise « en douceur » propre
  (double-buffering + application asynchrone du réglage) reste faisable plus tard.
- Harnais : animations forcées OFF en test — 8 pages × 3 tailles, 0 erreur ; build 0 erreur.

## v14.36 — Le Copilote AGIT (il ne se contente plus d'ouvrir un panneau)
- Le chat **mesure ton PC pour de vrai** et répond avec **tes** chiffres, sans clic :
  fréquence réelle de chaque écran vs son maximum · charge et températures CPU/GPU ·
  espace disque libre **et récupérable** · bibliothèques de jeu manquantes.
- Il **exécute** ensuite, mais **jamais sans ton clic** : passer l'écran à sa fréquence max,
  créer un point de restauration, libérer l'espace disque. Chaque bouton annonce ce qui va
  changer et rappelle que c'est réversible — la promesse fondatrice de Fluide est intacte.
- **Il n'invente pas de problème** : la correction n'est proposée que s'il y a réellement
  quelque chose à corriger (écrans déjà au maximum → « Rien à corriger de ce côté »).
- Tout tourne **en tâche de fond** (un point de restauration prend une minute) : la fenêtre
  ne fige jamais, et le compte-rendu revient dans la conversation.
- Visuel : bulles **indigo** (le vert résiduel a sauté), profondeur et arrondis propres,
  **heure sur chaque message**, en-tête aligné sur le texte — « TOI » n'est plus rogné.
- Harnais : 8 pages × 3 tailles, 40/40 fenêtres, 0 erreur ; échange complet vérifié en capture.

## v14.35 — « Flux », la mascotte du Copilote
- Le chat a enfin un **personnage** (l'ancienne mascotte est partie avec le rebrand) :
  **Flux**, un orbe dont la **bouche EST la courbe de frametime** du logo.
- **Elle réagit vraiment** : bouche plate quand la santé du PC est bonne (≥ 60), en
  **dents de scie** quand il y a un problème, trois points tant que le bilan n'est pas
  calculé. Mêmes seuils que l'anneau du QG → l'app tient un discours cohérent.
- Dessinée **en vectoriel dans le code** (`Mascot.cs`) : aucune image embarquée, nette à
  toute taille, suit automatiquement l'accent indigo.
- Vérifiée en capture : sur cette machine (santé 48 %), Flux alerte bien.

## v14.34 — Les 40 fenêtres du menu ⋯ passent à l'indigo (+ 2 bugs d'affichage)
- **Le thème des fenêtres était resté vert.** `Theme.cs` avait sa **propre palette**, séparée
  du shell : fonds verdâtres, surlignage de menu vert, liseré signature vert→bleu. Le menu ⋯
  et ses ~40 fenêtres juraient donc avec le QG indigo. Tokens repris (`#08080C` / `#101018` /
  `#22222F`), surlignage de menu indigo, liseré signature **indigo → violet**.
- **Bug « & » corrigé** : « Bibliothèques **&** applis de jeu » s'affichait « Bibliothèques
  applis de jeu » — WinForms traite `&` comme un raccourci clavier dans les `Label`. Rendu
  littéral partout (`UseMnemonic = false`), vérifié en capture.
- **Listes blanches en thème sombre** : seule `CheckedListBox` était traitée, donc toute
  `ListBox` simple restait **blanche**. Le traitement couvre désormais la classe de base.
- **Chevauchement dans « Santé de mon PC »** : le texte d'explication (4 lignes quand il y a
  des points graves ET d'attention) débordait **sous** le graphique de tendance. Hauteur du
  libellé et position du graphique recalées.

## v14.33 — Présence Discord (parité FPSDoctor, la dernière)
- **Rich Presence Discord natif** : quand elle est activée, Fluide affiche « Optimise son PC ·
  avec Fluide » dans le statut Discord de l'utilisateur, avec logo et compteur de temps. C'était
  le **seul point de l'apparence FPSDoctor qui manquait** ; il est désormais couvert.
- **Zéro dépendance** : passe par l'IPC local de Discord (`discord-ipc-N`, canal nommé) au lieu
  d'embarquer une DLL tierce. Se tait proprement si Discord n'est pas lancé.
- **Activable / désactivable** depuis *Réglages système → Présence Discord*, choix persisté.
  Reste **inerte tant que l'App ID Discord n'est pas renseigné** (placeholder par défaut) : rien
  ne fuit et rien ne s'affiche tant que le vendeur n'a pas créé son app sur le portail Discord.
- Démarre automatiquement au lancement si l'option est cochée ; s'arrête à la fermeture.
- Harnais : build Release **réussi**, publication `dist` vérifiée (DLL + EXE générés).

## v14.32 — Identité couleur propre : l'indigo Fluide
- **Nouvel accent `#818CF8`** (indigo) à la place du vert `#00FF88` — qui était, d'après les
  commentaires du code, **le hex exact du concurrent**. Dernier morceau d'habillage repris,
  désormais retiré : nom, logo, thème et couleur sont maintenant tous propres à Fluide.
- Fonds légèrement bleutés (`#08080C`, cartes `#101018`, survol `#1A1A28`) : la palette tient
  ensemble au lieu d'un accent posé sur du noir pur.
- Appliqué partout d'un seul tenant : `FpsUi` (source unique), rail, anneau de santé, boutons,
  interrupteurs, overlay en jeu, badges, et les **boutons d'action des 43 fenêtres** classiques.
- **Icône `.ico` reforgée** en indigo (9 tailles) et **landing basculée** (thèmes clair et
  sombre, favicon, boutons) — l'app et le site sont enfin de la même couleur.
- Les couleurs **sémantiques sont préservées** : ambre pour l'alerte, rouge pour l'erreur.
- Harnais : 8 pages × 3 tailles, 40/40 fenêtres, 0 erreur.

## v14.31 — Finition esthétique (suite) : survols et courbes
- **États de survol / d'appui** : les boutons (fantôme et néon) s'éclaircissent au survol et
  s'enfoncent au clic ; les **cartes de statistiques** s'éclaircissent avec un liseré néon
  quand la souris passe dessus (suivi du survol sur la carte *et* ses enfants).
- **Graphe système lissé** : courbe en spline (tension 0,4) au lieu d'une ligne brisée, et
  **aire en dégradé** qui s'efface vers le bas — lecture plus douce, vraie profondeur.
- Commentaires du fichier d'interface corrigés : la palette et les polices sont décrites
  comme celles de **Fluide** (elles étaient décrites comme reprises d'un concurrent).
- *Non retenu volontairement* : l'entrée en glissé des cartes. En WinForms, déplacer des
  panneaux à fond transparent à chaque image provoque un scintillement visible — le résultat
  aurait été moins bon que pas d'animation du tout.

## v14.30 — Finition esthétique (le QG s'anime) + panneau Discord
- **Animation d'entrée du tableau de bord** : l'anneau de santé se **remplit** de 0 % à son
  score et les compteurs **montent** jusqu'à leur valeur (courbe ease-out, ~340 ms). La
  couleur de l'anneau reste celle du score final (pas de clignotement rouge→vert).
- **Respect de l'accessibilité** : animation désactivée si les effets visuels Windows sont
  coupés (`SystemInformation.UIEffectsEnabled`) — et sous le harnais, qui doit voir l'état final.
- **Panneau Discord** (☰ → ⚙ Réglages système) : démarrage avec Windows, réduction en zone
  de notification, démarrage réduit — lus dans la vraie configuration de Discord, écrits avec
  **sauvegarde `.bak`**, entièrement réversibles. Affiche si Discord tourne et sa mémoire.
  **Honnête** : accélération matérielle, overlay en jeu et QoS ne sont pas exposés hors de
  Discord — le panneau le dit et t'y renvoie au lieu de faire semblant.
- Harnais : **40/40 fenêtres** (Discord inclus), 8 pages × 3 tailles, 0 erreur.

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
