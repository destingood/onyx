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

        public static void Tout()
        {
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
