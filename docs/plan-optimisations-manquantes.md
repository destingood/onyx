# ONYX — optimisations manquantes à gain prouvé (plan du 17/11/2026)

## Synthèse

ONYX a déjà **210 réglages** dans son catalogue (`src/Tweaks.cs`). Côté registre et services, il
ne manque rien qui ait un gain mesuré et publié : la recherche n'a trouvé **aucun test chiffré**
pour la plupart des réglages de ce type (Nagle, MSI, priorités, minuteur, services).

Les gains prouvés qui manquent sont des **vérifications** : ONYX ne voit pas encore certaines
causes de perte mesurées (RAM en simple canal, Ryzen X3D mal réglé, Windows ou BIOS en retard).
Elles ne modifient rien sur le PC, donc elles ne peuvent pas créer de problème.

Le plan tient en 9 ajouts, en 3 phases, plus une liste de choses à **ne pas** ajouter.

Limite : la plupart des chiffres viennent de résumés de recherche, les pages d'origine n'ont pas
toutes été relues. Chaque ligne marquée [VERIFICATION REQUIRED] est à relire à la source avant
d'écrire un chiffre dans l'application.

## Ce qui existe déjà (à ne pas refaire)

| Sujet | Où |
|---|---|
| RAM sous sa vitesse nominale (XMP/EXPO) | `src/Diagnostics.cs:73` |
| Écran sous sa fréquence maximale | `src/Diagnostics.cs:238` |
| Intégrité de la mémoire (VBS/HVCI) signalée | `src/Diagnostics.cs:177` |
| Pilote GPU ancien | `src/Diagnostics.cs:170` |
| Plafond d'images sous la fréquence de l'écran | réglage `frame_cap` |
| Capture Xbox en arrière-plan, applis de fond, logiciels RGB | `gamedvr_off`, `src/ApplisDeFond.cs`, `src/PolleursDeCapteurs.cs` |
| Réglages néfastes (fichier d'échange coupé, HPET forcé) | `src/CheckupForm.cs:207-364` |
| Mesure avant/après et annulation des régressions | `src/Preuve.cs`, `src/PreuveJeu.cs` |
| Cœurs préférés par jeu (Intel P/E, X3D) | `src/Topologie.cs` |

## Développement — les 9 ajouts

### Phase 1 — vérifications sans risque, gain mesuré

**État au 17/11/2026** : ajouts 1, 3 et 4 codés (`src/ConstatsMateriel.cs`, branchés dans
`src/Diagnostics.cs`, 40 tests). Ajout 2 codé sans mesure en jeu, sur la seule
recommandation d'AMD rapportée par IDC et XDA : `power_ultimate` perd ses marques
Recommandé/eSport sur X3D à deux CCD (`src/Tweaks.cs`), constat pilote / plan / mode Jeu.
`cpu_unpark_cores` gardé : il règle la valeur de base du plan (100 % par défaut sur le
9950X3D testé), pas le profil du mode Jeu où AMD gare les cœurs. `xbox_services_off` n'est
dans aucune sélection d'office. `gamedvr_off` et `gamedvr_policy_off` écartés aussi sur ces
processeurs (la détection des jeux passe par la Game Bar ; sources secondaires : Windows
Forum citant Notebookcheck et AMD, forum Overclockers UK) ; le constat signale la stratégie
`AllowGameDVR=0`, et s'affiche sur le 9950X3D testé, où elle était posée. Reste
[VERIFICATION REQUIRED] : la page AMD d'origine (recherches sur amd.com sans résultat).
La mesure en jeu (Équilibré contre Performances ultimes, Game DVR coupé ou non) reste utile
pour confirmer. Écart avec
le tableau : le constat HAGS renvoie vers Optimisations par son texte, sans bouton. Vérifié
sur un seul PC (2 barrettes, HAGS actif, Windows 25H2), où aucun des trois ne s'affiche ; les
cas positifs ne sont couverts que par les tests.

| # | Ajout | Gain publié | Ce qu'ONYX fait | Effort |
|---|---|---|---|---|
| 1 | **RAM en simple canal** | TechSpot (déc. 2025, Ryzen 7 9700X + RTX 5090) : de 0 à −23 % de FPS selon le jeu, jusqu'à −33 % sur le 1 % low. [Source](https://www.techspot.com/article/3066-single-stick-vs-dual-channel-ram/), chiffres lus par extraction automatique | Constat dans le diagnostic quand une seule barrette est installée. Aujourd'hui : texte d'aide seulement (`src/KnowledgeBase.cs`) | Faible |
| 2 | **Garde Ryzen X3D à deux CCD** (7900X3D, 7950X3D, 9900X3D, 9950X3D) | Pas de chiffre indépendant. Mécanisme : le pilote AMD gare le CCD sans V-Cache pendant un jeu, ce qui demande le mode Jeu, la Game Bar et le plan Équilibré. [VERIFICATION REQUIRED] : page d'aide AMD « 3D V-Cache Performance Optimizer » | Ne plus proposer d'office `cpu_unpark_cores`, `power_ultimate` et `xbox_services_off` sur ces processeurs ; constat si le pilote `amd3dvcache` manque. Aujourd'hui : aucune garde (`src/Hardware.cs`, `src/Tweaks.cs:1504`) | Moyen |
| 3 | **Windows en retard sur Ryzen 7000/9000** | Hardware Unboxed (été 2024) : environ +10 % avec Windows 11 24H2. [Source secondaire](https://gazlog.jp/entry/windows11-24h2-ryzen-boost/) [VERIFICATION REQUIRED] | Constat si le build est antérieur à 24H2 sur ces processeurs et que la mise à jour KB5041587 manque (elle apporte l'optimisation à 22H2 et 23H2 ; seuil de révision 4112 [VERIFICATION REQUIRED] : page Microsoft de KB5041587) | Faible |
| 4 | **Planification GPU (HAGS) coupée sur RTX 40/50** | Pas un gain de FPS direct : la génération d'images DLSS ne fonctionne pas sans. [Source](https://support.cdprojektred.com/en/witcher-3/pc/sp-technical/issue/2278/dlss-frame-generation-how-to-enable-hardware-accelerated-gpu-scheduling) | Constat avec bouton vers le réglage `hags` existant | Faible |

### Phase 2 — table des problèmes connus

| # | Ajout | Gain publié | Ce qu'ONYX fait | Effort |
|---|---|---|---|---|
| 5 | **Mises à jour et pilotes fautifs connus** | Mise à jour KB5066835 (oct. 2025) avec carte NVIDIA : jusqu'à −50 % dans un jeu, corrigé par le pilote 581.94. [Source](https://www.notebookcheck.net/Benchmark-shows-Windows-11-update-dropped-frame-rates-by-50-before-Nvidia-hotfix-driver.1170810.0.html) | Une table de données (build Windows + version de pilote → conseil), facile à tenir à jour | Moyen |
| 6 | **BIOS ancien sur AM5** | AGESA 1.2.0.2b sur 9800X3D (fin 2024) : +1 à +7 % de FPS. [Source](https://gazlog.jp/entry/amd-newbios-ryzen9000-performance-improvement/) [VERIFICATION REQUIRED] | Conseil seulement (date du BIOS déjà lue dans `src/ComponentInfo.cs`). Jamais de mise à jour automatique | Faible |
| 7 | **Intel APO absent** (Core 14e génération et Core Ultra) | Intel (sept. 2025) : jusqu'à +14 % sur une liste de jeux. Chiffres du constructeur, non reproduits. [Source](https://www.tomshardware.com/pc-components/cpus/intel-apo-update-boosts-gaming-performance-by-up-to-14-percent-in-select-titles-15-new-games-added-to-the-support-list) | Conseil avec lien quand le processeur est compatible et l'outil absent | Faible |

### Phase 3 — par jeu

| # | Ajout | Gain publié | Ce qu'ONYX fait | Effort |
|---|---|---|---|---|
| 8 | **Reflex / Anti-Lag 2 coupé dans le jeu** | TechPowerUp : 82 → 35 ms sur Overwatch, quand la carte graphique est saturée ; aucun gain sinon. [Source](https://techpowerup.com/review/nvidia-reflex-review-test-ldat-v2/3.html) [VERIFICATION REQUIRED] | Lire le fichier de réglages des jeux compatibles (lecture seule) et signaler si l'option est coupée. Clés par jeu à établir [VERIFICATION REQUIRED] | Élevé |
| 9 | **Resizable BAR coupé** | TechPowerUp (2021) : +2 à +4 % en moyenne, parfois négatif selon le jeu. [Source](https://techpowerup.com/review/nvidia-pci-express-resizable-bar-performance-test/27.html) [VERIFICATION REQUIRED] | Constat d'information + guide BIOS existant. Lecture fiable de l'état à confirmer [VERIFICATION REQUIRED] | Moyen |

## À ne pas ajouter

| Idée | Raison |
|---|---|
| Nettoyeur de RAM (vidage de la liste d'attente) | Aucune mesure publiée |
| Vidage régulier du cache de shaders | Aucune mesure ; provoque des saccades de recompilation |
| Fichier d'échange coupé ou fixé | Plantages signalés, déconseillé par Microsoft (déjà détecté comme néfaste) |
| Plus de services coupés, « debloat » | Aucun test indépendant |
| Réinstallation systématique du pilote (DDU) | Aucune mesure |
| Atténuations Spectre coupées | Gain nul à faible sur processeur récent, risque de sécurité (réglage déjà présent, hors préréglages) |
| Auto HDR, mise à l'échelle, DNS | Aucune mesure sur les FPS ou la latence |

## Plan d'action

1. Phase 1 dans l'ordre 1, 4, 3, 2 : les trois premiers sont des constats ajoutés à
   `Diagnostics.cs`, avec un test chacun dans `tests/`.
2. Ajout 2 (X3D) : mesurer d'abord sur le 9950X3D de Lucas avec « Mesure en jeu »
   (plan Équilibré contre Performances ultimes, cœurs déparqués ou non), puis coder la garde
   selon le résultat.
3. Phase 2 : écrire la table des problèmes connus comme un fichier de données, pas du code.
4. Phase 3 : commencer par Fortnite et un second jeu, puis étendre.

## Optimisation & prochaines étapes

- **Décision à prendre (ajout 2)** : sur un X3D à deux CCD, qui choisit les cœurs — le pilote
  AMD, ou les CPU Sets d'ONYX (`src/Topologie.cs`) ? Les deux ensemble ne sont pas testés.
- **Étiquette de preuve** : plusieurs réglages du préréglage eSport n'ont aucune mesure publiée
  (`nagle`, `netthrottle`, `w32ps`, `timer_global`, `gpu_msi`). Afficher pour chaque réglage
  « mesuré », « dépend du PC » ou « non mesuré » rendrait le catalogue honnête sans rien retirer.
- **Non vérifié** : l'état actuel des filtres de jeu de l'application NVIDIA (−15 % fin 2024,
  désactivés par défaut depuis) ; la page Microsoft actuelle sur l'intégrité de la mémoire.
