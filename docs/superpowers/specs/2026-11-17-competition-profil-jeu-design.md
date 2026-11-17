# Profil par jeu, topologie du processeur et bouton Compétition — conception (phase 3)

Approche choisie par l'utilisateur : **« Améliorer et prouver »**. On améliore l'existant au
lieu d'empiler des fonctions, et chaque changement visible en jeu se juge avec la mesure en jeu
avant/après de la phase 2C.

## Intention

Le but est de gagner en latence et en régularité des FPS pendant une partie, sans régler
quoi que ce soit à la main, et sans rien garder qui fasse baisser les FPS.

### Constat dans le code (17/11/2026)

1. **Le profil par jeu ne marche que dans une fenêtre secondaire.**
   - Ce qu'il fait : affinité, priorité Haute et plan « Performances », avec le plan
     d'origine restauré à la sortie du jeu.
   - Où il tourne : dans le minuteur de `MainForm` (`src/MainForm.cs:1583-1630`), la fenêtre
     « Optimiseur complet ».
   - La fenêtre principale (`DashboardForm`) ne le déclenche jamais.
2. **Le mode « Automatique » n'est pas ce qu'il annonce.**
   - Son libellé dit « Évite Core 0 / E-Cores ».
   - En réalité, il calcule « tous les cœurs sauf le 0 » une fois pour toutes, à
     l'enregistrement (`src/GameProfileForm.cs:237`).
   - Aucune détection des cœurs P/E d'Intel, ni du CCD à V-Cache des Ryzen X3D.
3. **Le plan d'alimentation d'origine n'est gardé qu'en mémoire.** Si ONYX se ferme pendant
   la partie, il n'est jamais rétabli.
4. **Il n'y a pas de geste unique « Compétition ».** « Tout rétablir » (`Engine.Run(all,false,…)`)
   annule tout, pas seulement ce qu'un geste a changé.

### Objectifs

- Un profil par jeu actif dès qu'ONYX tourne, appliqué au lancement du jeu et retiré à sa sortie.
- Un « Auto » qui suit la vraie topologie du processeur.
- Un bouton « Compétition » qui active tout d'un clic, avec un « Annuler Compétition » qui
  remet exactement l'état d'avant.

### Non-objectifs

- Déplacer les processus de fond sur les cœurs E. C'est écarté : cela impose une affinité à
  des logiciels tiers, sans preuve de gain.
- Piloter NvAPI ou Reflex directement.

### Anti-objectifs

- Brider un jeu sans pouvoir le défaire.
- Garder un réglage que la mesure en jeu montre nuisible.
- Laisser le PC dans un état modifié après un plantage d'ONYX.

## 1. Topologie du processeur (`src/Topologie.cs`)

Le fichier a deux parties : une **partie pure**, testée, qui décide, et une **partie machine**
qui lit le processeur.

### Partie machine

`GetLogicalProcessorInformationEx` fournit, pour chaque processeur logique :
- l'`EfficiencyClass` du cœur (relation `RelationProcessorCore`) ;
- le cache L3 et sa taille (relation `RelationCache`, niveau 3).

`GetSystemCpuSetInformation` fait le lien entre l'index logique et l'identifiant de CPU Set.

### Règle « Auto » (partie pure)

| Cas | Cœurs préférés pour le jeu |
|---|---|
| `EfficiencyClass` différentes (Intel hybride) | les cœurs de classe la plus haute (cœurs P) |
| Sinon, plusieurs groupes de cache L3 de tailles différentes (Ryzen X3D à deux CCD) | le groupe au plus grand cache L3 |
| Sinon (processeur homogène) | tous sauf le cœur physique 0 (les deux processeurs logiques s'il y a l'hyperthreading). C'est le comportement d'aujourd'hui, corrigé pour l'hyperthreading. |
| Un seul cœur, ou lecture impossible | aucune restriction |

### Ce qui change pour « Auto »

- La règle est **calculée au moment d'appliquer**, plus enregistrée sous forme de masque :
  un masque enregistré devient faux si le processeur change.
- Elle est appliquée par **CPU Sets** (`SetProcessDefaultCpuSets`) et non par une affinité
  stricte :
  - c'est une préférence ;
  - les fils d'exécution qui fixent leur propre affinité ne sont pas forcés ;
  - elle disparaît avec le processus.
