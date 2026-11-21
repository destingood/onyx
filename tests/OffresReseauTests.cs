namespace BTOptimizer.Tests
{
    /// <summary>
    /// Test RÉSEAU à la demande (« dotnet run --project tests/ONYX.Tests.csproj -- reseau ») :
    /// appelle les vraies sources. Hors du banc par défaut, qui reste hors ligne.
    /// </summary>
    internal static class OffresReseauTests
    {
        public static void Tout()
        {
            Banc.Titre("Bons plans et Actus : sources réelles (réseau)");

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
        }
    }
}
