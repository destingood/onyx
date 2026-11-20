# Réversibilité exacte (phase 1) — plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal :** « Rétablir » remet chaque réglage dans l'état exact d'avant ONYX (valeur, type, existence), et le préréglage « Sûr » ne coche que des réglages annulables à l'identique, sans risque et sans promesse non prouvée.

**Architecture :** l'état d'origine est capturé AU MOMENT DE L'ÉCRITURE, dans les primitives de `Sys` (registre, alimentation, services, tâches), pendant l'`Apply()` d'un réglage ; aucun des 210 `Apply` n'est réécrit. Un journal JSON par réglage (`bt-etat-origine.json`) est rejoué par « Rétablir » ; sans journal, l'ancien `Revert()` sert de secours. Toute écriture que les primitives ne savent pas capturer (bcdedit, fsutil, netsh, profil NVIDIA) marque le réglage « annulation non garantie ».

**Tech Stack :** C# `net10.0-windows10.0.19041.0`, WinForms, `System.Text.Json` (inclus dans .NET, aucun paquet ajouté), banc `tests/ONYX.Tests.csproj` (assertions maison `Banc.Verifie` / `Banc.Egal`).

**Spec :** `docs/plan-parite-linux.md`, phase 1 (ajouts 1, 2, 3).

## Global Constraints

- Aucun paquet NuGet ajouté. `System.Text.Json` du framework.
- Convention du dépôt : logique PURE dans un fichier compilé par `tests/ONYX.Tests.csproj`, lecture/écriture machine dans un fichier `…Machine.cs` non compilé par le banc.
- Noms, commentaires et messages en français, comme le reste de `src/`.
- Le comportement des `Apply()` existants ne change pas. Seuls `Engine.Run` et les primitives d'écriture sont modifiés.
- `Engine.cs:70-71` est le seul endroit qui appelle `Apply()`/`Revert()` : c'est là que se branche le journal.
- Le test aller-retour refuse de tourner hors machine virtuelle (il applique puis annule les 210 réglages).
- Fichier du journal : `AppPaths.File("bt-etat-origine.json")`.
- Compilation : `dotnet build E:\bt\BTOptimizer.csproj -c Release -v q` → `0 Erreur(s)` (ou `0 Error(s)`).
- Banc : `dotnet run --project E:\bt\tests\ONYX.Tests.csproj` → dernière ligne `N OK, 0 KO`.
- Git : depuis le shell de Claude, `git` refuse le dépôt (« dubious ownership »). Les étapes « Commit » demandent l'accord de Lucas pour `git config --global --add safe.directory E:/bt`, ou se font depuis son terminal. Rien n'est poussé.

## Review Focus

1. **Journal illisible ou corrompu** (fichier tronqué, édité à la main) : ONYX ne plante pas, « Rétablir » retombe sur l'ancien `Revert()`, le fichier est mis de côté (`.corrompu-AAAAMMJJ-HHmmss`) et non écrasé en silence. → Task 1.
2. **Réglage déjà appliqué par une ancienne version d'ONYX** (présent dans `bt-appliques.txt`, absent du journal) : ne pas capturer, sinon « l'origine » serait la valeur d'ONYX et « Rétablir » ne ferait rien. → Task 2.
3. **Deux réglages qui écrivent la même valeur**, rétablis dans un ordre ou dans l'autre : l'état final après les deux rétablissements est l'origine. → Task 2.
4. **Réglage appliqué deux fois** : la seconde fois ne remplace pas l'origine capturée la première fois. → Task 2.
5. **Échec au milieu d'une restauration** (accès refusé, service verrouillé) : le journal n'est pas modifié, l'erreur remonte dans le journal d'ONYX, un second « Rétablir » peut finir le travail. → Task 2 (pureté de `Retire`) et Task 3 (sauvegarde seulement en cas de succès).

---

### Task 1 : Journal d'état d'origine — modèle, encodage, fichier

**Files :**
- Create : `src/EtatOrigine.cs`
- Create : `tests/EtatOrigineTests.cs`
- Modify : `tests/ONYX.Tests.csproj` (ajouter `<Compile Include="..\src\EtatOrigine.cs" />`)
- Modify : `tests/Banc.cs:333` (appeler `EtatOrigineTests.Tout();`)

