# Profil par jeu, topologie et Compétition — plan de construction

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal :** profil par jeu actif dans la fenêtre principale, avec un « Auto » qui suit la
topologie réelle du processeur (cœurs P / CCD V-Cache) par CPU Sets, et un bouton
« Compétition » réversible exactement, jugeable par la mesure en jeu.

**Architecture :** chaque module a deux parties.
- Une partie pure, testée au banc : `Topologie.Preferes`, `ProfilJeu.Decide`, les
  `Competition.*`, `Preuve.JugeJeu`.
- Une partie machine : Win32, processus, powercfg, fenêtres.

Les états sont écrits en fichiers clé=valeur sous `AppPaths.File`, dans le même style que
`Preuve.Ecrit`.

**Tech Stack :** C# .NET 10 WinForms, P/Invoke kernel32. Le banc maison est
`tests/ONYX.Tests.csproj`, avec `Banc.Verifie(nom, attendu, obtenu)`.

**Spec :** `docs/superpowers/specs/2026-11-17-competition-profil-jeu-design.md`, qui fait foi, et
en particulier sa section « Décisions de construction ».

## Global Constraints

### Commandes

| Usage | Commande ou réglage |
|---|---|
| Git | `git -c safe.directory=E:/bt …` |
| Branche | `feat/sans-cle`. Ne rien pousser. |
| Compilation | `"/c/Program Files/dotnet/dotnet" build BTOptimizer.csproj -c Release -p:OutDir="$O/out/"` |
| Variante BTTEST | `-p:DefineConstants=BTTEST -p:OutDir="$O/outtest/" -p:IntermediateOutputPath="$O/objtest/"` |
| Banc | `"/c/Program Files/dotnet/dotnet" run --project tests/ONYX.Tests.csproj -c Release` |

Pour la compilation, `$O` vaut
`C:/Users/User/AppData/Local/Temp/claude/959ccad4428ff1b3/330cbc41-bdad-48c1-840c-52a667547bd0/scratchpad`.
L'exe du dépôt peut être verrouillé, d'où ce dossier de sortie.

Le banc doit rester à 0 KO. Base : 367 OK.

### Fichiers et commits

