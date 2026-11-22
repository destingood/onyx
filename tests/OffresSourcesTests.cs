using System;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Bons plans : URL de recherche Steam et lecture des réponses Steam, Epic et GamerPower
    /// (formes réelles relevées le 21/11/2026, raccourcies).
    /// </summary>
    internal static class OffresSourcesTests
    {
        const string Recherche = "{\"desc\":\"\",\"items\":[" +
          "{\"name\":\"Euro Truck Simulator 2\",\"logo\":\"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/227300/da84/capsule_sm_120.jpg?t=1\"}," +
          "{\"name\":\"Lot\",\"logo\":\"https://shared.akamai.steamstatic.com/store_item_assets/steam/subs/555/capsule_sm_120.jpg\"}," +
          "{\"name\":\"FINAL FANTASY VII REBIRTH\",\"logo\":\"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/2909400/61fd/capsule_sm_120.jpg?t=2\"}," +
          "{\"name\":\"Doublon\",\"logo\":\"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/227300/x/capsule_sm_120.jpg\"}]}";
        const string Fiches = "{\"response\":{\"store_items\":[" +
          "{\"appid\":2909400,\"success\":1,\"visible\":true,\"name\":\"FINAL FANTASY VII REBIRTH\",\"tagids\":[122,19,21],\"assets\":{\"asset_url_format\":\"steam/apps/2909400/${FILENAME}?t=1793774936\",\"library_capsule\":\"480bd879/library_600x900.jpg\",\"header\":\"64a1/header.jpg\"},\"release\":{\"steam_release_date\":1737641006}," +
            "\"best_purchase_option\":{\"final_price_in_cents\":\"749\",\"original_price_in_cents\":\"4999\",\"discount_pct\":85,\"active_discounts\":[{\"discount_end_date\":1796234400}]}}," +
          "{\"appid\":1245620,\"success\":1,\"visible\":true,\"name\":\"ELDEN RING\",\"tagids\":[1695],\"assets\":{\"asset_url_format\":\"steam/apps/1245620/${FILENAME}\",\"header\":\"9a81/header.jpg\"},\"release\":{\"steam_release_date\":1645744078},\"best_purchase_option\":{\"final_price_in_cents\":\"5999\"}}," +
          "{\"appid\":570,\"success\":1,\"visible\":true,\"name\":\"Dota 2\",\"is_free\":true,\"tagids\":[113],\"best_purchase_option\":{}}," +
          "{\"appid\":999,\"success\":1,\"name\":\"Prix illisible\",\"best_purchase_option\":{\"final_price_in_cents\":\"\"}}," +
          "{\"appid\":42,\"success\":2}]}}";
        const string Epic = "{\"data\":{\"Catalog\":{\"searchStore\":{\"elements\":[" +
          "{\"title\":\"Mark of the Deep\",\"id\":\"1f9a\",\"keyImages\":[{\"type\":\"OfferImageWide\",\"url\":\"https://cdn1.epicgames.com/offer/x/wide.jpg\"},{\"type\":\"OfferImageTall\",\"url\":\"https://cdn1.epicgames.com/offer/x/tall.jpg\"},{\"type\":\"Thumbnail\",\"url\":\"https://cdn1.epicgames.com/offer/x/thumb.jpg\"}],\"productSlug\":null,\"catalogNs\":{\"mappings\":[{\"pageSlug\":\"mark-of-the-deep-f7dc08\",\"pageType\":\"productHome\"}]}," +
            "\"price\":{\"totalPrice\":{\"discountPrice\":0,\"originalPrice\":1950,\"currencyCode\":\"EUR\"}}," +
            "\"promotions\":{\"promotionalOffers\":[{\"promotionalOffers\":[{\"startDate\":\"2026-11-19T16:00:00.000Z\",\"endDate\":\"2026-11-26T16:00:00.000Z\"}]}],\"upcomingPromotionalOffers\":[]}}," +
          "{\"title\":\"Bientôt\",\"id\":\"b2\",\"price\":{\"totalPrice\":{\"discountPrice\":1999,\"originalPrice\":1999}}," +
            "\"promotions\":{\"promotionalOffers\":[],\"upcomingPromotionalOffers\":[{\"promotionalOffers\":[{\"startDate\":\"2026-11-26T16:00:00.000Z\",\"endDate\":\"2026-12-03T16:00:00.000Z\"}]}]}}," +
          "{\"title\":\"Sans promo\",\"id\":\"c3\",\"price\":{\"totalPrice\":{\"discountPrice\":0,\"originalPrice\":0}},\"promotions\":null}]}}}}";
        const string Gp = "[{\"id\":3853,\"title\":\"Mark of the Deep (Epic Games) Giveaway\",\"worth\":\"$19.99\",\"thumbnail\":\"https://www.gamerpower.com/offers/1/6aff1d0e4942d.jpg\",\"image\":\"https://www.gamerpower.com/offers/1b/6aff1d0e4942d.jpg\",\"open_giveaway_url\":\"https://www.gamerpower.com/open/mark-of-the-deep-epic-games-giveaway\"," +
            "\"published_date\":\"2026-11-19 11:00:14\",\"type\":\"Game\",\"platforms\":\"PC, Epic Games Store\",\"end_date\":\"2026-11-26 23:59:00\",\"status\":\"Active\"}," +
          "{\"id\":7,\"title\":\"Jeu Console Giveaway\",\"thumbnail\":\"javascript:alert(1)\",\"open_giveaway_url\":\"https://www.gamerpower.com/open/x\",\"published_date\":\"2026-11-18 09:00:00\",\"platforms\":\"Playstation 5, Xbox Series X|S, Nintendo Switch, DRM-Free\",\"end_date\":\"N/A\"}," +
          "{\"id\":8,\"title\":\"Lien piégé\",\"open_giveaway_url\":\"javascript:alert(1)\",\"platforms\":\"PC\",\"end_date\":\"N/A\"}]";

        public static void Tout()
        {
            Banc.Titre("Bons plans : sources Steam, Epic, GamerPower");
            // Portail captif ou page d'erreur en HTML répondus en 200 : ni lus ni mis en cache.
            Banc.Verifie("réponse plausible : JSON et XML acceptés", true, OffresSources.ReponsePlausible("{\"a\":1}") && OffresSources.ReponsePlausible(" \r\n[1]") && OffresSources.ReponsePlausible("﻿<?xml version=\"1.0\"?><rss/>") && OffresSources.ReponsePlausible("<rss version=\"2.0\"></rss>") && OffresSources.ReponsePlausible("<!-- flux --><feed/>"));
            Banc.Verifie("réponse plausible : page HTML refusée", true, !OffresSources.ReponsePlausible("<!DOCTYPE html><html><body>Connexion Wi-Fi</body></html>") && !OffresSources.ReponsePlausible("  <HTML><head>") && !OffresSources.ReponsePlausible("<head><title>x</title>"));
            Banc.Verifie("réponse plausible : vide ou texte brut refusé", true, !OffresSources.ReponsePlausible("") && !OffresSources.ReponsePlausible(null) && !OffresSources.ReponsePlausible("Connectez-vous pour continuer"));
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
            Banc.Verifie("URL fiches : images demandées", true, Uri.UnescapeDataString(uf).Contains("\"include_assets\":true"));
            Banc.Verifie("image Steam : bandeau horizontal avec empreinte", true, fs[0].Image == "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/2909400/64a1/header.jpg?t=1793774936");
            Banc.Verifie("image Steam : sans jaquette, le bandeau", true, fs[1].Image == "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/1245620/9a81/header.jpg");
            Banc.Verifie("image Steam : sans assets, aucune", true, fs[2].Image == null);
            Banc.Verifie("image Epic : visuel horizontal en haute résolution", true, ep[0].Image == "https://cdn1.epicgames.com/offer/x/wide.jpg?w=1280&resize=1");
            Banc.Verifie("image GamerPower : image complète, lien piégé ignoré", true, gp[0].Image == "https://www.gamerpower.com/offers/1b/6aff1d0e4942d.jpg" && gp[1].Image == null);
            Banc.Verifie("GamerPower : rien de disponible (objet)", true, OffresSources.GratuitsGamerPower("{\"status\":0,\"status_message\":\"No active giveaways available at the moment, please try again later.\"}").Count == 0);
        }
    }
}
