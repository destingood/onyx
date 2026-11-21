# ONYX — latence et FPS au niveau de Linux, en réversible (plan du 20/11/2026)

## Synthèse

Trois faits ressortent de la recherche, et ils changent la cible :

1. **FPS moyens : sur un PC de bureau à carte graphique dédiée, Windows 11 est à égalité ou
   devant Linux en 2026.** L'avance de Linux se voit surtout sur les consoles portables, les
   puces graphiques intégrées et les machines à peu de mémoire.
2. **Latence : Windows est devant par défaut.** La seule comparaison mesurée trouvée montre
   Linux derrière, et rattrapant l'écart après réglage du compositeur.
3. **Ce que Linux fait mieux, quand il le fait : la régularité** (moins de charge de fond,
   shaders précompilés par Steam). C'est là qu'il reste du gain à prendre sous Windows.

Conséquence : promettre « les mêmes FPS que Linux » n'a pas de sens sur le PC visé, ils y sont
déjà. Le plan vise donc (a) retirer ce que Windows ajoute et que Linux n'a pas, (b) prouver
chaque gain sur le PC avec la mesure déjà présente dans ONYX, (c) rendre chaque réglage
**exactement** annulable.

Le point (c) est le vrai manque face à la contrainte « que du réversible » : aujourd'hui
« Rétablir » écrit une valeur fixe, pas la valeur d'avant. Exemple constaté :
`cpu_unpark_cores` remet 10 % (`src/Tweaks.cs:1519`) alors que le 9950X3D de test était à
100 % avant.

Le plan tient en **12 ajouts, 4 phases**, plus une liste de choses à ne pas ajouter.

**Hypothèses retenues** (à corriger si fausses) :
- Cible : tous les utilisateurs d'ONYX, vérification sur le PC de Lucas (9950X3D, GeForce RTX).
- « N'impacte pas des choses » = aucune perte de sécurité, de fonction ou de données, aucun
  risque anti-triche, aucune application fermée sans être rouverte.
- Code en C#, dans l'existant (pas de réécriture).

**Limite** : les chiffres ci-dessous ont été lus par extraction automatique des pages, pas par
relecture intégrale. Les lignes marquées [VERIFICATION REQUIRED] n'ont pas pu être lues à la
source.

## Ce que disent les mesures