- Fichiers en UTF-8 sans BOM, fins de ligne LF : `sed -i 's/\r$//' fichier` après chaque Edit.
- Pas de fichier JSON ni de dépendance NuGet nouvelle.
- Messages et textes en français.
- Fin de chaque message de commit : `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

### Fichiers d'état

- `bt-profiljeu-etat.txt`
- `bt-competition.txt`
- inchangé : `bt-game-affinity.txt`, au format `jeu|mode|masque`.

### Règles de comportement

- « Auto » passe par des CPU Sets, **jamais** par une affinité stricte, même en cas d'échec.
- « Manuel » garde l'affinité stricte.
- Plus de 64 processeurs logiques, ou plusieurs groupes de processeurs : aucune restriction.
- Plan « Performances » : `8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c`.
- Compétition, valeurs par défaut :

  | Case | Par défaut |
  |---|---|
  | Profil « Auto » | cochée |
  | Mode Jeu automatique | cochée |
  | Réglages eSport | cochée |
  | Plafond FPS | décochée |

## Review Focus

1. **Refus d'accès sur le processus du jeu** (anti-triche) :
   - un seul message au journal par lancement du jeu, pas un toutes les 2 s ;
   - aucune affinité stricte en repli.
   - Test : tâche 4, `Decide` ne redemande pas d'appliquer pour le même jeu déjà traité.
2. **ONYX fermé pendant une partie** :
   - au lancement suivant, le plan d'origine est rétabli grâce au fichier d'état.
   - Test : tâche 4, aller-retour `EcritEtat` / `LitEtat`, et un état vide rend null.
3. **Compétition activée deux fois** : la seconde activation est refusée. Sinon l'état
   « d'avant » serait écrasé par un état déjà modifié.
   - Test : tâche 6, `PeutActiver(etatExistant)` vaut false.
4. **Processeur homogène avec SMT** : les **deux** processeurs logiques du cœur 0 sont écartés.
   Sans SMT, seul le processeur 0 l'est.
   - Test : tâche 1.
5. **Deux CCD de même cache** (7950X, pas X3D) : on applique la règle « homogène », pas un
   seul CCD.
   - Test : tâche 1.

---

### Task 1 : Topologie — règle pure

**Files :**
- Create : `src/Topologie.cs` (partie pure seulement dans cette tâche)
- Test : `tests/TopologieTests.cs`
- Modify : `tests/ONYX.Tests.csproj` (`<Compile Include="..\src\Topologie.cs" />` et
  `TopologieTests.cs`), et `tests/Banc.cs` (`TopologieTests.Tout();` après `PreuveTests.Tout();`)

**Interfaces :**
- Produces :
  - `internal static partial class Topologie`
  - `public sealed class Proc { public int Index; public int Coeur; public int Efficacite; public int L3; public long L3Octets; }`
    - `Index` : processeur logique, groupe 0.
    - `Coeur` : identifiant du cœur physique.
    - `L3` : identifiant du groupe de cache L3.
  - `public static List<int> Preferes(IList<Proc> procs)` : index triés. Une liste vide veut
    dire « aucune restriction ».
  - `public static string Libelle(IList<Proc> procs)` : par exemple
    « Intel hybride : 16 logiques P sur 24 », « X3D : CCD 96 Mo », « homogène : cœur 0 écarté ».

- [ ] **Step 1 : écrire `TopologieTests.Tout()`**

  Une fabrique `P(index, coeur, eff, l3, octets)`. Assertions :

  | Cas | Construction | Résultat attendu |
  |---|---|---|
  | Hybride | 8 cœurs P (eff 1, SMT : 16 logiques 0..15) + 8 cœurs E (eff 0, logiques 16..23), un seul L3 | `Preferes` = 0..15 |
  | X3D | logiques 0..15 sur L3 0 de 96 Mo, logiques 16..31 sur L3 1 de 32 Mo, eff toutes 0 | `Preferes` = 0..15 |
  | X3D inversé | le CCD à 96 Mo porte les index 16..31 | `Preferes` = 16..31 |
  | Deux CCD de même taille | 32 Mo et 32 Mo, SMT | `Preferes` = tout sauf 0 et 1 (cœur 0) |
  | Homogène SMT | 8 cœurs, 16 logiques, un L3 | tout sauf {0, 1} |
  | Homogène sans SMT | 6 logiques sur 6 cœurs | {1..5} |
  | Un seul cœur, 2 logiques | | liste vide |
  | Liste vide ou null | | liste vide |
  | Index ≥ 64 | | liste vide |

- [ ] **Step 2 : lancer le banc**

  Attendu : échec de compilation, car `Topologie` n'existe pas encore.

- [ ] **Step 3 : implémenter `Preferes` et `Libelle`**

  Ordre des règles de la spec §1 :
  1. Si l'`Efficacite` varie : garder la classe maximale.
  2. Sinon, si les tailles L3 diffèrent : garder les processeurs du groupe L3 à plus grande
     taille.
  3. Sinon, s'il y a au moins 2 cœurs : retirer tous les processeurs logiques du `Coeur` qui
     porte l'index 0.
  4. Sinon : liste vide.

- [ ] **Step 4 : lancer le banc**

  Attendu : tous les tests de topologie OK, 0 KO.

- [ ] **Step 5 : commit** — `Topologie : choix des cœurs du jeu (P, V-Cache, sinon hors cœur 0)`

### Task 2 : Topologie — lecture machine, CPU Sets et `-topologie`

**Files :**
- Create : `src/TopologieMachine.cs` (partie machine, non incluse au banc)
- Modify : `src/Cli.cs` (commande `-topologie`)

**Interfaces :**
- Consumes : `Topologie.Proc`, `Topologie.Preferes`, `Topologie.Libelle`
- Produces (`partial class Topologie`, dans `TopologieMachine.cs`) :
  - `public static List<Proc> Lit()` : liste vide si l'appel échoue ou s'il y a plus d'un groupe
    de processeurs. Mise en cache pour la durée du processus.
  - `public static bool AppliqueCpuSets(int pid, IList<int> indices, Action<string, int> log)`

- [ ] **Step 1 : implémenter `Lit()`**

  Deux appels `GetLogicalProcessorInformationEx` :
  - `RelationProcessorCore` (0) : `EfficiencyClass`, et `GroupMask` du cœur → `Coeur`
    (rang du cœur) ;
  - `RelationCache` (2), filtrer `Level == 3` : `CacheSize`, et `GroupMask` → `L3` (rang du cache).

  Tout `GroupMask.Group != 0` → rendre une liste vide.

- [ ] **Step 2 : implémenter `AppliqueCpuSets`**

  1. Appeler `GetSystemCpuSetInformation`.
  2. Faire correspondre `LogicalProcessorIndex` et `Group == 0` avec l'`Id` du CPU Set.
  3. `OpenProcess(0x2000 /*PROCESS_SET_LIMITED_INFORMATION*/, false, pid)`.
  4. `SetProcessDefaultCpuSets(h, ids, (uint)ids.Length)`, puis `CloseHandle`.

  Cas particuliers :
  - Indices vides → `SetProcessDefaultCpuSets(h, null, 0)`, ce qui retire la préférence.
  - Échec → `log("CPU Sets refusés pour <nom> (code N) : jeu laissé tel quel.", 2)` et
    `return false`.
  - **Pas** d'affinité en repli.

- [ ] **Step 3 : ajouter la commande `Cli -topologie`**

  Elle affiche `Libelle(Lit())` et la liste `Preferes`, puis rend 0.

- [ ] **Step 4 : vérifier**

  1. Compiler en Release et en BTTEST : 0 erreur.
  2. Lancer `"$O/out/ONYX.exe" -topologie`, en administrateur, sur ce PC.
     Attendu : un libellé cohérent avec le processeur installé (`wmic cpu get name` pour
     comparer).

- [ ] **Step 5 : commit** — `Topologie : lecture du processeur, CPU Sets, commande -topologie`

### Task 3 : Plafond FPS dans la mesure en jeu

**Files :**
- Modify : `src/Preuve.cs` (`MesureJeu`, `EcritJeu`, `LitJeu`, `JugeJeu`), `src/PreuveJeu.cs`
  (`Mesure`), `tests/PreuveTests.cs`

**Interfaces :**
- Produces : `MesureJeu.Plafond` (`int`, défaut `-1` = inconnu) ; clé `plafond=` dans
  `EcritJeu` et `LitJeu`.

- [ ] **Step 1 : tests ajoutés dans la section « Preuve — validation en jeu »**

  | Avant | Après | Verdict attendu |
  |---|---|---|
  | `Plafond = 0` | `Plafond = 138` | `Incomparable` |
  | `-1` | `138` | même verdict que sans plafond (Neutre pour J(144,90) contre J(140,85)) |
  | `138` | `138` | jugé normalement |

  Aller-retour `LitJeu(EcritJeu(m))` : `Plafond` est conservé.

  Texte sans clé `plafond=` → `Plafond == -1`.

- [ ] **Step 2 : lancer le banc.** Attendu : KO sur les nouveaux tests.

- [ ] **Step 3 : implémenter**

  - Dans `JugeJeu`, après le contrôle de validité : si `avant.Plafond >= 0 && apres.Plafond >= 0
    && avant.Plafond != apres.Plafond`, alors `Incomparable`.
  - Dans `PreuveJeu.Mesure` : `m.Plafond = FrameCap.Pose()` (dans un try ; en cas d'échec, -1).
  - Dans `PreuveForm.FiniJeu`, cas Incomparable : si les plafonds diffèrent, écrire
    « Le plafond FPS a changé entre les deux mesures (x → y) : comparaison impossible. »

- [ ] **Step 4 : lancer le banc.** Attendu : 0 KO.

- [ ] **Step 5 : commit** — `Mesure en jeu : un plafond FPS changé rend la comparaison impossible`

### Task 4 : ProfilJeu — décision et état (pur)

**Files :**
- Create : `src/ProfilJeu.cs` (partie pure)
- Test : `tests/ProfilJeuTests.cs`
- Modify : `tests/ONYX.Tests.csproj`, `tests/Banc.cs`

**Interfaces :**
- Produces (`internal static partial class ProfilJeu`) :
  - `public enum Geste { Rien, Appliquer, Retirer }`
  - `public sealed class Etat { public string Jeu; public string PlanOrigine; }`
    - `Jeu` : jeu profilé en cours, null si aucun.
    - `PlanOrigine` : GUID, null si ONYX n'a pas basculé le plan.
  - `public static Geste Decide(string jeuEnCours, string jeuDetecte)`
    - `null`, `"X"` → Appliquer ;
    - `"X"`, `"X"` → Rien ;
    - `"X"`, `null` → Retirer ;
    - `"X"`, `"Y"` → Retirer ; le tour suivant appliquera Y.
  - `public enum Mode { Aucun, CpuSets, Affinite }`
  - `public static Mode Cible(string mode, long masque, IList<int> preferes, out long masqueAffinite)`
    - `"Auto"` + `preferes` non vide → `CpuSets` ;
    - `"Auto"` + `preferes` vide → `Aucun` ;
    - `"Manuel"` + masque ≠ 0 → `Affinite` (`masqueAffinite = masque`) ;
    - autre → `Aucun`.
  - `public static string EcritEtat(Etat e)`, `public static Etat LitEtat(string texte)` : null
    si le texte est vide ou si `Jeu` est absent. Clés : `jeu=`, `plan=`.
  - `public const string PlanPerformances = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";`
  - `public static bool DoitBasculer(string planActif)` : vrai si `planActif` n'est ni null ni
    égal (sans tenir compte de la casse) à `PlanPerformances`.

