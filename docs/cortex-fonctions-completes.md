# Razer Cortex 11.12 — toutes les fonctions, onglet par onglet, face à ONYX 16.09

Relevé du 27/11/2026.

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
| Enregistrer / charger la liste de processus dans un fichier | ❌ |
| Spécial : libérer la RAM | ✅ |
| Spécial : plan d'alimentation | ✅ |
| Spécial : réveiller tous les cœurs | ✅ `cpu_unpark_cores` |
| Spécial : affinité (applis de fond poussées sur d'autres cœurs, masques par nombre de cœurs) | ✅ |
| Spécial : priorité du lanceur (Steam) relevée | ➖ priorité du jeu par profil |
| Spécial : applis de fond coupées | ✅ mode Efficacité |
| Spécial : télémétrie et rapports d'erreurs coupés pendant la partie | ✅ DiagTrack |
| Spécial : Cortana coupée | sans objet sous Windows 11 |
| Spécial : touche Windows bloquée | ✅ v16.09 |
| Spécial : touches rémanentes coupées | ✅ v16.09 |
| Spécial : vider le presse-papiers | 🚫 destructif, gain nul |
| Spécial : « Bureau de jeu » (le jeu tourne sur un bureau Windows à part, raccourci pour en sortir) | ❌ |
| 59 services arrêtés en jeu (liste Windows 10 et 11) | 🚫 mesuré le 27/11 : moins de 2 s de processeur en 2 h |
| Raccourcis clavier : booster, restaurer, sortir du Bureau de jeu | ➖ Ctrl+Alt+G seulement |

### 1.2 Booster Prime
| Fonction de Cortex | ONYX |
|---|---|
| 34 jeux distincts (48 entrées avec les variantes régionales) : ARC Raiders, Apex Legends, Baldur's Gate 3, Battlefield 6, Black Myth: Wukong, Call of Duty, Counter-Strike 2, Crimson Desert, Cyberpunk 2077, Delta Force, Diablo IV, Dota 2, Elden Ring, Fortnite, Genshin Impact, GTA V, Hogwarts Legacy, Honkai: Star Rail, League of Legends, Lost Ark, Marvel Rivals, Minecraft, Naraka: Bladepoint, Overlord 2, Overwatch, PUBG, Palworld, Roblox, Rocket League, Rust, Sons of the Forest, Starfield, Valorant, Valheim | ➖ 3 jeux (Fortnite, Cyberpunk 2077, Black Ops 6) — décision : on s'arrête là |
| 4 modes par jeu (dont Compétitif et Équilibré) | ✅ 4 modes |
| Réglage par réglage en Bas / Moyen / Élevé | ✅ aperçu avant écriture |
| Résolution et mode plein écran changés par le préréglage | ? |
| FPS prédits par IA | 🚫 remplacé par la mesure réelle de 60 s |
| Revenir en arrière, rafraîchir, indiquer le dossier du jeu à la main | ✅ « Rétablir » ; dossier manuel ? |
| Afficher les FPS depuis l'écran du préréglage | ✅ |
| Réglage d'image : luminosité, contraste, teinte, saturation, bouton restaurer | ✅ filtre de couleurs |
| Liste des jeux et des réglages mise à jour par Internet | ❌ (figée dans ONYX) |

---

## 2. Lanceur (Launcher)

### 2.1 Bibliothèque
| Fonction de Cortex | ONYX |
|---|---|
| Vues : tous les jeux, installés, favoris, récents | ✅ |
| Jeux possédés non installés, bouton Installer | ✅ Steam (v16.08) |
| Grille ou liste | ❌ grille seule |
| Tri et regroupement : temps de jeu, dernière partie, plateforme, genre, étiquette, note, installé ou non | ➖ tri présent ; genre et note absents |
| Recherche, filtre, rafraîchir | ✅ |
| Ajouter un jeu à la main (parcourir un .exe) | ✅ ? |
| Étiquettes | ✅ v16.08 |
| Genres (38 genres) | ❌ |
| Ma note personnelle sur un jeu, « je recommande » | ✅ v16.10 appréciation sur 5 et note écrite ; « je recommande » ❌ |
| Note de la plateforme (avis) | ❌ |
| Plateformes lues : Steam, Epic, GOG, EA/Origin, Ubisoft, Battle.net, Xbox, Humble, WeGame, jeux « directs » | ✅ multi-lanceur (WeGame et Humble ?) |

### 2.2 Fiche d'un jeu
| Fonction de Cortex | ONYX |
|---|---|
| Lancer, installer, désinstaller, ouvrir le dossier, retirer de la liste | ✅ |
| Favori | ✅ |
| Temps total, dernière partie | ✅ journal de jeu |
| Développeur, éditeur, date de sortie, genre | ❌ |
| Onglets Général / Activité / Ma progression (succès) | ➖ succès Steam (v16.08) |
| Modifier : jaquette, image de fond, icône | ✅ jaquette et fond ; icône ❌ |
| Modifier : surnom du jeu | ✅ « Nom affiché » des options du jeu (déjà là avant ce relevé) |
| Modifier : cible de lancement, paramètres de lancement, dossier de départ | ✅ options de lancement ; dossier de départ ? |
| Modifier : liens web (plusieurs par jeu) | ✅ v16.10 (12 liens, http et https seulement) |
| Résolution conseillée | ❌ |
| Courbe d'une partie : FPS moyen / min / max, usage et température processeur et carte graphique, RAM, résolution, durée | ➖ FPS et bilan ; températures par partie ? |
| Export de la courbe en CSV | ✅ moniteur FPS |
| État Booster Prime sur la fiche (optimal, optimiser, annuler, FPS prédits) | ✅ préréglage sur la fiche |

### 2.3 Accueil « Récents » et résumé
| Fonction de Cortex | ONYX |
|---|---|
| Jeux récents | ✅ |
| Résumé : temps joué, comparaison avec hier / semaine dernière / mois dernier, jeu et genre les plus joués | ✅ bilan 7 j / 30 j / tout ; comparaison avec la période d'avant ❌ |
| Progression des succès | ✅ Steam |
| Encart liste de souhaits (gratuits, à paraître) | ➖ dans Bons plans |
| Actualités (blog Razer) | ✅ Actus |
| « Populaire en ce moment » | ❌ |
| Serveurs Discord : ajouter les siens, serveurs conseillés, rejoindre | ❌ (ONYX a la présence Discord, que Cortex n'a pas) |
| Disposition de l'accueil modifiable et enregistrée | ❌ |
| Profil public ou privé, page de résumé d'un autre joueur | 🚫 compte et serveurs |
| Carte postale à partager : jeux favoris, plus joués, temps total, succès ; image sur le Bureau, code HTML / BBCode | ❌ |

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
| Barre d'outils en jeu, heure, mes clips, réglages | ❌ (ONYX n'affiche que le compteur) |
| Compteur : marche/arrêt, automatique au lancement du jeu | ✅ |
| Compteur : position, taille (petit, moyen, grand), style (empilé ou en ligne), couleur du texte, aperçu | ✅ v16.10 position, taille, couleur ; style en ligne et aperçu ❌ |
| Jusqu'à 12 mesures au choix : FPS ; processeur (usage, température, fréquence, tension, ventilateur, puissance) ; carte graphique (usage, température, mémoire vidéo, ventilateur) ; RAM ; disque ; débit descendant et montant ; batterie ; heure ; durée de la partie | ✅ v16.10 douze lignes : FPS, 1 % bas, processeur, carte graphique, températures, mémoire vidéo, watts, RAM, débit réseau, heure, durée, courbe ; ventilateurs, fréquence, tension, disque, batterie ❌ (ventilateurs : pilote noyau) |
| Graphique créé à la fin de la partie, notification | ✅ bilan de partie |
| Widget Xbox Game Bar | ❌ |

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
| Mode console (interface plein écran à la manette) | ➖ mode manette dans Mes jeux |

---

## 3. System Booster

| Fonction de Cortex | ONYX |
|---|---|
| **Accélérer** : analyse, tout cocher / décocher, optimiser, « revenir aux valeurs Windows » | ✅ catalogue de réglages, bien plus large |
| **Nettoyer** : analyse, taille par catégorie, résumé, dernière analyse et dernier nettoyage | ✅ |
| Nettoyer : navigateurs (alerte si le navigateur est ouvert), dossiers temporaires Windows, Corbeille | ✅ |
| Nettoyer : liste d'exclusions (éléments à ne jamais nettoyer) | ✅ v16.10 dossiers exclus, en plus de « Ne plus me proposer » |
| Nettoyer : garder les cookies de certains sites | ❌ (ONYX ne touche pas aux cookies) |
| Nettoyer : nettoyage programmé | ➖ rappel hebdomadaire, sans suppression automatique (règle : confirmation) |
| **Défragmenter les jeux** : liste, nombre de fragments, jeux sur SSD écartés | ✅ |
| **Ma machine** : vue d'ensemble, processeur (cœurs, multiplicateur, caches L1/L2/L3), carte mère, RAM (emplacements), carte graphique (pilote, date), disque, écran, audio, caméra, clavier, souris, micrologiciel, adresse MAC | ✅ infos système ; clavier / souris / caméra ? |

---

## 4. Game Clips

| Fonction de Cortex | ONYX |
|---|---|
| Raccourci : garder les N dernières secondes | ✅ (capture Windows, Win+Alt+G) |
| Durée et qualité du clip, mode d'enregistrement | ➖ réglages Windows |
| Zone : plein écran ou fenêtre du jeu | ➖ Windows |
| Micro, curseur de la souris visible | ➖ Windows |
| Générique Razer en fin de clip | sans objet |
| Dossier d'enregistrement, déplacer les fichiers | ✅ dossier ; déplacement ? |
| Limite d'espace disque, suppression automatique des plus anciens, détail de l'espace utilisé | ✅ v16.10 limite et espace utilisé ; les plus anciens sont proposés puis envoyés à la Corbeille après accord, jamais supprimés seuls |
| Bibliothèque : récents, mes clips, par jeu, recherche | ✅ |
| Tri par nom ou date | ➖ ? |
| Renommer un clip | ✅ v16.10 |
| Suppression par lot | ➖ Corbeille, par lot ? |
| Cloud : envoi, téléchargement, synchronisation, expiration, filtre local / cloud | 🚫 serveurs |
| Clips automatiques par l'API officielle du jeu : CS2, Dota 2, LoL | ❌ non testable chez Lucas |
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
| Import liste de souhaits Humble | ❌ (GOG fait à la place) |
| Alerte baisse de prix | ✅ |
| Alerte sortie d'un jeu suivi | ➖ date connue, alerte ? |
| Alertes par e-mail (immédiat ou quotidien) | 🚫 serveurs |
| Pastilles de notification | ✅ |

---

## 6. Boutique d'applications (module « Buff » / AppGet — nom affiché à vérifier)

| Fonction de Cortex | ONYX |
|---|---|
| Catalogue d'applis et d'extensions de navigateur pour joueurs, fiche détaillée | ❌ |
| Notes des utilisateurs, historique des versions, jeux et systèmes compatibles | ❌ |
| Téléchargement, dossier de téléchargement, « Mes applis », désinstallation | ❌ |
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
| Langue (10 langues) | ❌ français seul |
| Thème sombre ou clair | ❌ sombre seul |
| Page de démarrage au choix | ? |
| Lancement avec Windows, masquage automatique | ✅ |
| Accélération matérielle de l'interface | sans objet |
| Lancer les jeux avec Cortex, quitter Cortex à la fermeture du jeu | ❌ |
| Raccourcis clavier modifiables, tout réinitialiser | ❌ raccourcis fixes |
| Notifications par catégorie | ➖ |
| Recommandations de matériel Razer par catégorie | sans objet |
| Mode invité ou compte Razer, fusion des données | sans objet (aucun compte) |
| Mise à jour, canal bêta (Insider), retour d'expérience | ✅ mise à jour ; canal bêta ❌ |
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
5. **Bibliothèque en liste** (en plus de la grille) et regroupement.
6. **Bilan : comparaison avec la période d'avant** et **carte du bilan en image** à partager.
7. **Bureau de jeu** (bureau Windows séparé pour le jeu).
8. **Raccourcis clavier modifiables.**
9. **Souhaits Humble.**
10. **Clips automatiques par analyse d'image pour Fortnite** : gros chantier, modèle de reconnaissance à construire.

## Sources web
- https://www.razer.com/cortex
- https://www.razer.com/cortex/game-booster
- https://www.razer.com/eu-en/cortex/system-booster
- https://www.razer.com/cortex/xbox-game-bar
- https://www.razer.com/paid-to-play-pc
- https://www.razer.com/razer-ava
- https://insider.razer.com/razer-cortex-39/cortex-pc-squad-rewards-season-15-76993