- Si `SetProcessDefaultCpuSets` échoue (par exemple, refus d'un anti-triche), ONYX l'écrit au
  journal et **ne retombe pas** sur une affinité stricte.

Le mode « Manuel » (choix des cœurs par l'utilisateur) garde l'affinité stricte.

## 2. Profil par jeu dans la fenêtre principale (`src/ProfilJeu.cs`)

Le code de `MainForm` est extrait dans un module partagé. Ce module est appelé toutes les 2 s
par le minuteur de `DashboardForm`, déjà en place pour `AutoJeu.Tick`. `MainForm` appelle le
même module, ce qui supprime le doublon.

### Au lancement d'un jeu qui a un profil

1. Le jeu reçoit ses cœurs (« Auto » ou « Manuel ») et la priorité Haute.
2. ONYX passe sur le plan « Performances ».
3. Le plan d'origine est écrit dans `bt-profiljeu-etat.txt` **avant** la bascule.

### À la sortie du jeu

ONYX rétablit le plan d'origine et efface le fichier d'état.

### Au démarrage d'ONYX

Si le fichier d'état existe (ONYX s'est fermé pendant une partie), ONYX rétablit le plan, comme
`GameBoost.Soigne` le fait pour le Mode Jeu.

### Tests de la partie pure

- Choix du jeu à profiler parmi les processus.
- Transitions lancement et sortie.
- Lecture et écriture du fichier d'état.

## 3. Bouton « Compétition » (`src/Competition.cs` + fenêtre)

Le bouton se trouve sur la page Optimisations, à côté d'« Optimiser et prouver ». La fenêtre
propose des cases à cocher :

| Case | Par défaut | Effet |
|---|---|---|
| Profil « Auto » pour tous les jeux reconnus sans profil | cochée | §1 et §2 sur les jeux de `GameScan.Known()` |
| Mode Jeu automatique | cochée | `AutoJeu.Actif = true` |
| Réglages eSport | cochée | réglages marqués `Esport`, appliqués **avec la preuve au repos** de la phase 2A (`PreuveRun` limité à cette sélection) |
| Plafond FPS sous la fréquence de l'écran (NVIDIA, écran G-Sync/FreeSync) | **décochée** | `FrameCap.Applique(FrameCap.Recommande(hz))` |

### Activation

1. Un point de restauration est **toujours** créé avant.
2. L'état d'avant de chaque élément est écrit dans `bt-competition.txt` :
   - Mode Jeu automatique : activé ou non ;
   - plafond FPS d'avant ;
   - profils ajoutés ;
   - réglages réellement appliqués.

### « Annuler Compétition »

Ce bouton remet **exactement** cet état-là :
- seulement les réglages qu'il a appliqués, pas les autres ;
- seulement les profils qu'il a ajoutés, pas ceux de l'utilisateur.

### Lien avec la preuve en jeu

- Les réglages appliqués par Compétition sont ajoutés à « la dernière preuve »
  (`PreuveJeu.EnregistreDernierePreuve`).
- La fenêtre rappelle l'ordre conseillé : « Mesure en jeu — avant », puis Compétition, puis
  « Mesure en jeu — APRÈS ».
- Si une baisse est confirmée en jeu, la fenêtre de preuve propose aussi « Annuler
  Compétition » quand Compétition est active.

### Le plafond FPS et la mesure en jeu

Un plafond baisse les FPS par construction. Le plafond actif est donc relevé dans chaque
mesure en jeu. Si le plafond diffère entre « avant » et « après », le verdict est
**Incomparable**, avec l'explication. C'est pour cela que la case est décochée par défaut.

## Décisions de construction

Points soulevés par la relecture. Ce sont des choix du concepteur, que l'utilisateur peut corriger.

1. **Profils existants.**
   - Le format `jeu|mode|masque` de `bt-game-affinity.txt` est gardé ; il reste compatible avec
     `MainForm`.
   - Pour une ligne « Auto », le masque est **ignoré** et la règle §1 s'applique. C'est ce que
     le libellé promettait déjà.
   - « Manuel » ne change pas.
2. **Profils ajoutés par Compétition.**
   - `bt-competition.txt` liste les jeux ajoutés.
   - Au moment d'annuler, le profil n'est retiré que s'il vaut encore « Auto ». S'il a été
     modifié entre-temps, l'utilisateur en a fait le sien : ONYX le garde.
3. **Jeux concernés.** Seulement les jeux `Detected` (installés), pas tout le catalogue.
4. **Réglages eSport.**
   - On prend `Gardes` et `EnAttente` du rapport `PreuveRun`, c'est-à-dire ce qui reste
     appliqué.
   - Un réglage déjà actif avant n'est pas compté (`DejaActifs`).
   - L'annulation passe par `Engine.Run(liste, false, true, false, log)`.
5. **Plafond d'avant.**
   - C'est la valeur `FrameCap.Pose()` (le plafond posé par ONYX, 0 s'il n'y en a pas).
   - L'annulation la rétablit (`Applique(valeur)`, ou retrait si 0).
   - Limite : un plafond réglé à la main dans le panneau NVIDIA n'est pas lisible. Il serait
     remplacé, comme aujourd'hui avec `FrameCap.Applique`.
6. **Plafond dans la mesure en jeu.**
   - Nouveau champ `Plafond` dans `MesureJeu`, avec `-1` = inconnu. Un ancien fichier sans ce
     champ vaut `-1`.
   - Incomparable seulement si les deux valeurs sont connues et différentes.
   - Tests ajoutés dans `PreuveTests`.
7. **Plusieurs jeux en même temps.**
   - Un seul jeu profilé à la fois : le premier trouvé, comme aujourd'hui.
   - Ses cœurs et sa priorité s'appliquent à **tous** les processus de ses exécutables.
   - Si le plan actif est déjà « Performances », ONYX ne bascule pas et n'écrit aucun état.
8. **Mode Jeu et plan d'alimentation.** `GameBoost` ne touche pas au plan d'alimentation
   (recherche de `powercfg` / `PowerProfile` négative). Le profil par jeu est donc seul à le
   changer.
9. **Deux fenêtres.**
   - `ProfilJeu` garde un état statique unique, protégé par un verrou, avec un drapeau « geste
     en cours » comme `AutoJeu`.
   - `MainForm` et `DashboardForm` peuvent l'appeler toutes les deux sans double application.

## Approches écartées

- Envoyer les processus de fond sur les cœurs E : risque sur des logiciels tiers, sans preuve
  de gain.
- Affinité stricte pour « Auto » : force aussi les fils d'exécution que le jeu place lui-même,
  et rien ne la défait si ONYX plante. Les CPU Sets sont plus doux.
- Compétition = « Tout rétablir » pour annuler : effacerait des réglages que l'utilisateur
  avait choisis avant.

## Limites [VERIFICATION REQUIRED]

- Réserver les cœurs P peut faire baisser les FPS des jeux qui utilisent beaucoup de fils
  d'exécution. La mesure en jeu avant/après sert à le détecter.
- La réaction des anti-triche (Vanguard, EAC, BattlEye) aux CPU Sets ou à la priorité imposés
  par un autre programme n'est pas documentée dans le dépôt. Un refus est seulement journalisé.
- Sur les Ryzen X3D, le pilote chipset AMD et la Game Bar orientent déjà les jeux vers le CCD
  à V-Cache. Les CPU Sets d'ONYX vont dans le même sens et ne s'y opposent pas, mais leur
  apport n'est pas mesuré.
- Le plan « Performances » (`8c5e7fda…`) n'existe pas toujours (portables en veille
  moderne). L'échec est journalisé, sans effet.