- [ ] **Step 1 : tests**

  - Les quatre cas de `Decide`.
  - Les cas de `Cible`, dont « Auto » avec un ancien masque « tout sauf 0 » et `preferes` vide
    → `Aucun` : le masque est ignoré.
  - `DoitBasculer` : plan Performances (en majuscules) → false ; null → false ; un autre
    GUID → true.
  - Aller-retour de l'état ; `LitEtat("")` → null.

- [ ] **Step 2 à 4 : rouge, implémentation, vert**

- [ ] **Step 5 : commit** — `ProfilJeu : décision lancement/sortie et état (pur)`

### Task 5 : ProfilJeu — machine, branchement dans les deux fenêtres

**Files :**
- Create : `src/ProfilJeuMachine.cs` (hors banc)
- Modify :
  - `src/MainForm.cs:1570-1630` : remplacer le bloc « Dynamic Affinity & Priority » et la
    restauration du plan par `ProfilJeu.Tick(Log);`, et retirer `_activeGameName` /
    `_originalPowerProfile` s'ils deviennent inutilisés ;
  - `src/DashboardForm.cs:1254` : `ProfilJeu.Tick(Log);` juste après `AutoJeu.Tick` ;
  - `src/DashboardForm.cs:675` : `try { ProfilJeu.Soigne(Log); } catch { }` ;
  - `src/GameProfileForm.cs:119` : libellé « Automatique (cœurs P ou V-Cache, sinon hors
    cœur 0) ».

