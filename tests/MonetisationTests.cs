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
        }
    }
}
