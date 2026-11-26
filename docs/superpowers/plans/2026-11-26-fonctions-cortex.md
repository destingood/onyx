# Les fonctions de Razer Cortex qui manquent à ONYX — plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ajouter à ONYX les cinq fonctions de Razer Cortex qu'il n'a pas : temps de jeu et bilan de partie, import de la liste de souhaits Steam, préréglages graphiques Fortnite réversibles, bibliothèque de clips avec capture des 30 dernières secondes, et rappel de nettoyage hebdomadaire.

**Architecture:** Chaque lot suit le découpage d'ONYX : une moitié PURE testée au banc (`Xxx.cs`), une moitié machine (`XxxMachine.cs`, disque, registre, réseau), puis l'interface. Rien ne s'injecte dans un jeu, tout ce qui modifie quelque chose se rétablit à l'identique, et là où Cortex prédit (FPS), ONYX mesure avec ce qu'il a déjà (`FpsEtw`, `PreuveJeu`).

**Tech Stack:** C# / .NET 10 WinForms (`net10.0-windows10.0.19041.0`), banc unitaire `tests/ONYX.Tests.csproj` (`Banc.Verifie`, `Banc.Egal`). Aucune nouvelle dépendance.

**Spec:** demande de Lucas du 26/11/2026 (« cherche toutes les fonctions sympa de Razer Cortex à ajouter dans mon app et fait le plan ») + la comparaison ci-dessous. Pas d'autre document.

## Comparaison Cortex / ONYX (26/11/2026)

Sources Cortex : razer.com/cortex et ses pages `/booster`, `/booster-prime`, `/launcher`, `/handheld`, `/deals`, `/game-clips`, lues le 26/11/2026. Côté ONYX : code de `src/` au commit `a55f3ac` (v16.04).

| Fonction de Cortex | Dans ONYX | Décision |
|---|---|---|
| Game Booster : couper les applis de fond, libérer la RAM | `GameBoost.cs` (suspend, ne ferme rien, rétablit tout) | Déjà là |
| Auto-boost au lancement d'un jeu, restauration à la sortie | `AutoJeu.cs`, `VeilleJeu.cs` | Déjà là |
| Veille processeur coupée, cœurs réservés au jeu | `PowerPlans.cs`, `CoeurDedie.cs` ; veille coupée mesurée puis rejetée le 25/11/2026 | Déjà là |
| Compteur FPS en jeu | `StatsOverlay.cs` + `FpsEtw.cs` | Déjà là |
| System Booster : fichiers inutiles, réglages système | `JunkScan.cs`, `Winapp2.cs`, `CachesApplis.cs`, `Tweaks.cs` | Déjà là |
| Nettoyage programmé | Rien | **Lot E** |
| Lanceur multi-plateforme | `GameLibrary.cs`, `BibliothequeJeux.cs` | Déjà là |
| Temps de jeu, jeux récents, résumé | Rien (seule la date Steam est lue) | **Lot A** |
| Deals : comparateur, alertes de baisse, jeux gratuits | `PageBonsPlans.cs`, `Suivi.Decide` | Déjà là |
| Import de la liste de souhaits Steam | Rien (« Suivre mes jeux installés » seulement) | **Lot B** |
| Booster Prime : 4 préréglages graphiques par jeu, retour arrière | Rien (Fortnite : lecture seule de Reflex) | **Lot C**, Fortnite d'abord |
| Booster Prime : prédiction des FPS par IA | Rien | Remplacé par la mesure avant/après de `PreuveJeu` (Lot C) |
| Booster Prime : teinte, luminosité, contraste, saturation | `ColorFilter.cs` | Déjà là |
| Game Clips : 30 dernières secondes, bibliothèque | Rien | **Lot D** |
| Game Clips : détection automatique des temps forts, cloud | Rien | Écarté : lecture de l'état du jeu, incompatible avec « aucune injection » et les anticheats |
| Mode manette (Handheld) | Rien (`ModeXbox.cs` n'est qu'un conseil) | Reporté : 11 pages peintes à rendre navigables, plan à part si Lucas le veut |
| Remote Play | Rien | Écarté : Sunshine + Moonlight le font gratuitement, au plus un guide |
| Play to Earn (Razer Silver), Playtest, appli mobile | Rien | Écarté : programme de récompenses financé et serveurs |
| Succès synchronisés | Rien | Écarté : clé d'API ou profil public par plateforme |