**Interfaces :**
- Consumes : `Topologie.Lit/Preferes/AppliqueCpuSets`, `ProfilJeu.Decide/Cible/EcritEtat/LitEtat/DoitBasculer`,
  `Sys.LoadGameAffinity`, `GameScan.ExesFor`, `Sys.SetProcessAffinity/SetProcessPriority/GetActivePowerProfile/SetActivePowerProfile`
- Produces :
  - `public static void Tick(Action<string, int> log)` : non bloquant. Il sort tout de suite
    si un geste est en cours ; sinon le travail part sur `Task.Run`, avec un drapeau
    `_occupe`.
  - `public static void Soigne(Action<string, int> log)`
  - `public const string FichierEtat = "bt-profiljeu-etat.txt";`

Comportement de `Tick` :
1. Jeu détecté = premier jeu de `LoadGameAffinity()` dont un exécutable tourne (même ordre
   qu'aujourd'hui).
2. Sur **Appliquer** :
   - pour **chaque** processus de ses exécutables, appliquer `Cible` (CPU Sets ou affinité) et
     la priorité Haute ;
   - si `DoitBasculer(GetActivePowerProfile())`, écrire l'état (jeu + plan) **avant** de
     basculer sur Performances ; sinon écrire l'état avec `plan` vide ;
   - une ligne de journal par jeu.