**Interfaces :**
- Produces (namespace `BTOptimizer`, `internal static class EtatOrigine`) :
  - `public sealed class Item { public string Genre; public string Cible; public string Nom; public bool Existe; public string Kind; public string Valeur; public long Seq; }`
    - `Genre` ∈ `"reg"`, `"cle"`, `"alim"`, `"service"`, `"tache"`.
    - `reg` : `Cible` = chemin complet tel que `RegistryKey.Name` (`HKEY_LOCAL_MACHINE\…`, `HKEY_USERS\S-1-…\…`, `HKEY_CURRENT_USER\…`), `Nom` = nom de valeur (`""` pour la valeur par défaut), `Kind` = nom de `RegistryValueKind`, `Valeur` = encodée.
    - `cle` : `Cible` = chemin complet, `Existe` = la clé existait.
    - `alim` : `Cible` = GUID du sous-groupe, `Nom` = GUID du paramètre, `Valeur` = `"ac;dc"` en décimal.
    - `service` : `Cible` = nom du service, `Valeur` = `"start;delayed;running"` (`start` = valeur `Start`, `-1` si absent ; `delayed` et `running` = `0`/`1`).
    - `tache` : `Cible` = chemin de la tâche, `Valeur` = `"active"`, `"inactive"` ou `"inconnue"`.
  - `public sealed class Entree { public string Id; public string Date; public bool Complet = true; public List<string> NonCouvert = new(); public List<Item> Items = new(); }`
  - `public sealed class Journal { public int Version = 1; public long Prochain = 1; public List<Entree> Entrees = new(); public bool Corrompu; }`
  - `public static string Identite(Item i)` → `Genre + "|" + Cible + "|" + Nom`, insensible à la casse (normalisé en `ToUpperInvariant`).
  - `public static string EncodeValeur(object valeur, RegistryValueKind kind)` / `public static object DecodeValeur(string texte, RegistryValueKind kind)`.
  - `public static Journal Lire(string json)` / `public static string Ecrire(Journal j)`.

- [ ] **Step 1 : écrire les tests qui échouent** (`tests/EtatOrigineTests.cs`, classe `EtatOrigineTests`, méthode `Tout()`)

```csharp
Banc.Titre("État d'origine : encodage et fichier");
// Aller-retour d'encodage, un cas par type
Banc.Egal("DWord", "4294967295", EtatOrigine.EncodeValeur(unchecked((int)0xFFFFFFFF), RegistryValueKind.DWord));
Banc.Verifie("DWord relu", true, (int)EtatOrigine.DecodeValeur("4294967295", RegistryValueKind.DWord) == -1);
Banc.Verifie("QWord relu", true, (long)EtatOrigine.DecodeValeur(EtatOrigine.EncodeValeur(1L << 40, RegistryValueKind.QWord), RegistryValueKind.QWord) == 1L << 40);
Banc.Egal("ExpandString non développée", "%SystemRoot%\\x", (string)EtatOrigine.DecodeValeur(EtatOrigine.EncodeValeur("%SystemRoot%\\x", RegistryValueKind.ExpandString), RegistryValueKind.ExpandString));
Banc.Egal("MultiString", "a|b||c", string.Join("|", (string[])EtatOrigine.DecodeValeur(EtatOrigine.EncodeValeur(new[] { "a", "b", "", "c" }, RegistryValueKind.MultiString), RegistryValueKind.MultiString)));
Banc.Egal("Binary en hexadécimal", "00FF10", EtatOrigine.EncodeValeur(new byte[] { 0, 255, 16 }, RegistryValueKind.Binary));
// Fichier
var j = new EtatOrigine.Journal();
j.Entrees.Add(new EtatOrigine.Entree { Id = "cpu_unpark_cores", Date = "2026-11-20",
    Items = { new EtatOrigine.Item { Genre = "alim", Cible = "s", Nom = "p", Existe = true, Valeur = "100;100", Seq = 1 } } });
var relu = EtatOrigine.Lire(EtatOrigine.Ecrire(j));
Banc.Egal("relecture : valeur", "100;100", relu.Entrees[0].Items[0].Valeur);
Banc.Verifie("relecture : non corrompu", false, relu.Corrompu);
Banc.Verifie("texte vide = journal vide, non corrompu", true, EtatOrigine.Lire("").Entrees.Count == 0 && !EtatOrigine.Lire("").Corrompu);
Banc.Verifie("texte illisible = journal vide marqué corrompu", true, EtatOrigine.Lire("{tronqu").Corrompu);
Banc.Verifie("identité insensible à la casse", true,
    EtatOrigine.Identite(new EtatOrigine.Item { Genre = "reg", Cible = "HKEY_LOCAL_MACHINE\\A", Nom = "x" })
 == EtatOrigine.Identite(new EtatOrigine.Item { Genre = "reg", Cible = "hkey_local_machine\\a", Nom = "X" }));
```

