using System;
using System.Collections.Generic;

namespace BTOptimizer.Tests
{
    internal static class MonetisationTests
    {
        internal static void Tout()
        {
            Banc.Titre("Monétisation transparente et liens affiliés");
            string original = "https://www.gog.com/fr/game/test?currency=EUR&edition=deluxe#details";
            var settings = new Monetisation.Settings { Enabled = true, SupportUrl = "https://ko-fi.com/onyx-test", AffiliateRules = new List<Monetisation.AffiliateRule> {
                new Monetisation.AffiliateRule { Enabled = true, MerchantHost = "www.gog.com", UrlTemplate = "https://tracking.test/click?publisher=123&destination={url}" }
            }};
            var resolved = Monetisation.Resolve(settings, original);
            Banc.Verifie("la destination et sa requête sont encodées entièrement", true, resolved.Affiliated && resolved.Url == "https://tracking.test/click?publisher=123&destination=" + Uri.EscapeDataString(original));
            Banc.Verifie("pas de redirection pour un domaine voisin", false, Monetisation.Resolve(settings, "https://www.gog.com.evil.test/game").Affiliated);
            Banc.Verifie("pas de réécriture des redirections CheapShark", false, Monetisation.Resolve(settings, "https://www.cheapshark.com/redirect?dealID=abc").Affiliated);
            Banc.Verifie("pas de réécriture de Steam sans programme configuré", false, Monetisation.Resolve(settings, "https://store.steampowered.com/app/1/").Affiliated);
            Banc.Verifie("configuration absente : lien intact", true, original == Monetisation.Resolve(null, original).Url);
            settings.Enabled = false;
            Banc.Verifie("désactivation globale : lien intact", true, original == Monetisation.Resolve(settings, original).Url);
            Banc.Verifie("désactivation globale : soutien masqué", true, Monetisation.Support(settings) == null);
            settings.Enabled = true;
            Banc.Verifie("lien de soutien configuré", true, settings.SupportUrl == Monetisation.Support(settings));
            settings.AffiliateRules[0].Enabled = false;
            Banc.Verifie("règle inactive : aucun affilié", false, Monetisation.Resolve(settings, original).Affiliated);
            settings.AffiliateRules[0].Enabled = true;
            foreach (string bad in new[] { "javascript:alert(1)", "http://tracking.test/?url={url}", "https://user:secret@tracking.test/?url={url}", "https://127.0.0.1/?url={url}", "https://localhost/?url={url}", "https://tracking.test/no-destination", "https://tracking.test/?a={url}&b={url}", "https://tracking.test/?url={url}&id={secret}" })
            {
                settings.AffiliateRules[0].UrlTemplate = bad;
                Banc.Verifie("modèle refusé : " + bad, false, Monetisation.Resolve(settings, original).Affiliated);
            }
            foreach (string json in new[] { "", "{", "null", "{\"enabled\":true,\"version\":2}", "{\"enabled\":\"true\"}" })
                Banc.Verifie("configuration invalide inactive : " + json, false, Monetisation.Parse(json).Enabled);
            Banc.Verifie("version valide lue", true, Monetisation.Parse("{\"version\":1,\"enabled\":true}").Enabled);
            settings.Sponsor = new Monetisation.Campaign { Id = "test", Advertiser = "Démonstration", Title = "Partenaire de test", Description = "Exemple de placement", Url = "https://partner.test/", StartsUtc = "2026-11-01T00:00:00Z", EndsUtc = "2026-12-01T00:00:00Z" };
            var start = DateTimeOffset.Parse(settings.Sponsor.StartsUtc); var end = DateTimeOffset.Parse(settings.Sponsor.EndsUtc);
            Banc.Verifie("campagne à sa date de début", true, Monetisation.ActiveCampaign(settings, start) != null);
            Banc.Verifie("campagne future masquée", true, Monetisation.ActiveCampaign(settings, start.AddSeconds(-1)) == null);
            Banc.Verifie("campagne expirée masquée dès sa date de fin", true, Monetisation.ActiveCampaign(settings, end) == null);
            settings.Sponsor.Url = "https://user:pass@partner.test/";
            Banc.Verifie("URL sponsor avec identifiants refusée", true, Monetisation.ActiveCampaign(settings, start) == null);
            settings.Sponsor.Url = "https://partner.test/"; settings.Sponsor.Advertiser = "";
            Banc.Verifie("annonceur anonyme refusé", true, Monetisation.ActiveCampaign(settings, start) == null);
            settings.Sponsor.Advertiser = "Test"; settings.Sponsor.EndsUtc = "date incorrecte";
            Banc.Verifie("date invalide : campagne masquée", true, Monetisation.ActiveCampaign(settings, start) == null);

            Banc.Titre("Affiliation Instant Gaming (paramètre igr)");
            var ig = new Monetisation.Settings { Enabled = true, AffiliateRules = new List<Monetisation.AffiliateRule> {
                new Monetisation.AffiliateRule { Enabled = true, MerchantHost = "www.instant-gaming.com", AppendQuery = "igr=destingood" } } };
            string recherche = "https://www.instant-gaming.com/fr/rechercher/?q=Assassin%27s%20Creed%20Mirage%20Deluxe";
            var r = Monetisation.Resolve(ig, recherche);
            Banc.Verifie("recherche : igr ajouté, requête conservée", true, r.Affiliated && r.Url == recherche + "&igr=destingood");
            Banc.Egal("page sans requête : igr seul", "https://www.instant-gaming.com/fr/123-test/?igr=destingood", Monetisation.Resolve(ig, "https://www.instant-gaming.com/fr/123-test/").Url);
            Banc.Egal("igr d'un autre affilié remplacé", "https://www.instant-gaming.com/fr/?a=1&igr=destingood", Monetisation.Resolve(ig, "https://www.instant-gaming.com/fr/?igr=autre&a=1").Url);
            Banc.Egal("ancre conservée", "https://www.instant-gaming.com/fr/?igr=destingood#avis", Monetisation.Resolve(ig, "https://www.instant-gaming.com/fr/#avis").Url);
            Banc.Verifie("autre boutique intacte", false, Monetisation.Resolve(ig, "https://www.g2a.com/fr/search?query=x").Affiliated);
            Banc.Verifie("domaine imitant Instant Gaming refusé", false, Monetisation.Resolve(ig, "https://www.instant-gaming.com.evil.test/").Affiliated);
            foreach (string bad in new[] { "igr", "igr=", "=x", "igr=a&b=c", "igr=a b", "igr=<x>", "igr=x#y" })
            {
                ig.AffiliateRules[0].AppendQuery = bad;
                Banc.Verifie("paramètre refusé : " + bad, false, Monetisation.Resolve(ig, recherche).Affiliated);
            }

            Banc.Titre("Affiliation GOG (hôte af.gog.com, paramètre as)");
            var gog = new Monetisation.Settings { Enabled = true, AffiliateRules = new List<Monetisation.AffiliateRule> {
                new Monetisation.AffiliateRule { Enabled = true, MerchantHost = "www.gog.com", LinkHost = "af.gog.com", AppendQuery = "as=1234567" } } };
            var g = Monetisation.Resolve(gog, "https://www.gog.com/fr/game/heroes_of_might_and_magic_3_complete_edition");
            Banc.Verifie("lien de jeu : hôte af et as ajouté", true, g.Affiliated && g.Url == "https://af.gog.com/fr/game/heroes_of_might_and_magic_3_complete_edition?as=1234567");
            Banc.Egal("requête et ancre conservées", "https://af.gog.com/games?sort=popularity&as=1234567#top", Monetisation.Resolve(gog, "https://www.gog.com/games?sort=popularity#top").Url);
            Banc.Verifie("lien déjà en af.gog.com : non retraité", false, Monetisation.Resolve(gog, "https://af.gog.com/game/x").Affiliated);
            foreach (string bad in new[] { "af.gog.com.evil.test", "evil.test", "gog.com.evil.test", "af gog.com", "user@af.gog.com", "af.gog.com:8080", "127.0.0.1" })
            {
                gog.AffiliateRules[0].LinkHost = bad;
                Banc.Verifie("hôte refusé : " + bad, false, Monetisation.Resolve(gog, "https://www.gog.com/fr/game/x").Affiliated);
            }
            gog.AffiliateRules[0].LinkHost = "AF.GOG.COM";
            Banc.Egal("hôte en majuscules normalisé", "https://af.gog.com/game/x?as=1234567", Monetisation.Resolve(gog, "https://www.gog.com/game/x").Url);
            var lu = Monetisation.Parse("{\"version\":1,\"enabled\":true,\"affiliateRules\":[{\"enabled\":true,\"merchantHost\":\"www.gog.com\",\"linkHost\":\"af.gog.com\",\"appendQuery\":\"as=42\"}]}");
            Banc.Egal("linkHost lu depuis le JSON", "https://af.gog.com/game/x?as=42", Monetisation.Resolve(lu, "https://www.gog.com/game/x").Url);

            Banc.Titre("Configuration livrée : rémunérée sans déranger");
            string chemin = Livree();
            var livree = Monetisation.Parse(chemin == null ? null : System.IO.File.ReadAllText(chemin));
            Banc.Verifie("config/monetisation.json trouvée", true, chemin != null);
            Banc.Verifie("aucune bannière partenaire", true, livree.Sponsor == null);
            Banc.Verifie("aucun bouton de soutien", true, Monetisation.Support(livree) == null);
            Banc.Verifie("lien Instant Gaming affilié", true, Monetisation.Resolve(livree, recherche).Url == recherche + "&igr=destingood");

            Banc.Titre("Mention d'affiliation unique (pied de page)");
            Banc.Verifie("configuration livrée : mention affichée", true, Monetisation.HasAffiliation(livree));
            Banc.Verifie("mention : affiliation et commission nommées", true, Monetisation.Notice.Contains("affiliés") && Monetisation.Notice.Contains("commission"));
            Banc.Verifie("aucune configuration : pas de mention", false, Monetisation.HasAffiliation(null));
            Banc.Verifie("aucune règle : pas de mention", false, Monetisation.HasAffiliation(new Monetisation.Settings { Enabled = true }));
            ig.AffiliateRules[0].AppendQuery = "igr=destingood";
            Banc.Verifie("règle active : mention", true, Monetisation.HasAffiliation(ig));
            ig.AffiliateRules[0].Enabled = false;
            Banc.Verifie("règle inactive : pas de mention", false, Monetisation.HasAffiliation(ig));
            ig.AffiliateRules[0].Enabled = true; ig.Enabled = false;
            Banc.Verifie("monétisation désactivée : pas de mention", false, Monetisation.HasAffiliation(ig));
        }

        private static string Livree()
        {
            for (var d = new System.IO.DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            {
                string p = System.IO.Path.Combine(d.FullName, "config", "monetisation.json");
                if (System.IO.File.Exists(p) && System.IO.File.Exists(System.IO.Path.Combine(d.FullName, "BTOptimizer.csproj"))) return p;
            }
            return null;
        }
    }
}