## Global Constraints

- Aucune injection dans un processus de jeu, aucune lecture de sa mémoire (anticheats, règles d'Epic).
- Fortnite : n'écrire que des valeurs que le menu du jeu propose, jamais de fichier en lecture seule, jamais pendant que le jeu tourne.
- Tout changement est réversible à l'identique ; une copie du fichier touché part dans `Documents\ONYX\Sauvegardes` avant la première écriture.
- Rien n'est supprimé sans confirmation explicite ; suppression = Corbeille.
- Aucune donnée personnelle ne sort du PC, sauf le SteamID envoyé à Steam lui-même (Lot B), après un clic.
- Aucune nouvelle dépendance NuGet. Écritures sur disque : fichier temporaire puis remplacement (comme `Magasin.Ecrit`).
- Aucun travail périodique sur le fil d'interface (acquis de la v15.97).
- Git : toujours `git -c safe.directory=E:/bt`. Un commit par tâche, message terminé par la ligne `Co-Authored-By` en vigueur dans la session.
- Banc : `dotnet run --project tests/ONYX.Tests.csproj -c Release` → 0 KO, total en hausse. Chaque nouvelle classe de tests s'inscrit dans `tests/Banc.cs` (appel `XxxTests.Tout();`).
- Textes en français, ton d'ONYX : dire ce qui est mesuré, ce qui ne l'est pas.
- Pas de sous-agents ni de sessions annexes (préférence de Lucas). Publication : seulement sur un « publie » explicite.

## Review Focus

1. **Jeu qui plante puis se relance en 10 s** : une seule partie au journal, pas deux. → test Tâche 1.
2. **PC mis en veille ou ONYX fermé pendant une partie** : la partie s'arrête au dernier relevé vu, pas à l'heure du réveil. → test Tâche 1.
3. **Réglage Fortnite changé à la main dans le jeu après un préréglage** : « Rétablir » ne l'écrase pas. → test Tâche 5.
4. **Clé absente du `GameUserSettings.ini`** (mise à jour du jeu) : rien n'est ajouté, la clé est listée comme non appliquée. → test Tâche 5.
5. **Liste de souhaits de plus de 50 jeux, ou profil privé** : les 50 premiers par priorité, message qui dit combien restent dehors ; profil privé → message d'aide, pas une liste vide muette. → test Tâche 4.

---

## Lot A — Temps de jeu et bilan de partie

### Task 1: Journal de jeu, partie pure

**Files:**
- Create: `src/JournalJeu.cs`
- Test: `tests/JournalJeuTests.cs`

**Interfaces:**
- Produces :

```csharp
internal static class JournalJeu
{
    public const int GraceSecondes = 30;   // relance après plantage : même partie
    public const int MinSecondes = 60;     // en dessous : pas une partie (flash de launcher)
    public sealed class Partie { public string Jeu; public DateTime Debut; public int Secondes; public double FpsMoyen; public double FpsBas; } // FPS 0 = non mesuré
    public sealed class Etat { public string Jeu; public DateTime Debut; public DateTime DernierVu; }
    public static string JeuParmi(IEnumerable<string> processus, Dictionary<string, string> exeVersJeu); // nom du jeu ou null
    public static Partie Avance(Etat e, string jeu, DateTime maintenant); // rend la partie CLOSE à ce relevé, sinon null
    public static List<Partie> Lire(string texte);
    public static string Ecrire(IEnumerable<Partie> parties); // « Jeu \t yyyy-MM-ddTHH:mm:ss \t secondes \t fpsMoyen \t fpsBas », culture invariante
    public static int Secondes(List<Partie> parties, string jeu, DateTime depuis);
    public static DateTime? DernierePartie(List<Partie> parties, string jeu);
    public static string Duree(int secondes); // 45 → « moins d'une minute », 2700 → « 45 min », 7500 → « 2 h 05 »
}
```

- [ ] **Step 1: Écrire les tests qui échouent** dans `tests/JournalJeuTests.cs` (`internal static class JournalJeuTests { public static void Tout() }`), avec `t0 = new DateTime(2026, 11, 26, 20, 0, 0)` :
  - `PartieSimple` : relevés « Fortnite » de t0 à t0+600 s toutes les 2 s, puis null pendant 32 s → une `Partie` rendue au premier relevé où l'absence dépasse 30 s, `Secondes == 600`, `Debut == t0`.
  - `RelanceApresPlantage` : jeu vu 300 s, absent 10 s, revu 300 s, absent 32 s → une seule partie, `Secondes == 610`.
  - `VeilleDuPc` : jeu vu jusqu'à t0+300 s, relevé suivant à t0+8 h avec null → partie de 300 s (fin = `DernierVu`).
  - `FlashIgnore` : jeu vu 40 s puis absent → `Avance` rend toujours null.
  - `ChangementDeJeu` : « Fortnite » 120 s puis « cs2 » directement → la partie Fortnite est rendue au premier relevé cs2, l'état porte cs2.
  - `AllerRetour` : `Lire(Ecrire(x))` rend les mêmes champs ; une ligne abîmée est ignorée, les autres lues.
  - `Duree` : les trois valeurs du commentaire ci-dessus, par `Banc.Egal`.
  - `JeuParmi` : casse ignorée, avec ou sans `.exe` ; null si aucun.
- [ ] **Step 2: Inscrire `JournalJeuTests.Tout();` dans `tests/Banc.cs`, lancer le banc** → échec de compilation (`JournalJeu` inconnu).
- [ ] **Step 3: Écrire `src/JournalJeu.cs`** selon les signatures.
- [ ] **Step 4: Relancer le banc** → 0 KO.
- [ ] **Step 5: Commit** `Journal de jeu : parties, durées et format du fichier (partie pure)`.

### Task 2: Journal branché sur la veille, affiché dans Jeux et Stockage

**Files:**
- Create: `src/JournalJeuMachine.cs`
- Modify: `src/AutoJeuVeille.cs` (là où `VeilleJeu.Prend()` rend le relevé, ligne 34), `src/PageGames.cs`, `src/GameDetailForm.cs`, `src/DormantGames.cs`

**Interfaces:**
- Consumes : `JournalJeu` (Tâche 1), `VeilleJeu.Releve.Noms`, `GameLibrary` (jeux installés), `AppPaths.File`.
- Produces : `internal static partial class JournalJeu { public static void Tick(VeilleJeu.Releve r); public static List<Partie> Parties { get; } public static event Action Change; }` ; fichier `bt-journal-jeu.txt`.

- [ ] **Step 1: Index exe → jeu.** Construit une fois, sur un fil de fond : pour chaque jeu installé, les `*.exe` de son dossier (profondeur ≤ 3), sauf ceux dont le nom contient `crash`, `launcher`, `anticheat`, `eac`, `redist`, `unins`, `setup`, `helper`, `report`. Les trois exécutables de `VeilleJeu.ExesPrioritaires()` y sont toujours.
- [ ] **Step 2: `Tick`** appelé avec le relevé déjà pris par la veille (aucune énumération de processus en plus) ; une partie close est ajoutée et le fichier réécrit (temporaire puis remplacement), hors fil d'interface.
- [ ] **Step 3: Page Jeux** : sous le nom de chaque jeu détecté, « 12 h · il y a 2 j » (total, dernière partie) ; rien si aucune partie.
- [ ] **Step 4: Fiche du jeu** : temps sur 7 jours, 30 jours et au total, puis les 5 dernières parties (date, durée, FPS si mesurés).
- [ ] **Step 5: Jeux dormants** : quand Steam ne donne pas de date, prendre `JournalJeu.DernierePartie` au lieu de « inconnue ».
- [ ] **Step 6: Vérifier pour de vrai** : lancer un jeu 2 minutes, le fermer, attendre 30 s → une ligne dans `bt-journal-jeu.txt`, durée à ± 4 s, visible sur la tuile. Banc → 0 KO.
- [ ] **Step 7: Commit** `Temps de jeu : journal des parties, affiché dans Jeux et Jeux dormants`.

### Task 3: Bilan de partie avec les FPS

**Files:**
- Modify: `src/JournalJeu.cs`, `src/JournalJeuMachine.cs`, `src/GameModeForm.cs` (une case à cocher)
- Test: `tests/JournalJeuTests.cs`

**Interfaces:**
- Consumes : `FpsEtw.Start()`, `FpsEtw.Snapshot(double windowMs)` → `ProcStat.Fps`, `ProcStat.Total` ; `DashboardForm.Notifie(string titre, string texte)`.
- Produces : `public static void Resume(List<double> fpsParSeconde, long images, double secondes, out double moyen, out double bas)` (moyen = images / secondes ; bas = 1er centile des relevés par seconde, 0 si moins de 100 relevés) ; `public static string Bilan(Partie p)`.

- [ ] **Step 1: Tests** : `Resume` sur 600 relevés à 240 dont 6 à 120, images = 143 280, secondes = 600 → `moyen == 238,8`, `bas == 120` ; moins de 100 relevés → `bas == 0`. `Bilan` : « 1 h 12 · 238 FPS en moyenne · 141 au plus bas » ; sans mesure : « 1 h 12 ».
- [ ] **Step 2: Banc** → KO attendus, puis écrire le code, banc → 0 KO.
- [ ] **Step 3: Option « Mesurer les FPS de chaque partie »**, coupée par défaut (la session ETW demande les droits administrateur). Activée : `FpsEtw` démarre à l'ouverture de la partie, un relevé par seconde sur le fil de la veille, arrêt à la clôture.
- [ ] **Step 4: Notification de fin de partie** : titre = nom du jeu, texte = `Bilan(p)`.
- [ ] **Step 5: Vérifier pour de vrai** : option cochée, 3 minutes de jeu avec l'overlay ONYX affiché → l'écart entre la moyenne du bilan et celle de l'overlay reste sous 3 %.
- [ ] **Step 6: Commit** `Bilan de partie : durée et FPS mesurés à la fermeture du jeu`.

---

## Lot B — Liste de souhaits Steam

### Task 4: Importer la liste de souhaits dans « Suivis »

**Files:**
- Create: `src/ListeSouhaits.cs`
- Modify: `src/PageBonsPlans.cs` (à côté de `_installes`, ligne 136)
- Test: `tests/ListeSouhaitsTests.cs`

**Interfaces:**
- Consumes : `Suivi.AjouteInstalle(List<JeuSuivi>, int appId, string nom)`, `Suivi.Max` (50), `Magasin.Suivis`, `Magasin.SauveSuivis()`, `SteamGames.SteamPath()`.
- Produces :

```csharp
internal static class ListeSouhaits
{
    public static string SteamId64(string loginusersVdf);   // compte marqué "MostRecent" "1", sinon le premier, sinon null
    public static string Url(string steamId64);             // https://api.steampowered.com/IWishlistService/GetWishlist/v1/?steamid=<id>
    public static List<int> AppIds(string json);            // triés par "priority" croissante, 0 en dernier
    public static string Compte(int ajoutes, int dejaSuivis, int laisses); // « 12 jeux ajoutés, 3 déjà suivis, 20 laissés de côté (50 jeux suivis au plus). »
}
```

- [ ] **Step 1: Vérifier l'adresse** [VERIFICATION REQUIRED] : l'ancienne adresse `store.steampowered.com/wishlist/profiles/<id>/wishlistdata` est fermée depuis novembre 2024 (Stack Overflow 79286532). Lire le SteamID dans `<Steam>\config\loginusers.vdf`, appeler `Url(id)` sans clé, noter la forme exacte de la réponse (attendu : `{"response":{"items":[{"appid":…,"priority":…,"date_added":…}]}}`). Si Steam exige une clé, arrêter la tâche et le dire à Lucas : il n'y a pas d'autre voie sans compte développeur.
- [ ] **Step 2: Tests** : `SteamId64` sur un vdf à deux comptes dont le second est `MostRecent` → le second ; `AppIds` sur la réponse notée à l'étape 1 (priorités 2, 0, 1 → ordre 1, 2, 0) ; `{"response":{}}` → liste vide ; json abîmé → liste vide, pas d'exception ; `Compte(12, 3, 20)` par `Banc.Egal`.
- [ ] **Step 3: Banc** → KO, écrire `src/ListeSouhaits.cs`, banc → 0 KO.
- [ ] **Step 4: Bouton « Importer ma liste de souhaits Steam »** dans l'onglet Suivis. Appel réseau hors fil d'interface ; noms des jeux par les fiches Steam déjà chargées par la page, sinon « Jeu <appid> » jusqu'à la prochaine actualisation. Résultat affiché avec `Compte`. Liste vide → « Liste vide ou profil privé : Steam > Profil > Modifier le profil > Confidentialité > Détails du jeu : Public. »
- [ ] **Step 5: Vérifier pour de vrai** : clic → les jeux apparaissent dans Suivis, un second clic n'ajoute rien (« 0 jeu ajouté, N déjà suivis »).
- [ ] **Step 6: Commit** `Bons plans : importer la liste de souhaits Steam dans les jeux suivis`.

---

## Lot C — Préréglages graphiques Fortnite

Noms de clés relevés le 26/11/2026 dans le `GameUserSettings.ini` de Lucas. Les valeurs par mode sont une **proposition à valider par Lucas**, pas un fait mesuré ; l'échelle `sg.*` va de 0 (bas) à 3 (épique).

| Clé (section) | Compétition | Performance | Équilibré | Qualité |
|---|---|---|---|---|
| `sg.ViewDistanceQuality` (`[ScalabilityGroups]`) | 2 | 2 | 2 | 3 |
| `sg.AntiAliasingQuality` | 0 | 1 | 2 | 3 |
| `sg.ShadowQuality` | 0 | 0 | 2 | 3 |
| `sg.GlobalIlluminationQuality` | 0 | 0 | 1 | 3 |
| `sg.ReflectionQuality` | 0 | 0 | 1 | 3 |
| `sg.PostProcessQuality` | 0 | 0 | 2 | 3 |
| `sg.TextureQuality` | 1 | 2 | 2 | 3 |
| `sg.EffectsQuality` | 0 | 1 | 2 | 3 |
| `sg.FoliageQuality` | 0 | 1 | 2 | 3 |
| `sg.ShadingQuality` | 0 | 1 | 2 | 3 |
| `sg.LandscapeQuality` | 0 | 1 | 2 | 3 |
| `bMotionBlur` (`[/Script/FortniteGame.FortGameUserSettings]`) | False | False | False | False |
| `bUseVSync` | False | False | False | False |
| `bUseNanite` | False | False | False | True |

Jamais touchés : résolution, `FrameRateLimit` (c'est le rôle de `FrameCap`), mode plein écran, moteur de rendu (`[D3DRHIPreference]`), `LatencyTweak2`, DLSS/TSR.

### Task 5: Édition du fichier de réglages, partie pure

**Files:**
- Create: `src/ReglagesFortnite.cs`
- Test: `tests/ReglagesFortniteTests.cs`

**Interfaces:**
- Produces :

```csharp
internal static class ReglagesFortnite
{
    public enum Mode { Competition, Performance, Equilibre, Qualite }
    public sealed class Changement { public string Section, Cle, Avant, Apres; }
    public static List<Changement> Prevoit(string ini, Mode m, out List<string> clesAbsentes); // seulement ce qui change vraiment
    public static string Applique(string ini, List<Changement> changements);
    public static string Retablit(string iniActuel, List<Changement> changements); // remet Avant seulement là où la valeur vaut encore Apres
    public static List<Changement> Lire(string texte);
    public static string Ecrire(IEnumerable<Changement> c); // « Section \t Cle \t Avant \t Apres »
}
```

- [ ] **Step 1: Tests** sur un ini d'essai de trois sections (`[/Script/FortniteGame.FortGameUserSettings]`, `[ScalabilityGroups]`, `[ChatSettings]`), fins de ligne `\r\n` :
  - `SeulementLesEcarts` : valeur déjà égale à la cible → absente de `Prevoit`.
  - `RienDAutreNeBouge` : après `Applique`, toute ligne hors des clés changées est identique octet pour octet, ordre et `\r\n` compris ; `[ChatSettings]` intact.
  - `CleAbsente` : ini sans `sg.LandscapeQuality` → non ajoutée, présente dans `clesAbsentes`.
  - `MemeCleDansDeuxSections` : une clé `bUseVSync` placée dans `[ChatSettings]` n'est pas touchée.
  - `RetablitExact` : `Retablit(Applique(ini, c), c) == ini`.
  - `RetablitRespecteLeJoueur` : après `Applique`, `sg.TextureQuality` passé à la main de 1 à 3 → `Retablit` laisse 3 et remet tout le reste.
  - `AllerRetour` : `Lire(Ecrire(c))` rend les mêmes changements.
- [ ] **Step 2: Banc** → KO, écrire `src/ReglagesFortnite.cs` (table des cibles = le tableau ci-dessus), banc → 0 KO.
- [ ] **Step 3: Commit** `Préréglages Fortnite : édition réversible du fichier de réglages (partie pure)`.

### Task 6: Préréglages dans la fiche Fortnite, mesure avant/après

**Files:**
- Create: `src/ReglagesFortniteMachine.cs`
- Modify: `src/GameDetailForm.cs`

**Interfaces:**
- Consumes : `ReglagesFortnite` (Tâche 5), `Sauvegardes` (dossier), `PreuveJeu` (60 s avant, 60 s après), `VeilleJeu.NomsProcessus()`.
- Produces : `internal static partial class ReglagesFortnite { public static string Chemin { get; } public static bool JeuOuvert(); public static string Applique(Mode m); public static string Retablit(); }` (chaîne rendue = compte-rendu de ce qui a été réellement écrit) ; état dans `bt-reglages-fortnite.txt`.

- [ ] **Step 1: Machine** : chemin `%LOCALAPPDATA%\FortniteGame\Saved\Config\WindowsClient\GameUserSettings.ini` ; refus si `FortniteClient-Win64-Shipping` tourne (« Ferme Fortnite : le jeu réécrit ce fichier en quittant. ») ; refus si le fichier est en lecture seule (ONYX ne retire pas l'attribut) ; copie datée dans les sauvegardes avant la première écriture ; écriture par temporaire puis remplacement, encodage d'origine conservé.
- [ ] **Step 2: Fiche Fortnite** : quatre boutons de mode. Un clic affiche d'abord la liste « réglage : avant → après » et les clés absentes, puis « Appliquer ». Bouton « Rétablir » actif tant qu'un état est enregistré.
- [ ] **Step 3: Mesure** : après « Appliquer », proposer « Mesurer en jeu » (parcours existant de `PreuveJeu`). Aucun chiffre de FPS n'est annoncé avant la mesure.
- [ ] **Step 4: Vérifier pour de vrai** : appliquer « Compétition », ouvrir Fortnite, comparer chaque ligne du menu Vidéo au tableau ; noter toute valeur que le jeu a remise d'office et la retirer de la table. Fermer, « Rétablir » → `fc /b` entre le fichier et la sauvegarde ne signale que les lignes changées dans le jeu entre-temps.
- [ ] **Step 5: Commit** `Fortnite : quatre préréglages graphiques, aperçu avant d'appliquer, retour exact`.

---

## Lot D — Clips

Décision : ONYX n'embarque pas d'encodeur. Windows (Game Bar, Win+Alt+G) et l'appli NVIDIA (Alt+F10) enregistrent déjà les 30 dernières secondes avec l'encodeur de la carte ; ONYX les allume, dit ce que ça coûte, et range les clips. Un enregistreur maison (Windows.Graphics.Capture + Media Foundation) serait un plan à part.

### Task 7: Bibliothèque de clips

**Files:**
- Create: `src/Clips.cs`, `src/ClipsMachine.cs`, `src/ClipsForm.cs`
- Modify: `src/PageGames.cs` (bouton « Clips »), `src/GameDetailForm.cs` (clips du jeu)
- Test: `tests/ClipsTests.cs`

**Interfaces:**
- Produces :

```csharp
internal static partial class Clips
{
    public sealed class Clip { public string Chemin, Jeu; public DateTime Date; public long Octets; }
    public static bool EstVideo(string chemin);                     // .mp4 .mkv .mov .webm, casse ignorée
    public static string JeuDepuisChemin(string racine, string chemin); // sous-dossier direct de la racine, sinon nom du fichier avant la date, sinon « Autres »
    public static List<Clip> Trie(IEnumerable<Clip> clips, string jeu); // plus récent d'abord ; jeu null = tous
    public static List<Clip> Balaye(IEnumerable<string> racines, int maxFichiers); // machine : profondeur ≤ 3
}
```

- [ ] **Step 1: Tests** : `JeuDepuisChemin(@"C:\V", @"C:\V\Fortnite\clip.mp4")` → « Fortnite » ; `(@"C:\V\Captures", @"C:\V\Captures\Fortnite 2026-11-26 21-04-11.mp4")` → « Fortnite » ; `(@"C:\V", @"C:\V\a.mp4")` → « Autres » ; `EstVideo("x.MP4")` vrai, `EstVideo("x.png")` faux ; `Trie` : ordre par date décroissante, filtre par jeu insensible à la casse.
- [ ] **Step 2: Banc** → KO, écrire `src/Clips.cs`, banc → 0 KO.
- [ ] **Step 3: Racines** : dossier Captures de Windows (dossier connu `FOLDERID_AppCaptures`, à défaut `%USERPROFILE%\Videos\Captures`), `%USERPROFILE%\Videos`, plus les dossiers ajoutés par l'utilisateur (`bt-clips-dossiers.txt`). Balayage sur un fil de fond, borné à 5 000 fichiers.
- [ ] **Step 4: `ClipsForm`** : liste par jeu (vignette de l'Explorateur, date, taille). Actions : Lire (lecteur par défaut), Afficher dans le dossier, Copier le fichier (se colle dans Discord), Corbeille après confirmation. Aucune suppression définitive.
- [ ] **Step 5: Vérifier pour de vrai** : un clip pris avec Win+Alt+G apparaît après « Actualiser », se lit, se copie dans Discord, part à la Corbeille et s'y retrouve.
- [ ] **Step 6: Commit** `Clips : bibliothèque des captures par jeu`.

### Task 8: Allumer la capture des 30 dernières secondes

**Files:**
- Create: `src/CaptureFond.cs`
- Modify: `src/Tweaks.cs` (un réglage `clips_fond_on`, près de `gamedvr_off` ligne 534), `src/ClipsForm.cs`
- Test: `tests/CaptureFondTests.cs`

**Interfaces:**
- Produces : `internal static class CaptureFond { public enum Etat { BloqueeParStrategie, Coupee, Active } public static Etat Juge(int? strategie, int? captureApplis, int? historique); public static string Explique(Etat e, bool gamedvrOffApplique); }`

- [ ] **Step 1: Relever les vraies clés** [VERIFICATION REQUIRED] : `reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR"` et `reg query "HKCU\System\GameConfigStore"` avant puis après avoir basculé « Enregistrer ce qui s'est passé » dans Paramètres > Jeux > Captures. Attendu : `HistoricalCaptureEnabled` et `HistoricalBufferLength` ; écrire dans le code les noms et valeurs réellement observés.
- [ ] **Step 2: Tests** : `Juge(0, 1, 1)` → `BloqueeParStrategie` ; `Juge(null, 0, 1)` → `Coupee` ; `Juge(null, 1, 0)` → `Coupee` ; `Juge(null, 1, 1)` → `Active`. `Explique(BloqueeParStrategie, true)` nomme le réglage « Désactiver Game DVR » et son bouton « Rétablir ».
- [ ] **Step 3: Banc** → KO, écrire `src/CaptureFond.cs`, banc → 0 KO.
- [ ] **Step 4: Réglage `clips_fond_on`** : ni recommandé ni eSport, réversible par le mécanisme d'état d'origine existant ; refuse de s'appliquer tant que `gamedvr_off` ou `gamedvr_policy_off` est appliqué et le dit. Description : « Windows garde en mémoire les 30 dernières secondes. Coût non mesuré sur ton PC : mesure-le. »
- [ ] **Step 5: Bandeau dans `ClipsForm`** : état (`Explique`), bouton Activer / Rétablir, rappel du raccourci Win+Alt+G, et « Mesurer le coût en jeu » (`PreuveJeu`). Si l'appli NVIDIA est installée : une ligne « Relecture instantanée NVIDIA : Alt+Z puis Alt+F10 », sans pilotage.
- [ ] **Step 6: Vérifier pour de vrai** : activer, jouer, Win+Alt+G → un fichier de 30 s arrive dans la bibliothèque ; « Rétablir » remet les valeurs relevées à l'étape 1.
- [ ] **Step 7: Commit** `Clips : capture des 30 dernières secondes de Windows, activable et mesurable`.

---

## Lot E — Rappel de nettoyage

### Task 9: Analyse hebdomadaire du poids mort, avec notification

**Files:**
- Create: `src/RappelNettoyage.cs`
- Modify: `src/StorageConfigForm.cs` (case à cocher), `src/DashboardForm.cs` (démarrage)
- Test: `tests/RappelNettoyageTests.cs`

**Interfaces:**
- Consumes : `JunkScan.Scan(long minBytes, int totalBudgetMs, Action<string,int> log)`, `JunkScan.Total(List<Item>)`, `JunkScan.DefaultMinBytes`, `DashboardForm.Notifie`.
- Produces : `internal static class RappelNettoyage { public const int Jours = 7; public static bool Du(DateTime? derniere, DateTime maintenant); public static string Texte(long octets, int elements); }` ; état dans `bt-rappel-nettoyage.txt` (activé, date de la dernière analyse).

- [ ] **Step 1: Tests** : `Du(null, t)` vrai ; `Du(t.AddDays(-6), t)` faux ; `Du(t.AddDays(-7), t)` vrai ; `Du(t.AddDays(1), t)` vrai (horloge reculée) ; `Texte(5368709120, 3)` → « 5 Go à récupérer dans 3 éléments. Ouvre Stockage pour choisir. » ; `Texte` sous 1 Go → null (pas de notification).
- [ ] **Step 2: Banc** → KO, écrire `src/RappelNettoyage.cs`, banc → 0 KO.
- [ ] **Step 3: Brancher** : case « Me prévenir chaque semaine » dans la configuration du Stockage, décochée par défaut. Cinq minutes après le démarrage d'ONYX, si la case est cochée, `Du` est vrai et aucun jeu ne tourne : `JunkScan.Scan(DefaultMinBytes, 20000, …)` sur un fil de fond, puis une notification si `Texte` n'est pas null. Rien n'est supprimé.
- [ ] **Step 4: Vérifier pour de vrai** : case cochée, date du fichier d'état reculée de 8 jours, redémarrer ONYX → notification dans les 6 minutes, la date est mise à jour, pas de seconde notification au redémarrage suivant.
- [ ] **Step 5: Commit** `Stockage : rappel hebdomadaire du poids mort, sans rien supprimer`.

---

### Task 10: Version, journal et vérification d'ensemble

**Files:**
- Modify: `CHANGELOG.md`, `src/WhatsNew.cs`, le numéro de version là où la 16.04 est posée (`git -c safe.directory=E:/bt show 7cdd8e0 --stat` montre les fichiers)

- [ ] **Step 1: Banc complet** → 0 KO, total supérieur à celui du départ (à noter avant la Tâche 1).
- [ ] **Step 2: Banc de fluidité** de la v15.97 (`BT_PERF=1 BT_PERF_VISIBLE=1 BT_PERF_BUDGET=1`) → aucune ligne `PERF-KO` : les pages Jeux et Bons plans restent dans leur budget avec le journal et l'import.
- [ ] **Step 3: Entrée de `CHANGELOG.md`** et « Quoi de neuf », une ligne par lot, dans le ton des entrées existantes.
- [ ] **Step 4: Commit** `v16.05 — Temps de jeu, liste de souhaits Steam, préréglages Fortnite, clips, rappel de nettoyage`.
- [ ] **Step 5: S'arrêter là.** Publication (release, winget) seulement si Lucas écrit « publie ».