- [ ] **Step 2 : lancer le banc** — `dotnet run --project E:\bt\tests\ONYX.Tests.csproj` → échec de compilation (`EtatOrigine` introuvable).
- [ ] **Step 3 : implémenter `src/EtatOrigine.cs`** — encodage : DWord/QWord en décimal invariant non signé (DWord lu en `uint`, rendu en `int`), String/ExpandString tels quels, MultiString en tableau JSON dans la chaîne (`JsonSerializer.Serialize(string[])`), Binary/None/Unknown en hexadécimal majuscule. `Lire` attrape toute exception de `JsonSerializer` et rend `new Journal { Corrompu = true }`. Options JSON : `IncludeFields = true`, `WriteIndented = true`.
- [ ] **Step 4 : relancer le banc** → `N OK, 0 KO`.
- [ ] **Step 5 : commit** — `feat(reversibilite): journal d'état d'origine (modèle et fichier)`.

### Task 2 : Règles du journal (capture, retrait, écarts)

**Files :**
- Modify : `src/EtatOrigine.cs`
- Modify : `tests/EtatOrigineTests.cs`

**Interfaces :**
- Consumes : Task 1.
- Produces :
  - `public static bool DoitCapturer(bool entreeExiste, bool dejaDansAppliques)` → `entreeExiste || !dejaDansAppliques`.
  - `public static Entree Ouvre(Journal j, string id, DateTime quand)` → l'entrée existante, sinon une nouvelle ajoutée (date `yyyy-MM-dd`).
  - `public static void Note(Journal j, Entree e, Item avant)` → ajoute `avant` avec `Seq = j.Prochain++` seulement si aucune entrée de `e.Items` n'a la même `Identite`.
  - `public static Journal Retire(Journal j, string id, out List<Item> aRestaurer)` → rend un NOUVEAU journal sans l'entrée `id` ; `j` n'est pas modifié. Règle pour chaque item `x` de l'entrée retirée : s'il existe dans une autre entrée un item de même identité avec `Seq > x.Seq`, celui de plus petit `Seq` parmi eux reçoit `Existe`, `Kind`, `Valeur` de `x` et `x` n'est pas restauré ; sinon `x` va dans `aRestaurer`. `aRestaurer` est trié par `Seq` décroissant.
  - `public static List<string> Ecarts(List<Item> origine, List<Item> actuel)` → une ligne lisible par item différent ; `reg` compare `Existe`, puis `Kind` et `Valeur` si les deux existent ; `cle` compare `Existe` ; `service` compare `start` et `delayed` (pas `running`) ; `alim` et `tache` comparent `Valeur`. Item absent de `actuel` = écart « non relu ».
  - `public static int[] LitPowercfg(string sortie)` → `[ac, dc]` = les deux DERNIÈRES lignes de la forme `…: 0x<hex>` de `powercfg /query` (indépendant de la langue) ; `null` si moins de deux.

- [ ] **Step 1 : tests qui échouent**