3. Sur **Retirer** :
   - rétablir le plan si l'état en a un ;
   - supprimer le fichier ;
   - pas de retrait des CPU Sets, puisqu'ils disparaissent avec le processus.

L'état en mémoire est statique, sous verrou, et partagé par les deux fenêtres.

- [ ] **Step 1 : implémenter**

- [ ] **Step 2 : compiler Release et BTTEST, lancer le banc.** Attendu : 0 erreur, 0 KO.

- [ ] **Step 3 : test manuel** (consigné dans le message de fin, pas au banc)
  1. Profil « Auto » sur un jeu installé.
  2. Lancer le jeu, ONYX ouvert sur le tableau de bord. Attendu au journal : CPU Sets appliqués
     et plan basculé, `powercfg /getactivescheme` = Performances.
  3. Fermer le jeu. Attendu : plan d'origine rétabli.

- [ ] **Step 4 : commit** — `Profil par jeu actif dans la fenêtre principale, plan rétabli après plantage`

### Task 6 : Compétition — état et règles (pur) + sélection imposée dans PreuveRun

**Files :**
- Create : `src/Competition.cs` (partie pure)
- Test : `tests/CompetitionTests.cs`
- Modify : `tests/ONYX.Tests.csproj`, `tests/Banc.cs`, `src/PreuveRun.cs`

**Interfaces :**
- Produces (`internal static partial class Competition`) :
  - `public sealed class Etat { public DateTime Active; public bool AutoJeuAvant; public int PlafondAvant = -1; public List<string> Profils = new List<string>(); public List<string> Reglages = new List<string>(); }`
    - `PlafondAvant = -1` : plafond non touché.
  - `public static string Ecrit(Etat e)`, `public static Etat Lit(string texte)` : null si
    `Active` est absent. Listes séparées par `,`.
  - `public static bool PeutActiver(Etat existant)` : vrai si `existant == null`.
  - `public static List<string> JeuxSansProfil(IEnumerable<string> installes, IDictionary<string, Tuple<string, long>> profils)`
  - `public static List<string> ProfilsARetirer(Etat e, IDictionary<string, Tuple<string, long>> profils)` :
    garde seulement ceux encore présents **et** en mode « Auto ».
  - `public const string Fichier = "bt-competition.txt";`
- Produces (`PreuveRun`) :
  - `public static Rapport Lance(List<Tweak> all, Action<string, int> log, Func<bool> arret, IList<string> idsImposes)` :
    si `idsImposes` n'est pas null, cette liste remplace `Hardware.AutoTuneIds`.
  - La surcharge à 3 paramètres appelle celle-ci avec `null`.

- [ ] **Step 1 : tests**
  - Aller-retour d'`Etat`, avec des listes vides et pleines.
  - `PeutActiver(null)` vaut true ; `PeutActiver(new Etat())` vaut false.
  - `JeuxSansProfil` exclut, sans tenir compte de la casse, les jeux qui ont déjà un profil.
  - `ProfilsARetirer` :
    - un profil passé en « Manuel » est gardé ;
    - un profil supprimé par l'utilisateur est ignoré ;
    - un profil resté « Auto » est retiré.

- [ ] **Step 2 à 4 : rouge, implémentation, vert.**

  Pour `PreuveRun`, il suffit de compiler et de faire passer le banc.

- [ ] **Step 5 : commit** — `Compétition : état réversible et règles (pur) ; PreuveRun accepte une sélection`

### Task 7 : Compétition — activation, annulation, fenêtre, bouton

**Files :**
- Create : `src/CompetitionMachine.cs` (hors banc), `src/CompetitionForm.cs`
- Modify :
  - `src/PageOptimisations.cs:72-92,269` : bouton `_bCompet` « 🏁 Compétition », largeur 130,
    placé à gauche de `_bProve` ;
  - `src/PreuveForm.cs` (`FiniJeu`, cas Régression).

