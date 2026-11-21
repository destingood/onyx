using System.Linq;

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

            // Revue finale : un serveur qui se tait en plein corps de réponse ne bloque pas plus de 12 s.
            var ecoute = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            ecoute.Start();
            int port = ((System.Net.IPEndPoint)ecoute.LocalEndpoint).Port;
            var muet = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    using (var c = ecoute.AcceptTcpClient())
                    {
                        byte[] debut = System.Text.Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: 100000\r\n\r\nabc");
                        c.GetStream().Write(debut, 0, debut.Length);
                        System.Threading.Thread.Sleep(30000);
                    }
                }
                catch { }
            });
            var chrono = System.Diagnostics.Stopwatch.StartNew();
            string lu = OffresReseau.Texte("http://127.0.0.1:" + port + "/muet-" + System.DateTime.UtcNow.Ticks, 0).GetAwaiter().GetResult();
            chrono.Stop();
            try { ecoute.Stop(); } catch { }
            Banc.Verifie("délai de 12 s tenu pendant la lecture du corps (" + (int)chrono.Elapsed.TotalSeconds + " s)", true, lu == null && chrono.Elapsed.TotalSeconds < 16);

            var promos = OffresReseau.RechercheSteam(new Offres.Filtre { PlafondCts = 1000 }, true, false, 0).GetAwaiter().GetResult();
            Banc.Verifie("Steam : au moins 10 promos lues", true, promos.Count >= 10);
            int avecImage = promos.FindAll(o => o.Image != null && o.Image.StartsWith("https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/")).Count;
            Banc.Verifie("Steam : image avec empreinte pour 90 % des promos (" + avecImage + "/" + promos.Count + ")", true, promos.Count > 0 && avecImage * 10 >= promos.Count * 9);
            Banc.Verifie("Steam : noms, prix en euros, liens", true, promos.TrueForAll(o => o.Nom.Length > 0 && o.SteamAppId > 0 && Offres.LienSur(o.Url)) && promos.Exists(o => o.PrixCts > 0 && o.Remise > 0));
            Banc.Verifie("Steam : plafond 10 € tenu après filtre", true, Offres.Filtrer(promos, new Offres.Filtre { PlafondCts = 1000 }).Count >= 5);
            var terme = OffresReseau.RechercheSteam(new Offres.Filtre { Texte = "elden ring" }, false, false, 0).GetAwaiter().GetResult();
            Banc.Verifie("Steam : recherche par nom", true, terme.Exists(o => o.SteamAppId == 1245620));
            Banc.Verifie("Steam : nouveautés", true, OffresReseau.RechercheSteam(new Offres.Filtre(), false, true, 0).GetAwaiter().GetResult().Count >= 10);
            Banc.Verifie("Steam : catégorie Action", true, OffresReseau.RechercheSteam(new Offres.Filtre { Tag = 19 }, true, false, 0).GetAwaiter().GetResult().Count >= 5);
            var grat = OffresReseau.Gratuits().GetAwaiter().GetResult();
            int gratImage = grat.FindAll(o => o.Image != null).Count;
            Banc.Verifie("gratuits : image pour 90 % des offres (" + gratImage + "/" + grat.Count + ")", true, grat.Count > 0 && gratImage * 10 >= grat.Count * 9);
            Banc.Verifie("gratuits : au moins une source répond", true, grat.Count >= 1 && grat.TrueForAll(o => o.PrixCts == 0 && Offres.LienSur(o.Url)));
            var actus = OffresReseau.Actualites(new[] { new JeuSuivi { AppId = 730, Nom = "Counter-Strike 2" } }).GetAwaiter().GetResult();
            Banc.Verifie("actus : au moins 20 titres, 2 sources", true, actus.Count >= 20 && actus.Exists(a => a.Source != actus[0].Source));
            Banc.Verifie("actus : flux du jeu suivi, source = nom du jeu", true, actus.Exists(a => a.Source == "Counter-Strike 2"));
            var desSites = actus.FindAll(a => a.Source != "Counter-Strike 2");
            int actusImage = desSites.FindAll(a => a.Image != null).Count;
            Banc.Verifie("actus : image pour 90 % des titres des sites (" + actusImage + "/" + desSites.Count + ")", true, desSites.Count > 0 && actusImage * 10 >= desSites.Count * 9);
            Banc.Verifie("actus : AppId porté par le flux du jeu suivi", true, actus.FindAll(a => a.Source == "Counter-Strike 2").TrueForAll(a => a.AppId == 730) && desSites.TrueForAll(a => a.AppId == 0));

            // Stores hors Steam : au moins 10 promos chacun, liens sûrs, image pour 90 %.
            string tousCs = OffresSources.IdsCheapShark(OffresSources.LitChoixBoutiques(null));
            foreach (string src in new[] { "gog", "cheapshark", "nintendo", "itch" })
            {
                var p = OffresReseau.PromosStore(src, null, 0, tousCs).GetAwaiter().GetResult();
                int img = p.Offres.FindAll(o => o.Image != null || o.SteamAppId > 0).Count;
                Banc.Verifie(src + " : 10 promos ou plus (" + p.Offres.Count + "), liens, prix, images " + img, true, !p.Echec && p.Offres.Count >= 10
                    && p.Offres.TrueForAll(o => o.Nom.Length > 0 && Offres.LienSur(o.Url) && o.PrixCts >= 0) && img * 10 >= p.Offres.Count * 9);
                if (src == "cheapshark") Banc.Verifie("cheapshark : plusieurs stores, prix US", true, p.Offres.Select(o => o.Source).Distinct().Count() >= 3 && p.Offres.TrueForAll(o => o.Devise == "USD"));
                if (src == "gog" || src == "nintendo") Banc.Verifie(src + " : prix en euros, remises", true, p.Offres.TrueForAll(o => o.Devise == "EUR") && p.Offres.FindAll(o => o.Remise > 0).Count * 10 >= p.Offres.Count * 9);
            }
            var gogPage2 = OffresReseau.PromosStore("gog", null, 1, null).GetAwaiter().GetResult();
            Banc.Verifie("gog : page suivante", true, gogPage2.Offres.Count >= 10);
            Banc.Verifie("recherche par nom (GOG, CheapShark)", true, OffresReseau.PromosStore("gog", "witcher", 0, null).GetAwaiter().GetResult().Offres.Count >= 1
                && OffresReseau.PromosStore("cheapshark", "batman", 0, tousCs).GetAwaiter().GetResult().Offres.Count >= 1);

            // Comparateur : le même jeu dans plusieurs stores officiels (Elden Ring par AppID, puis par titre exact).
            var cmp = OffresReseau.Comparaison(new Offre { Source = "steam", Id = "1245620", Nom = "ELDEN RING", SteamAppId = 1245620 }).GetAwaiter().GetResult();
            Banc.Verifie("comparateur : 3 stores ou plus (" + cmp.Count + "), Steam compris, triés, liens sûrs", true, cmp.Count >= 3 && cmp.Any(x => x.Store == "Steam")
                && cmp.Zip(cmp.Skip(1), (a, b) => a.PrixCts <= b.PrixCts).All(x => x) && cmp.TrueForAll(x => Offres.LienSur(x.Url)));
            Banc.Verifie("comparateur : par titre exact", true, OffresReseau.Comparaison(new Offre { Source = "epic", Id = "x", Nom = "ELDEN RING" }).GetAwaiter().GetResult().Count >= 1);
        }
    }
}