```csharp
Banc.Titre("État d'origine : règles");
Banc.Verifie("ancienne version d'ONYX : pas de capture", false, EtatOrigine.DoitCapturer(false, true));
Banc.Verifie("premier passage : capture", true, EtatOrigine.DoitCapturer(false, false));
Banc.Verifie("entrée existante : on la complète", true, EtatOrigine.DoitCapturer(true, true));

EtatOrigine.Item R(string v) => new EtatOrigine.Item { Genre = "reg", Cible = "HKEY_LOCAL_MACHINE\\K", Nom = "x", Existe = v != null, Kind = "DWord", Valeur = v };
// Réapplication : l'origine ne bouge pas
var j = new EtatOrigine.Journal();
var a = EtatOrigine.Ouvre(j, "A", new DateTime(2026, 11, 20));
EtatOrigine.Note(j, a, R("0"));
EtatOrigine.Note(j, a, R("1"));
Banc.Egal("réapplication : origine conservée", "0", a.Items.Single().Valeur);
// Même valeur, A puis B ; retrait de A d'abord
var b = EtatOrigine.Ouvre(j, "B", new DateTime(2026, 11, 20));
EtatOrigine.Note(j, b, R("1"));                       // B voit la valeur écrite par A
var j2 = EtatOrigine.Retire(j, "A", out var r1);
Banc.Verifie("A retiré pendant que B tient la valeur : rien à restaurer", true, r1.Count == 0);
Banc.Egal("B hérite de l'origine de A", "0", j2.Entrees.Single().Items.Single().Valeur);
var j3 = EtatOrigine.Retire(j2, "B", out var r2);
Banc.Egal("puis B retiré : on restaure l'origine", "0", r2.Single().Valeur);
// Même valeur, retrait de B d'abord
var j4 = EtatOrigine.Retire(j, "B", out var r3);
Banc.Egal("B retiré d'abord : on remet la valeur de A", "1", r3.Single().Valeur);
// Pureté
Banc.Verifie("Retire ne modifie pas le journal d'entrée", true, j.Entrees.Count == 2 && j.Entrees[1].Items[0].Valeur == "1");
// Valeur absente à l'origine
Banc.Verifie("origine absente vs actuel présent = écart", true, EtatOrigine.Ecarts(new() { R(null) }, new() { R("1") }).Count == 1);
Banc.Verifie("identiques = aucun écart", true, EtatOrigine.Ecarts(new() { R("0") }, new() { R("0") }).Count == 0);
// powercfg
string sortie = "  Paramètre minimal possible : 0x00000000\r\n  Paramètre maximal possible : 0x00000064\r\n  Index AC actuel : 0x00000064\r\n  Index DC actuel : 0x0000000a\r\n";
Banc.Egal("powercfg : AC et DC", "100;10", string.Join(";", EtatOrigine.LitPowercfg(sortie)));
Banc.Verifie("powercfg : sortie vide", true, EtatOrigine.LitPowercfg("") == null);
```

- [ ] **Step 2 : lancer le banc** → échec (méthodes absentes).
- [ ] **Step 3 : implémenter les cinq fonctions** dans `src/EtatOrigine.cs` (`Retire` travaille sur une copie profonde via `Lire(Ecrire(j))`).
- [ ] **Step 4 : relancer le banc** → `N OK, 0 KO`.
- [ ] **Step 5 : commit** — `feat(reversibilite): règles de capture et de restauration`.

### Task 3 : Capture et restauration du registre, branchées sur Engine

**Files :**
- Create : `src/EtatOrigineMachine.cs`
- Modify : `src/Sys.cs:97-150` (`SetUser`, `DelUser`, `SetMachine`, `DelMachine`) et chaque autre `SetValue`/`DeleteValue` de `Sys.cs` (19 au total, dont `SetGpuMsi` :697 et `SetMsiForClass` :774)
- Modify : `src/Engine.cs:70-71`

**Interfaces :**
- Consumes : Tasks 1-2 ; `TweakDrift.JournalPath`, `TweakDrift.Parse(string)` (`src/TweakDrift.cs:37`, `:405`).
- Produces (`internal static class EtatOrigineMachine`) :
  - `public static void AvantValeur(RegistryKey cle, string nom)` — à appeler juste avant `SetValue`/`DeleteValue` sur `cle`. Lit avec `RegistryValueOptions.DoNotExpandEnvironmentNames`. Ne fait rien hors capture.
  - `public static void AvantCle(RegistryKey racine, string sousCle)` — à appeler avant `CreateSubKey` dans `SetUser`/`SetMachine` ; note `cle` avec `Existe`.
  - `public static void NonCouvert(string quoi)` — hors capture ou sous `Couvert()` : rien ; sinon `Complet = false` et `quoi` ajouté à `NonCouvert`.
  - `public static IDisposable Couvert()` — compteur `[ThreadStatic]` ; utilisé par les primitives des Tasks 4.
  - `public static EtatOrigine.Item Lit(EtatOrigine.Item modele)` — état actuel pour la même identité (Task 4 ajoute `alim`, `service`, `tache`).
  - `public static void Restaure(EtatOrigine.Item origine)` — `reg` : réécrit la valeur avec son type, ou la supprime si `Existe == false` ; `cle` : si `Existe == false`, supprime la clé seulement si elle n'a plus ni valeur ni sous-clé.
  - `public static void Applique(Tweak t)` et `public static void Retablit(Tweak t)`.
  - Chemin complet → clé : racines `HKEY_LOCAL_MACHINE`, `HKEY_USERS`, `HKEY_CURRENT_USER`, `HKEY_CLASSES_ROOT`.

