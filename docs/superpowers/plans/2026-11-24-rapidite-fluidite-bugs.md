# Rapidité, fluidité et bugs d'ONYX — plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Aucune action de l'interface d'ONYX ne bloque plus de 100 ms (30 ms pour revenir sur une page déjà ouverte), rien ne gèle le fil d'interface en tâche de fond, et les bugs trouvés par mesure sont corrigés avec un test chacun.

**Architecture:** On mesure d'abord (banc `BT_PERF`, fenêtre réellement affichée), on corrige ensuite la cause dominante de chaque dépassement, on remesure. Trois leviers : (1) la page Optimisations passe de ~1 300 contrôles Windows à un seul contrôle peint et virtualisé ; (2) tout ce qui est périodique ou lent (PDH, liste des processus) sort du fil d'interface ; (3) une page déjà affichée ne se reconstruit que si ses données ont changé.

**Tech Stack:** C# / .NET 10 WinForms (`net10.0-windows10.0.19041.0`), harnais `BTTEST` dans `src/Program.cs`, banc unitaire `tests/ONYX.Tests.csproj`. Aucune nouvelle dépendance.

**Spec:** demande de Lucas du 24/11/2026 (« améliore la rapidité et la fluidité d'ONYX à son paroxysme et corrige tous les bugs ») + mesures de référence ci-dessous. Pas d'autre document.

## Mesures de référence (24/11/2026, cette machine, Release, fenêtre affichée)

Commande : voir Tâche 1. Deux séries, valeurs en ms. « creation » = `Goto(page)` seul ; « suite » = travail différé posté au fil d'interface ; « retour » = `Goto` sur une page déjà créée.

| Étape | Mesuré | Budget visé |
|---|---|---|
| Optimisations.creation | 2 087 – 2 174 | ≤ 150 |
| Optimisations.suite | 155 – 161 | ≤ 30 |
| Optimisations.peinture | 144 – 146 | ≤ 35 |
| Optimisations.retour | 648 – 709 | ≤ 30 |
| Actus.retour | 327 – 342 | ≤ 30 |
| Jeux.retour | 228 | ≤ 30 |
| Bons plans.retour | 176 – 185 | ≤ 30 |
| Bons plans.creation | 121 – 128 | ≤ 100 |
| Consultation.creation + suite | 81 – 89 + 69 – 71 | ≤ 100 au total |
| Consultation.peinture | 63 – 72 | ≤ 35 |
| Consultation.retour.suite | 70 – 75 | ≤ 30 |
| hwmonitor.constructeur (fil d'interface, 1re page) | 237 – 240 | ≤ 5 |
| jeuconnu.moyenne (fil d'interface, toutes les 2 s, ×2 si Mode Jeu auto) | 6,9 – 7,7 | 0 sur le fil d'interface |
| shell.constructeur / shell.affichage | 96 / 67 | inchangé (pas un levier) |
| Autres pages (création, retour, peinture) | ≤ 60 | ≤ 100 / 30 / 35 |

Faits établis par mesure, à ne pas rediscuter :
- **Emballage** : ReadyToRun et/ou non compressé ne changent rien de mesurable (4 variantes × 6 lancements : 1 242–1 666 ms dans tous les cas, exe de 60 à 220 Mo). On ne touche pas à la publication.
- `Catalog.All()` : 22 ms au premier appel, 0,1 ms ensuite. Pas un levier.
- Hors écran (sans handles Windows) Optimisations ne coûte que 200 ms : le coût réel vient des handles et des messages Windows de ~210 cartes × 6 contrôles. D'où le contrôle unique.

## Global Constraints

- Aucun changement de comportement des optimisations elles-mêmes (`Tweak.Apply/Revert/Check`), ni des textes affichés.
- Accessibilité conservée : chaque carte reste atteignable au clavier et lisible par un lecteur d'écran (nom, description, état coché).
- Aucune nouvelle dépendance NuGet ; pas de changement d'emballage ni de manifeste.
- Git : toujours `git -c safe.directory=E:/bt`. Un commit par tâche, message terminé par la ligne `Co-Authored-By` en vigueur dans la session.
- Aucun secret, aucune donnée personnelle dans le dépôt.
- Banc unitaire : `dotnet run --project tests/ONYX.Tests.csproj -c Release` → 0 KO (1 258 OK au départ, le total ne peut que monter).
- Pas de sous-agents ni de sessions annexes (préférence de Lucas).
- Publication (Tâche 9) : seulement sur un « publie » explicite de Lucas.

## Review Focus

1. **Filtre qui ne garde aucune optimisation** (recherche sans résultat) : liste vide, message existant conservé, pas d'exception, défilement remis à 0. → test Tâche 2.
2. **Fenêtre redimensionnée très étroite ou maximisée sur un écran large** : 1 colonne au minimum, aucune carte coupée, hauteur totale recalculée. → test Tâche 2.
3. **Retour de la zone de notification après des heures** : les valeurs du Dashboard et de Système se remettent à bouger tout de suite. → test Tâche 6.
4. **Relevé de processus plus long que 2 s** (machine chargée, antivirus) : pas d'empilement de relevés, pas de bascule Mode Jeu en double. → test Tâche 4.
5. **Page quittée ou fenêtre fermée pendant un travail de fond** (échantillon matériel, relevé de jeu) : aucun `ObjectDisposedException`, le moniteur est libéré une seule fois. → test Tâche 3 et Tâche 4.

---

### Task 1: Banc de mesure commité, budgets et sentinelle de gel

**Files:**
- Modify: `src/Program.cs` (`TestHarness.Run`, `MesurePerf` — déjà écrits, non commités)
- Create: `src/SentinelleGel.cs`

**Interfaces:**
- Produces : `BT_PERF=1` (+ `BT_PERF_VISIBLE=1`) imprime `PERF;étape;ms`. `BT_PERF_BUDGET=1` : code de sortie 1 et ligne `PERF-KO;étape;ms;budget` pour chaque dépassement.
- Produces : `internal static class SentinelleGel { static void Demarre(Control cible, int seuilMs); static int Arrete(); /* nombre de gels */ }` — un fil de fond poste un `BeginInvoke` toutes les 25 ms et imprime `GEL;ms` quand la réponse du fil d'interface dépasse `seuilMs`. Compilé seulement sous `BTTEST`.

- [ ] **Step 1 : budgets dans `MesurePerf`.** `private static double Budget(string etape)` : `.creation` → 100 (Optimisations : 150), `.suite` et `.retour` et `.retour.suite` → 30, `.peinture` → 35, `hwmonitor.constructeur` → 5, sinon `double.MaxValue`. Si `BT_PERF_BUDGET=1`, `note` imprime `PERF-KO` et `MesurePerf` renvoie le nombre de dépassements → `Environment.Exit(n > 0 ? 1 : 0)`.
- [ ] **Step 2 : étapes ajoutées au banc.** Après la boucle des retours : `Optimisations.recherche` (page 1, champ de recherche = `"dns"`, `DoEvents`, budget 30) ; `fuite.gdi` et `fuite.user` (20 tours des 11 pages, différence de `GetGuiResources(processus, 0)` et `(…, 1)` avant/après, budget 50 objets chacun).
- [ ] **Step 3 : `SentinelleGel`** démarrée par `MesurePerf` sur `dash` avec un seuil de 100 ms pendant les boucles ; imprime le total `PERF;gels;N` (budget 0, vérifié à partir de la Tâche 5).
- [ ] **Step 4 : exécuter et constater l'échec attendu.**

```bash
cd /e/bt && dotnet build BTOptimizer.csproj -c Release -o build/perf-test -p:DefineConstants=BTTEST --nologo -v q
BT_PERF=1 BT_PERF_VISIBLE=1 BT_PERF_BUDGET=1 BT_UITEST=1 dotnet build/perf-test/BTOptimizer.dll | grep -E "^PERF-KO"; echo "code=$?"
```

Attendu : des lignes `PERF-KO` pour au moins Optimisations (creation, suite, peinture, retour), Actus.retour, Jeux.retour, Bons plans (creation, retour), Consultation, hwmonitor.constructeur.
- [ ] **Step 5 : commit** `perf: banc BT_PERF (budgets, gels, fuites GDI)`.

### Task 2: Optimisations — un seul contrôle peint et virtualisé

**Files:**
- Create: `src/GrilleCartes.cs` (calcul pur, sans WinForms)
- Create: `src/TweakListe.cs` (le contrôle)
- Modify: `src/PageOptimisations.cs` (`Populate`, `MakeCard` 199–282, `LayoutCards` 328–361, `RefreshStatesAsync` 283–294, recherche ligne 55, `OnShown` ligne 30)
- Modify: `src/Program.cs:2641` (test du bouton « détails », à porter sur `TweakListe`)
- Test: `tests/GrilleCartesTests.cs` (+ ajout de `src/GrilleCartes.cs` aux sources liées de `tests/ONYX.Tests.csproj` et appel dans le lanceur du banc)

**Interfaces:**
- Produces :

```csharp
internal static class GrilleCartes
{
    public struct Case { public int X, Y, W, H; }
    public const int Ecart = 16, LargeurMin = 360;
    public static int Colonnes(int largeurDispo);                                   // max(1, largeurDispo / 360)
    public static int LargeurCarte(int largeurDispo);                               // largeurDispo / Colonnes - 16
    public static Case[] Place(int largeurDispo, int[] hauteurs, out int hauteurTotale);  // rangée = hauteur max de ses cartes
    public static void Visibles(Case[] cases, int defilement, int hauteurVue, out int premier, out int dernier); // aucun → 0, -1
    public static int IndexA(Case[] cases, int x, int y);                           // -1 hors carte
}

internal sealed class TweakListe : Control
{
    public void Definir(IReadOnlyList<Tweak> visibles);   // remplace la liste filtrée ; défilement à 0 si la liste change
    public void Etat(string id, bool actif);              // invalide le seul rectangle de la carte
    public void EtatsCharges();                           // remplace l'actuel _statesLoaded
    public event Action<Tweak, bool> Bascule;             // clic ou Espace sur l'interrupteur : état demandé
    internal int Nombre { get; }
    internal Rectangle RectCarte(int index);              // coordonnées client, défilement compris
    internal void TestClicDetails(int index);             // pour le harnais
    internal bool Deplie(int index);
}
```

- Consumes : `Tweak` (`Id`, `Name`, `Desc`, `Category`, `Check`), `OnyxDesign.PaintCard`, `OnyxDesign.IconWell`, `FpsUi.*`, dessin de `ToggleSwitch` (extraire sa peinture en méthode statique réutilisable plutôt que la recopier).

- [ ] **Step 1 : tests de `GrilleCartes` (échec attendu : type inconnu).**

```csharp
Banc.Egal("300 px : 1 colonne", 1, GrilleCartes.Colonnes(300));
Banc.Egal("1300 px : 3 colonnes", 3, GrilleCartes.Colonnes(1300));
Banc.Egal("0 px : 1 colonne", 1, GrilleCartes.Colonnes(0));
int total; var c = GrilleCartes.Place(736, new[] { 100, 140, 90, 90, 120 }, out total);   // 2 colonnes de 352
Banc.Egal("carte 1 à droite", 368, c[1].X);
Banc.Egal("rangée 2 sous la plus haute de la rangée 1", 156, c[2].Y);
Banc.Egal("rangée 3", 262, c[4].Y);
Banc.Egal("hauteur totale (écart final compris)", 398, total);
int a, b; GrilleCartes.Visibles(c, 150, 100, out a, out b);
Banc.Verifie("vue 150–250 : rangée 2 seule", true, a == 2 && b == 3);
GrilleCartes.Visibles(new GrilleCartes.Case[0], 0, 500, out a, out b);
Banc.Verifie("liste vide : aucune carte", true, a == 0 && b == -1);
GrilleCartes.Visibles(c, 5000, 100, out a, out b);
Banc.Verifie("défilement au-delà de la fin : aucune carte", true, b < a);
Banc.Egal("clic dans l'écart entre deux cartes", -1, GrilleCartes.IndexA(c, 360, 20));
```

- [ ] **Step 2 :** `dotnet run --project tests/ONYX.Tests.csproj -c Release` → échec de compilation sur `GrilleCartes`.
- [ ] **Step 3 : écrire `GrilleCartes`.**
- [ ] **Step 4 :** banc unitaire → 0 KO.
- [ ] **Step 5 : écrire `TweakListe`.** Un seul `Control` double-tamponné, `AutoScroll` remplacé par une barre de défilement verticale propre (`VScrollBar` enfant) et la molette. `OnPaint` ne dessine que `Visibles(...)`. Rendu et dimensions repris à l'identique de `MakeCard`/`LayoutCards` (puits d'icône 32 px en 16,16 ; titre `FpsUi.H3` ; description `FpsUi.Small` repliée à 52 px ; bouton « détails » seulement si la description dépasse 52 px ; ligne de statut ; interrupteur en haut à droite). Hauteurs mesurées une fois par (`Id`, largeur de carte) et gardées en cache ; cache vidé quand la largeur change. Survol, clic (interrupteur → `Bascule`, « détails » → déplier), clavier : flèches = carte focalisée, Espace = bascule, Entrée = déplier, la carte focalisée est amenée dans la vue. Accessibilité : `CreateAccessibilityInstance()` renvoie un objet dont les enfants sont les cartes filtrées (`Role = CheckButton`, `Name` = titre, `Description` = description, `State` = `Checked`/`Focused`, `DoDefaultAction` = bascule, `Bounds` = `RectCarte`).
- [ ] **Step 6 : brancher la page.** `PageOptimisations` : `_flow` remplacé par `TweakListe _liste` ; `Populate()` devient `_liste.Definir(filtrées)` (filtre = puce + `Match`) ; `MakeCard`, `LayoutCards`, `_toggles` supprimés ; le gestionnaire de bascule existant (confirmation, application, journal) est appelé depuis `_liste.Bascule` sans changement de logique ; `RefreshStatesAsync` appelle `_liste.Etat(id, actif)` puis `EtatsCharges()` ; `OnShown` : `DoLayout(); if (!_built) { Populate(); _built = true; } RefreshStatesAsync();` — `DoLayout` ne refait rien si la taille n'a pas changé.
- [ ] **Step 7 : tests du harnais** (dans `TestShellUi`, à la place du test de `Program.cs:2641`) :
  - après `Goto(1)` : `_liste.Nombre` == nombre d'optimisations du catalogue ; `page.Controls` ne contient plus de carte (moins de 40 contrôles descendants au total) ;
  - recherche `"zzzzqq"` → `Nombre == 0`, aucune exception, puis recherche vide → `Nombre` d'origine ;
  - `ClientSize` 420×600 puis 2400×1300 : `RectCarte(Nombre - 1)` a une largeur ≥ 284 et tient dans la largeur du contrôle ;
  - `TestClicDetails(i)` sur une carte à description longue → `Deplie(i)` vrai et `RectCarte(i).Height` augmente ;
  - l'objet d'accessibilité expose `Nombre` enfants, le premier avec `Role == CheckButton` et un `Name` non vide.
- [ ] **Step 8 : vérifier.** Banc unitaire 0 KO ; `BT_UITEST=1 dotnet build/perf-test/BTOptimizer.dll` sans KO ; banc `BT_PERF_BUDGET` : plus aucune ligne `PERF-KO;Optimisations.*`.
- [ ] **Step 9 : contrôle visuel.** `BT_UISHOT` de la page avant/après (même taille de fenêtre), comparer les deux captures : même disposition, mêmes textes.
- [ ] **Step 10 : commit** `perf(optimisations): liste peinte et virtualisée (2,1 s → < 150 ms)`.

### Task 3: `HwMonitor` — amorçage PDH hors du fil d'interface

**Files:**
- Modify: `src/HwMonitor.cs:143-153` (constructeur), `Sample()`, `Dispose`
- Modify si besoin : `src/MonitorForm.cs`, `src/StatsOverlay.cs`, `src/StressForm.cs`, `src/ThermalForm.cs`, `src/MainForm.cs:1550`

**Interfaces:**
- Produces : `new HwMonitor()` ne fait plus que `FindNvidiaSmi()`. `private void Amorce()` (PDH : `PdhOpenQuery`, `PdhAddEnglishCounter`, premier `PdhCollectQueryData`) est appelée une fois, sous verrou, au début de `Sample()`. `Dispose()` sans `Sample()` préalable ne ferme rien et ne lève pas.

- [ ] **Step 1 : test** dans le harnais (`BT_UITEST`) : `var sw = Stopwatch.StartNew(); var m = new HwMonitor();` → `sw.ElapsedMilliseconds < 20` ; `m.Dispose()` sans exception ; second moniteur : `Sample()` deux fois à 300 ms d'écart sur un fil de fond → `CpuLoad` du second échantillon ∈ [0, 100] ; `Dispose()` pendant un `Sample()` en cours (autre fil) → aucune exception.
- [ ] **Step 2 :** exécuter → échec sur la durée du constructeur (~240 ms).
- [ ] **Step 3 : implémenter `Amorce()`.**
- [ ] **Step 4 : appelants.** Pour chacun des cinq fichiers ci-dessus, si le premier `Sample()` est appelé sur le fil d'interface, le déplacer dans `Task.Run` + `BeginInvoke` (modèle : `PageDashboard.Sample`, lignes 226–240).
- [ ] **Step 5 :** harnais sans KO ; banc `BT_PERF_BUDGET` : plus de `PERF-KO;hwmonitor.constructeur`.
- [ ] **Step 6 : commit** `perf(hwmonitor): amorçage PDH au premier échantillon, hors fil d'interface`.

### Task 4: Veille de jeu — un seul relevé de processus, hors du fil d'interface

**Files:**
- Create: `src/VeilleJeu.cs`
- Modify: `src/DashboardForm.cs:1243-1270` (`AutoTimer`), `src/GameScan.cs:21-36`, `src/AutoJeuVeille.cs:30` (`Tick`), `src/ProfilJeuMachine.cs:33,86` (`Tick`, `NomsProcessus`)
- Test: `tests/VeilleJeuTests.cs`

**Interfaces:**
- Produces :

```csharp
internal static class VeilleJeu
{
    public sealed class Releve { public bool PleinEcran; public string JeuConnu; public string[] Noms; }
    public static Releve Prend();                                        // un seul Process.GetProcesses(), noms distincts
    public static string JeuConnuParmi(IEnumerable<string> noms);        // pur ; ensemble des noms connus construit une fois
}
```

- `GameScan.RunningKnownGame()` : conservé pour ses autres appelants, délègue à `VeilleJeu.JeuConnuParmi`.
- `AutoJeu.Tick(Action<string> log, Action apres, VeilleJeu.Releve r)` et `ProfilJeu.Tick(Action<string> log, VeilleJeu.Releve r)` : surcharges qui n'énumèrent plus les processus ; les anciennes signatures appellent `VeilleJeu.Prend()` puis la surcharge.
- `DashboardForm.AutoTimer()` : `if (Interlocked.Exchange(ref _veille, 1) == 1) return;` puis `Task.Run` → `VeilleJeu.Prend()` → `BeginInvoke(Applique(r))` → `_veille = 0` dans un `finally` (y compris si la fenêtre est détruite).

- [ ] **Step 1 : tests unitaires (échec attendu : type inconnu).**

```csharp
Banc.Egal("jeu connu trouvé", "cs2", VeilleJeu.JeuConnuParmi(new[] { "explorer", "cs2" }));
Banc.Egal("casse ignorée", "CS2", VeilleJeu.JeuConnuParmi(new[] { "CS2" }));
Banc.Verifie("aucun jeu", true, VeilleJeu.JeuConnuParmi(new[] { "explorer", "chrome" }) == null);
Banc.Verifie("liste vide", true, VeilleJeu.JeuConnuParmi(new string[0]) == null);
Banc.Verifie("liste nulle", true, VeilleJeu.JeuConnuParmi(null) == null);
```

- [ ] **Step 2 :** banc unitaire → échec de compilation.
- [ ] **Step 3 : écrire `VeilleJeu`, brancher `GameScan`, `AutoJeu`, `ProfilJeu`, `AutoTimer`.** Tout ce qui touche des contrôles (`Log`, `Crosshair.AutoTick`, `AnimSettings.SetGameRunning`) reste dans `Applique`, sur le fil d'interface.
- [ ] **Step 4 : tests du harnais.** (a) `AutoTimer()` (passé `internal`) appelé 50 fois de suite : durée totale < 50 ms sur le fil d'interface ; (b) `VeilleJeu.Prend` remplacé par un relevé factice qui dort 3 s (`internal static Func<Releve> PrendPourTest`) : 3 appels à `AutoTimer()` → un seul relevé lancé ; (c) `dash.Dispose()` pendant le relevé factice → aucune exception dans `bt-erreurs.txt` ni sur la sortie.
- [ ] **Step 5 :** banc unitaire 0 KO, harnais sans KO.
- [ ] **Step 6 : commit** `perf(veille): un relevé de processus par tick, hors fil d'interface`.

### Task 5: Retour sur une page sans reconstruction (Jeux, Actus, Bons plans, Consultation)

**Files:**
- Modify: `src/PageGames.cs:103-119` (`OnShown`, `Render`), `src/PageActus.cs:100-106` (`OnShown`, `Affiche`), `src/PageBonsPlans.cs:210-219` (`OnShown`, `MajOnglets`, `_partner.RefreshCampaign`), `src/PageConsultation.cs:365-385` (`OnShown`, `Greet`, peinture)

**Interfaces:**
- Règle commune : chaque page garde `private string _signatureRendu` = représentation de tout ce que son rendu lit (identifiants affichés dans l'ordre, filtres, mode, largeur). `OnShown` ne reconstruit que si la signature a changé ; la méthode de rendu enregistre la signature à la fin.

- [ ] **Step 1 : ventiler avant de corriger.** Dans le banc, sous `BT_PERF_DETAIL=1`, chronométrer séparément dans le `OnShown` de chaque page ses appels (`DoLayout`, rendu, `RefreshCampaign`, `MajOnglets`, `Greet`, `Invalidate(true)`) et noter la cause dominante de chacune dans le message de commit. Ne corriger que la cause mesurée.
- [ ] **Step 2 : tests du harnais (échec attendu avant correction).**
  - Jeux : après chargement, compter les appels à `Render()` (`internal int RendusPourTest`) ; `Goto(0); Goto(2);` → compteur inchangé ; masquer un jeu par l'API qu'utilise la fiche puis `Goto(2)` → compteur +1 et la tuile absente (régression du commentaire de `PageGames.cs:107`).
  - Actus : `Goto(0); Goto(10);` → `Affiche()` non rappelée ; ajouter un jeu suivi → `Recharge()` rappelée (comportement actuel conservé).
  - Bons plans : `Goto(0); Goto(9);` → aucun contrôle recréé (même instance de la première carte d'offre avant/après) ; alerte de suivi non vue → sous-onglet « Suivis » toujours affiché.
  - Consultation : `Goto(0); Goto(6);` → le champ de saisie a le focus, le message d'accueil n'est pas dupliqué.
- [ ] **Step 3 : implémenter la signature page par page**, un commit par page si les causes diffèrent.
- [ ] **Step 4 : Consultation.creation et .peinture** : appliquer la cause trouvée au Step 1 (budget 100 / 35).
- [ ] **Step 5 :** harnais sans KO ; banc `BT_PERF_BUDGET` : aucune ligne `PERF-KO` restante, `PERF;gels;0`.
- [ ] **Step 6 : commit** `perf(pages): pas de reconstruction au retour si rien n'a changé`.

### Task 6: Bug — valeurs figées au retour de la zone de notification

**Files:**
- Modify: `src/PageDashboard.cs:81-85`, `src/PageSystem.cs:118-121`

**Interfaces:**
- Produces : `internal bool MinuteurActif` sur `PageDashboard` et `PageSystem`.
- Cause (lue dans le code, à confirmer par le test) : `OnVisibleChanged` arrête le minuteur quand la fenêtre est cachée et ne le relance jamais ; `DashboardForm.RestoreFromTray()` (ligne 1177) ne rappelle pas `OnShown`.

- [ ] **Step 1 : test du harnais** (fenêtre affichée hors écran) : `Goto(0)` → `MinuteurActif` vrai ; `dash.Hide(); DoEvents` → faux ; `dash.Show(); DoEvents` → **vrai** ; idem `Goto(7)` pour Système ; page non courante → son minuteur reste arrêté après `Show()`.
- [ ] **Step 2 :** exécuter → échec « minuteur arrêté après retour ».
- [ ] **Step 3 : corriger** : `if (_disposed) return; if (Visible) { Sample(); _timer.Start(); } else _timer.Stop();` dans les deux pages.
- [ ] **Step 4 :** harnais sans KO.
- [ ] **Step 5 : commit** `fix: Dashboard et Système reprennent leur rafraîchissement au retour de la zone de notification`.

### Task 7: Chasse aux bugs par preuves

**Files:**
- Modify selon résultats : `src/DashboardForm.cs:1145`, `src/MainForm.cs:466`, `src/PageConsultation.cs:815` (menus contextuels locaux) ; fichiers listés au Step 3.

- [ ] **Step 1 : tour complet du harnais.** Lancer chaque mode d'interface et relever tout KO ou exception : `BT_UITEST` seul, puis avec `BT_GAMES_UI`, `BT_DEALS_UI`, `BT_STORAGE_UI`, `BT_CHECKUP_UI`, `BT_LAB_UI`, `BT_COPILOT_UI`, `BT_COMPARE_UI`, `BT_BADGES_UI`, `BT_HARDWARE_UI`, `BT_LOGO_UI`, `BT_MONET_UI` (=1 chacun). Chaque KO devient une sous-tâche : cause, test qui échoue, correctif, commit.
- [ ] **Step 2 : menus contextuels détruits trop tôt** (classe du plantage 15.95, `bt-erreurs.txt`). Pour les trois sites ci-dessus : test du harnais qui ouvre le menu, simule un clic d'élément (`PerformClick`) puis `Close()` et `DoEvents` → aucune `ObjectDisposedException`. Correctif si échec : libération différée, modèle `PageBonsPlans.cs:271`.
- [ ] **Step 3 : appels bloquants sur le fil d'interface.** `grep -nE "\.Result\b|\.Wait\(\)|GetAwaiter\(\)\.GetResult\(\)|WaitForExit\(" src/*.cs` (12 fichiers, dont `Updater.cs`, `OffresReseau.cs`, `Ocr.cs`, `WebSearch.cs`). Pour chaque occurrence, remonter à l'appelant : gestionnaire d'événement ou minuteur WinForms sans `Task.Run` = à déplacer en fond (`Task.Run` + `BeginInvoke`). Preuve : `SentinelleGel` à 100 ms pendant le tour du Step 1 → 0 ligne `GEL`.
- [ ] **Step 4 : fuites.** Banc : `fuite.gdi` et `fuite.user` ≤ 50 objets après 20 tours. Si dépassement : chercher par dichotomie de page, corriger (`Dispose` des `Font`/`Brush`/`Bitmap`/contrôles retirés).
- [ ] **Step 5 : journal d'erreurs.** Après tous les passages, `bt-erreurs.txt` du dossier de test n'a aucune nouvelle entrée.
- [ ] **Step 6 :** un commit `fix:` par bug corrigé, avec son test.

### Task 8: Vérification d'ensemble

- [ ] **Step 1 :** `dotnet run --project tests/ONYX.Tests.csproj -c Release` → 0 KO.
- [ ] **Step 2 :** banc `BT_PERF=1 BT_PERF_VISIBLE=1 BT_PERF_BUDGET=1`, trois séries → code 0 les trois fois ; reporter le tableau avant/après dans `CHANGELOG.md` (section 15.97).
- [ ] **Step 3 :** exe réel publié en local (procédure 15.96, sans mise en ligne), lancé en administrateur : ouvrir chaque page, taper dans la recherche d'Optimisations, basculer une optimisation réversible puis la rétablir, réduire dans la zone de notification 1 minute puis revenir — tout répond sans gel visible.
- [ ] **Step 4 :** relecture du diff complet (`git diff <base>..HEAD`) : rien hors périmètre, aucun code de mesure actif hors `BTTEST`.

### Task 9: Version 15.97 (sur « publie » de Lucas uniquement)

- [ ] **Step 1 :** version 15.97 dans `BTOptimizer.csproj`, `installer/BTOptimizer.iss`, `CHANGELOG.md`.
- [ ] **Step 2 :** procédure de publication de la 15.96 (archive, `dotnet publish`, ISCC, zip, `gh release create v15.97 -R destingood/onyx-releases --target main --latest`, manifeste winget + `winget validate`), commit et push.

---

## Hors périmètre (non mesuré, à décider après ce plan)

- **Cadence des animations** (`Anim`) : le harnais les coupe (`Anim.ForceOff`), aucune mesure n'existe. À mesurer après la Tâche 5, quand le fil d'interface ne sera plus bloqué par le reste.
- **Durée des optimisations elles-mêmes** (application d'un tweak, nettoyage, scans) : travail de fond, pas de gel d'interface constaté.
- **Démarrage à froid** (96 ms de constructeur + 67 ms d'affichage, plus l'extraction du fichier unique) : pas de levier trouvé côté code ; l'emballage a été testé sans gain.