## Laissé au constructeur

- Disposition de la fenêtre Compétition.
- Le nom et le format clé=valeur des fichiers d'état (même style que `Preuve.Ecrit`).
- L'ordre des appels Win32 et la gestion des machines à plus de 64 processeurs logiques
  (groupes) : au minimum, ne rien appliquer plutôt qu'appliquer un masque faux.

## Réalisation

- `src/Topologie.cs` (pur, `tests/TopologieTests.cs`) et `src/TopologieMachine.cs` (lecture
  GLPIEx, CPU Sets) ; commande `-topologie` dans `src/Cli.cs`.
- `src/ProfilJeu.cs` (pur, `tests/ProfilJeuTests.cs`) et `src/ProfilJeuMachine.cs` ; appelé par
  `DashboardForm` (Tick toutes les 2 s, `Arrete` à la fermeture) et `MainForm` (minuteur de garde).
- `src/Competition.cs` (pur, `tests/CompetitionTests.cs`), `src/CompetitionMachine.cs`,
  `src/CompetitionForm.cs` ; bouton dans `src/PageOptimisations.cs` ; branche Régression de
  `src/PreuveForm.cs` ; surcharge `PreuveRun.Lance(…, idsImposes)`.
- `MesureJeu.Plafond` dans `src/Preuve.cs`, relevé dans `src/PreuveJeu.cs`.

Écarts et choix du constructeur :
- Le mode manuel est écrit « Manual » par `GameProfileForm` : `ProfilJeu.Cible` accepte
  « Manual » et « Manuel » (le plan ne citait que « Manuel »).
- L'état de la Compétition est réécrit après chaque étape, pas seulement au début et à la fin.
- Plafond actuel illisible ou plafond recommandé impossible : le plafond n'est pas touché.
- L'annulation supprime l'état même si des réglages n'ont pas pu être rétablis (message et
  sauvegarde .reg, comme l'annulation de la preuve).
- Bascule vers « Performances élevées » vérifiée par relecture ; plan absent → journal, plan
  inchangé, rien à rétablir.
- Page Optimisations : quand la place manque, la recherche puis le bouton Compétition passent
  sur une deuxième ligne, alignés à droite.
- Revue finale : pas de « Soigne » au lancement (il effaçait l'état d'une partie en cours).
  Le premier tour relit l'état : même jeu en cours → état adopté, plan d'origine compris ;
  sinon plan d'origine rétabli. Fermer ONYX rétablit le plan tout de suite.
- GUID du plan lu par motif, indépendant de la langue de Windows. « Performances optimales »
  n'est jamais remplacé par « Performances élevées ».
- Compétition : `ModeJeuPose` (et non l'état d'avant) — l'annulation n'éteint que le Mode Jeu
  qu'elle a allumé, après en être sortie. Profils posés seulement sur les jeux à exécutable
  connu ; listes séparées par « | » (des noms de jeux contiennent une virgule).
- Non vérifié en direct (l'application exige les droits administrateur) : `-topologie`, le
  profil appliqué en jeu, l'activation et l'annulation de la Compétition, la mise en page.