Déroulé de `Applique(t)` : charger le journal ; `deja` = `t.Id` présent dans `TweakDrift.Parse(File.ReadAllText(TweakDrift.JournalPath))` (fichier absent → `false`) ; si `DoitCapturer(entrée existe, deja)`, ouvrir l'entrée et la placer dans le `[ThreadStatic]` courant ; `t.Apply()` dans un `try/finally` qui vide le courant puis sauvegarde le journal **même si `Apply` a levé** (les écritures partielles doivent rester annulables) ; relancer l'exception.

Déroulé de `Retablit(t)` : sans entrée → `t.Revert()`. Entrée avec `Complet == false` → `t.Revert()` d'abord (hors capture), puis le journal. Puis `Retire`, `Restaure` de chaque item dans l'ordre rendu, en collectant les erreurs. Aucune erreur → sauvegarder le nouveau journal. Au moins une erreur → ne pas sauvegarder, lever `InvalidOperationException` listant les items en échec.

Sauvegarde : écrire `…json.tmp` puis `File.Move(tmp, final, overwrite: true)`. Chargement d'un fichier `Corrompu` : le renommer en `bt-etat-origine.json.corrompu-yyyyMMdd-HHmmss` et repartir d'un journal vide.

- [ ] **Step 1 : brancher `Engine.cs:70-71`** sur `EtatOrigineMachine.Applique(t)` / `EtatOrigineMachine.Retablit(t)`.
- [ ] **Step 2 : implémenter `src/EtatOrigineMachine.cs`** (registre seulement) et poser `AvantCle`/`AvantValeur` dans les quatre primitives de `Sys.cs:97-150`, puis `AvantValeur` devant chacun des autres `SetValue`/`DeleteValue` de `Sys.cs`. `DelUserSubKeyTree` (:151) appelle `NonCouvert("suppression d'arborescence " + sub)`.
- [ ] **Step 3 : compiler** → `0 Erreur(s)` ; banc → `N OK, 0 KO`.
- [ ] **Step 4 : vérifier sur machine virtuelle** (ONYX lancé en administrateur) : noter `reg query "HKCU\Control Panel\Mouse"`, appliquer puis rétablir `mouse_accel` depuis l'interface, relancer la requête. Attendu : `MouseSpeed`, `MouseThreshold1`, `MouseThreshold2` identiques à avant (et non `1`/`6`/`10` si l'origine était autre). Mettre `MouseSpeed` à `2` avant le test pour le rendre probant.
- [ ] **Step 5 : commit** — `feat(reversibilite): Rétablir rejoue l'état d'origine du registre`.

### Task 4 : Alimentation, services, tâches, écritures non couvertes

**Files :**
- Modify : `src/EtatOrigineMachine.cs`
- Modify : `src/Sys.cs` — `SetPowerValue` :573, `SetUsbSuspend` :546, `ConfigureService` :901, `SetScheduledTask` :962, `Run`/`RunThrow` (~:400)
- Modify : `src/IrqTuning.cs`, `src/NicLatency.cs`, `src/NagleGuard.cs`, `src/QosGaming.cs`, `src/UsbPower.cs` (écritures directes : `AvantValeur` devant chaque `SetValue`/`DeleteValue`)
- Modify : `src/NvProfile.cs`, `src/FrameCap.cs` (méthodes publiques qui écrivent : `NonCouvert("profil NVIDIA")` en première ligne)

