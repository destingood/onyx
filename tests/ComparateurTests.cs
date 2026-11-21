using System;
using System.Linq;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Comparateur des Bons plans : prix du même jeu dans les stores officiels (CheapShark, par AppID Steam
    /// ou par titre exact) et liens de recherche vers les revendeurs de clés (forme réelle relevée le 21/11/2026, raccourcie).
    /// </summary>
    internal static class ComparateurTests
    {
        const string Deals = "[" +
          "{\"title\":\"ELDEN RING\",\"dealID\":\"a1\",\"storeID\":\"15\",\"gameID\":\"204097\",\"salePrice\":\"39.59\",\"normalPrice\":\"59.99\",\"isOnSale\":\"1\",\"savings\":\"34.005667\",\"steamAppID\":\"1245620\"}," +
          "{\"title\":\"ELDEN RING\",\"dealID\":\"b2\",\"storeID\":\"3\",\"gameID\":\"204097\",\"salePrice\":\"41.99\",\"normalPrice\":\"59.99\",\"isOnSale\":\"1\",\"savings\":\"30\"}," +
          "{\"title\":\"ELDEN RING\",\"dealID\":\"b3\",\"storeID\":\"3\",\"gameID\":\"204097\",\"salePrice\":\"45.00\",\"normalPrice\":\"59.99\",\"isOnSale\":\"1\",\"savings\":\"25\"}," +
          "{\"title\":\"ELDEN RING\",\"dealID\":\"c4\",\"storeID\":\"1\",\"gameID\":\"204097\",\"salePrice\":\"59.99\",\"normalPrice\":\"59.99\",\"isOnSale\":\"0\",\"savings\":\"0.000000\"}," +
          "{\"title\":\"ELDEN RING\",\"dealID\":\"d5\",\"storeID\":\"99\",\"gameID\":\"204097\",\"salePrice\":\"1.00\",\"normalPrice\":\"59.99\"}," +
          "{\"title\":\"ELDEN RING\",\"dealID\":\"javascript:x\",\"storeID\":\"7\",\"gameID\":\"204097\",\"salePrice\":\"2.00\",\"normalPrice\":\"59.99\"}," +
          "{\"title\":\"ELDEN RING\",\"dealID\":\"e6\",\"storeID\":\"7\",\"gameID\":\"204097\",\"salePrice\":\"\",\"normalPrice\":\"59.99\"}]";

        const string Jeu = "{\"info\":{\"title\":\"ELDEN RING\",\"steamAppID\":\"1245620\"},\"cheapestPriceEver\":{\"price\":\"23.99\",\"date\":1732034595},\"deals\":[]}";
        const string Bce = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><gesmes:Envelope xmlns:gesmes=\"http://www.gesmes.org/xml/2002-08-01\" xmlns=\"http://www.ecb.int/vocabulary/2002-08-01/eurofxref\">"
            + "<Cube><Cube time='2026-11-20'><Cube currency='JPY' rate='170.12'/><Cube currency='USD' rate='1.0990'/></Cube></Cube></gesmes:Envelope>";

        public static void Tout()
        {
            Banc.Verifie("comparateur : identifiant CheapShark du jeu gardé", true, OffresSources.PrixCompares(Deals).TrueForAll(x => x.Jeu == 204097));
            Banc.Verifie("historique : URL du jeu", true, OffresSources.UrlJeuCheapShark(204097) == "https://www.cheapshark.com/api/1.0/games?id=204097" && OffresSources.UrlJeuCheapShark(0) == null);
            var h = OffresSources.Historique(Jeu);
            Banc.Verifie("historique : plus bas prix et date", true, h.PlusBasCts == 2399 && h.PlusBasLe == new DateTime(2024, 11, 19, 16, 43, 15, DateTimeKind.Utc));
            Banc.Verifie("historique : illisible → inconnu", true, OffresSources.Historique("<html>").PlusBasCts == -1 && OffresSources.Historique("{\"cheapestPriceEver\":{}}").PlusBasCts == -1);

            DateTime jour;
            decimal taux = OffresSources.TauxUsd(Bce, out jour);
            Banc.Verifie("BCE : taux du dollar et date", true, taux == 1.0990m && jour == new DateTime(2026, 11, 20));
            Banc.Verifie("BCE : XML invalide ou piégé → 0", true, OffresSources.TauxUsd(null, out jour) == 0m && OffresSources.TauxUsd("<a>", out jour) == 0m
                && OffresSources.TauxUsd("<?xml version=\"1.0\"?><!DOCTYPE a [<!ENTITY x SYSTEM \"file:///C:/Windows/win.ini\">]><a><Cube currency='USD' rate='&x;'/></a>", out jour) == 0m
                && OffresSources.TauxUsd("<a><Cube currency='USD' rate='-2'/></a>", out jour) == 0m);
            Banc.Verifie("conversion en euros (arrondie au centime)", true, OffresSources.EnEuros(3499, 1.0990m) == 3184 && OffresSources.EnEuros(0, 1.1m) == 0
                && OffresSources.EnEuros(3499, 0m) == -1 && OffresSources.EnEuros(-1, 1.1m) == -1);

            int niveau;
            Banc.Verifie("verdict : au plus bas", true, OffresSources.Verdict(2399, 2399, out niveau) == "Prix le plus bas jamais vu" && niveau == 2
                && OffresSources.Verdict(2000, 2399, out niveau) == "Prix le plus bas jamais vu");
            Banc.Verifie("verdict : proche du plus bas (moins de 10 %)", true, OffresSources.Verdict(2599, 2399, out niveau) == "Proche du plus bas historique (+8 %)" && niveau == 1);
            Banc.Verifie("verdict : attendre", true, OffresSources.Verdict(5999, 2399, out niveau) == "Déjà vu 60 % moins cher : une promo reviendra sans doute" && niveau == 0);
            var prixUs = OffresSources.PrixCompares(Deals);
            var carteFr = new Offre { Source = "gog", Id = "1", Nom = "ELDEN RING", PrixCts = 3650, PrixOrigineCts = 5999, Remise = 40, Devise = "EUR", Url = "https://www.gog.com/fr/game/elden" };
            var l = OffresSources.Lignes(carteFr, prixUs, 1.0990m);
            Banc.Verifie("lignes : prix de la carte en euros ajouté, tri en euros", true, string.Join(",", l.Select(x => x.Store)) == "Fanatical,GOG (France),Green Man Gaming,Steam"
                && l[0].EurCts == 3602 && l[1].EurCts == 3650 && l[1].Devise == "EUR" && l[1].DeLaCarte && l[1].Url == carteFr.Url && l[0].Devise == "USD" && !l[0].DeLaCarte);
            var sansTaux = OffresSources.Lignes(carteFr, prixUs, 0m);
            Banc.Verifie("lignes : sans taux, dollars triés entre eux, euros à part en tête", true, sansTaux[0].Store == "GOG (France)" && sansTaux[1].Store == "Fanatical" && sansTaux[1].EurCts == -1);
            var carteUs = new Offre { Source = "fanatical", Id = "204097", Nom = "ELDEN RING", PrixCts = 3959, Devise = "USD", Url = "https://www.cheapshark.com/redirect?dealID=a1" };
            Banc.Verifie("lignes : carte en dollars déjà dans la liste, pas de doublon", true, OffresSources.Lignes(carteUs, prixUs, 1.0990m).Count == 3
                && OffresSources.Lignes(carteUs, prixUs, 1.0990m)[0].DeLaCarte && OffresSources.Lignes(null, null, 1m).Count == 0);
            Banc.Verifie("aller plus loin : SteamDB et IsThereAnyDeal", true, OffresSources.UrlSteamDb(1245620) == "https://steamdb.info/app/1245620/" && OffresSources.UrlSteamDb(0) == null
                && OffresSources.UrlRevendeur(OffresSources.Itad, "Elden Ring™") == "https://isthereanydeal.com/search/?q=Elden%20Ring" && !OffresSources.Revendeurs.Contains(OffresSources.Itad));
            Banc.Verifie("verdict : historique inconnu", true, OffresSources.Verdict(5999, -1, out niveau) == null && OffresSources.Verdict(-1, 2399, out niveau) == null);
            Banc.Verifie("comparateur : URL par AppID Steam", true,
                OffresSources.UrlComparaison(1245620, "Elden Ring") == "https://www.cheapshark.com/api/1.0/deals?steamAppID=1245620&pageSize=30&sortBy=Price");
            Banc.Verifie("comparateur : URL par titre exact sans AppID", true,
                OffresSources.UrlComparaison(0, " Hades II ") == "https://www.cheapshark.com/api/1.0/deals?title=Hades%20II&exact=1&pageSize=30&sortBy=Price");
            Banc.Verifie("comparateur : rien à chercher", true, OffresSources.UrlComparaison(0, "  ") == null && OffresSources.UrlComparaison(-5, null) == null);

            var p = OffresSources.PrixCompares(Deals);
            Banc.Verifie("comparateur : un prix par store connu, du moins cher au plus cher", true,
                string.Join(",", p.Select(x => x.Store)) == "Fanatical,Green Man Gaming,Steam");
            Banc.Verifie("comparateur : prix, remise et lien", true, p[0].PrixCts == 3959 && p[0].PrixOrigineCts == 5999 && p[0].Remise == 34
                && p[0].Url == "https://www.cheapshark.com/redirect?dealID=a1" && p[1].PrixCts == 4199 && p[2].Remise == 0);
            Banc.Verifie("comparateur : JSON illisible", true, OffresSources.PrixCompares(null).Count == 0 && OffresSources.PrixCompares("{\"a\":1}").Count == 0);

            Banc.Verifie("revendeurs : les six, places de marché signalées", true,
                string.Join(",", OffresSources.Revendeurs.Select(r => r.Nom)) == "Instant Gaming,G2A,Eneba,Kinguin,Gamivo,AllKeyShop"
                && OffresSources.Revendeurs.Where(r => r.PlaceDeMarche).Select(r => r.Nom).SequenceEqual(new[] { "G2A", "Eneba", "Kinguin", "Gamivo" }));
            var u = OffresSources.Revendeurs.ToDictionary(r => r.Nom, r => OffresSources.UrlRevendeur(r, "Assassin's Creed® Mirage™ : Deluxe"));
            Banc.Verifie("revendeurs : recherche Instant Gaming", true, u["Instant Gaming"] == "https://www.instant-gaming.com/fr/rechercher/?q=Assassin%27s%20Creed%20Mirage%20Deluxe");
            Banc.Verifie("revendeurs : recherche G2A, Eneba, Kinguin", true, u["G2A"] == "https://www.g2a.com/fr/search?query=Assassin%27s%20Creed%20Mirage%20Deluxe"
                && u["Eneba"] == "https://www.eneba.com/fr/store/all?text=Assassin%27s%20Creed%20Mirage%20Deluxe"
                && u["Kinguin"] == "https://www.kinguin.net/listing?phrase=Assassin%27s%20Creed%20Mirage%20Deluxe");
            Banc.Verifie("revendeurs : recherche dans le chemin (Gamivo, AllKeyShop)", true, u["Gamivo"] == "https://www.gamivo.com/search/Assassin%27s%20Creed%20Mirage%20Deluxe"
                && u["AllKeyShop"] == "https://www.allkeyshop.com/blog/catalogue/search-Assassin%27s+Creed+Mirage+Deluxe/");
            Banc.Verifie("revendeurs : liens sûrs, nom vide ou piégé", true, u.Values.All(Offres.LienSur)
                && OffresSources.UrlRevendeur(OffresSources.Revendeurs[0], " ™ ") == null
                && OffresSources.UrlRevendeur(OffresSources.Revendeurs[4], "../../admin?x=1#y").StartsWith("https://www.gamivo.com/search/admin%20x%201%20y"));
        }
    }
}
