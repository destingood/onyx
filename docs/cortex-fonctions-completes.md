# Razer Cortex 11.12 — toutes les fonctions, onglet par onglet, face à ONYX 16.09

Relevé du 27/11/2026, complété le 28/11/2026 (second passage : 2 910 identifiants d'écran, dont 1 031 pour le nettoyage et les réglages système, et 508 réglages de Booster Prime).

**Sources (faits et noms de fonctions seulement, aucun texte ni code repris) :**
- Cortex installé sur le PC : `appsettings.json` (18 modules), `BoostSettings\*.ini`, `AutoClipCVEngine\profiles`, `AutoClipGameApiPlugins`.
- Fichier d'interface public de Cortex 11.12 : liste des pages (69 routes) et identifiants des écrans (1 210).
- Liste publique des jeux Booster Prime (`boostprime/4.0.0.0/config.json`).
- Pages razer.com (voir en bas).

**Légende :** ✅ présent dans ONYX · ➖ partiel · ❌ absent · 🚫 écarté par décision antérieure · « ? » non vérifié dans le code.

---

## 1. Game Booster

### 1.1 Boost
| Fonction de Cortex | ONYX |
|---|---|
| Boost maintenant / Restaurer maintenant | ✅ Mode Jeu |
| Boost automatique au lancement d'un jeu, restauration à la sortie | ✅ |
| Liste à cocher en 4 groupes : processus, services Windows, services non-Windows, éléments spéciaux | ✅ (ONYX suspend, ne ferme pas) |
| Réglages recommandés en un clic | ✅ |
| Compteurs : éléments optimisés, restaurés, RAM libérée ; usage processeur, carte graphique, mémoire | ✅ |
| Enregistrer / charger la liste de processus dans un fichier | ✅ v16.12 (le fichier ne contient que des exclusions) |
| Spécial : libérer la RAM | ✅ |
| Spécial : plan d'alimentation | ✅ |
| Spécial : réveiller tous les cœurs | ✅ `cpu_unpark_cores` |
| Spécial : affinité (applis de fond poussées sur d'autres cœurs, masques par nombre de cœurs) | ✅ |
| Spécial : priorité du lanceur (Steam) relevée | ✅ v16.12 option « lanceur un cran au-dessus » (jamais « haute ») |
| Spécial : applis de fond coupées | ✅ mode Efficacité |
| Spécial : télémétrie et rapports d'erreurs coupés pendant la partie | ✅ DiagTrack |
| Spécial : Cortana coupée | sans objet sous Windows 11 |
| Spécial : touche Windows bloquée | ✅ v16.09 |
| Spécial : touches rémanentes coupées | ✅ v16.09 |
| Spécial : vider le presse-papiers | 🚫 destructif, gain nul |
| Spécial : fermer explorer.exe pendant la partie | 🚫 va avec le Bureau de jeu ; si le jeu plante, plus de barre des tâches ni de Bureau |
| Spécial : mises à jour automatiques de Windows coupées pendant la partie | ❌ relevé du 28/11 (ONYX a seulement le réglage « pas de redémarrage automatique session ouverte ») |
| Spécial : mise en veille du processeur coupée | 🚫 mesuré et rejeté le 25/11/2026 sur le PC de l'utilisateur |
| Spécial : « Bureau de jeu » (le jeu tourne sur un bureau Windows à part, raccourci pour en sortir) | 🚫 v16.12 : les jeux Steam, Epic et Battle.net se relancent par leur lanceur, ouvert sur le bureau normal (Steam : SteamAPI_RestartAppIfNecessary) ; le jeu n'apparaîtrait pas sur le bureau à part, et l'utilisateur risquerait de rester devant un bureau vide |
| 59 services arrêtés en jeu (liste Windows 10 et 11) | 🚫 mesuré le 27/11 : moins de 2 s de processeur en 2 h |
| Raccourcis clavier : booster, restaurer, sortir du Bureau de jeu | ✅ v16.12 modifiables (Mode Jeu, afficher ONYX, compteur, capture) ; Bureau de jeu 🚫 |

### 1.2 Booster Prime
| Fonction de Cortex | ONYX |
|---|---|
| 34 jeux distincts (48 entrées avec les variantes régionales) : ARC Raiders, Apex Legends, Baldur's Gate 3, Battlefield 6, Black Myth: Wukong, Call of Duty, Counter-Strike 2, Crimson Desert, Cyberpunk 2077, Delta Force, Diablo IV, Dota 2, Elden Ring, Fortnite, Genshin Impact, GTA V, Hogwarts Legacy, Honkai: Star Rail, League of Legends, Lost Ark, Marvel Rivals, Minecraft, Naraka: Bladepoint, Overlord 2, Overwatch, PUBG, Palworld, Roblox, Rocket League, Rust, Sons of the Forest, Starfield, Valorant, Valheim | ➖ 3 jeux (Fortnite, Cyberpunk 2077, Black Ops 6) — décision : on s'arrête là |
| 4 modes par jeu (dont Compétitif et Équilibré) | ✅ 4 modes |
| Réglage par réglage en Bas / Moyen / Élevé | ✅ aperçu avant écriture |
| Résolution et mode plein écran changés par le préréglage | ✅ v16.15 : au choix, Fortnite et Black Ops 6, valeurs du menu du jeu acceptées par l'écran ; Cyberpunk 2077 non proposé |
| FPS prédits par IA | 🚫 remplacé par la mesure réelle de 60 s |
| Revenir en arrière, rafraîchir, indiquer le dossier du jeu à la main | ✅ « Rétablir » ; dossier manuel ? |
| Afficher les FPS depuis l'écran du préréglage | ✅ |
| Réglage d'image : luminosité, contraste, teinte, saturation, bouton restaurer | ✅ filtre de couleurs |
| Liste des jeux et des réglages mise à jour par Internet | 🚫 préréglages arrêtés à 3 jeux (décision) ; la liste suit les mises à jour d'ONYX |

---

## 2. Lanceur (Launcher)

### 2.1 Bibliothèque
| Fonction de Cortex | ONYX |
|---|---|
| Vues : tous les jeux, installés, favoris, récents | ✅ |
| Jeux possédés non installés, bouton Installer | ✅ Steam (v16.08) |
| Grille ou liste | ✅ v16.11 (bouton « Grille ⌄ ») |
| Tri et regroupement : temps de jeu, dernière partie, plateforme, genre, étiquette, note, installé ou non | ✅ tri, et regroupement par plateforme, étiquette ou dernière partie (v16.11) ; genre absent |
| Recherche, filtre, rafraîchir | ✅ |
| Ajouter un jeu à la main (parcourir un .exe) | ✅ vérifié le 28/11 (par dossier d'installation, ou par programme dans les profils de jeu) |
| Étiquettes | ✅ v16.08 |
| Genres (38 genres) | ✅ v16.12 genre de la fiche Steam, regroupement par genre |
| Ma note personnelle sur un jeu, « je recommande » | ✅ v16.10 appréciation et note ; « je recommande » v16.12 |
| Note de la plateforme (avis) | ✅ v16.12 avis Steam (« 85 % positives sur … avis ») |
| Plateformes lues : Steam, Epic, GOG, EA/Origin, Ubisoft, Battle.net, Xbox, Humble, WeGame, jeux « directs » | ✅ multi-lanceur, HoYoPlay compris ; WeGame et Humble App ❌ non lus (vérifié le 28/11 ; WeGame : plateforme chinoise) |

### 2.2 Fiche d'un jeu
| Fonction de Cortex | ONYX |
|---|---|
| Lancer, installer, désinstaller, ouvrir le dossier, retirer de la liste | ✅ |
| Favori | ✅ |
| Temps total, dernière partie | ✅ journal de jeu |
| Développeur, éditeur, date de sortie, genre | ✅ v16.12 fiche Steam |
| Onglets Général / Activité / Ma progression (succès) | ➖ succès Steam (v16.08) |
| Modifier : jaquette, image de fond, icône | ✅ jaquette et fond ; icône v16.12 |
| Modifier : surnom du jeu | ✅ « Nom affiché » des options du jeu (déjà là avant ce relevé) |
| Modifier : cible de lancement, paramètres de lancement, dossier de départ | ✅ options de lancement et dossier de départ |
| Modifier : liens web (plusieurs par jeu) | ✅ v16.10 (12 liens, http et https seulement) |
| Résolution conseillée | 🚫 sans source fiable par jeu et par machine |
| Courbe d'une partie : FPS moyen / min / max, usage et température processeur et carte graphique, RAM, résolution, durée | ✅ FPS et bilan ; charge et température du processeur et de la carte graphique, RAM par partie en v16.13 (pas de courbe tracée, ni de résolution) |
| Export de la courbe en CSV | ✅ moniteur FPS |
| État Booster Prime sur la fiche (optimal, optimiser, annuler, FPS prédits) | ✅ préréglage sur la fiche |

### 2.3 Accueil « Récents » et résumé
| Fonction de Cortex | ONYX |
|---|---|
| Jeux récents | ✅ |
| Résumé : temps joué, comparaison avec hier / semaine dernière / mois dernier, jeu et genre les plus joués | ✅ bilan 7 j / 30 j / tout, comparaison avec la période d'avant, carte image à partager (v16.11) |
| Progression des succès | ✅ Steam |
| Encart liste de souhaits (gratuits, à paraître) | ➖ dans Bons plans |
| Actualités (blog Razer) | ✅ Actus |
| « Populaire en ce moment » | ✅ v16.12 onglet Populaires (classement public Steam) |
| Serveurs Discord : ajouter les siens, serveurs conseillés, rejoindre | ✅ v16.12 « Mes serveurs Discord » (liens d'invitation, ouverture d'un double-clic) ; serveurs conseillés 🚫 (liste tenue par Razer) |
| Disposition de l'accueil modifiable et enregistrée | 🚫 gros chantier pour peu d'usage |
| Profil public ou privé, page de résumé d'un autre joueur | 🚫 compte et serveurs |
| Carte postale à partager : jeux favoris, plus joués, temps total, succès ; image sur le Bureau, code HTML / BBCode | ✅ image v16.11 ; HTML, BBCode et texte v16.12 |

### 2.4 Plateformes (comptes reliés)
| Fonction de Cortex | ONYX |
|---|---|
| Relier Steam, GOG, EA/Origin, Xbox, Humble (pas à pas) | ➖ lecture locale, sans compte |
| Synchroniser bibliothèque et succès | ➖ Steam local |
| Amis : liste, invitation, lien à copier | 🚫 compte et serveurs |
| Confidentialité, état affiché | sans objet |

### 2.5 En jeu (overlay)
| Fonction de Cortex | ONYX |
|---|---|
| Barre d'outils en jeu, heure, mes clips, réglages | 🚫 impossible en plein écran exclusif sans injection (anticheat) |
| Compteur : marche/arrêt, automatique au lancement du jeu | ✅ |
| Compteur : position, taille (petit, moyen, grand), style (empilé ou en ligne), couleur du texte, aperçu | ✅ v16.10 position, taille, couleur ; style en ligne et aperçu v16.12 |
| Jusqu'à 12 mesures au choix : FPS ; processeur (usage, température, fréquence, tension, ventilateur, puissance) ; carte graphique (usage, température, mémoire vidéo, ventilateur) ; RAM ; disque ; débit descendant et montant ; batterie ; heure ; durée de la partie | ✅ v16.10 douze lignes ; fréquence réelle du processeur, disque et batterie v16.12 ; ventilateur de la carte graphique v16.13 (API du constructeur) ; ventilateur, tension et puissance du processeur ❌ (pilote noyau) |
| Graphique créé à la fin de la partie, notification | ✅ bilan de partie |
| Minuteur et compte à rebours au compteur : démarrer, remettre à zéro, durée au choix, signal sonore, raccourcis | ✅ v16.15 |
| Widget Xbox Game Bar | 🚫 demande une application UWP à part |

### 2.6 Streaming = Remote Play
| Fonction de Cortex | ONYX |
|---|---|
| PC hôte : marche/arrêt, code PIN, appairage auto ou manuel, appareils appairés | 🚫 Sunshine + Moonlight le font gratuitement |
| Hôte : encodeur NVIDIA / AMD / Intel / logiciel, port, IPv4/IPv6, sortie audio | 🚫 |
| Client : appairer, liste des jeux, bureau distant, résolution et FPS, plein écran / sans bordure / fenêtré / écran virtuel, audio stéréo / 5.1 / 7.1, mise en veille de l'hôte, raccourci de déconnexion, applis masquées | 🚫 |
| Installer / désinstaller les composants hôte et client | 🚫 |

### 2.7 Divers lanceur
| Fonction de Cortex | ONYX |
|---|---|
| Mini-jeux (jeux web instantanés) | 🚫 |
| Playtest : tester un jeu, rendre un rapport, gagner du Silver | 🚫 |
| Acheter avec Razer Gold | 🚫 |
| Mode console (interface plein écran à la manette) | ✅ v16.12 Mes jeux en plein écran (F11, bouton Menu de la manette, Échap pour sortir) |

---

## 3. System Booster

| Fonction de Cortex | ONYX |
|---|---|
| **Accélérer** : analyse, tout cocher / décocher, optimiser, « revenir aux valeurs Windows » | ✅ catalogue de réglages, bien plus large. Cortex : 82 réglages en 5 groupes (bureau, cache disque, système de fichiers, réseau, services Windows) ; ONYX a les mêmes familles (délais de menu et de fermeture des programmes, noms courts NTFS, date de dernier accès, SysMain, registre à distance…). Les 18 services à couper 🚫 : sur le PC de l'utilisateur, 13 des 14 présents sont déjà arrêtés (relevé du 28/11). Réglages sans effet sur Windows 11 (DLL inutilisées, Internet Explorer) ou risqués (mémoire virtuelle coupée) 🚫 |
| **Nettoyer** : analyse, taille par catégorie, résumé, dernière analyse et dernier nettoyage | ✅ |
| Nettoyer : navigateurs (alerte si le navigateur est ouvert), dossiers temporaires Windows, Corbeille | ✅ |
| Nettoyer : résidus par jeu (627 entrées : journaux, rapports de plantage, fichiers temporaires dans les dossiers des jeux) et par application (161 entrées) | ➖ v16.15 : journaux et rapports de plantage des jeux Unreal Engine, du lanceur Epic et de Steam, vers la Corbeille ; pas de liste de 627 entrées ni de fichiers temporaires dans les dossiers d'installation |
| Nettoyer : registre (21 catégories) | 🚫 aucun gain mesurable attendu, risque de casser une application ; ONYX ne supprime pas de clés de registre en masse |
| Nettoyer : historique de navigation, formulaires (10 catégories « vie privée ») | 🚫 ONYX ne touche qu'aux caches des navigateurs |
| Nettoyer : lancer le nettoyage tout seul après l'analyse | 🚫 règle : confirmation |
| Nettoyer : liste d'exclusions (éléments à ne jamais nettoyer) | ✅ v16.10 dossiers exclus, en plus de « Ne plus me proposer » |
| Nettoyer : garder les cookies de certains sites | 🚫 ONYX ne touche pas aux cookies |
| Nettoyer : nettoyage programmé | ➖ rappel hebdomadaire, sans suppression automatique (règle : confirmation) |
| **Défragmenter les jeux** : liste, nombre de fragments, jeux sur SSD écartés | ✅ |
| **Ma machine** : vue d'ensemble, processeur (cœurs, multiplicateur, caches L1/L2/L3), carte mère, RAM (emplacements), carte graphique (pilote, date), disque, écran, audio, caméra, clavier, souris, micrologiciel, adresse MAC | ✅ infos système ; clavier, souris et caméra par l'inventaire des périphériques (toutes les classes, vérifié le 28/11) |

---

## 4. Game Clips

| Fonction de Cortex | ONYX |
|---|---|
| Raccourci : garder les N dernières secondes | ✅ (capture Windows, Win+Alt+G) |
| Durée et qualité du clip, mode d'enregistrement | ➖ réglages Windows |
| Zone : plein écran ou fenêtre du jeu | ➖ Windows |
| Micro, curseur de la souris visible | ➖ Windows |
| Générique Razer en fin de clip | sans objet |
| Dossier d'enregistrement, déplacer les fichiers | ✅ dossier ; « Déplacer vers… » en v16.13 (autre disque compris, rien n'est écrasé) |
| Limite d'espace disque, suppression automatique des plus anciens, détail de l'espace utilisé | ✅ v16.10 limite et espace utilisé ; les plus anciens sont proposés puis envoyés à la Corbeille après accord, jamais supprimés seuls |
| Bibliothèque : récents, mes clips, par jeu, recherche | ✅ |
| Tri par nom ou date | ✅ v16.12 récents, anciens, nom, taille |
| Renommer un clip | ✅ v16.10 |
| Suppression par lot | ✅ Corbeille, par lot (existait) ; Ctrl+A v16.12 |
| Cloud : envoi, téléchargement, synchronisation, expiration, filtre local / cloud | 🚫 serveurs |
| Clips automatiques par l'API officielle du jeu : CS2, Dota 2, LoL | 🚫 demande d'écrire un fichier de configuration dans le dossier du jeu ; LoL non testable |
| Clips automatiques par **analyse de l'image** (26 jeux : Apex Legends, Battlefield 6, Borderlands 4, Crimson Desert, Delta Force, Elden Ring, Elden Ring Nightreign, Euro Truck Simulator 2, **Fortnite**, Forza Horizon 6, Genshin Impact, GTA V, Helldivers 2, Kingdom Come: Deliverance 2, Marvel Rivals, Mount & Blade II, Overwatch, Palworld, PUBG, Rainbow Six, Red Dead Redemption 2, Roblox, Rocket League, Street Fighter 6, Valorant, War Thunder) | ❌ — ne lit ni la mémoire ni le processus du jeu ; demande un modèle de reconnaissance par jeu |
| Pseudo en jeu à renseigner (pour reconnaître ses propres actions) | ❌ |

---

## 5. Deals (bons plans)

| Fonction de Cortex | ONYX |
|---|---|
| Onglets : Jeux, Bundles, Découverte, Gratuits, Razer Gold, Liste de souhaits | ✅ sauf Razer Gold |
| Comparateur de prix entre boutiques | ✅ |
| Plus bas historique | ✅ |
| Seuils de remise : toute remise, 33 %, 50 %, 70 % | ✅ filtres prix |
| Import liste de souhaits Steam | ✅ |
| Import liste de souhaits Humble | 🚫 demande la connexion au compte (cookie) |
| Alerte baisse de prix | ✅ |
| Alerte fin de promotion | ✅ (moins de 48 h avant la fin, une seule alerte par promo ; vérifié le 28/11) |
| Alerte sortie d'un jeu suivi | ✅ v16.12 notification le jour de la sortie, avec le prix |
| Alertes par e-mail (immédiat ou quotidien) | 🚫 serveurs |
| Pastilles de notification | ✅ |

---

## 6. Boutique d'applications (module « Buff » / AppGet — nom affiché à vérifier)

| Fonction de Cortex | ONYX |
|---|---|
| Catalogue d'applis et d'extensions de navigateur pour joueurs, fiche détaillée | ➖ applis de jeu par winget ; fiche (version, éditeur, description, licence) en v16.13 ; pas d'extensions de navigateur |
| Notes des utilisateurs, historique des versions, jeux et systèmes compatibles | ❌ (données du serveur de Razer, sans équivalent public) |
| Téléchargement, dossier de téléchargement, « Mes applis », désinstallation | ➖ installation et liste des applis présentes ; désinstallation en v16.13 (après confirmation, jamais une bibliothèque essentielle) ; le dossier est celui de winget |
| Recommandations par notification, achats intégrés, bandeau partenaires | 🚫 |

---

## 7. Récompenses

| Fonction de Cortex | ONYX |
|---|---|
| Succès propres à Cortex | 🚫 |
| Play to Earn : Razer Silver en jouant | 🚫 programme financé |
| Squad Rewards : équipe de 4, saisons, cagnotte de Silver | 🚫 |
| Share and Win : tickets par partage et parrainage | 🚫 |
| Tirage mensuel, vitrine des lots | 🚫 |

---

## 8. Razer AVA (assistant IA, bêta, limité à certaines régions)

| Fonction de Cortex | ONYX |
|---|---|
| Assistant IA de jeu, inscription à la bêta | ➖ ONYX a son Copilote (local) |

---

## 9. Réglages de l'application

| Fonction de Cortex | ONYX |
|---|---|
| Langue (10 langues) | 🚫 français seul (chantier hors de proportion) |
| Thème sombre ou clair | 🚫 sombre seul (chantier hors de proportion) |
| Page de démarrage au choix | ✅ v16.13 (⋯ → Système → « Ouvrir ONYX sur la page… ») |
| Lancement avec Windows, masquage automatique | ✅ |
| Accélération matérielle de l'interface | sans objet |
| Lancer les jeux avec Cortex, quitter Cortex à la fermeture du jeu | ✅ lancement depuis Mes jeux ; quitter à la fermeture du jeu 🚫 (ONYX tient le journal de jeu et le Mode Jeu auto) |
| Fermer le lanceur (Steam, Epic…) à la fin de la partie | ➖ v16.15 : Steam seulement (`steam.exe -shutdown`) ; les autres lanceurs n'ont pas de commande de fermeture documentée |
| Avertir quand les notifications de Windows sont coupées | ❌ relevé du 28/11 (les bulles de la zone de notification d'ONYX en dépendent) |
| « Quoi de neuf » affiché après une mise à jour | ➖ les notes sont montrées avant l'installation ; après, une notification seulement |
| Raccourcis clavier modifiables, tout réinitialiser | ✅ v16.12 |
| Notifications par catégorie | ✅ v16.12 (Gardien, mise à jour, jeux suivis, nettoyage) |
| Recommandations de matériel Razer par catégorie | sans objet |
| Mode invité ou compte Razer, fusion des données | sans objet (aucun compte) |
| Mise à jour, canal bêta (Insider), retour d'expérience | ✅ mise à jour ; préversions v16.12 |
| Installateur léger en ligne (lit la dernière version, contrôle la place sur le disque, télécharge puis installe) | ✅ v16.14 (`ONYX-Installation.exe`, empreinte SHA256 contrôlée) |
| Mise à jour reportable, version à ignorer, mise à jour obligatoire | ✅ v16.14 (« Plus tard », « Ignorer cette version » ; une mise à jour importante ne s'ignore pas, mais n'est jamais installée de force) |
| Mise à jour appliquée en arrière-plan | ✅ v16.14, en option coupée par défaut (hors partie, ONYX réduit dans la zone de notification) |
| Module déjà téléchargé non retéléchargé, ménage des anciens installateurs | ✅ v16.14 |
| Installation par modules, service Windows de mise à jour | 🚫 ONYX tient en un seul exécutable ; un service qui tourne en permanence pour une mise à jour par semaine n'a pas d'intérêt |
| Promotion d'autres logiciels dans l'installateur, statistiques d'installation | 🚫 par choix : rien d'autre n'est installé, rien ne sort du PC |
| Collecte de données d'usage (activée dans la configuration) | sans objet : rien ne sort du PC |

---

## 10. Hors du logiciel PC
- Appli mobile Cortex (jeux, récompenses, notifications) : 🚫
- Sons d'interface (4 sons) : sans objet

---

## Manques réalisables dans ONYX, du plus utile au moins utile
1. ~~**Compteur en jeu au choix**~~ : fait en v16.10 (sans les ventilateurs, qui demandent un pilote noyau).
2. ~~**Clips** : renommer, limite d'espace disque~~ : fait en v16.10.
3. ~~**Nettoyage : liste d'exclusions.**~~ Fait en v16.10.
4. ~~**Fiche jeu** : surnom, liens web, note personnelle~~ : fait en v16.10 (le surnom existait déjà).
5. ~~**Bibliothèque en liste** (en plus de la grille) et regroupement~~ : fait en v16.11 (pas de regroupement par genre : ONYX ne connaît pas le genre des jeux).
6. ~~**Bilan : comparaison avec la période d'avant** et **carte du bilan en image** à partager~~ : fait (comparaison déjà là, carte en v16.11).
7. ~~**Bureau de jeu**~~ : écarté en v16.12 (les lanceurs ouvrent le jeu sur le bureau normal).
8. ~~**Raccourcis clavier modifiables.**~~ Fait en v16.12.
9. ~~**Souhaits Humble.**~~ Écarté : connexion au compte nécessaire.
10. **Clips automatiques par analyse d'image pour Fortnite** : gros chantier, modèle de reconnaissance à construire.
11. ~~**Applis : fiche et désinstallation**, **ventilateur de la carte graphique au compteur**, **page d'ouverture au choix**, **charge et températures par partie**, **déplacer des clips**~~ : fait en v16.13.
12. ~~**Installation et mise à jour** : installateur en ligne, installation automatique en option, version à ignorer, mise à jour importante, contrôle de la place sur le disque~~ : fait en v16.14.
13. ~~**Fermer le lanceur à la fin de la partie**~~ : fait pour Steam (v16.15).
14. ~~**Minuteur et compte à rebours au compteur en jeu**~~ : fait (v16.15).
15. ~~**Résolution et mode d'affichage dans les préréglages**~~ : fait pour Fortnite et Black Ops 6 (v16.15).
16. ~~**Nettoyage des résidus des jeux installés**~~ : journaux et rapports de plantage (v16.15).
17. **Téléchargements de Windows Update mis en pause pendant la partie** : à mesurer avant de décider (les 59 services arrêtés en jeu ont déjà été écartés par la mesure).
18. **Petits** : avertir quand les notifications de Windows sont coupées ; « quoi de neuf » après une mise à jour.

## Ce qui reste absent, et pourquoi (état v16.15)
- **Installation par modules et mises à jour partielles** : Cortex télécharge chaque module à part. ONYX est un seul exécutable de 58 Mo : le découper compliquerait l'installation sans gain mesurable. Pas de reprise d'un téléchargement interrompu non plus, pour la même raison.
- **Service Windows de mise à jour, statistiques d'installation, autres logiciels proposés à l'installation** : écartés par choix (rien ne tourne en permanence, rien ne sort du PC, rien d'autre n'est installé).
- **Clips automatiques et pseudo en jeu** : Cortex reconnaît les moments forts par analyse d'image, avec un modèle par jeu. ONYX n'en a pas ; le construire est un chantier à part.
- **Ventilateur, tension et puissance du processeur, ventilateurs du boîtier** : leur lecture demande un pilote noyau, qu'ONYX n'installe pas.
- **Notes des utilisateurs et historique des versions des applis** : données du serveur de Razer.
- **Plateformes reliées, souhaits, récompenses, Remote Play, Razer AVA** : comptes ou services en ligne de Razer.
- **Nettoyeur de registre, historique de navigation, fermeture d'explorer.exe, 18 services à couper** (relevé du 28/11) : gain nul ou risque, écartés.
- **WeGame et Humble App** : non lus par ONYX.
- **Nettoyage programmé sans confirmation** : écarté, ONYX ne supprime rien sans confirmation.
- **Réglages d'enregistrement des clips Windows (qualité, durée)** : non écrits par ONYX, les valeurs de registre n'ont pas pu être vérifiées.

## Sources web
- https://www.razer.com/cortex
- https://www.razer.com/cortex/game-booster
- https://www.razer.com/eu-en/cortex/system-booster
- https://www.razer.com/cortex/xbox-game-bar
- https://www.razer.com/paid-to-play-pc
- https://www.razer.com/razer-ava
- https://insider.razer.com/razer-cortex-39/cortex-pc-squad-rewards-season-15-76993