**Interfaces :**
- Consumes : Tasks 1-3, `EtatOrigine.LitPowercfg`.
- Produces : `public static void AvantAlim(string sousGroupe, string parametre)`, `public static void AvantService(string nom)`, `public static void AvantTache(string chemin)` ; `Lit` et `Restaure` gèrent `alim` (`powercfg /query SCHEME_CURRENT <sg> <p>` ; restauration `/setacvalueindex`, `/setdcvalueindex`, `/setactive SCHEME_CURRENT`), `service` (valeur `Start` + `DelayedAutostart` sous `HKLM\SYSTEM\CurrentControlSet\Services\<nom>`, `Sys.IsServiceRunning` ; restauration `sc config start=` avec 2+delayed → `delayed-auto`, 2 → `auto`, 3 → `demand`, 4 → `disabled`, puis `StartService`/`StopService` selon `running`), `tache` (`Sys.ScheduledTaskDisabled` ; restauration `Sys.SetScheduledTask`).

- [ ] **Step 1 : capture** — `SetPowerValue` et `SetUsbSuspend` : `AvantAlim(...)` puis corps sous `using (EtatOrigineMachine.Couvert())`. `ConfigureService` : `AvantService(name)` puis corps sous `Couvert()`. `SetScheduledTask` : `AvantTache` puis `Couvert()`. `LitPowercfg` rend `null` → `NonCouvert("alimentation " + parametre)`.
- [ ] **Step 2 : écritures non couvertes** — `Sys.Run`/`RunThrow` appellent `EtatOrigineMachine.NonCouvert(Path.GetFileName(exe) + " " + args)` (sans effet sous `Couvert()`). Lectures seules lancées par `Run` pendant un `Apply` (ex. `sc query`) : les envelopper dans `Couvert()` à leur appel dans `Sys` (`IsServiceRunning`, `ScheduledTaskDisabled`).
- [ ] **Step 3 : autres fichiers** — `AvantValeur` dans les cinq fichiers listés, `NonCouvert` dans `NvProfile.cs` et `FrameCap.cs`.
- [ ] **Step 4 : compiler, banc** → `0 Erreur(s)`, `N OK, 0 KO`.
- [ ] **Step 5 : vérifier sur machine virtuelle** — `powercfg /query SCHEME_CURRENT SUB_PROCESSOR CPMINCORES` ; régler l'index AC à `100` à la main ; appliquer puis rétablir `cpu_unpark_cores`. Attendu : AC = `0x00000064` (et non `0x0000000a`).
- [ ] **Step 6 : commit** — `feat(reversibilite): alimentation, services et tâches journalisés`.

### Task 5 : Test aller-retour en ligne de commande

**Files :**
- Create : `src/AllerRetour.cs`
- Modify : `src/EtatOrigine.cs` (`EstMachineDeTest`) et `tests/EtatOrigineTests.cs`
- Modify : `src/Cli.cs:89-123` (sous-commande et mode `ids=`)

**Interfaces :**
- Consumes : Tasks 1-4, `Catalog.All()` (`src/Tweaks.cs:61`), `Sys.Init()`.
- Produces :
  - `public static bool EstMachineDeTest(string fabricant, string modele)` (pur) → vrai si l'un contient, sans tenir compte de la casse : `VMware`, `VirtualBox`, `QEMU`, `KVM`, `Xen`, `Parallels`, ou fabricant `Microsoft Corporation` ET modèle `Virtual Machine` (Hyper-V, Windows Sandbox).
  - `internal static class AllerRetour { public static int Lance(List<Tweak> selection, Action<string, int> log) }` : pour chaque réglage, `Applique`, relire l'entrée du journal, copier ses items, `Retablit`, `Lit` chaque item, `Ecarts`. Une ligne de rapport par réglage : `OK`, `ÉCART <n> : <détails>`, `NON COUVERT : <quoi>`, `NON CAPTURÉ` (aucune entrée créée) ou `ÉCHEC : <message>`. Rapport écrit dans `AppPaths.File("aller-retour-yyyyMMdd-HHmmss.txt")`. Code de retour 0 si toutes les lignes sont `OK`, 6 sinon.
  - CLI : `BTOptimizer.exe -allerretour [all|ids=a,b,c]` (défaut `all`) ; hors machine de test (WMI `Win32_ComputerSystem.Manufacturer`/`Model`) → message d'erreur et code 7, aucune écriture. Le mode `ids=a,b,c` est aussi accepté par `-apply` et `-revert`.

