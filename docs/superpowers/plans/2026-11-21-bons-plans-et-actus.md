# Onglets « Bons plans » et « Actus » — plan d'implémentation

> **Pour l'agent qui exécute (Opus 5.5) :** SOUS-SKILL REQUISE : superpowers:subagent-driven-development (recommandé) ou superpowers:executing-plans, tâche par tâche. Les étapes utilisent des cases (`- [ ]`) pour le suivi.

**But :** ajouter à ONYX deux pages : « Bons plans » (jeux gratuits, promos, nouveautés, recherche, plafonds de prix, catégories, plateformes, favoris, suivi d'un jeu avec notification Windows et dans l'app) et « Actus » (titres d'actualité jeux vidéo, favoris).

**Architecture :** toute la décision (modèle d'offre, filtres, lecture des JSON et des flux RSS, règle de notification, format des fichiers) vit dans quatre fichiers PURS compilés dans le banc ; le réseau, le disque, les minuteurs et l'interface vivent dans des fichiers « machine » séparés. Deux pages `FpsPage` ajoutées en fin de rail (index 9 et 10), une veille de fond qui ne tourne que s'il y a au moins un jeu suivi.

**Pile :** C# WinForms `net10.0-windows10.0.19041.0`, `HttpClient`, `System.Text.Json`, `System.Xml` ; aucun paquet NuGet nouveau, aucune clé d'API.

**Spec :** pas de document de conception séparé. La demande de Lucas (21/11/2026) : « un nouvel onglet pour les jeux gratuits et en promo par catégorie, avec une barre de recherche, des options de prix (10 €, 20 €, moins de 1 €…), les nouveaux jeux ; un autre onglet avec les actus ; les gens peuvent mettre en favori ; pour les jeux ils peuvent sélectionner un jeu et dès qu'il y a une promo ils sont notifiés (Windows et dans l'app) ; et aussi par plateforme ». Les hypothèses H1 à H8 ci-dessous la complètent et font foi.

## Hypothèses (à faire corriger par Lucas avant ou pendant l'exécution)

- **H1 — Sources sans clé.** Promos payantes et nouveautés : Steam seulement (prix en euros). Jeux gratuits : Epic Games Store (euros) et GamerPower (toutes plateformes, consoles et mobile compris). Les promos payantes des autres boutiques (GOG, Humble, Fanatical…) en euros demandent IsThereAnyDeal, donc un compte et une clé d'API embarquée : **hors de ce plan**, décision de Lucas. CheapShark est écarté : ses prix sont en dollars US et ne correspondent pas aux prix français.
- **H2 — « Par plateforme » = filtre.** Le filtre de plateforme porte sur les jeux gratuits (Steam, Epic Games, GOG, PlayStation, Xbox, Switch, Android, iOS, PC). Un jeu suivi déclenche aussi une notification s'il devient gratuit sur une autre plateforme (rapprochement par nom).
- **H3 — Suivi = jeux Steam.** On suit un jeu par son identifiant Steam. 50 jeux au plus.
- **H4 — Notification seulement quand ONYX tourne** (fenêtre ouverte ou réduite dans la zone de notification ; le démarrage avec Windows existe déjà, tâche « ONYXDemarrage »). Aucun service séparé. Contrôle 90 s après le lancement puis toutes les 6 h ; **reporté tant qu'un jeu tourne** (ONYX est un outil de latence : pas de réseau de fond en partie).
- **H5 — « Dans l'app »** = pastille chiffrée sur l'icône « Bons plans » du rail + jeux en promo en tête du sous-onglet « Suivis ». Pas de nouvelle fenêtre de toast.
- **H6 — Actus = titres et liens seulement.** Titre, source, date ; clic = ouverture dans le navigateur. Aucun contenu d'article n'est repris (droit d'auteur).
- **H7 — Plafonds de prix inclusifs :** « Gratuit », « 1 € max », « 5 € max », « 10 € max », « 20 € max ».
- **H8 — Réseau à la demande.** Aucun appel tant que l'utilisateur n'a pas ouvert une des deux pages ni suivi un jeu.

## Faits vérifiés par appel réel le 21/11/2026 (ne pas les re-deviner)

| Source | Appel | Ce qui revient |
|---|---|---|
| Steam, recherche | `https://store.steampowered.com/search/results/?json=1&cc=fr&l=french&category1=998&count=50&start=0` + options | `{"desc":…,"items":[{"name":…,"logo":"https://…/steam/apps/227300/<hash>/capsule_sm_120.jpg?t=…"}]}` : **ni identifiant ni prix** ; l'identifiant se lit dans `logo` (`/steam/apps/(\d+)/`). Options vérifiées : `specials=1` (promos), `tags=<tagid>`, `sort_by=Released_DESC` (nouveautés), `maxprice=5`, `maxprice=10`, `maxprice=20` (respectés). **`maxprice=1` n'est pas respecté** (renvoie jusqu'à 55,99 €) et `maxprice=free&specials=1` renvoie 0 résultat. `term=<texte>` : [VERIFICATION REQUIRED] sur cette route (vérifié seulement sur `/api/storesearch/`) — le test réseau de la tâche 5 le tranche. |
| Steam, fiches par lot | `https://api.steampowered.com/IStoreBrowseService/GetItems/v1?input_json=<JSON encodé>` avec `{"ids":[{"appid":N},…],"context":{"language":"french","country_code":"FR"},"data_request":{"include_basic_info":true,"include_tag_count":5,"include_release":true,"include_platforms":true}}` | `response.store_items[]` : `appid`, `name`, `success` (1), `visible`, `is_free` (true ou absent), `tagids` (entiers), `release.steam_release_date` (secondes Unix), `best_purchase_option` : `final_price_in_cents` (**chaîne**, ex. `"749"`), `original_price_in_cents` (chaîne, **absent hors promo**), `discount_pct` (entier, absent hors promo), `active_discounts[0].discount_end_date` (secondes Unix). Jeu gratuit (Dota 2) : `is_free:true`, pas de prix. 50 jeux par appel : URL d'environ 2 050 caractères, accepté. |
| Steam, catégories | `https://store.steampowered.com/tagdata/populartags/french` | Action 19, Aventure 21, RPG 122, Stratégie 9, Simulation 599, Indépendant 492, Casual 597, Multijoueur 3859, Coop 1685, FPS 1663, Tir 1774, Course 699, Sport 701, Horreur 1667, Survie 1662, Monde ouvert 1695, Gestion 12472, Construction 1643. |
| Steam, autres | `/api/appdetails?appids=a,b` sans `filters=price_overview` → **HTTP 400** ; `/api/getappsingenre/` ne renvoie que `{type,id}` par jeu. | Ne pas les utiliser. |
| Epic, gratuits | `https://store-site-backend-static.ak.epicgames.com/freeGamesPromotions?locale=fr&country=FR&allowCountries=FR` (**non officiel**) | `data.Catalog.searchStore.elements[]` : `title`, `id`, `price.totalPrice.discountPrice` et `originalPrice` (centimes, `currencyCode:"EUR"`), `promotions.promotionalOffers[0].promotionalOffers[0].startDate/endDate` (ISO 8601 UTC), `catalogNs.mappings[0].pageSlug`, `offerMappings[0].pageSlug`, `productSlug` (peut être `null`). `promotions` peut être `null`. |
| GamerPower, gratuits | `https://www.gamerpower.com/api/giveaways?type=game` | Tableau (15 éléments) : `id`, `title` (« Mark of the Deep (Epic Games) Giveaway »), `worth` (dollars), `open_giveaway_url`, `published_date` (`2026-11-19 11:00:14`), `end_date` (`2026-11-26 23:59:00` ou `N/A`), `platforms` (« PC, Epic Games Store » ; valeurs vues : PC, Steam, Epic Games Store, DRM-Free, Itch.io, Android, iOS, VR). **Quand rien n'est disponible : HTTP 201 et un objet** `{"status":0,"status_message":"No active giveaways…"}`. Conditions lues : gratuit, 10 requêtes/s au plus, **lien actif vers GamerPower.com obligatoire**. |
| Flux d'actus | jeuxvideo.com `/rss/rss.xml`, gamekult.com `/feed.xml`, actugaming.net `/feed/`, pcgamer.com `/rss/` (831 Ko), `store.steampowered.com/feeds/news/app/<appid>/?l=french` | Tous en RSS 2.0, UTF-8, sans DOCTYPE : `<item>` avec `<title>`, `<link>`, `<pubDate>`, `<guid>`. |

Limite de débit de Steam : non publiée [VERIFICATION REQUIRED] ; d'où le cache et l'espacement imposés plus bas.

## Contraintes globales

- C# dans le projet existant ; **aucun paquet NuGet nouveau, aucune clé d'API, aucun compte**.
- Convention pur/machine d'ONYX : un fichier pur n'utilise ni réseau, ni disque, ni horloge (`DateTime.UtcNow` est passé en paramètre), ni WinForms. Les fichiers purs sont ajoutés à `tests/ONYX.Tests.csproj` par `<Compile Include="..\src\X.cs" />` et testés dans `tests/XTests.cs` (`internal static class XTests { public static void Tout() { Banc.Titre("…"); Banc.Verifie(nom, true, condition); } }`), enregistré dans `tests/Banc.cs`.
- Banc : `dotnet run --project tests/ONYX.Tests.csproj` → **0 KO** (883 OK avant ce plan). Build : `dotnet build BTOptimizer.csproj -c Release -v q` → **0 Erreur(s), 0 Avertissement(s)**.
- **Ne pas lancer BTOptimizer.exe** sur le PC de Lucas : la vérification visuelle est la liste de contrôle de la tâche 10, faite par Lucas.
- Réseau : `User-Agent: ONYX`, délai 12 s, jamais sur le thread d'interface, réponse limitée à 2 Mo, au moins 1 s entre deux requêtes vers le même hôte, toute erreur → `null` ou liste vide (jamais d'exception jusqu'à l'interface).
- Aucune donnée de l'utilisateur dans les requêtes, sauf le texte qu'il tape dans la recherche (envoyé à Steam).
- **Liens :** un lien venu d'une API ou d'un flux n'est ouvert que si `Offres.LienSur(url)` est vrai (URL absolue `http` ou `https`). Ouverture par `Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })`.
- **XML :** `XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null }`.
- Actus : jamais le contenu (`<description>`, `<content:encoded>`) — titre, lien, date, source seulement.
- Attribution visible et cliquable en pied de la page Bons plans : « Jeux gratuits : GamerPower.com · Prix : Steam, Epic Games Store ».
- Fins de ligne : nouveaux fichiers en LF ; conserver celles de chaque fichier modifié (`CHANGELOG.md` est en CRLF) — vérifier par comptage avant et après.
- Textes de l'interface en français, tutoiement, même ton que le reste de l'app. Composants et couleurs : `FpsUi` (`GhostButton`, `GoldButton`, `CardPanel`, `PaintCard`), `PillButton`, modèle de `PageGames.cs` (recherche l.52-57, cartes l.247-270).
- Ne pas décaler les index de pages existants (0 à 8) : `ShowPage(n)` est appelé ailleurs.

## Points de revue

1. **Source en panne ou de forme inattendue** (Epic non officiel, GamerPower qui renvoie un objet en HTTP 201, HTML à la place du JSON) → liste vide et ligne « Source indisponible pour l'instant », jamais d'exception. Tests : tâche 2.
2. **Lien hostile** (`javascript:`, `file:`, chemin UNC, lien relatif) dans une API ou un flux → jamais ouvert. Tests : tâches 1 et 4.
3. **Prix absent ou non numérique** (jeu gratuit sans `best_purchase_option`, chaîne vide) → `PrixCts` vaut 0 ou -1, jamais une fausse promo ni une notification. Tests : tâches 2 et 3.
4. **Notification en double** (même promo revue toutes les 6 h, redémarrage d'ONYX, erreur réseau entre deux contrôles) → une seule notification par promo ; une fiche manquante ne réarme rien. Tests : tâche 3.
5. **Fichier de favoris ou de suivis abîmé, titre avec tabulation ou retour à la ligne** → les lignes valides sont gardées, l'écriture ne casse jamais le format. Tests : tâche 3.

## Fichiers

| Fichier | Rôle | Banc |
|---|---|---|
| `src/Offres.cs` | pur : `Offre`, prix, plafonds, catégories, filtre, tri, dédoublonnage, `LienSur` | oui |
| `src/OffresSources.cs` | pur : URL des sources, lecture des JSON Steam / Epic / GamerPower | oui |
| `src/Suivi.cs` | pur : `Favoris` (lire, écrire, basculer), `Suivi` (lire, écrire, décider des alertes) | oui |
| `src/Actus.cs` | pur : sources, lecture RSS, fusion, filtre, âge | oui |
| `src/OffresReseau.cs` | machine : HTTP, cache disque, espacement, appels composés | test réseau à la demande |
| `src/SuiviVeille.cs` | machine : fichiers `bt-suivis.txt` / `bt-favoris-*.txt`, minuteur, notifications | non |
| `src/PageBonsPlans.cs`, `src/PageActus.cs` | interface | non |
| `src/DashboardForm.cs`, `src/NavIcons.cs` | rail : 2 entrées, hauteur adaptative, pastille, `Notifie` | non |
| `src/LicenseForm.cs`, `CHANGELOG.md` | textes | non |

---

### Tâche 1 : `Offres` — modèle, prix, filtre, tri

**Fichiers :** créer `src/Offres.cs`, `tests/OffresTests.cs` ; modifier `tests/ONYX.Tests.csproj`, `tests/Banc.cs`.

**Interfaces — produit :**

```csharp
internal sealed class Offre
{
    public string Source;          // "steam" | "epic" | "gamerpower"
    public string Id;              // identifiant dans la source
    public string Nom;
    public int PrixCts = -1;       // centimes d'euro ; 0 = gratuit ; -1 = inconnu
    public int PrixOrigineCts = -1;
    public int Remise;             // 0 à 100
    public DateTime? Fin;          // fin de l'offre, UTC
    public DateTime? Sortie;       // date de sortie, UTC
    public string[] Plateformes = new string[0];
    public int[] Tags = new int[0];   // tagids Steam
    public string Url;
    public int SteamAppId;         // 0 hors Steam
    public string Cle { get { return Source + ":" + Id; } }
}

internal static class Offres
{
    public sealed class Filtre { public string Texte; public int PlafondCts = -1; public int Tag; public string Plateforme; }

    public static readonly int[] PlafondsCts;                       // { 0, 100, 500, 1000, 2000 }
    public static readonly KeyValuePair<string, int>[] Categories;   // les 18 du tableau des faits, dans cet ordre
    public static readonly string[] Plateformes;                     // "Steam","Epic Games","GOG","PlayStation","Xbox","Switch","Android","iOS","PC"

    public static string LibellePlafond(int cts);   // -1 "Tous", 0 "Gratuit", 100 "1 € max", 500 "5 € max", 1000 "10 € max", 2000 "20 € max"
    public static string Prix(int cts);             // -1 "—", 0 "Gratuit", 749 "7,49 €", 2000 "20 €", 50 "0,50 €"
    public static bool SousPlafond(Offre o, int plafondCts);   // plafond < 0 : vrai ; sinon PrixCts >= 0 && PrixCts <= plafond
    public static string NomNormalise(string nom);  // minuscules, sans accents, chiffres et lettres seulement
    public static bool LienSur(string url);
    public static List<Offre> Filtrer(IEnumerable<Offre> offres, Filtre f);
    public static List<Offre> Trier(List<Offre> offres, string tri);   // "remise" (défaut), "prix", "recent", "nom"
    public static List<Offre> Dedoublonner(IEnumerable<Offre> offres);
}
```

Règles que les signatures ne fixent pas :
- `Filtrer` : `Texte` comparé par `NomNormalise` (contient) ; `Tag` 0 = tous ; `Plateforme` vide = toutes, sinon égalité sans casse sur un élément de `Plateformes` ; une offre de nom vide est écartée.
- `Trier` : stable ; « remise » décroissant, « prix » croissant avec les prix inconnus en dernier, « recent » par `Sortie` décroissant avec les `null` en dernier, « nom » par ordre alphabétique sans casse.
- `Dedoublonner` : une seule offre par `Cle` ; puis, parmi les offres gratuites, une seule par `NomNormalise` en préférant la source `epic` à `gamerpower` (le même jeu gratuit arrive par les deux).

- [ ] **Étape 1 : écrire les tests qui échouent** (`tests/OffresTests.cs`, enregistrer `OffresTests.Tout()` dans `tests/Banc.cs`, ajouter `<Compile Include="..\src\Offres.cs" />`)

```csharp
static Offre O(string src, string id, string nom, int prix, int remise, params string[] plats)
{ return new Offre { Source = src, Id = id, Nom = nom, PrixCts = prix, Remise = remise, Plateformes = plats }; }

Banc.Verifie("prix 749", true, Offres.Prix(749) == "7,49 €");
Banc.Verifie("prix rond", true, Offres.Prix(2000) == "20 €");
Banc.Verifie("prix sous 1 €", true, Offres.Prix(50) == "0,50 €");
Banc.Verifie("prix gratuit", true, Offres.Prix(0) == "Gratuit");
Banc.Verifie("prix inconnu", true, Offres.Prix(-1) == "—");
Banc.Verifie("libellés", true, Offres.LibellePlafond(-1) == "Tous" && Offres.LibellePlafond(0) == "Gratuit" && Offres.LibellePlafond(100) == "1 € max" && Offres.LibellePlafond(2000) == "20 € max");
Banc.Verifie("plafond inclusif", true, Offres.SousPlafond(O("steam", "1", "A", 1000, 0), 1000));
Banc.Verifie("au-dessus du plafond", true, !Offres.SousPlafond(O("steam", "1", "A", 1001, 0), 1000));
Banc.Verifie("gratuit sous 1 €", true, Offres.SousPlafond(O("epic", "1", "A", 0, 100), 100));
Banc.Verifie("plafond Gratuit refuse 99 centimes", true, !Offres.SousPlafond(O("steam", "1", "A", 99, 0), 0));
Banc.Verifie("prix inconnu hors plafond", true, !Offres.SousPlafond(O("steam", "1", "A", -1, 0), 2000));
Banc.Verifie("prix inconnu sans plafond", true, Offres.SousPlafond(O("steam", "1", "A", -1, 0), -1));
Banc.Verifie("nom normalisé", true, Offres.NomNormalise("  Pokémon™ : Épée & Bouclier ") == "pokemonepeebouclier");
Banc.Verifie("nom null", true, Offres.NomNormalise(null) == "");
Banc.Verifie("lien https", true, Offres.LienSur("https://store.steampowered.com/app/730/"));
Banc.Verifie("lien http", true, Offres.LienSur("http://exemple.fr/a"));
Banc.Verifie("lien javascript refusé", true, !Offres.LienSur("javascript:alert(1)"));
Banc.Verifie("lien file refusé", true, !Offres.LienSur("file:///C:/Windows/System32/calc.exe"));
Banc.Verifie("lien UNC refusé", true, !Offres.LienSur("\\\\serveur\\partage\\x.exe"));
Banc.Verifie("lien relatif refusé", true, !Offres.LienSur("/app/730") && !Offres.LienSur("") && !Offres.LienSur(null));

var l = new List<Offre> {
    O("steam", "1", "Zelda-like", 1999, 50, "Steam"), O("steam", "2", "Éclipse", 499, 80, "Steam"),
    O("epic", "3", "Mark of the Deep", 0, 100, "Epic Games"), O("gamerpower", "9", "Mark of the Deep", 0, 100, "PC", "Epic Games"),
    O("steam", "4", "", 100, 10, "Steam"), O("steam", "2", "Éclipse", 499, 80, "Steam") };
l[0].Tags = new[] { 19, 21 };
var f = new Offres.Filtre();
Banc.Verifie("nom vide écarté", true, Offres.Filtrer(l, f).Count == 5);
f.Texte = "eclipse"; Banc.Verifie("texte sans accent", true, Offres.Filtrer(l, f).Count == 2);
f = new Offres.Filtre { PlafondCts = 500 }; Banc.Verifie("5 € max", true, Offres.Filtrer(l, f).Count == 4);
f = new Offres.Filtre { Tag = 19 }; Banc.Verifie("catégorie", true, Offres.Filtrer(l, f).Count == 1);
f = new Offres.Filtre { Plateforme = "epic games" }; Banc.Verifie("plateforme sans casse", true, Offres.Filtrer(l, f).Count == 2);
var d = Offres.Dedoublonner(l);
Banc.Verifie("doublon de clé retiré, gratuit en double : Epic gardé", true, d.Count == 4 && d.Exists(o => o.Source == "epic") && !d.Exists(o => o.Source == "gamerpower"));
Banc.Verifie("tri remise", true, Offres.Trier(d, "remise")[0].Remise == 100);
Banc.Verifie("tri prix : inconnu en dernier", true, Offres.Trier(new List<Offre> { O("steam", "1", "A", -1, 0), O("steam", "2", "B", 300, 0) }, "prix")[0].Id == "2");
Banc.Verifie("tri nom", true, Offres.Trier(new List<Offre> { O("steam", "1", "b", 1, 0), O("steam", "2", "A", 1, 0) }, "nom")[0].Id == "2");
Banc.Verifie("18 catégories, Action = 19", true, Offres.Categories.Length == 18 && Offres.Categories[0].Key == "Action" && Offres.Categories[0].Value == 19);
```

- [ ] **Étape 2 :** lancer le banc → échec de compilation (`Offres` absent).
- [ ] **Étape 3 :** écrire `src/Offres.cs` selon l'interface et les règles ci-dessus. `Prix` : séparateur décimal virgule, une espace simple (U+0020) avant « € » — les tests comparent des chaînes exactes —, pas de décimales si le prix est rond. `NomNormalise` : décomposition `FormD` (pas `FormKD`), puis lettres et chiffres seulement.
- [ ] **Étape 4 :** banc → 0 KO.
- [ ] **Étape 5 :** `git add src/Offres.cs tests/OffresTests.cs tests/ONYX.Tests.csproj tests/Banc.cs && git commit -m "Bons plans : modèle d'offre, plafonds de prix, filtre et tri"`

---

### Tâche 2 : `OffresSources` — URL et lecture des JSON

**Fichiers :** créer `src/OffresSources.cs`, `tests/OffresSourcesTests.cs` ; modifier `tests/ONYX.Tests.csproj`, `tests/Banc.cs`.

**Interfaces — consomme :** `Offre`, `Offres.NomNormalise`, `Offres.LienSur`. **Produit :**

```csharp
internal static class OffresSources
{
    public const string UrlEpic = "https://store-site-backend-static.ak.epicgames.com/freeGamesPromotions?locale=fr&country=FR&allowCountries=FR";
    public const string UrlGamerPower = "https://www.gamerpower.com/api/giveaways?type=game";
    public const int LotSteam = 50;

    public static string UrlRechercheSteam(string texte, int tag, int plafondCts, bool promos, bool nouveautes, int debut);
    public static List<int> IdsRechercheSteam(string json);
    public static string UrlFichesSteam(IEnumerable<int> appids);     // null si la liste est vide ; au plus LotSteam identifiants
    public static List<Offre> FichesSteam(string json);
    public static List<Offre> GratuitsEpic(string json, DateTime maintenantUtc);
    public static List<Offre> GratuitsGamerPower(string json);
}
```

Règles :
- `UrlRechercheSteam` : base du tableau des faits avec `start=<debut>` ; `&specials=1` si `promos` ; `&sort_by=Released_DESC` si `nouveautes` ; `&tags=<tag>` si `tag > 0` ; `&term=<Uri.EscapeDataString(texte)>` si `texte` non vide ; `&maxprice=5` pour un plafond de 100 ou 500, `&maxprice=10` pour 1000, `&maxprice=20` pour 2000, rien pour -1 ou 0 (le filtre exact est toujours refait par `Offres.Filtrer`).
- `IdsRechercheSteam` : identifiants lus dans `items[].logo` par `/steam/apps/(\d+)/`, sans doublon, dans l'ordre ; un `logo` de lot (`/steam/subs/`, `/steam/bundles/`) est ignoré.
- `FichesSteam` : ne garde que `success == 1` et `name` non vide. `Source = "steam"`, `Id = appid`, `SteamAppId = appid`, `Url = "https://store.steampowered.com/app/<appid>/"`, `Plateformes = { "Steam" }`, `Tags = tagids`, `Sortie` depuis `release.steam_release_date` (0 ou absent → `null`). Prix : `is_free == true` → `PrixCts = 0`, `Remise = 0` ; sinon `final_price_in_cents` (chaîne **ou** nombre) → `PrixCts`, absent ou illisible → -1 ; `original_price_in_cents` absent → `PrixOrigineCts = PrixCts` ; `discount_pct` absent → 0 ; `Fin` depuis `active_discounts[0].discount_end_date`.
- `GratuitsEpic` : ne garde que les éléments dont `price.totalPrice.discountPrice == 0`, `originalPrice > 0`, et qui ont une promotion **en cours** (`startDate <= maintenantUtc < endDate`) dans `promotions.promotionalOffers[].promotionalOffers[]`. `Source = "epic"`, `PrixCts = 0`, `PrixOrigineCts = originalPrice`, `Remise = 100`, `Fin = endDate`, `Plateformes = { "Epic Games" }`. `Url` : `https://store.epicgames.com/fr/p/<slug>` avec le premier non vide de `catalogNs.mappings[0].pageSlug`, `offerMappings[0].pageSlug`, `productSlug` ; aucun → `https://store.epicgames.com/fr/free-games`.
- `GratuitsGamerPower` : racine objet (cas « rien de disponible ») → liste vide. `Source = "gamerpower"`, `Id = id`, `PrixCts = 0`, `Remise = 100`, `Url = open_giveaway_url` (offre écartée si `!Offres.LienSur`), `Sortie = published_date`, `Fin = end_date` (`N/A` → `null`). `Nom` : `title` sans le suffixe « Giveaway » puis sans la parenthèse finale (« Mark of the Deep (Epic Games) Giveaway » → « Mark of the Deep »). `Plateformes` : `platforms` découpé sur la virgule, chaque valeur ramenée à un libellé de `Offres.Plateformes` — contient « playstation » → « PlayStation », « xbox » → « Xbox », « switch » → « Switch », « epic » → « Epic Games », « steam » → « Steam », « gog » → « GOG », « android » → « Android », « ios » → « iOS », « pc » exact → « PC » ; valeur inconnue gardée telle quelle ; sans doublon.
- Toute entrée `null`, vide ou non JSON → liste vide, sans exception.

- [ ] **Étape 1 : écrire les tests qui échouent.** Jeux d'essai (formes réelles relevées le 21/11/2026, raccourcies) :

```csharp
const string Recherche = "{\"desc\":\"\",\"items\":[" +
  "{\"name\":\"Euro Truck Simulator 2\",\"logo\":\"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/227300/da84/capsule_sm_120.jpg?t=1\"}," +
  "{\"name\":\"Lot\",\"logo\":\"https://shared.akamai.steamstatic.com/store_item_assets/steam/subs/555/capsule_sm_120.jpg\"}," +
  "{\"name\":\"FINAL FANTASY VII REBIRTH\",\"logo\":\"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/2909400/61fd/capsule_sm_120.jpg?t=2\"}," +
  "{\"name\":\"Doublon\",\"logo\":\"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/227300/x/capsule_sm_120.jpg\"}]}";
const string Fiches = "{\"response\":{\"store_items\":[" +
  "{\"appid\":2909400,\"success\":1,\"visible\":true,\"name\":\"FINAL FANTASY VII REBIRTH\",\"tagids\":[122,19,21],\"release\":{\"steam_release_date\":1737641006}," +
    "\"best_purchase_option\":{\"final_price_in_cents\":\"749\",\"original_price_in_cents\":\"4999\",\"discount_pct\":85,\"active_discounts\":[{\"discount_end_date\":1796234400}]}}," +
  "{\"appid\":1245620,\"success\":1,\"visible\":true,\"name\":\"ELDEN RING\",\"tagids\":[1695],\"release\":{\"steam_release_date\":1645744078},\"best_purchase_option\":{\"final_price_in_cents\":\"5999\"}}," +
  "{\"appid\":570,\"success\":1,\"visible\":true,\"name\":\"Dota 2\",\"is_free\":true,\"tagids\":[113],\"best_purchase_option\":{}}," +
  "{\"appid\":999,\"success\":1,\"name\":\"Prix illisible\",\"best_purchase_option\":{\"final_price_in_cents\":\"\"}}," +
  "{\"appid\":42,\"success\":2}]}}";
const string Epic = "{\"data\":{\"Catalog\":{\"searchStore\":{\"elements\":[" +
  "{\"title\":\"Mark of the Deep\",\"id\":\"1f9a\",\"productSlug\":null,\"catalogNs\":{\"mappings\":[{\"pageSlug\":\"mark-of-the-deep-f7dc08\",\"pageType\":\"productHome\"}]}," +
    "\"price\":{\"totalPrice\":{\"discountPrice\":0,\"originalPrice\":1950,\"currencyCode\":\"EUR\"}}," +
    "\"promotions\":{\"promotionalOffers\":[{\"promotionalOffers\":[{\"startDate\":\"2026-11-19T16:00:00.000Z\",\"endDate\":\"2026-11-26T16:00:00.000Z\"}]}],\"upcomingPromotionalOffers\":[]}}," +
  "{\"title\":\"Bientôt\",\"id\":\"b2\",\"price\":{\"totalPrice\":{\"discountPrice\":1999,\"originalPrice\":1999}}," +
    "\"promotions\":{\"promotionalOffers\":[],\"upcomingPromotionalOffers\":[{\"promotionalOffers\":[{\"startDate\":\"2026-11-26T16:00:00.000Z\",\"endDate\":\"2026-12-03T16:00:00.000Z\"}]}]}}," +
  "{\"title\":\"Sans promo\",\"id\":\"c3\",\"price\":{\"totalPrice\":{\"discountPrice\":0,\"originalPrice\":0}},\"promotions\":null}]}}}}";
const string Gp = "[{\"id\":3853,\"title\":\"Mark of the Deep (Epic Games) Giveaway\",\"worth\":\"$19.99\",\"open_giveaway_url\":\"https://www.gamerpower.com/open/mark-of-the-deep-epic-games-giveaway\"," +
    "\"published_date\":\"2026-11-19 11:00:14\",\"type\":\"Game\",\"platforms\":\"PC, Epic Games Store\",\"end_date\":\"2026-11-26 23:59:00\",\"status\":\"Active\"}," +
  "{\"id\":7,\"title\":\"Jeu Console Giveaway\",\"open_giveaway_url\":\"https://www.gamerpower.com/open/x\",\"published_date\":\"2026-11-18 09:00:00\",\"platforms\":\"Playstation 5, Xbox Series X|S, Nintendo Switch, DRM-Free\",\"end_date\":\"N/A\"}," +
  "{\"id\":8,\"title\":\"Lien piégé\",\"open_giveaway_url\":\"javascript:alert(1)\",\"platforms\":\"PC\",\"end_date\":\"N/A\"}]";
DateTime t = new DateTime(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);

string u = OffresSources.UrlRechercheSteam("elden ring", 19, 1000, true, false, 50);
Banc.Verifie("URL : base", true, u.StartsWith("https://store.steampowered.com/search/results/?json=1&cc=fr&l=french&category1=998&count=50&start=50"));
Banc.Verifie("URL : options", true, u.Contains("&specials=1") && u.Contains("&tags=19") && u.Contains("&maxprice=10") && u.Contains("&term=elden%20ring") && !u.Contains("sort_by"));
Banc.Verifie("URL : 1 € max demande 5 au serveur", true, OffresSources.UrlRechercheSteam(null, 0, 100, true, false, 0).Contains("&maxprice=5"));
Banc.Verifie("URL : nouveautés sans plafond", true, OffresSources.UrlRechercheSteam("", 0, -1, false, true, 0).Contains("&sort_by=Released_DESC") && !OffresSources.UrlRechercheSteam("", 0, -1, false, true, 0).Contains("maxprice") && !OffresSources.UrlRechercheSteam("", 0, -1, false, true, 0).Contains("specials"));
var ids = OffresSources.IdsRechercheSteam(Recherche);
Banc.Verifie("identifiants : lot et doublon écartés", true, ids.Count == 2 && ids[0] == 227300 && ids[1] == 2909400);
Banc.Verifie("identifiants : HTML à la place du JSON", true, OffresSources.IdsRechercheSteam("<html>").Count == 0 && OffresSources.IdsRechercheSteam(null).Count == 0);
string uf = OffresSources.UrlFichesSteam(new[] { 730, 570 });
Banc.Verifie("URL fiches", true, uf.StartsWith("https://api.steampowered.com/IStoreBrowseService/GetItems/v1?input_json=") && Uri.UnescapeDataString(uf).Contains("{\"appid\":730}") && Uri.UnescapeDataString(uf).Contains("\"country_code\":\"FR\""));
Banc.Verifie("URL fiches : liste vide", true, OffresSources.UrlFichesSteam(new int[0]) == null);
var fs = OffresSources.FichesSteam(Fiches);
Banc.Verifie("fiches : échec écarté", true, fs.Count == 4);
Banc.Verifie("fiche en promo", true, fs[0].SteamAppId == 2909400 && fs[0].PrixCts == 749 && fs[0].PrixOrigineCts == 4999 && fs[0].Remise == 85 && fs[0].Fin == DateTimeOffset.FromUnixTimeSeconds(1796234400).UtcDateTime && fs[0].Url == "https://store.steampowered.com/app/2909400/" && fs[0].Tags.Length == 3);
Banc.Verifie("fiche hors promo", true, fs[1].PrixCts == 5999 && fs[1].PrixOrigineCts == 5999 && fs[1].Remise == 0 && fs[1].Fin == null);
Banc.Verifie("fiche gratuite : 0, pas de promo", true, fs[2].PrixCts == 0 && fs[2].Remise == 0);
Banc.Verifie("prix illisible : inconnu", true, fs[3].PrixCts == -1 && fs[3].Remise == 0);
Banc.Verifie("fiches : entrée invalide", true, OffresSources.FichesSteam("{}").Count == 0 && OffresSources.FichesSteam("pas du json").Count == 0);
var ep = OffresSources.GratuitsEpic(Epic, t);
Banc.Verifie("Epic : seul le gratuit en cours", true, ep.Count == 1 && ep[0].Nom == "Mark of the Deep" && ep[0].PrixCts == 0 && ep[0].PrixOrigineCts == 1950 && ep[0].Url == "https://store.epicgames.com/fr/p/mark-of-the-deep-f7dc08" && ep[0].Fin == new DateTime(2026, 11, 26, 16, 0, 0, DateTimeKind.Utc));
Banc.Verifie("Epic : promo finie", true, OffresSources.GratuitsEpic(Epic, new DateTime(2026, 11, 27, 0, 0, 0, DateTimeKind.Utc)).Count == 0);
Banc.Verifie("Epic : forme inattendue", true, OffresSources.GratuitsEpic("{\"data\":null}", t).Count == 0 && OffresSources.GratuitsEpic("[]", t).Count == 0);
var gp = OffresSources.GratuitsGamerPower(Gp);
Banc.Verifie("GamerPower : lien piégé écarté", true, gp.Count == 2);
Banc.Verifie("GamerPower : nom nettoyé, plateformes", true, gp[0].Nom == "Mark of the Deep" && gp[0].Plateformes.Length == 2 && gp[0].Plateformes[0] == "PC" && gp[0].Plateformes[1] == "Epic Games" && gp[0].Fin == new DateTime(2026, 11, 26, 23, 59, 0));
Banc.Verifie("GamerPower : consoles, fin N/A", true, gp[1].Fin == null && string.Join(",", gp[1].Plateformes) == "PlayStation,Xbox,Switch,DRM-Free");
Banc.Verifie("GamerPower : rien de disponible (objet)", true, OffresSources.GratuitsGamerPower("{\"status\":0,\"status_message\":\"No active giveaways available at the moment, please try again later.\"}").Count == 0);
```

- [ ] **Étape 2 :** banc → échec de compilation.
- [ ] **Étape 3 :** écrire `src/OffresSources.cs` (`System.Text.Json.JsonDocument`, tout l'accès aux propriétés par `TryGetProperty` et contrôle de `ValueKind`).
- [ ] **Étape 4 :** banc → 0 KO.
- [ ] **Étape 5 :** `git commit -m "Bons plans : URL des sources et lecture des réponses Steam, Epic et GamerPower"`

---

### Tâche 3 : `Favoris` et `Suivi` — fichiers et règle de notification

**Fichiers :** créer `src/Suivi.cs` (les deux classes), `tests/SuiviTests.cs` ; modifier `tests/ONYX.Tests.csproj`, `tests/Banc.cs`.

**Interfaces — consomme :** `Offre`, `Offres.Prix`, `Offres.NomNormalise`. **Produit :**

```csharp
internal sealed class Favori { public string Cle, Source, Url, Titre; public DateTime Date; }

internal static class Favoris
{
    public static List<Favori> Lire(string texte);            // une ligne = Cle \t Source \t yyyy-MM-dd \t Url \t Titre
    public static string Ecrire(IEnumerable<Favori> favoris);
    public static bool Contient(List<Favori> favoris, string cle);
    public static bool Bascule(List<Favori> favoris, Favori f);   // ajoute en tête ou retire ; renvoie vrai si le favori est présent après l'appel
    public static Favori DepuisOffre(Offre o, DateTime aujourdhui);   // Cle = o.Cle
}

internal sealed class JeuSuivi { public int AppId; public string Nom; public int RemiseNotifiee; public string GratuitNotifie = ""; }

internal static class Suivi
{
    public const int Max = 50;
    public sealed class Alerte { public string Titre, Texte, Url; public int AppId; }

    public static List<JeuSuivi> Lire(string texte);          // une ligne = AppId \t RemiseNotifiee \t GratuitNotifie \t Nom
    public static string Ecrire(IEnumerable<JeuSuivi> jeux);
    public static bool Ajoute(List<JeuSuivi> jeux, Offre o);   // faux si déjà suivi, si SteamAppId == 0 ou si Max est atteint ; RemiseNotifiee = o.Remise
    public static bool Retire(List<JeuSuivi> jeux, int appId);
    public static List<Alerte> Decide(List<JeuSuivi> jeux, List<Offre> fiches, List<Offre> gratuits);   // met à jour RemiseNotifiee et GratuitNotifie
    public static List<Alerte> Regroupe(List<Alerte> alertes, int max);
}
```

Règles :
- `Lire` (les deux) : ligne vide, trop courte ou d'identifiant illisible → ignorée, les autres gardées ; doublon de clé → première occurrence ; date de favori illisible → `DateTime.MinValue`, ligne gardée. `Ecrire` : tabulations, retours chariot et sauts de ligne des champs remplacés par une espace ; une ligne par entrée, terminée par `\n`.
- `Decide`, pour chaque jeu suivi dont une fiche Steam est présente dans `fiches` (même `SteamAppId`) : `Remise > 0 && Remise > RemiseNotifiee` → alerte `Titre = "<Nom> : −<Remise> %"`, `Texte = "<Prix(PrixCts)> au lieu de <Prix(PrixOrigineCts)> sur Steam"`, `Url` de la fiche, puis `RemiseNotifiee = Remise` ; `Remise == 0` → `RemiseNotifiee = 0` (réarmement) ; **fiche absente → rien ne change**. Le nom du jeu suivi est mis à jour depuis la fiche.
- `Decide`, gratuits : pour chaque offre de `gratuits` avec `PrixCts == 0` dont `NomNormalise(Nom)` égale celui d'un jeu suivi et dont `Cle != GratuitNotifie` → alerte `Titre = "<Nom du jeu suivi> est gratuit"`, `Url` de l'offre, `Texte = "Sur <première plateforme>" + (Fin connue ? " jusqu'au <dd/MM>" : "")`, puis `GratuitNotifie = Cle`.
- `Regroupe` : au plus `max` alertes ; au-delà, les `max - 1` premières puis une alerte `Titre = "<n> autres jeux suivis en promo"`, `Texte = "Ouvre Bons plans › Suivis"`, `Url = null`.

- [ ] **Étape 1 : écrire les tests qui échouent**

```csharp
var d0 = new DateTime(2026, 11, 21);
var fav = new List<Favori>();
var o = new Offre { Source = "steam", Id = "2909400", SteamAppId = 2909400, Nom = "FINAL\tFANTASY\r\nVII", Url = "https://store.steampowered.com/app/2909400/", PrixCts = 749, PrixOrigineCts = 4999, Remise = 85 };
Banc.Verifie("favori ajouté", true, Favoris.Bascule(fav, Favoris.DepuisOffre(o, d0)) && Favoris.Contient(fav, "steam:2909400"));
string txt = Favoris.Ecrire(fav);
Banc.Verifie("écriture : une ligne, 5 champs", true, txt.Split('\n').Length == 2 && txt.TrimEnd('\n').Split('\t').Length == 5);
var relu = Favoris.Lire(txt);
Banc.Verifie("relecture", true, relu.Count == 1 && relu[0].Cle == "steam:2909400" && relu[0].Titre == "FINAL FANTASY  VII" && relu[0].Date == d0 && relu[0].Url == o.Url);
Banc.Verifie("favori retiré", true, !Favoris.Bascule(fav, Favoris.DepuisOffre(o, d0)) && fav.Count == 0);
Banc.Verifie("fichier abîmé : lignes valides gardées", true, Favoris.Lire("tronquée\tx\n\n" + txt + "a\tb\tpas-une-date\thttps://x.fr\tT\n").Count == 2);
Banc.Verifie("fichier vide ou null", true, Favoris.Lire("").Count == 0 && Favoris.Lire(null).Count == 0);

var jeux = new List<JeuSuivi>();
Banc.Verifie("suivre un jeu déjà en promo : pas d'alerte immédiate", true, Suivi.Ajoute(jeux, o) && jeux[0].RemiseNotifiee == 85);
Banc.Verifie("déjà suivi", true, !Suivi.Ajoute(jeux, o));
Banc.Verifie("hors Steam refusé", true, !Suivi.Ajoute(jeux, new Offre { Source = "epic", Id = "x", Nom = "X" }));
Func<int, Offre> fiche = r => new Offre { Source = "steam", Id = "2909400", SteamAppId = 2909400, Nom = "FINAL FANTASY VII REBIRTH", Url = o.Url, PrixCts = r == 0 ? 4999 : 4999 * (100 - r) / 100, PrixOrigineCts = 4999, Remise = r };
var aucune = new List<Offre>();
Banc.Verifie("même promo : rien", true, Suivi.Decide(jeux, new List<Offre> { fiche(85) }, aucune).Count == 0);
Banc.Verifie("nom mis à jour depuis la fiche", true, jeux[0].Nom == "FINAL FANTASY VII REBIRTH");
Banc.Verifie("fiche absente : rien, rien ne réarme", true, Suivi.Decide(jeux, aucune, aucune).Count == 0 && jeux[0].RemiseNotifiee == 85);
Banc.Verifie("fin de promo : réarme sans alerte", true, Suivi.Decide(jeux, new List<Offre> { fiche(0) }, aucune).Count == 0 && jeux[0].RemiseNotifiee == 0);
var a = Suivi.Decide(jeux, new List<Offre> { fiche(50) }, aucune);
Banc.Verifie("nouvelle promo : une alerte", true, a.Count == 1 && a[0].Titre == "FINAL FANTASY VII REBIRTH : −50 %" && a[0].Texte.Contains("au lieu de 49,99 € sur Steam") && a[0].Url == o.Url && a[0].AppId == 2909400);
Banc.Verifie("contrôle suivant : pas de doublon", true, Suivi.Decide(jeux, new List<Offre> { fiche(50) }, aucune).Count == 0);
Banc.Verifie("promo plus forte : nouvelle alerte", true, Suivi.Decide(jeux, new List<Offre> { fiche(75) }, aucune).Count == 1);
Banc.Verifie("promo plus faible : rien", true, Suivi.Decide(jeux, new List<Offre> { fiche(60) }, aucune).Count == 0);
var relus = Suivi.Lire(Suivi.Ecrire(jeux));
Banc.Verifie("suivis : aller-retour", true, relus.Count == 1 && relus[0].AppId == 2909400 && relus[0].RemiseNotifiee == 75 && relus[0].Nom == "FINAL FANTASY VII REBIRTH");
Banc.Verifie("redémarrage : pas de doublon", true, Suivi.Decide(relus, new List<Offre> { fiche(75) }, aucune).Count == 0);
var g = new List<Offre> { new Offre { Source = "epic", Id = "1f9a", Nom = "Final Fantasy VII: Rebirth", PrixCts = 0, Remise = 100, Plateformes = new[] { "Epic Games" }, Fin = new DateTime(2026, 11, 26, 16, 0, 0, DateTimeKind.Utc), Url = "https://store.epicgames.com/fr/p/x" } };
var ag = Suivi.Decide(relus, aucune, g);
Banc.Verifie("gratuit ailleurs : alerte", true, ag.Count == 1 && ag[0].Titre == "FINAL FANTASY VII REBIRTH est gratuit" && ag[0].Texte.StartsWith("Sur Epic Games jusqu'au 26/11"));
Banc.Verifie("gratuit : pas de doublon", true, Suivi.Decide(relus, aucune, g).Count == 0);
Banc.Verifie("jeu gratuit non suivi : rien", true, Suivi.Decide(relus, aucune, new List<Offre> { new Offre { Source = "epic", Id = "z", Nom = "Autre", PrixCts = 0 } }).Count == 0);
var plein = new List<JeuSuivi>(); for (int i = 1; i <= Suivi.Max; i++) plein.Add(new JeuSuivi { AppId = i, Nom = "J" + i });
Banc.Verifie("50 jeux au plus", true, !Suivi.Ajoute(plein, o));
Banc.Verifie("retirer", true, Suivi.Retire(plein, 3) && plein.Count == 49 && !Suivi.Retire(plein, 3));
var cinq = new List<Suivi.Alerte>(); for (int i = 0; i < 5; i++) cinq.Add(new Suivi.Alerte { Titre = "T" + i });
var r3 = Suivi.Regroupe(cinq, 3);
Banc.Verifie("regroupement", true, r3.Count == 3 && r3[1].Titre == "T1" && r3[2].Titre == "3 autres jeux suivis en promo" && Suivi.Regroupe(cinq, 5).Count == 5);
Banc.Verifie("suivis abîmés", true, Suivi.Lire("abc\t0\t\tX\n12\n730\t10\t\tCounter-Strike 2\n").Count == 1);
```

- [ ] **Étape 2 :** banc → échec de compilation.
- [ ] **Étape 3 :** écrire `src/Suivi.cs`.
- [ ] **Étape 4 :** banc → 0 KO.
- [ ] **Étape 5 :** `git commit -m "Bons plans : favoris, jeux suivis et règle de notification"`

---

### Tâche 4 : `Actus` — flux RSS

**Fichiers :** créer `src/Actus.cs`, `tests/ActusTests.cs` ; modifier `tests/ONYX.Tests.csproj`, `tests/Banc.cs`.

**Interfaces — consomme :** `Offres.LienSur`, `Offres.NomNormalise`, `Favori`. **Produit :**

```csharp
internal sealed class Actu { public string Titre, Lien, Source; public DateTime Date; }   // Date en UTC ; DateTime.MinValue = inconnue

internal static class Actus
{
    public sealed class SourceActu { public string Nom, Url; }
    public static readonly SourceActu[] Sources;   // jeuxvideo.com, Gamekult, ActuGaming, PC Gamer (EN) — URL du tableau des faits
    public const int MaxAffiche = 120;

    public static string UrlFluxSteam(int appId);                 // https://store.steampowered.com/feeds/news/app/<appId>/?l=french
    public static List<Actu> LitRss(string xml, string source);
    public static List<Actu> Fusionne(IEnumerable<List<Actu>> flux, int max);   // sans doublon de Lien, Date décroissante
    public static List<Actu> Filtre(List<Actu> actus, string texte, string source);
    public static string Age(DateTime dateUtc, DateTime maintenantUtc);
    public static Favori EnFavori(Actu a);                        // Cle = Lien
}
```

Règles : `Fusionne` garde la première occurrence d'un même `Lien`, dans l'ordre des flux reçus. `LitRss` lit les `<item>` RSS 2.0 (`title`, `link`, `pubDate`) avec les réglages XML des contraintes globales ; titre passé par `WebUtility.HtmlDecode`, espaces repliés, coupé à 200 caractères ; item sans titre ou dont le lien n'est pas `Offres.LienSur` → écarté ; date illisible → `DateTime.MinValue` ; XML invalide ou vide → liste vide. `Age` : moins de 1 h → « il y a N min » (au moins 1) ; moins de 24 h → « il y a N h » ; moins de 48 h → « hier » ; sinon `dd/MM` ; date inconnue ou future → chaîne vide.

- [ ] **Étape 1 : écrire les tests qui échouent**

```csharp
const string Rss = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><rss version=\"2.0\"><channel><title>Flux</title>" +
  "<item><title>Mise à jour de Counter-Strike 2</title><link>https://store.steampowered.com/news/app/730/view/1</link><pubDate>Sat, 21 Nov 2026 10:30:00 +0000</pubDate><description>&lt;p&gt;texte long&lt;/p&gt;</description></item>" +
  "<item><title><![CDATA[Test &amp; avis : l&#039;été   du jeu]]></title><link>https://www.gamekult.com/a/2</link><pubDate>Fri, 20 Nov 2026 08:00:00 +0100</pubDate></item>" +
  "<item><title>Lien piégé</title><link>javascript:alert(1)</link><pubDate>Sat, 21 Nov 2026 09:00:00 +0000</pubDate></item>" +
  "<item><title></title><link>https://x.fr/vide</link></item>" +
  "<item><title>Sans date</title><link>https://x.fr/sansdate</link><pubDate>n'importe quoi</pubDate></item>" +
  "</channel></rss>";
var l = Actus.LitRss(Rss, "Test");
Banc.Verifie("3 items valides", true, l.Count == 3);
Banc.Verifie("titre, lien, source, date", true, l[0].Titre == "Mise à jour de Counter-Strike 2" && l[0].Lien == "https://store.steampowered.com/news/app/730/view/1" && l[0].Source == "Test" && l[0].Date == new DateTime(2026, 11, 21, 10, 30, 0, DateTimeKind.Utc));
Banc.Verifie("entités décodées, espaces repliés, fuseau converti", true, l[1].Titre == "Test & avis : l'été du jeu" && l[1].Date == new DateTime(2026, 11, 20, 7, 0, 0, DateTimeKind.Utc));
Banc.Verifie("date illisible", true, l[2].Date == DateTime.MinValue);
Banc.Verifie("XML invalide", true, Actus.LitRss("<rss><channel><item>", "T").Count == 0 && Actus.LitRss(null, "T").Count == 0 && Actus.LitRss("<html><body>404</body></html>", "T").Count == 0);
Banc.Verifie("DOCTYPE avec entité : aucune expansion", true, !Actus.LitRss("<?xml version=\"1.0\"?><!DOCTYPE rss [<!ENTITY x SYSTEM \"file:///C:/Windows/win.ini\">]><rss><channel><item><title>a&x;b</title><link>https://x.fr/1</link></item></channel></rss>", "T").Exists(a => a.Titre.Contains("fonts") || a.Titre.Length > 10));
var autre = new List<Actu> { new Actu { Titre = "Doublon", Lien = "https://www.gamekult.com/a/2", Source = "B", Date = new DateTime(2026, 11, 21, 11, 0, 0, DateTimeKind.Utc) }, new Actu { Titre = "Récent", Lien = "https://x.fr/r", Source = "B", Date = new DateTime(2026, 11, 21, 11, 30, 0, DateTimeKind.Utc) } };
var fus = Actus.Fusionne(new[] { l, autre }, 10);
Banc.Verifie("fusion : sans doublon, récent d'abord, sans date en dernier", true, fus.Count == 4 && fus[0].Titre == "Récent" && fus[3].Titre == "Sans date");
Banc.Verifie("fusion : plafond", true, Actus.Fusionne(new[] { l, autre }, 2).Count == 2);
Banc.Verifie("filtre texte sans accent", true, Actus.Filtre(fus, "ete", null).Count == 1);
Banc.Verifie("filtre source", true, Actus.Filtre(fus, null, "B").Count == 1 && Actus.Filtre(fus, "", "").Count == 4);
DateTime n = new DateTime(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);
Banc.Verifie("âge", true, Actus.Age(n.AddSeconds(-20), n) == "il y a 1 min" && Actus.Age(n.AddMinutes(-12), n) == "il y a 12 min" && Actus.Age(n.AddHours(-3), n) == "il y a 3 h" && Actus.Age(n.AddHours(-30), n) == "hier" && Actus.Age(n.AddDays(-9), n) == "12/11" && Actus.Age(DateTime.MinValue, n) == "" && Actus.Age(n.AddHours(2), n) == "");
Banc.Verifie("flux Steam", true, Actus.UrlFluxSteam(730) == "https://store.steampowered.com/feeds/news/app/730/?l=french");
Banc.Verifie("4 sources, toutes en https", true, Actus.Sources.Length == 4 && Array.TrueForAll(Actus.Sources, s => s.Url.StartsWith("https://")));
Banc.Verifie("favori d'actu", true, Actus.EnFavori(l[0]).Cle == l[0].Lien && Actus.EnFavori(l[0]).Titre == l[0].Titre);
```

- [ ] **Étape 2 :** banc → échec de compilation.
- [ ] **Étape 3 :** écrire `src/Actus.cs` (`XmlReader.Create(new StringReader(xml), réglages)` + `XmlDocument.Load`, dates par `DateTimeOffset.TryParse(…, CultureInfo.InvariantCulture, …)` puis `.UtcDateTime`).
- [ ] **Étape 4 :** banc → 0 KO.
- [ ] **Étape 5 :** `git commit -m "Actus : lecture des flux RSS, fusion, filtre et favoris"`

---

### Tâche 5 : `OffresReseau` — HTTP, cache, appels composés

**Fichiers :** créer `src/OffresReseau.cs`, `tests/OffresReseauTests.cs` ; modifier `tests/ONYX.Tests.csproj`, `tests/Banc.cs`.

**Interfaces — consomme :** tâches 1 à 4 (`JeuSuivi` vient de `src/Suivi.cs`) ; `AppPaths.File`. **Produit :**

```csharp
internal static class OffresReseau
{
    public static Task<string> Texte(string url, int cacheMinutes);
    public static Task<List<Offre>> RechercheSteam(Offres.Filtre f, bool promos, bool nouveautes, int debut);   // recherche → identifiants → fiches, dans l'ordre de la recherche
    public static Task<List<Offre>> FichesSteam(IEnumerable<int> appids, int cacheMinutes);                    // par lots de OffresSources.LotSteam
    public static Task<List<Offre>> Gratuits();                                                                // Epic + GamerPower, Offres.Dedoublonner
    public static Task<List<Actu>> Actualites(IEnumerable<JeuSuivi> suivis);                                   // Actus.Sources + flux Steam des 10 premiers jeux suivis (Source = nom du jeu), Actus.Fusionne(…, Actus.MaxAffiche)
    public static bool DerniereErreur(string source);                                                          // "steam" | "epic" | "gamerpower" | "actus" : vrai si le dernier appel a échoué
}
```

Règles : contraintes réseau globales. Cache : dossier `bt-offres-cache` à côté des fichiers `AppPaths.File`, un fichier par URL nommé par le SHA-256 hexadécimal de l'URL ; fichier plus jeune que `cacheMinutes` → pas d'appel ; appel en échec → contenu en cache même périmé s'il existe, sinon `null` ; `cacheMinutes == 0` → toujours un appel. Durées : recherche Steam 30 min, fiches Steam 30 min (0 pour la veille), gratuits 60 min, flux 15 min. Les fichiers de cache de plus de 7 jours sont supprimés au premier appel de la session. Un seul `HttpClient` statique. `Gratuits` et `Actualites` : une source en échec n'empêche pas les autres.

- [ ] **Étape 1 : test réseau à la demande.** Dans `tests/Banc.cs` (`Programme.Main`) : si les arguments contiennent `reseau`, n'exécuter que `OffresReseauTests.Tout()` puis le bilan ; sans cet argument, ce test n'est **pas** exécuté (le banc reste hors ligne). `OffresReseauTests.Tout()` appelle les vraies sources et vérifie :

```csharp
var promos = OffresReseau.RechercheSteam(new Offres.Filtre { PlafondCts = 1000 }, true, false, 0).GetAwaiter().GetResult();
Banc.Verifie("Steam : au moins 10 promos lues", true, promos.Count >= 10);
Banc.Verifie("Steam : noms, prix en euros, liens", true, promos.TrueForAll(o => o.Nom.Length > 0 && o.SteamAppId > 0 && Offres.LienSur(o.Url)) && promos.Exists(o => o.PrixCts > 0 && o.Remise > 0));
Banc.Verifie("Steam : plafond 10 € tenu après filtre", true, Offres.Filtrer(promos, new Offres.Filtre { PlafondCts = 1000 }).Count >= 5);
var terme = OffresReseau.RechercheSteam(new Offres.Filtre { Texte = "elden ring" }, false, false, 0).GetAwaiter().GetResult();
Banc.Verifie("Steam : recherche par nom", true, terme.Exists(o => o.SteamAppId == 1245620));
Banc.Verifie("Steam : nouveautés", true, OffresReseau.RechercheSteam(new Offres.Filtre(), false, true, 0).GetAwaiter().GetResult().Count >= 10);
Banc.Verifie("Steam : catégorie Action", true, OffresReseau.RechercheSteam(new Offres.Filtre { Tag = 19 }, true, false, 0).GetAwaiter().GetResult().Count >= 5);
var grat = OffresReseau.Gratuits().GetAwaiter().GetResult();
Banc.Verifie("gratuits : au moins une source répond", true, grat.Count >= 1 && grat.TrueForAll(o => o.PrixCts == 0 && Offres.LienSur(o.Url)));
var actus = OffresReseau.Actualites(new[] { new JeuSuivi { AppId = 730, Nom = "Counter-Strike 2" } }).GetAwaiter().GetResult();
Banc.Verifie("actus : au moins 20 titres, 2 sources", true, actus.Count >= 20 && actus.Exists(a => a.Source != actus[0].Source));
Banc.Verifie("actus : flux du jeu suivi, source = nom du jeu", true, actus.Exists(a => a.Source == "Counter-Strike 2"));
```

- [ ] **Étape 2 :** `dotnet run --project tests/ONYX.Tests.csproj -- reseau` → échec de compilation.
- [ ] **Étape 3 :** écrire `src/OffresReseau.cs`.
- [ ] **Étape 4 :** `dotnet run --project tests/ONYX.Tests.csproj -- reseau` → 0 KO. **Si « recherche par nom » échoue** (le paramètre `term` n'est pas vérifié sur cette route) : faire passer la recherche par nom par `https://store.steampowered.com/api/storesearch/?term=<texte>&cc=fr&l=french` (vérifié : `items[].id`, `name`, `price.final` en centimes), corriger `OffresSources` et ses tests, et le noter dans le message de commit. Puis banc sans argument → 0 KO.
- [ ] **Étape 5 :** `git commit -m "Bons plans : accès réseau avec cache et espacement, test réseau à la demande"`

---

### Tâche 6 : rail — deux pages, hauteur adaptative, pastille, `Notifie`

**Fichiers :** modifier `src/DashboardForm.cs` (l.18 `_pages`, l.1386-1396 glyphes et boucle, l.1511 `LayoutRail`, l.1625-1640 `CreatePage`, l.732-737 modèle de notification), `src/NavIcons.cs` ; créer `src/PageBonsPlans.cs` et `src/PageActus.cs` (squelettes : titre seulement).

**Interfaces — produit :**

```csharp
// NavIcons
public const int BonsPlans = 10, Actus = 11;        // Outils garde 9
// DashboardForm
public const int PageBonsPlansIdx = 9, PageActusIdx = 10;
public void SignaleBonsPlans(int nombre);            // pastille de la cellule Bons plans ; 0 l'efface
public void Notifie(string titre, string texte);     // WinToast.Show, repli sur la bulle de la zone de notification (même modèle que l.732-737)
// les deux passent par BeginInvoke quand elles sont appelées hors du thread d'interface
// pages
internal sealed class PageBonsPlans : FpsPage { public PageBonsPlans(DashboardForm host); public void MontreSuivis(); }
internal sealed class PageActus : FpsPage { public PageActus(DashboardForm host); }
```

- [ ] **Étape 1 :** `_pages = new FpsPage[11]` ; glyphes `"🏷"`, `"📰"` et libellés « Bons plans », « Actus » ajoutés en fin de tableaux ; l'icône de chaque cellule vient d'un tableau `{ 0, 1, 2, 3, 4, 5, 6, 7, 8, NavIcons.BonsPlans, NavIcons.Actus }` (aujourd'hui `cell.IconId = i`, ce qui donnerait l'icône « Outils » à l'index 9) ; `case 9` et `case 10` dans `CreatePage`.
- [ ] **Étape 2 :** `NavIcons.Paint` : `BonsPlans` = étiquette de prix (pentagone incliné avec un œillet), `Actus` = journal (rectangle, titre épais, trois lignes), au trait dans le repère 24×24 comme les autres.
- [ ] **Étape 3 :** `LayoutRail` adaptatif. Aujourd'hui : cellules de 48 px au pas de 56 ; avec 11 cellules il faut 804 px alors que la fenêtre peut descendre à 680. Nouveau calcul : `int dispo = _rail.Height - BrandH - 12 - ProfileH - 52; int pas = Math.Max(40, Math.Min(56, dispo / _nav.Count));` puis `c.SetBounds(9, y, cellW, pas - 8); y += pas;` ; le bouton Outils garde 44 px sous la dernière cellule. Appeler `LayoutRail()` quand la hauteur de la fenêtre change si ce n'est pas déjà le cas.
- [ ] **Étape 4 :** `SignaleBonsPlans` (propriété `Badge` de `NavCell`, l.1967) et `Notifie`. Ne pas modifier les deux appels existants à `WinToast.Show`.
- [ ] **Étape 5 :** build Release → 0 erreur, 0 avertissement ; banc → 0 KO.
- [ ] **Étape 6 :** `git commit -m "Rail : pages Bons plans et Actus, hauteur des cellules adaptée, pastille et notification"`

---

### Tâche 7 : stockage, veille et notifications (`SuiviVeille`)

**Fichiers :** créer `src/SuiviVeille.cs` ; modifier `src/DashboardForm.cs` (démarrage, à côté de `_rattrapage` l.119-127).

**Interfaces — consomme :** tâches 3, 5, 6. **Produit :**

```csharp
internal static class Magasin        // fichiers bt-suivis.txt, bt-favoris-jeux.txt, bt-favoris-actus.txt (AppPaths.File)
{
    public static List<JeuSuivi> Suivis { get; }            // chargés à la première lecture ; lus et modifiés sur le thread d'interface seulement
    public static List<Favori> FavorisJeux { get; }
    public static List<Favori> FavorisActus { get; }
    public static void SauveSuivis(); public static void SauveFavorisJeux(); public static void SauveFavorisActus();   // écriture dans un fichier temporaire puis remplacement
    public static event Action Change;                       // levé après chaque sauvegarde
}

internal static class SuiviVeille
{
    public static void Demarre(DashboardForm host);          // appelé une fois au démarrage
    public static Task Controle();                           // un contrôle tout de suite (bouton « Vérifier maintenant »)
    public static List<Offre> DernieresFiches { get; }       // fiches du dernier contrôle, pour le sous-onglet Suivis
    public static int AlertesNonVues { get; }
    public static void MarqueVues();                         // remet la pastille à 0
}
```

Règles :
- `Demarre` : minuteur WinForms de 60 s. Un contrôle est dû 90 s après le démarrage puis 6 h après le dernier contrôle abouti ; un contrôle dû est **reporté** tant que `Magasin.Suivis` est vide (aucun appel réseau), que `GameBoost.IsActive`, que `Native.IsGameFullscreen()` ou que `GameScan.RunningKnownGame() != null` ; après un échec réseau, nouvel essai 15 min plus tard.
- `Controle` : relève les identifiants suivis sur le thread d'interface ; dans `Task.Run`, `OffresReseau.FichesSteam(appids, 0)` + `OffresReseau.Gratuits()` ; puis **de retour sur le thread d'interface** (`BeginInvoke`) : `Suivi.Decide(Magasin.Suivis, fiches, gratuits)` → `Magasin.SauveSuivis()` → `Suivi.Regroupe(alertes, 3)` → pour chacune `host.Notifie(Titre, Texte)` → `AlertesNonVues += alertes.Count` → `host.SignaleBonsPlans(AlertesNonVues)`. Un seul contrôle à la fois. Si les deux appels réseau échouent, le contrôle compte comme un échec et `Decide` n'est pas appelé.
- Aucune notification si la liste d'alertes est vide ; aucun son ajouté.

- [ ] **Étape 1 :** écrire `Magasin` et `SuiviVeille` ; appeler `SuiviVeille.Demarre(this)` dans le constructeur de `DashboardForm`, sous `try { } catch { }` comme ses voisins.
- [ ] **Étape 2 :** build Release → 0 erreur, 0 avertissement ; banc → 0 KO.
- [ ] **Étape 3 :** `git commit -m "Bons plans : veille des jeux suivis, reportée pendant une partie, notification Windows et pastille"`

---

### Tâche 8 : page « Bons plans »

**Fichiers :** modifier `src/PageBonsPlans.cs`.

**Interfaces — consomme :** tâches 1, 3, 5, 7 ; `GameArt.Get(int appId, Action onReady)` pour les vignettes Steam ; `GameLibrary.ScanAll()` (`InstalledGame.SteamAppId`, `Name`).

Disposition :

```
Bons plans                                              [ Rechercher un jeu…            ]
[Promos] [Gratuits] [Nouveautés] [Suivis •2] [Favoris]
Prix : [Tous] [Gratuit] [1 € max] [5 € max] [10 € max] [20 € max]   Catégorie [Toutes ▾]  Plateforme [Toutes ▾]  Tri [Remise ▾]
┌───────────────┐ ┌───────────────┐ ┌───────────────┐ ┌───────────────┐
│ vignette      │ │               │ │               │ │               │
│ Nom du jeu    │ │               │ │               │ │               │
│ −85 %  7,49 € │ │               │ │               │ │               │
│ 49,99 € · Steam · jusqu'au 02/12   ☆  🔔 │
└───────────────┘ └───────────────┘ └───────────────┘ └───────────────┘
                              [‹ Précédent]  Page 1  [Suivant ›]
Jeux gratuits : GamerPower.com · Prix : Steam, Epic Games Store
```

Comportement :
- **Filtre local des résultats Steam : le plafond de prix seulement.** Le texte et la catégorie sont appliqués par Steam (recherche approchée ; les fiches ne portent que 5 tags) : les refiltrer localement retirerait de bons résultats. `Offres.Filtrer` complet (texte, plateforme) ne sert que pour Gratuits et Favoris.
- **Promos** : `RechercheSteam(filtre, promos: true, nouveautes: false, debut)` puis plafond et `Offres.Trier` ; tri par défaut « remise ». Le plafond « Gratuit » bascule sur le sous-onglet Gratuits (Steam n'a pas de filtre « gratuit en promo »). Si une page compte moins de 24 cartes après le plafond, charger le lot suivant (`debut + 50`), 3 lots au plus par action.
- **Gratuits** : `Gratuits()` ; plafonds et catégorie désactivés (grisés) ; filtre de plateforme actif (`Offres.Plateformes`) ; tri par fin d'offre la plus proche.
- **Nouveautés** : `RechercheSteam(filtre, promos: false, nouveautes: true, debut)` ; tri par défaut « recent ».
- **Suivis** : jeux de `Magasin.Suivis` avec leur fiche (`SuiviVeille.DernieresFiches`, sinon `FichesSteam(…, 30)`), promos en tête ; boutons « Vérifier maintenant » (`SuiviVeille.Controle()`) et « Suivre mes jeux installés » (jeux Steam de `GameLibrary.ScanAll()` ajoutés par `Suivi.Ajoute`, dans la limite de 50) ; **dans ce sous-onglet la recherche porte sur tout le catalogue Steam** (`promos: false`) pour trouver un jeu à suivre ; texte d'aide quand la liste est vide : « Cherche un jeu puis clique sur la cloche : tu seras prévenu dès qu'il passe en promo. » ; à l'ouverture : `SuiviVeille.MarqueVues()`.
- **Favoris** : `Magasin.FavorisJeux`, sans appel réseau ; filtre texte local.
- Plateforme masquée dans Promos, Nouveautés et Suivis (une seule plateforme : Steam).
- Carte : clic → ouvre `Url` si `Offres.LienSur` ; ☆ → `Favoris.Bascule` + `Magasin.SauveFavorisJeux()` ; 🔔 (offres Steam seulement) → `Suivi.Ajoute` / `Suivi.Retire` + `Magasin.SauveSuivis()` ; à 50 jeux suivis, message « 50 jeux suivis au maximum : retire-en un d'abord. ». Prix d'origine barré quand `Remise > 0`. Offre non Steam : pas de vignette, un fond `FpsUi` avec le nom de la plateforme.
- 24 cartes par page ; « Suivant » demande `debut + 50` à Steam quand la page locale est épuisée ; les vignettes ne sont demandées que pour les cartes de la page affichée.
- Saisie dans la recherche : attente de 400 ms sans frappe avant l'appel ; chaque chargement porte un numéro de génération et **une réponse arrivée après un changement de filtre est ignorée**.
- États : chargement (« Chargement… »), liste vide (« Aucun jeu ne correspond à ces filtres. »), erreur (`OffresReseau.DerniereErreur` : « Source indisponible pour l'instant. » + bouton « Réessayer »).
- Pied de page : les trois noms sont des liens (`https://www.gamerpower.com`, `https://store.steampowered.com`, `https://store.epicgames.com/fr/`).
- `OnShown()` : premier chargement seulement à la première ouverture (H8) ; rien dans le constructeur. Si `SuiviVeille.AlertesNonVues > 0` : `MontreSuivis()` (affiche le sous-onglet Suivis). Retour des tâches par `BeginInvoke`, sans effet si la page est détruite.

- [ ] **Étape 1 :** écrire la page.
- [ ] **Étape 2 :** build Release → 0 erreur, 0 avertissement ; banc → 0 KO.
- [ ] **Étape 3 :** `git commit -m "Page Bons plans : promos, gratuits, nouveautés, suivis et favoris"`

---

### Tâche 9 : page « Actus »

**Fichiers :** modifier `src/PageActus.cs`.

**Interfaces — consomme :** tâches 3, 4, 5, 7.

Disposition :

```
Actus                                                   [ Filtrer les titres…           ]
[Toutes] [Mes jeux suivis] [Favoris]      Source [Toutes ▾]      [Actualiser]
☆  Mise à jour de Counter-Strike 2                         Steam · il y a 12 min
★  Test : …                                                Gamekult · il y a 3 h
```

Comportement : **Toutes** = `OffresReseau.Actualites(Magasin.Suivis)` ; **Mes jeux suivis** = les seules actus dont `Source` n'est pas un nom de `Actus.Sources` (c'est alors le nom d'un jeu suivi) ; sans jeu suivi : « Suis un jeu dans Bons plans pour voir ses actus ici. » ; **Favoris** = `Magasin.FavorisActus`, sans réseau. Clic sur une ligne → navigateur si `Offres.LienSur`. ☆ → `Favoris.Bascule(…, Actus.EnFavori(a))` + `Magasin.SauveFavorisActus()`. Filtre texte et source locaux (`Actus.Filtre`). Âge par `Actus.Age(a.Date, DateTime.UtcNow)`. Mêmes états (chargement, vide, erreur) et même règle de génération que la tâche 8. Aucun extrait d'article. Premier chargement à la première ouverture seulement.

- [ ] **Étape 1 :** écrire la page.
- [ ] **Étape 2 :** build Release → 0 erreur, 0 avertissement ; banc → 0 KO.
- [ ] **Étape 3 :** `git commit -m "Page Actus : titres des flux, jeux suivis et favoris"`

---

### Tâche 10 : textes, contrôle final

**Fichiers :** modifier `src/LicenseForm.cs` (clause 4, l.34-40), `CHANGELOG.md` (section « ## Prochaine version », CRLF).

- [ ] **Étape 1 :** ajouter à la fin de la clause 4 de la licence — **texte à faire valider par Lucas avant publication** : « Les pages « Bons plans » et « Actus », quand vous les ouvrez ou quand vous suivez un jeu, interrogent les services publics de Steam, de l'Epic Games Store, de GamerPower et les flux des sites d'actualité affichés. Ces services reçoivent votre adresse IP et le nom du logiciel ; le texte tapé dans la recherche de jeux est envoyé à Steam. Rien d'autre n'est transmis. »
- [ ] **Étape 2 :** `CHANGELOG.md`, deux puces sous « ## Prochaine version » : la page Bons plans (promos Steam en euros, gratuits de toutes les plateformes, plafonds de prix, catégories, recherche, favoris, suivi avec notification Windows et pastille ; contrôle toutes les 6 h, jamais pendant une partie) et la page Actus (titres de jeuxvideo.com, Gamekult, ActuGaming, PC Gamer et des jeux suivis ; favoris).
- [ ] **Étape 3 :** build Release → 0 erreur, 0 avertissement ; banc → 0 KO ; `dotnet run --project tests/ONYX.Tests.csproj -- reseau` → 0 KO.
- [ ] **Étape 4 :** `git commit -m "Bons plans et Actus : licence et journal des versions"`. **Ne pas pousser, ne pas publier** sans demande de Lucas.
- [ ] **Étape 5 : liste de contrôle remise à Lucas** (à faire dans l'app, non vérifiable par l'agent) :
  1. le rail montre 11 icônes sans chevauchement à la taille minimale de la fenêtre (1040 × 680) et à la taille maximale ;
  2. Bons plans › Promos : « 10 € max » ne montre aucun jeu au-dessus de 10 € ; « 1 € max » aussi ; une catégorie change la liste ;
  3. Gratuits : le jeu Epic de la semaine apparaît une seule fois ; le filtre Plateforme réagit ;
  4. suivre un jeu, fermer et rouvrir ONYX : il est toujours dans Suivis ;
  5. « Vérifier maintenant » sur un jeu en promo ajouté **avant** sa promo : une notification Windows et une pastille ; un second clic n'en renvoie pas ;
  6. Actus : un clic ouvre l'article dans le navigateur ; l'étoile se retrouve dans Favoris après redémarrage ;
  7. Wi-Fi coupé : les deux pages affichent « Source indisponible pour l'instant. » sans plantage.

## Hors de ce plan

- Promos payantes des autres boutiques en euros (IsThereAnyDeal : compte, clé d'API, mention obligatoire) — décision de Lucas.
- Notification quand ONYX est fermé (demanderait un service ou une tâche planifiée dédiée).
- Vignettes des offres non Steam, seuil de prix par jeu suivi (« préviens-moi sous 15 € »), jeux « bientôt gratuits » d'Epic.