**Interfaces :**
- Consumes : tâches 4 à 6, `PreuveRun.Lance(…, idsImposes)`, `AutoJeu.Actif`,
  `FrameCap.Pose/Applique/Recommande/FrequenceEcran`, `GameScan.Known()/Detect()`,
  `Sys.Load/SaveGameAffinity`, `Engine.Run`, `Sys.CreateRestorePoint`
- Produces :
  - `public static Etat Active()` : lit le fichier, null s'il n'y a rien.
  - `public sealed class Choix { public bool Profils = true, ModeJeu = true, Esport = true, Plafond = false; }`
  - `public static PreuveRun.Rapport Active(List<Tweak> all, Choix c, Action<string, int> log, Func<bool> arret)`
  - `public static bool Annule(List<Tweak> all, Action<string, int> log)`

Ordre de `Active` :
1. Refuser si `!PeutActiver(Active())`.
2. Écrire l'état « d'avant » (`AutoJeuAvant`, et `PlafondAvant` si la case Plafond est cochée)
   **avant** tout changement.
3. Point de restauration, seulement si eSport est décoché. Sinon, celui de `PreuveRun` suffit.
4. Profils « Auto » pour `JeuxSansProfil` des jeux `Detected`.
5. `AutoJeu.Actif = true`.
6. eSport : `PreuveRun.Lance(all, log, arret, ids des t.Esport)`, puis `Reglages = Gardes + EnAttente`.
   `PreuveRun` alimente déjà « la dernière preuve ».
7. Plafond : `Applique(Recommande(FrequenceEcran()))`.
8. Réécrire l'état complet.

Ordre de `Annule` :
1. Rétablir les `Reglages` encore actifs (`Check() != false`) par
   `Engine.Run(l, false, true, false, log)`.
2. Retirer `ProfilsARetirer`.
3. Remettre `AutoJeu.Actif = AutoJeuAvant`.
4. Si `PlafondAvant >= 0` : `Applique(PlafondAvant)`.
5. Supprimer le fichier.

Fenêtre `CompetitionForm` :
- même style que `PreuveForm` (journal, `_stop` volatile, fermeture bloquée pendant le
  travail) ;
- les 4 cases et le texte « Conseil : Mesure en jeu — avant (fenêtre Optimiser et prouver),
  puis Compétition, puis Mesure en jeu — APRÈS » ;
- boutons « Activer » / « Arrêter » et « Annuler Compétition », ce dernier visible si
  `Active() != null`.

`PreuveForm.FiniJeu`, cas Régression confirmée, si `Competition.Active() != null` : proposer
« Annuler Compétition (tout ce qu'elle a changé) » au lieu de la liste de réglages. Puis
appeler `Competition.Annule` hors du fil de l'interface, et `ClotureCycle()`.

- [ ] **Step 1 : implémenter**

- [ ] **Step 2 : compiler Release et BTTEST, lancer le banc.** Attendu : 0 erreur, 0 KO.

- [ ] **Step 3 : test manuel** (consigné)
  1. Activer Compétition avec eSport décoché (rapide).
  2. Vérifier : profils ajoutés dans `bt-game-affinity.txt`, Mode Jeu automatique activé,
     `bt-competition.txt` présent.
  3. Annuler Compétition. Attendu : l'état d'avant revient, le fichier est supprimé.

- [ ] **Step 4 : commit** — `Bouton Compétition : activation en un clic et annulation exacte`

### Task 8 : Documentation, vérification, commit final

**Files :**
- Modify : `CHANGELOG.md` (section « Prochaine version »), spec (section « Réalisation »)

- [ ] **Step 1 : mettre à jour la documentation**

  - Dans le CHANGELOG : profil par jeu dans la fenêtre principale, Auto topologique, Compétition,
    plafond dans la mesure en jeu.
  - Dans la spec, section « Réalisation » : les fichiers créés et les écarts éventuels.

- [ ] **Step 2 : faire contrôler la branche par le sous-agent `verificateur`**, contre la spec
  et ce plan (Review Focus en tête). Corriger les défauts réels.

- [ ] **Step 3 : compiler (Release et BTTEST) et lancer le banc.** Attendu : 0 erreur, 0 KO.
  Puis commit — `Phase 3 : documentation`.