- [ ] **Step 1 : tests qui échouent**

```csharp
Banc.Verifie("Hyper-V", true, EtatOrigine.EstMachineDeTest("Microsoft Corporation", "Virtual Machine"));
Banc.Verifie("VMware", true, EtatOrigine.EstMachineDeTest("VMware, Inc.", "VMware20,1"));
Banc.Verifie("VirtualBox", true, EtatOrigine.EstMachineDeTest("innotek GmbH", "VirtualBox"));
Banc.Verifie("PC réel", false, EtatOrigine.EstMachineDeTest("Micro-Star International Co., Ltd.", "MS-7E12"));
Banc.Verifie("Surface (Microsoft mais réel)", false, EtatOrigine.EstMachineDeTest("Microsoft Corporation", "Surface Laptop 7"));
```

- [ ] **Step 2 : banc** → échec. **Step 3 : implémenter** `EstMachineDeTest`, `AllerRetour.Lance`, la CLI. **Step 4 : banc et compilation** → `N OK, 0 KO`, `0 Erreur(s)`.
- [ ] **Step 5 : vérifier le refus** sur le PC de Lucas : `BTOptimizer.exe -allerretour` → code 7, aucun fichier `bt-etat-origine.json` créé ou modifié.
- [ ] **Step 6 : commit** — `feat(reversibilite): test aller-retour réservé aux machines virtuelles`.

### Task 6 : Champs Risque et Preuve, préréglage « Sûr »

**Files :**
- Modify : `src/Model.cs:6-19`
- Modify : `src/Tweaks.cs` (affectations) et fin de `Catalog.All()` (:61)
- Modify : `src/PageOptimisations.cs:72-100` et `:47-61` (bouton, activation)
- Modify : `tests/ONYX.Tests.csproj` (ajouter `..\src\Model.cs`), `tests/CatalogueTests.cs`

**Interfaces :**
- Produces (`src/Model.cs`) :
  - `public enum Risque { Aucun, Securite, Fonction, AnnulationNonGarantie }`
  - `public enum Preuve { NonMesure, Mesure, DependDuPc, SansObjet }`
  - Dans `Tweak` : `public Risque Risque = Risque.Aucun;` `public Preuve Preuve = Preuve.NonMesure;` `public bool Sur { get { return Risque == Risque.Aucun && Preuve != Preuve.NonMesure; } }`

Règles d'affectation (spec : « réversible exact, risque aucun, preuve mesurée ou neutre » ; « neutre » = `SansObjet`, interprétation à valider par Lucas) :
- `Securite` : `spectre_off`, `vbs_off`, et tout réglage dont le nom ou la description annonce une baisse de sécurité (Defender, UAC, SmartScreen, pare-feu, mises à jour retardées, `LLMNR` compris).
- `Fonction` : coupe une fonction que l'utilisateur peut remarquer (recherche, notifications, Bluetooth, impression, localisation, OneDrive, Store, widgets, Copilot, Recall, mise en veille prolongée, fichier d'échange, Xbox / Game Bar, fermeture d'applications).
- `AnnulationNonGarantie` : `hpet_cleanup`, tout réglage dont l'`Apply` appelle `Sys.Run`/`RunThrow` directement (bcdedit, fsutil, netsh, powercfg hors `SetPowerValue`) ou `NvProfile`/`FrameCap`/`Native`, et tout réglage signalé par le test aller-retour (Task 7).
- `Preuve.Mesure` : `audio_msi`, `windowed_flip`, `frame_cap` seulement (sources dans `docs/plan-parite-linux.md`). Tout ajout exige une source citée dans `docs/`.
- `Preuve.DependDuPc` : `gpu_msi`, `msi_usb`, `msi_storage`, `msi_network`.
- `Preuve.SansObjet` : par défaut pour `Cat.Privacy` et `Cat.Souris` (règle posée en fin de `Catalog.All()` : ne remplace que `NonMesure`).
- `Preuve.NonMesure` reste explicite pour `timer_global`, `distribuer_minuteurs`, `irq_priorite_gpu`, `w32ps`, `fso_disable`, `nagle`, `netthrottle`, `sysresp`, `tcp_bbr2`.
- `Recommended` et `Esport` ne changent pas.