| Sujet | Mesure publiée | Source | Conséquence pour ONYX |
|---|---|---|---|
| FPS Windows contre Linux, bureau | Ryzen 7 9850X3D + RX 9070 XT, 16 Go : Windows devant de 5 à 9 % en moyenne (Cyberpunk 2077, Arc Raiders, Marvel Rivals). La phrase sur les 1 % low d'Arc Raiders est ambiguë dans l'article | [TechPowerUp, 15/07/2026](https://www.techpowerup.com/350791/windows-11-vs-linux-gaming-test-reveals-theres-more-to-it-than-fps), test de Meta PCs | Pas de retard à combler sur la moyenne |
| FPS, carte dédiée AMD | L'avance de SteamOS « disparaît en grande partie et s'inverse parfois » sur carte dédiée ; chute sur carte 8 Go en ray tracing | [TechSpot, déc. 2025](https://www.techspot.com/news/110525-benchmarks-show-windows-can-outperform-steamos-dedicated-gpus.html), d'après Ars Technica ; pas de chiffres par jeu sur la page | Idem |
| FPS, GeForce sous Linux | Anomalies de régularité (1 % et 0,1 % low) sur RTX 50 dans plusieurs jeux ; test non comparable à Windows selon l'auteur | [GamersNexus, 05/12/2025](https://gamersnexus.net/gpus/rip-windows-linux-gpu-gaming-benchmarks-bazzite) | Sur GeForce, Linux n'est pas la référence |
| Ordonnanceur Linux (BORE, sched-ext) | Aucun test publié n'isole son effet en jeu ; le développeur de CachyOS répond « pas grand-chose en jeu » | [Cloudzy, 2026](https://cloudzy.com/blog/is-cachyos-actually-faster/) | Rien à copier côté ordonnanceur |
| Latence clic → image | Windows « régulièrement plus bas » que Linux (Doom Eternal, Borderlands 3, 120 Hz, RTX 40 + Zen 4) ; l'écart se réduit après réglage de KWin | [farnoy.dev, 07/06/2026](https://farnoy.dev/posts/linux-latency) | Pas de retard à combler |
| Latence Linux, meilleur cas | 4,2 à 4,9 ms à 500 Hz ; XWayland ajoute 3,13 ms | [Marco Nett, 13/07/2026](https://marco-nett.de/blog/measuring-input-latency-on-linux-x11-vs-wayland-vrr-dxvk/) | La couche de composition compte : même logique sous Windows (ajout 6) |
| Mode Xbox (plein écran) | Ryzen 5 9600X + RTX 5060 Ti : aucun gain de FPS (marge d'erreur). Mémoire : 194 à 302 Mo de moins en jeu ; au repos 608 à 665 Mo de moins que l'application Xbox, 1 081 à 1 296 Mo de moins qu'avec Steam ouvert | [LTT Labs, 14/06/2026](https://www.lttlabs.com/articles/2026/06/14/we-tried-xbox-mode) | Conseil réservé aux PC à peu de mémoire (ajout 10) |
| Mode MSI | Core i9-14900F + RX 7800 XT : carte graphique, USB et NVMe **déjà en MSI d'origine**. Seul l'audio sur ligne partagée change : 120,0 → 120,7 FPS, 1 % low 9,04 → 8,76 ms. Carte graphique forcée hors MSI : DPC p99 8,5 → 116 µs | [RigPolice, 19/09/2026](https://rigpolice.com/system/articles/enable-msi-mode/) | `gpu_msi`, `msi_usb`, `msi_storage`, `msi_network` ne font rien sur un PC récent (ajout 4) |
| Résolution du minuteur à 0,5 ms | Latence **plus haute** qu'à 1 ms dans un test de forum ; réglages d'interruptions sans effet mesurable hors contention | [Blur Busters, forum](https://forums.blurbusters.com/viewtopic.php?t=9858) | `timer_global` hors préréglage sûr (ajout 5) |
| Affinité des interruptions | Les éditeurs d'outils disent eux-mêmes ne pas l'avoir mesurée | [interrupt-affinity-utility](https://github.com/vadyaravadim/interrupt-affinity-utility) | Idem |
| Intégrité de la mémoire | Activée d'office sur les PC compatibles depuis la mise à jour d'octobre 2026, seulement si elle n'était pas configurée : un PC où elle a été coupée n'est pas modifié ([Microsoft, Windows IT Pro Blog](https://techcommunity.microsoft.com/blog/windows-itpro-blog/expanding-memory-integrity-protection-across-windows-devices/4551984), lu le 21/11/2026) ; reste désactivable. Titre de Tom's Hardware : « jusqu'à 10 % » même sur RTX 4090 | [Gadget Hacks](https://windows.gadgethacks.com/news/windows-11-memory-integrity-fps-impact-what-changes/) ; [Tom's Hardware](https://www.tomshardware.com/news/windows-vbs-harms-performance-rtx-4090) [VERIFICATION REQUIRED] page non lue | Informer, ne pas couper d'office (ajout 11) |
| Shaders précompilés (ASD) | Disponible sur AMD, Intel, NVIDIA, Qualcomm ; Windows 11 24H2 ou plus ; pilote AMD 26.6.1 ou plus ; toutes les RTX. Le jeu doit l'avoir intégré. « Jusqu'à 90 % » de chargement en moins : chiffre de Microsoft | [Microsoft DirectX](https://devblogs.microsoft.com/directx/advanced-shader-delivery-expands-public-preview-with-amd/), [ElevenForum](https://www.elevenforum.com/t/advanced-shader-delivery-now-available-across-windows-11-devices.50305/) | Équivalent Windows du cache Steam sous Linux (ajout 8) |
| Jeux fenêtrés | Passage du modèle « blt » au modèle « flip » pour DirectX 10 et 11 : « réduit la latence d'image » | [Microsoft](https://support.microsoft.com/en-us/windows/hardware/display-graphics/optimizations-for-windowed-games-in-windows-11) | Déjà dans ONYX (`windowed_flip`) ; il manque la vérification par jeu (ajout 6) |
| DXVK sous Windows | Non pris en charge officiellement par ses auteurs | [DXVK, wiki](https://github.com/doitsujin/dxvk/wiki/Windows) | À ne pas ajouter |
| Exclusions Defender | Microsoft : les exclusions de dossier « bloquent complètement l'analyse ». Aucune mesure en jeu trouvée | [Microsoft Learn](https://learn.microsoft.com/en-us/defender-endpoint/microsoft-defender-endpoint-antivirus-performance-mode) | Hors préréglage sûr |

Non lu : le test LDAT Windows 11 contre Wayland publié sur Reddit (page inaccessible à
l'outil) ; le test Phoronix cité en commentaire (CachyOS 76,19 contre Windows 76,12 sur
RTX 5090 portable, soit une égalité).

## Ce qui existe déjà (à ne pas refaire)

210 réglages (`src/Tweaks.cs`), dont : intégrité de la mémoire (`vbs_off`), jeux fenêtrés
(`windowed_flip`), MPO (`mpo_off`), plein écran (`fso_disable`), mode Jeu (`game_mode`),
minuteur (`timer_global`, `distribuer_minuteurs`), MSI (5 réglages), priorité et cœurs par jeu
(`ProfilJeu.cs`, `Topologie.cs`), applis de fond (`AutoJeu.cs`, `GameBoost.cs`,
`ApplisDeFond.cs`), profil NVIDIA et plafond d'images (`NvProfile.cs`, `FrameCap.cs`), réseau,
USB, mémoire, télémétrie. Mesure : `Preuve.cs` (DPC, gigue), `PreuveJeu.cs` et `FpsEtw.cs`
(FPS), `EnqueteLatence.cs`. Absents : DXVK, EPP, Anti-Lag, taille du cache de shaders, lecture
du mode de présentation.

## Développement — les 12 ajouts

### Phase 1 — réversibilité exacte (condition de tout le reste)

État actuel, relu dans le code :
- `Revert` écrit des valeurs fixes (`src/Tweaks.cs:79-84`, souris remise à 1/6/10).
- Photo `.reg` avant modification pour 143 réglages sur 210 (`src/Engine.cs:39-42`) ; les 67
  autres n'en ont pas. La photo n'est pas rejouée par « Rétablir ».
- `hpet_cleanup` : `Revert` vide, les valeurs `bcdedit` supprimées ne reviennent pas
  (`src/Tweaks.cs:664-669`).
- `gpu_msi` : `Revert` appelle `SetGpuMsi(false)` (`src/Tweaks.cs:1531`), qui **supprime** la
  valeur `MSISupported` (`src/Sys.cs:722-726`). Si le pilote l'avait déjà écrite à 1 (cas normal
  selon RigPolice), « Rétablir » l'efface au lieu de la laisser : le PC ne revient pas à son
  état d'origine. Effet réel après redémarrage non testé.
- `ApplisDeFond` ferme des applications et ne les rouvre pas, par choix (`src/ApplisDeFond.cs:35`).
- Pas de champ « risque » : il n'existe que dans le libellé (`src/Model.cs:6-19`).

| # | Ajout | Ce qu'ONYX fait | Critère de réussite | Effort |
|---|---|---|---|---|
| 1 | **Journal de l'état d'origine** | Avant chaque `Apply`, enregistrer l'état réel : valeur de registre et son existence, type de démarrage et état du service, valeurs d'alimentation secteur et batterie, valeurs `bcdedit`, réglages du profil NVIDIA. « Rétablir » rejoue ce journal ; la valeur fixe ne sert que si le journal manque | Après Appliquer puis Rétablir, l'état est identique à l'octet près | Élevé |
| 2 | **Test aller-retour** | Commande qui, pour chaque réglage, fait photo → Appliquer → Rétablir → photo, et liste les écarts. À lancer dans une machine de test (bac à sable Windows ou machine virtuelle) | 0 écart, ou réglage marqué « non réversible » | Moyen |
| 3 | **Champs Risque et Preuve + préréglage « Sûr »** | Deux champs dans `Tweak` : Risque (aucun, sécurité, fonction, session) et Preuve (mesuré, dépend du PC, non mesuré). « Sûr » ne coche que : réversible exact, risque aucun, preuve mesurée ou neutre | Aucun réglage du préréglage « Sûr » n'échoue au test 2 ni ne porte un risque | Moyen |

### Phase 2 — retirer ce qui ne sert à rien

| # | Ajout | Ce qu'ONYX fait | Critère de réussite | Effort |
|---|---|---|---|---|
| 4 | **MSI conditionnel** | Lire le mode réel de chaque périphérique. Déjà en MSI : afficher « rien à faire » et ne rien écrire. Sur ligne partagée (cas de l'audio) : proposer | Sur le PC de test, aucun réglage MSI n'écrit quand le périphérique est déjà en MSI | Faible |
| 5 | **Étiquettes de preuve** | `timer_global`, `distribuer_minuteurs`, affinité et priorité d'interruptions, `w32ps`, `fso_disable`, `nagle`, `netthrottle` : marqués « non mesuré », sortis de « Sûr ». Ils y reviennent pour un PC donné si la mesure en jeu d'ONYX montre un gain confirmé | Le catalogue affiche la preuve de chaque réglage | Faible |

### Phase 3 — ce que Linux fait et que Windows peut faire sans rien casser

| # | Ajout | Ce qu'ONYX fait | Critère de réussite | Effort |
|---|---|---|---|---|
| 6 | **Mode de présentation par jeu** (lecture seule) | Pendant un jeu, lire si l'image passe directement à l'écran (« Independent Flip ») ou par le compositeur (« Composed »), où elle prend environ un rafraîchissement de plus (6,9 ms à 144 Hz, 4,2 ms à 240 Hz ; calcul, non mesuré). Constat avec bouton vers `windowed_flip`. Outil : PresentMon d'Intel (licence à vérifier) ou extension de `FpsEtw.cs` | Le mode affiché correspond à celui de PresentMon sur 3 jeux | Élevé |
| 7 | **Session de jeu entièrement rétablie** | Suspendre ou passer en mode Efficacité les processus de fond au lieu de les fermer ; tout rétablir à la sortie du jeu ; journal de session rejoué au démarrage suivant si ONYX a planté. Gain non publié : à mesurer avec `PreuveJeu` (1 % low avec et sans) | Après une session, la liste des processus et services est celle d'avant ; gain affiché seulement s'il est mesuré | Moyen |
| 8 | **Shaders précompilés** (lecture seule) | Constat si Windows est antérieur à 24H2 ou le pilote trop ancien pour ASD. Texte d'aide : ne pas vider le cache de shaders | Le constat se tait sur le PC de test (25H2, pilote 621.01) | Faible |
| 9 | **Reflex et Anti-Lag 2 coupés dans le jeu** (lecture seule) | Étendre la lecture faite pour Fortnite à d'autres jeux, un fichier de réglages vérifié par jeu. Fait le 21/11/2026 : Overwatch (`ReflexMode`), Battlefield 6 (`GstRender.NvidiaLowLatency`, `GstRender.AMDLowLatency`), Black Ops 6 (`NvidiaReflex`, `AmdAntilag2`), fichiers relus sur le PC de test. « 0 = coupé » pour Overwatch et Battlefield 6 : hypothèse tirée de l'ordre du menu [VERIFICATION REQUIRED] couper Reflex dans le jeu et relire la valeur | Un jeu ajouté = un fichier relu sur une vraie installation | Élevé |
| 10 | **Conseil mode Xbox** | Affiché seulement avec 16 Go ou moins, ou une puce graphique intégrée. Texte honnête : mémoire libérée, pas de FPS en plus | Absent sur le PC de test | Faible |
| 11 | **Intégrité de la mémoire : information** | Constat qui cite la mise à jour d'octobre 2026 quand la protection est active. Si `vbs_off` avait été appliqué, il dit que ce n'est pas cette mise à jour (Microsoft ne touche pas un PC où elle a été coupée). `vbs_off` sort du lot « Ré-appliquer » de la dérive et reste hors de tout préréglage (risque : sécurité). Fait : 21/11/2026 | Le constat cite la date et laisse le choix | Faible |

### Phase 4 — la preuve

| # | Ajout | Ce qu'ONYX fait | Critère de réussite | Effort |
|---|---|---|---|---|
| 12 | **Indicateur de régularité** | À partir de `FpsEtw.cs` : moyenne, 1 % low, rapport 1 % low / moyenne, temps d'image au 99e centile. Rapport avant/après par jeu. Annulation automatique quand une régression est confirmée par une seconde mesure (aujourd'hui `PreuveJeu` ne fait que la proposer) | Deux mesures du même état diffèrent de moins que le seuil choisi | Moyen |

## À ne pas ajouter

| Idée | Raison |
|---|---|
| DXVK ou VKD3D posés dans les jeux | Non pris en charge par ses auteurs sous Windows ; fichier injecté dans le jeu, risque anti-triche |
| Defender coupé, ou exclusions d'office | Analyse bloquée sur le dossier ; aucune mesure en jeu |
| Minuteur à 0,5 ms | Latence plus haute dans le seul test trouvé |
| Affinité des interruptions dans un préréglage | Jamais mesurée, y compris par ses promoteurs |
| Mode Xbox présenté comme gain de FPS | Aucun gain mesuré sur PC de bureau |
| « Ordonnanceur façon Linux » | Aucun effet isolé en jeu, même sous Linux |
| `vbs_off` dans un préréglage | Baisse de sécurité ; certains anti-triche recommandent de la garder [VERIFICATION REQUIRED : guide Riot] |
| Plus de services coupés | Aucun test indépendant (déjà écarté au plan du 17/11) |

## Plan d'action

1. Ajout 1, puis 2 : sans eux, rien d'autre ne respecte la contrainte.
2. Ajout 3 avec le classement des 210 réglages ; traiter d'abord `hpet_cleanup`,
   `cpu_unpark_cores`, `frame_cap`, `gpu_msi`, `ApplisDeFond`.
3. Ajouts 4 et 5 (faibles, retirent des écritures inutiles).
4. Ajout 12, pour mesurer les suivants.
5. Ajouts 7, 6, puis 8, 10, 11, puis 9.
6. Mesurer sur le 9950X3D : préréglage « Sûr » contre Windows d'origine, 3 jeux, 3 passes.

## Optimisation & prochaines étapes

- **Décision à prendre** : `ApplisDeFond` ferme des applications sans les rouvrir. Contrainte
  stricte = remplacer la fermeture par la suspension (ajout 7), ou la sortir de « Sûr ».
- **Décision à prendre** : PresentMon embarqué (plus simple, un exécutable de plus) ou lecture
  maison dans `FpsEtw.cs` (pas de dépendance, plus long).
- **Non vérifié** : chiffres par jeu du test Tom's Hardware sur VBS ; test LDAT Reddit ; version
  minimale du pilote NVIDIA pour ASD ; effet après redémarrage de la suppression de
  `MSISupported` ; date de l'article Gadget Hacks ; gain du
  mode Efficacité sur les processus de fond (aucune mesure publiée trouvée).
- **Ce que le plan ne promet pas** : un chiffre « égal à Linux ». ONYX ne peut pas mesurer Linux
  sur le même PC ; il peut montrer l'écart avant/après sous Windows.
