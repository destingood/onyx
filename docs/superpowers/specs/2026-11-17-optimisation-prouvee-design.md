# Optimisation prouvée — conception (phase 2, approche A puis C)

Décidé avec Lucas le 17/11/2026 : approche **A** (automatique, sans jeu) maintenant, puis **C**
(A + validation en jeu facultative). Ce document couvre A et réserve la place de C.

## Intention

ONYX applique aujourd'hui des réglages sans prouver leur effet. Le but : ne garder sur chaque PC
que ce qui ne dégrade pas la latence mesurée, et dire pour chaque réglage s'il a un gain mesuré,
un effet neutre, ou une régression annulée. Aucun chiffre de gain promis à l'avance.

- **Objectif** : un clic → mesure de référence → application par lots → nouvelle mesure →
  annulation automatique de ce qui dégrade → rapport.
- **Non-objectif** : mesurer les FPS sans jeu (impossible) ; ajouter des réglages (phase 3).
- **Anti-objectif** : annuler un réglage parce qu'il est « sans gain » visible. Beaucoup de
  réglages (Game DVR, optimisations plein écran…) n'agissent qu'en jeu : sans jeu ils paraissent
  neutres. **On n'annule que sur régression confirmée.**

## Mesure (sans jeu, environ 15 s)

Faite en même temps : session ETW noyau en direct (`EtwLive`) + compteurs PDH et gigue de
`Sleep(1)` (`Bench.Run`).

| Indicateur | Source | Pourquoi |
|---|---|---|
| Charge DPC+ISR (µs par seconde) | somme `DpcTotalUs + IsrTotalUs` / durée | stable, principal |
| Pire DPC/ISR (µs) | `WorstUs` | pics de latence pilote |
| Sleep(1) moyen et pire (ms) | `Bench` | réactivité de l'ordonnanceur |

Sans ETW (droits, pilote) : mesure dégradée sur les seuls compteurs PDH et la gigue, signalée.

## Verdict (logique pure, testée)

Régression si au moins un indicateur dépasse sa marge de bruit :
charge +15 % et +50 µs/s ; pire +30 % et +150 µs ; Sleep moyen +0,15 ms ; Sleep pire +1 ms.
Gain si au moins un baisse d'autant et aucun ne régresse. Sinon neutre.
**Toute régression est re-mesurée une fois** avant d'être retenue (filtre le bruit).

## Déroulé

1. Candidats : sélection « Auto adaptée au PC » (niveau équilibré), réglages pas encore actifs.
2. Sauvegarde .reg de tous les candidats + point de restauration, une seule fois.
3. Mesure de référence.
4. Réglages sans redémarrage, **un lot par catégorie** : appliquer → attendre 3 s → mesurer →
   verdict. Régression confirmée → annuler le lot, puis **recherche par moitiés** pour ne
   retirer que le ou les réglages fautifs. Chaque lot gardé devient la nouvelle référence.
5. Réglages à redémarrage : appliqués en un lot, mis « en attente de preuve » (fichier
   `bt-preuve-attente.txt`, clé=valeur : référence + liste). Au premier lancement d'ONYX après
   le redémarrage, une fois Windows démarré depuis au moins 3 min : mesure, verdict ; régression
   confirmée → lot annulé, redémarrage demandé.
6. Rapport : chaque réglage avec son statut, les mesures avant/après.

## Accès

- Page Optimisations : bouton « Optimiser et prouver ».
- Ligne de commande : `BTOptimizer.exe -prove` (code retour 0, 4 échec, 10 redémarrage à faire).
- Annulable : un bouton Arrêter stoppe entre deux lots ; un lot commencé est jugé jusqu'au bout
  (aucun réglage ne reste appliqué sans verdict). Pendant une recherche du fautif, l'arrêt est
  honoré avant chaque essai : les moitiés non essayées restent rétablies (« non appliquées »).
  En cas d'erreur, le lot non jugé est annulé.
- Un réglage dont la clé de registre n'a pas pu être sauvegardée est écarté du déroulé.

## Approches écartées

- B seul (en jeu) : manuel, bruyant ; gardé comme étape facultative de C.
- Annuler si « pas de gain » : retirerait à tort les réglages qui n'agissent qu'en jeu.

## Laissé au constructeur

Seuils exacts ajustables dans une seule classe ; durée de mesure (15 s) ; présentation du rapport.

## Réalisation

- `src/Preuve.cs` : verdict, seuils, recherche par moitiés, fichier d'attente (logique pure,
  `tests/PreuveTests.cs`).
- `src/PreuveRun.cs` : déroulé sur la machine ; `src/PreuveForm.cs` : fenêtre.
- Choix du constructeur : fichier d'attente en clé=valeur plutôt que JSON (aucune dépendance,
  testable) ; 12 s de compteurs + ~1 s de test Sleep(1) par mesure ; sans ETW, la charge vient
  des compteurs « % DPC/Interrupt Time » et n'est jamais comparée à une mesure ETW.

## Phase C — validation en jeu (réalisée)

Choix de l'utilisateur : mesure « jeu avant et après ».

- Bouton « 🎮 Mesure en jeu » dans la fenêtre Optimiser et prouver. Étape « avant » tant
  qu'aucune preuve n'a eu lieu depuis la dernière mesure « avant », sinon étape « APRÈS ».
- Mesure (`src/PreuveJeu.cs`) : attend jusqu'à 5 min qu'un jeu (`FpsEtw.ChoisirJeu`) soit au
  premier plan, puis compte 60 s **au premier plan avec images**. Pendant un alt-tab, la mesure
  est en pause et les images sont jetées (`FpsEtw.NouvellesImages`, repère atomique). Abandon
  si le jeu se ferme, après 15 min au total, ou sur Arrêter. À l'étape « après », seul le jeu
  de la mesure « avant » est accepté.
- Verdict (`Preuve.JugeJeu`, testé) :
  - Incomparable si une mesure a moins de 1000 images ou moins de 30 s, ou si le jeu diffère ;
  - Régression si le 1 % low baisse de 10 % ou plus, ou si les FPS moyens baissent de 7 % ou plus ;
  - Gain si l'un des deux monte d'autant ;
  - Neutre sinon.
- Une régression est confirmée par une seconde mesure de 60 s. Si elle est confirmée, ONYX
  propose d'annuler les réglages laissés appliqués par les preuves faites depuis la mesure
  « avant » (`bt-preuve-derniere.txt`, cumulé si plusieurs preuves). Il ne rétablit que les
  réglages encore actifs (`Check() != false`). Gain, neutre ou refus : les réglages sont gardés.
  Dans tous ces cas, le cycle est clos (le fichier « avant » est supprimé).
- Incomparable : rien n'est modifié et le fichier « avant » est conservé pour retenter la mesure.
- Mesure « après » refusée tant qu'un réglage attend sa vérification après redémarrage.
- Limite : les seuils sont des valeurs de départ, non calibrées sur machine réelle
  [VERIFICATION REQUIRED]. Le bruit d'une scène de jeu dépend de l'utilisateur, d'où la
  consigne « même jeu, même type de scène ».