- [ ] **Step 1 : tests qui échouent** (`CatalogueTests.Tout()`, lecture du source comme l'existant)

```csharp
// Model.cs compilé dans le banc
Banc.Verifie("Sûr : risque aucun + mesuré", true, new Tweak { Preuve = Preuve.Mesure }.Sur);
Banc.Verifie("Sûr : non mesuré exclu", false, new Tweak().Sur);
Banc.Verifie("Sûr : sécurité exclue", false, new Tweak { Risque = Risque.Securite, Preuve = Preuve.Mesure }.Sur);
// Source
foreach (string id in new[] { "spectre_off", "vbs_off" })
    Banc.Verifie(id + " : Risque.Securite", true, BlocDe(src, id).Contains("Risque = Risque.Securite"));
Banc.Verifie("hpet_cleanup : annulation non garantie", true, BlocDe(src, "hpet_cleanup").Contains("Risque.AnnulationNonGarantie"));
// Filet : tout bloc qui appelle Sys.Run/RunThrow, NvProfile., FrameCap. ou Native. porte un Risque autre que Aucun
foreach (var (id, bloc) in Blocs(src))
    if (Regex.IsMatch(bloc, @"Sys\.Run(Throw)?\(|NvProfile\.|FrameCap\.|Native\."))
        Banc.Verifie(id + " : écriture non journalisée hors « Sûr »", true, Regex.IsMatch(bloc, @"Risque = Risque\.(Securite|Fonction|AnnulationNonGarantie)"));
```

`BlocDe(src, id)` / `Blocs(src)` : texte de `Id = "<id>"` jusqu'au `});` suivant (aide privée dans `CatalogueTests`, sur le modèle de la regex existante :115).

- [ ] **Step 2 : banc** → échec. **Step 3 : implémenter** les enums, les affectations, la règle de fin de `Catalog.All()`. **Step 4 : banc** → `N OK, 0 KO`.
- [ ] **Step 5 : bouton** — dans `BuildPresets()` : `_bSur = FpsUi.GhostButton("Sûr")`, `Height = 30`, `Width = 70`, `ForeColor = FpsUi.Gold`, `Click += (s, e) => Batch(t => t.Sur, true, "Sûr — annulable à l'identique")` ; ajouté aux `Controls`, à la mise en page à côté de `_bReco`, et aux deux lignes d'activation de `Batch` (:54, :59). Infobulle : « Seulement des réglages sans effet de bord, annulables à l'identique, au gain mesuré ou sans enjeu de performance. »
- [ ] **Step 6 : compiler, ouvrir l'interface** → le bouton « Sûr » apparaît ; la boîte de confirmation annonce le nombre de réglages. Noter ce nombre et la liste des identifiants dans le message de commit.
- [ ] **Step 7 : commit** — `feat(catalogue): risque, preuve et préréglage Sûr`.

### Task 7 : Passage aller-retour sur machine virtuelle et classement final

**Files :**
- Modify : `src/Tweaks.cs` (réglages signalés)

- [ ] **Step 1 : préparer une machine virtuelle** Windows 11 25H2 (Hyper-V ou Windows Sandbox avec dossier partagé contenant `dist\`), point de contrôle pris avant.
- [ ] **Step 2 : lancer** `BTOptimizer.exe -allerretour all` en administrateur → rapport `aller-retour-*.txt`.
- [ ] **Step 3 : marquer** `Risque = Risque.AnnulationNonGarantie` chaque réglage en `ÉCART`, `NON COUVERT` ou `ÉCHEC` qui n'a pas déjà un risque plus fort ; `NON CAPTURÉ` est attendu seulement pour les réglages sans écriture (le vérifier un par un).
- [ ] **Step 4 : banc, compilation, relancer l'aller-retour sur un point de contrôle propre** → tous les réglages `Sûr` sont `OK`.
- [ ] **Step 5 : vérification sur le PC de Lucas** (lui seul, à son accord) : appliquer puis rétablir `cpu_unpark_cores` ; `powercfg /query SCHEME_CURRENT SUB_PROCESSOR CPMINCORES` → AC revenu à `0x00000064`.
- [ ] **Step 6 : commit** — `chore(catalogue): classement d'après l'aller-retour` ; joindre le rapport dans `docs/` (`docs/aller-retour-2026-11.txt`).
