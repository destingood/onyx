using System;
using System.Collections.Generic;
using System.Linq;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Bons plans multi-stores : GOG, CheapShark (Fanatical, Humble, Epic…), Nintendo eShop, itch.io,
    /// prix en dollars, choix des stores (formes réelles relevées le 21/11/2026, raccourcies).
    /// </summary>
    internal static class BoutiquesTests
    {
        const string Gog = "{\"pages\":12,\"productCount\":560,\"products\":[" +
          "{\"id\":\"2141207403\",\"slug\":\"wicked_island\",\"title\":\"Wicked Island\",\"price\":{\"final\":\"22,19 €\",\"base\":\"36,99 €\",\"discount\":\"-40%\",\"finalMoney\":{\"amount\":\"22.19\",\"currency\":\"EUR\",\"discount\":\"14.80\"},\"baseMoney\":{\"amount\":\"36.99\",\"currency\":\"EUR\"}}," +
            "\"coverVertical\":\"https://images.gog-statics.com/b2e3.jpg\",\"coverHorizontal\":\"https://images.gog-statics.com/9500.png\",\"storeLink\":\"https://www.gog.com/fr/game/wicked_island\",\"operatingSystems\":[\"windows\"],\"productType\":\"game\",\"releaseDate\":\"2026.11.17\"}," +
          "{\"id\":\"2\",\"title\":\"Lien piégé\",\"price\":{\"finalMoney\":{\"amount\":\"1.00\",\"currency\":\"EUR\"},\"baseMoney\":{\"amount\":\"2.00\",\"currency\":\"EUR\"}},\"storeLink\":\"javascript:alert(1)\"}," +
          "{\"id\":\"3\",\"title\":\"Sans prix\",\"storeLink\":\"https://www.gog.com/fr/game/x\"}," +
          "{\"id\":\"4\",\"title\":\"Image en clair\",\"price\":{\"finalMoney\":{\"amount\":\"5\",\"currency\":\"EUR\"},\"baseMoney\":{\"amount\":\"10.00\",\"currency\":\"EUR\"},\"discount\":\"-50%\"},\"coverVertical\":\"http://images.gog-statics.com/x.jpg\",\"coverHorizontal\":\"https://images.gog-statics.com/h.png\",\"storeLink\":\"https://www.gog.com/fr/game/y\"}]}";

        const string Cs = "[" +
          "{\"title\":\"Mark of the Deep\",\"dealID\":\"J3QoV3DZfxoGHf0gnngRHd1PS4AMm%2Fuqeh6CmIov%2Fk4%3D\",\"storeID\":\"25\",\"gameID\":\"297257\",\"salePrice\":\"0.00\",\"normalPrice\":\"19.99\",\"savings\":\"100.000000\",\"steamAppID\":\"1933370\",\"releaseDate\":1737676800,\"thumb\":\"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/1933370/29d6/capsule_231x87.jpg?t=1\"}," +
          "{\"title\":\"Need for Speed Heat Deluxe Edition\",\"dealID\":\"GQYhHzOtQcCuICwD5sXBHG91ZHdsyxYRQbVgR%2Bs4Kbc%3D\",\"storeID\":\"25\",\"gameID\":\"204097\",\"salePrice\":\"3.49\",\"normalPrice\":\"69.99\",\"savings\":\"95.013573\",\"steamAppID\":null,\"releaseDate\":0,\"thumb\":\"https://cdn1.epicgames.com/spt-assets/f424/need-for-speed-heat-1lhdh.jpg\"}," +
          "{\"title\":\"Fanatical\",\"dealID\":\"abc\",\"storeID\":\"15\",\"gameID\":\"77\",\"salePrice\":\"9.99\",\"normalPrice\":\"19.99\",\"savings\":\"50.025\",\"thumb\":\"https://cdn.fanatical.com/x.jpg\"}," +
          "{\"title\":\"Store inconnu\",\"dealID\":\"def\",\"storeID\":\"99\",\"gameID\":\"78\",\"salePrice\":\"1.00\",\"normalPrice\":\"2.00\",\"savings\":\"50\"}," +
          "{\"title\":\"Steam en double\",\"dealID\":\"ghi\",\"storeID\":\"1\",\"gameID\":\"79\",\"salePrice\":\"1.00\",\"normalPrice\":\"2.00\",\"savings\":\"50\"}," +
          "{\"title\":\"NFS Heat moins cher ailleurs\",\"dealID\":\"jkl\",\"storeID\":\"15\",\"gameID\":\"204097\",\"salePrice\":\"2.99\",\"normalPrice\":\"69.99\",\"savings\":\"95.7\",\"thumb\":\"https://cdn.fanatical.com/nfs.jpg\"}," +
          "{\"title\":\"Mark of the Deep payant ailleurs\",\"dealID\":\"mno\",\"storeID\":\"3\",\"gameID\":\"297257\",\"salePrice\":\"1.00\",\"normalPrice\":\"19.99\",\"savings\":\"95\"}," +
          "{\"title\":\"dealID piégé\",\"dealID\":\"a&b=<x>\",\"storeID\":\"15\",\"gameID\":\"80\",\"salePrice\":\"1.00\",\"normalPrice\":\"2.00\",\"savings\":\"50\"}]";

        const string Nin = "{\"response\":{\"numFound\":2598,\"start\":0,\"docs\":[" +
          "{\"title\":\"Mortal Glory 2\",\"url\":\"/fr-fr/Jeux/Jeux-a-telecharger-sur-Nintendo-Switch/Mortal-Glory-2-2874684.html\",\"nsuid_txt\":[\"70010000138342\"],\"image_url_sq_s\":\"https://www.nintendo.com/eu/media/images/1x1_MortalGlory2.jpg\",\"price_regular_f\":11.99,\"price_discounted_f\":9.59,\"price_discount_percentage_f\":20,\"system_names_txt\":[\"Nintendo Switch\"],\"fs_id\":\"2874684\",\"dates_released_dts\":[\"2025-08-08T00:00:00Z\"]}," +
          "{\"title\":\"Sans nsuid\",\"url\":\"/fr-fr/Jeux/x-1.html\",\"nsuid_txt\":null,\"image_url_sq_s\":\"https://www.nintendo.com/eu/media/images/b.jpg\",\"price_regular_f\":20,\"price_discounted_f\":10,\"price_discount_percentage_f\":50,\"system_names_txt\":[\"Nintendo Switch 2\"],\"fs_id\":\"555\"}," +
          "{\"title\":\"Lien externe\",\"url\":\"//evil.example/x\",\"nsuid_txt\":[\"70010000000001\"],\"price_regular_f\":5,\"price_discounted_f\":4,\"fs_id\":\"556\"}," +
          "{\"title\":\"Sans prix\",\"url\":\"/fr-fr/Jeux/y-2.html\",\"fs_id\":\"557\"}]}}";

        const string PrixNin = "{\"personalized\":false,\"country\":\"FR\",\"prices\":[" +
          "{\"title_id\":70010000138342,\"sales_status\":\"onsale\",\"regular_price\":{\"amount\":\"11,99 €\",\"currency\":\"EUR\",\"raw_value\":\"11.99\"},\"discount_price\":{\"amount\":\"4,89 €\",\"currency\":\"EUR\",\"raw_value\":\"4.89\",\"start_datetime\":\"2026-11-06T23:00:00Z\",\"end_datetime\":\"2026-11-21T22:59:59Z\"}}," +
          "{\"title_id\":70010000000099,\"sales_status\":\"onsale\",\"regular_price\":{\"raw_value\":\"9.99\"}}]}";

        const string Itch = "<?xml version=\"1.0\" encoding=\"UTF-8\" ?><rss version=\"2.0\"><channel><title>Top games on sale - itch.io</title>" +
          "<item><guid>https://deeplookgames.itch.io/fix-her-pipes</guid><title>Fix Her Pipes [34% Off] [$1.98] [Survival] [Windows]</title><plainTitle>Fix Her Pipes</plainTitle><imageurl>https://img.itch.zone/aW1n/315x250%23c/sTuEiP.png</imageurl><price>$1.98</price><currency>USD</currency><discountpercent>34</discountpercent><fullPrice>$3.00</fullPrice><saleends>Wed, 25 Nov 2026 10:34:00 GMT</saleends><link>https://deeplookgames.itch.io/fix-her-pipes</link><pubDate>Wed, 14 Oct 2026 09:04:54 GMT</pubDate><platforms><windows>yes</windows></platforms></item>" +
          "<item><guid>https://a.itch.io/b</guid><plainTitle>Euros</plainTitle><price>€2,50</price><currency>EUR</currency><discountpercent>50</discountpercent><fullPrice>€5,00</fullPrice><link>https://a.itch.io/b</link></item>" +
          "<item><guid>https://a.itch.io/c</guid><plainTitle>Yen</plainTitle><price>¥300</price><currency>JPY</currency><discountpercent>50</discountpercent><fullPrice>¥600</fullPrice><link>https://a.itch.io/c</link></item>" +
          "<item><guid>x</guid><plainTitle>Piégé</plainTitle><price>$1.00</price><currency>USD</currency><fullPrice>$2.00</fullPrice><link>javascript:alert(1)</link></item>" +
          "</channel></rss>";

        public static void Tout()
        {
            Banc.Titre("Bons plans : GOG, CheapShark, Nintendo, itch.io, choix des stores");

            // Prix en dollars : affichés tels quels, hors plafond en euros (sauf « Gratuit »).
            Banc.Verifie("prix en dollars", true, Offres.Prix(999, "USD") == "9,99 $" && Offres.Prix(2000, "USD") == "20 $" && Offres.Prix(0, "USD") == "Gratuit" && Offres.Prix(749, "EUR") == "7,49 €" && Offres.Prix(749, null) == "7,49 €");
            var usd = new Offre { Nom = "a", PrixCts = 4999, Devise = "USD" };
            Banc.Verifie("plafond en euros sans effet sur les dollars, Gratuit oui", true, Offres.SousPlafond(usd, 500) && !Offres.SousPlafond(usd, 0) && Offres.SousPlafond(new Offre { PrixCts = 0, Devise = "USD" }, 0) && new Offre().Devise == "EUR");
            Banc.Verifie("prix de l'offre selon sa devise", true, Offres.PrixDe(usd) == "49,99 $" && Offres.PrixOrigineDe(new Offre { PrixOrigineCts = 300, Devise = "USD" }) == "3 $");

            // Catalogue des stores.
            var cles = OffresSources.Boutiques.Select(b => b.Cle).ToList();
            Banc.Verifie("catalogue : 15 stores, clés uniques", true, cles.Count == 15 && cles.Distinct().Count() == 15 && cles[0] == "steam");
            Banc.Verifie("catalogue : noms affichés", true, OffresSources.NomBoutique("fanatical") == "Fanatical" && OffresSources.NomBoutique("epic") == "Epic Games Store" && OffresSources.NomBoutique("gamerpower") == "GamerPower" && OffresSources.NomBoutique("xyz") == "xyz" && OffresSources.NomBoutique(null) == "");
            Banc.Verifie("choix : absent ou illisible → tous", true, OffresSources.LitChoixBoutiques(null).Count == 15 && OffresSources.LitChoixBoutiques("").Count == 15 && OffresSources.LitChoixBoutiques("n'importe quoi").Count == 15);
            var choix = OffresSources.LitChoixBoutiques("v1\nsteam\ngog\ninconnu\nfanatical\n");
            Banc.Verifie("choix : clés connues seulement", true, choix.Count == 3 && choix.Contains("steam") && choix.Contains("fanatical") && !choix.Contains("inconnu"));
            Banc.Verifie("choix : aucun store reste aucun", true, OffresSources.LitChoixBoutiques(OffresSources.EcritChoixBoutiques(new HashSet<string>())).Count == 0);
            Banc.Verifie("choix : aller-retour dans l'ordre du catalogue", true, OffresSources.EcritChoixBoutiques(new HashSet<string> { "itch", "steam", "gog" }) == "v1\nsteam\ngog\nitch\n");
            Banc.Verifie("CheapShark : identifiants des stores choisis", true, OffresSources.IdsCheapShark(new HashSet<string> { "steam", "fanatical", "epic", "humble" }) == "11,15,25" && OffresSources.IdsCheapShark(new HashSet<string> { "steam", "gog" }) == "");

            // GOG (euros).
            Banc.Verifie("GOG : URL", true, OffresSources.UrlGog(null, 1) == "https://catalog.gog.com/v1/catalog?limit=48&order=desc:trending&discounted=eq:true&productType=in:game,pack&page=1&countryCode=FR&locale=fr-FR&currencyCode=EUR"
                && OffresSources.UrlGog("Elden ring & co", 3).EndsWith("&page=3&countryCode=FR&locale=fr-FR&currencyCode=EUR&query=like:Elden%20ring%20%26%20co"));
            var gog = OffresSources.PromosGog(Gog);
            Banc.Verifie("GOG : lien piégé et prix absent écartés", true, gog.Count == 2);
            Banc.Verifie("GOG : offre complète", true, gog[0].Source == "gog" && gog[0].Id == "2141207403" && gog[0].Nom == "Wicked Island" && gog[0].PrixCts == 2219 && gog[0].PrixOrigineCts == 3699 && gog[0].Remise == 40
                && gog[0].Url == "https://www.gog.com/fr/game/wicked_island" && gog[0].Image == "https://images.gog-statics.com/b2e3.jpg" && gog[0].Devise == "EUR" && gog[0].Plateformes[0] == "GOG" && gog[0].Sortie == new DateTime(2026, 11, 17, 0, 0, 0, DateTimeKind.Utc));
            Banc.Verifie("GOG : prix entier, image http refusée → horizontale", true, gog[1].PrixCts == 500 && gog[1].Remise == 50 && gog[1].Image == "https://images.gog-statics.com/h.png");
            Banc.Verifie("GOG : entrée invalide", true, OffresSources.PromosGog(null).Count == 0 && OffresSources.PromosGog("[]").Count == 0 && OffresSources.PromosGog("<html>").Count == 0);

            // CheapShark (dollars).
            Banc.Verifie("CheapShark : URL", true, OffresSources.UrlCheapShark("15,25", null, 0) == "https://www.cheapshark.com/api/1.0/deals?storeID=15,25&onSale=1&pageSize=60&pageNumber=0&sortBy=Deal%20Rating"
                && OffresSources.UrlCheapShark("15", "Elden ring", 2).EndsWith("&pageNumber=2&sortBy=Deal%20Rating&title=Elden%20ring"));
            var cs = OffresSources.PromosCheapShark(Cs);
            Banc.Verifie("CheapShark : store inconnu, Steam en double et dealID piégé écartés", true, cs.Count == 3);
            Banc.Verifie("CheapShark : offre Epic gratuite", true, cs[0].Source == "epic" && cs[0].Id == "297257" && cs[0].PrixCts == 0 && cs[0].PrixOrigineCts == 1999 && cs[0].Remise == 100 && cs[0].Devise == "USD"
                && cs[0].Url == "https://www.cheapshark.com/redirect?dealID=J3QoV3DZfxoGHf0gnngRHd1PS4AMm%2Fuqeh6CmIov%2Fk4%3D" && cs[0].SteamAppId == 1933370 && cs[0].Plateformes[0] == "Epic Games Store");
            Banc.Verifie("CheapShark : jaquette Steam par AppID plutôt que la vignette large", true, cs[0].Image == null);
            Banc.Verifie("CheapShark : vignette Epic réduite", true, OffresSources.PromosCheapShark(Cs.Replace("2.99", "9.99"))[1].Image == "https://cdn1.epicgames.com/spt-assets/f424/need-for-speed-heat-1lhdh.jpg?w=360&resize=1");
            Banc.Verifie("CheapShark : un seul deal par jeu, le moins cher, à sa place", true, cs[0].Source == "epic" && cs[0].PrixCts == 0
                && cs[1].Source == "fanatical" && cs[1].Id == "204097" && cs[1].PrixCts == 299 && cs[1].Remise == 96 && cs[1].SteamAppId == 0 && cs[1].Url.EndsWith("dealID=jkl"));
            Banc.Verifie("CheapShark : Fanatical", true, cs[2].Source == "fanatical" && cs[2].Remise == 50 && cs[2].Image == "https://cdn.fanatical.com/x.jpg" && cs[2].Sortie == null);
            Banc.Verifie("CheapShark : entrée invalide", true, OffresSources.PromosCheapShark(null).Count == 0 && OffresSources.PromosCheapShark("{}").Count == 0);

            // Nintendo eShop (euros).
            Banc.Verifie("Nintendo : URL", true, OffresSources.UrlNintendo(null, 0) == "https://searching.nintendo-europe.com/fr/select?q=*&fq=type:GAME%20AND%20price_has_discount_b:true&rows=48&start=0&sort=change_date%20desc&wt=json"
                && OffresSources.UrlNintendo("Mario \"Kart\" : 8!", 96).StartsWith("https://searching.nintendo-europe.com/fr/select?q=Mario%20Kart%208&") && OffresSources.UrlNintendo("Mario", 96).Contains("&start=96&"));
            var nin = OffresSources.PromosNintendo(Nin);
            Banc.Verifie("Nintendo : lien externe et prix absent écartés", true, nin.Count == 2);
            Banc.Verifie("Nintendo : offre complète", true, nin[0].Source == "nintendo" && nin[0].Id == "70010000138342" && nin[0].PrixCts == 959 && nin[0].PrixOrigineCts == 1199 && nin[0].Remise == 20
                && nin[0].Url == "https://www.nintendo.com/fr-fr/Jeux/Jeux-a-telecharger-sur-Nintendo-Switch/Mortal-Glory-2-2874684.html" && nin[0].Image == "https://www.nintendo.com/eu/media/images/1x1_MortalGlory2.jpg"
                && nin[0].Plateformes[0] == "Switch" && nin[0].Devise == "EUR" && nin[0].Sortie == new DateTime(2025, 8, 8, 0, 0, 0, DateTimeKind.Utc));
            Banc.Verifie("Nintendo : sans nsuid, identifiant fs", true, nin[1].Id == "fs555" && nin[1].PrixCts == 1000 && nin[1].Remise == 50);
            Banc.Verifie("Nintendo : URL des prix réels (nsuid seulement, 50 max)", true, OffresSources.UrlPrixNintendo(nin) == "https://api.ec.nintendo.com/v1/price?country=FR&lang=fr&ids=70010000138342" && OffresSources.UrlPrixNintendo(new List<Offre>()) == null);
            var maj = OffresSources.AppliquePrixNintendo(nin, PrixNin);
            Banc.Verifie("Nintendo : prix réel appliqué, fin de promo", true, maj.Count == 2 && maj[0].PrixCts == 489 && maj[0].PrixOrigineCts == 1199 && maj[0].Remise == 59 && maj[0].Fin == new DateTime(2026, 11, 21, 22, 59, 59, DateTimeKind.Utc));
            var finie = new List<Offre> { new Offre { Source = "nintendo", Id = "70010000000099", Nom = "x", PrixCts = 500 } };
            Banc.Verifie("Nintendo : promo finie retirée ; réponse illisible → index gardé", true, OffresSources.AppliquePrixNintendo(finie, PrixNin).Count == 0 && OffresSources.AppliquePrixNintendo(nin, "<html>").Count == 2 && OffresSources.AppliquePrixNintendo(nin, "<html>")[0].PrixCts == 959);

            // itch.io (flux RSS, dollars).
            var itch = OffresSources.PromosItch(Itch);
            Banc.Verifie("itch : devise inconnue et lien piégé écartés", true, itch.Count == 2);
            Banc.Verifie("itch : offre complète", true, itch[0].Source == "itch" && itch[0].Id == "deeplookgames.itch.io/fix-her-pipes" && itch[0].Nom == "Fix Her Pipes" && itch[0].PrixCts == 198 && itch[0].PrixOrigineCts == 300 && itch[0].Remise == 34
                && itch[0].Devise == "USD" && itch[0].Url == "https://deeplookgames.itch.io/fix-her-pipes" && itch[0].Image == "https://img.itch.zone/aW1n/315x250%23c/sTuEiP.png" && itch[0].Fin == new DateTime(2026, 11, 25, 10, 34, 0, DateTimeKind.Utc) && itch[0].Plateformes[0] == "itch.io");
            Banc.Verifie("itch : prix en euros", true, itch[1].Devise == "EUR" && itch[1].PrixCts == 250 && itch[1].PrixOrigineCts == 500 && itch[1].Image == null);
            Banc.Verifie("itch : XML invalide ou DTD", true, OffresSources.PromosItch(null).Count == 0 && OffresSources.PromosItch("<rss><channel><item>").Count == 0
                && OffresSources.PromosItch("<?xml version=\"1.0\"?><!DOCTYPE rss [<!ENTITY x SYSTEM \"file:///C:/Windows/win.ini\">]><rss><channel><item><plainTitle>a&x;b</plainTitle><price>$1</price><currency>USD</currency><fullPrice>$2</fullPrice><link>https://a.itch.io/z</link></item></channel></rss>").TrueForAll(o => o.Nom.Length < 10));

            // Plateformes manquantes des offres gratuites.
            var gp = OffresSources.GratuitsGamerPower("[{\"id\":1,\"title\":\"A Giveaway\",\"open_giveaway_url\":\"https://www.gamerpower.com/open/a\",\"platforms\":\"PC, Itch.io, Ubisoft, Origin, Battle.net, EA Games\"}]");
            Banc.Verifie("GamerPower : itch.io, Ubisoft, EA, Battle.net", true, gp.Count == 1 && string.Join(",", gp[0].Plateformes) == "PC,itch.io,Ubisoft,EA,Battle.net");
            Banc.Verifie("filtre des plateformes : nouvelles entrées", true, new[] { "itch.io", "Ubisoft", "EA", "Battle.net" }.All(p => Array.IndexOf(Offres.Plateformes, p) >= 0));
        }
    }
}
