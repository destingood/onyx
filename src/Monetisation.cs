using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.Json;

namespace BTOptimizer
{
    // Configuration éditeur publique : aucun identifiant utilisateur, secret ou appel réseau.
    internal static class Monetisation
    {
        public sealed class Settings
        {
            public int Version { get; set; } = 1;
            public bool Enabled { get; set; }
            public string SupportUrl { get; set; }
            public Campaign Sponsor { get; set; }
            public List<AffiliateRule> AffiliateRules { get; set; } = new List<AffiliateRule>();
        }
        public sealed class Campaign
        {
            public string Id { get; set; }
            public string Advertiser { get; set; }
            public string Title { get; set; }
            public string Description { get; set; }
            public string Url { get; set; }
            public string StartsUtc { get; set; }
            public string EndsUtc { get; set; }
        }
        public sealed class AffiliateRule
        {
            public bool Enabled { get; set; }
            public string MerchantHost { get; set; }
            public string UrlTemplate { get; set; }
            /// <summary>Paramètre d'affilié ajouté au lien de la boutique elle-même (ex. « igr=pseudo »
            /// chez Instant Gaming). Prioritaire sur UrlTemplate : aucune redirection par un tiers.</summary>
            public string AppendQuery { get; set; }
        }
        public sealed class Link { public string Url; public bool Affiliated; }
        internal static Settings Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > 65536) return new Settings();
            try
            {
                var settings = JsonSerializer.Deserialize<Settings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, MaxDepth = 8 });
                if (settings == null || settings.Version != 1 || (settings.AffiliateRules != null && settings.AffiliateRules.Count > 50)) return new Settings();
                return settings;
            }
            catch { return new Settings(); }
        }
        internal static bool PublicHttps(string url)
        {
            Uri uri; IPAddress address;
            return !string.IsNullOrWhiteSpace(url) && url.Length <= 4096 && !url.Any(char.IsWhiteSpace)
                && Uri.TryCreate(url, UriKind.Absolute, out uri) && uri.Scheme == "https" && uri.IsDefaultPort
                && string.IsNullOrEmpty(uri.UserInfo) && uri.HostNameType == UriHostNameType.Dns
                && uri.Host.Contains('.') && !uri.Host.EndsWith(".") && !uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
                && !uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) && !IPAddress.TryParse(uri.Host, out address);
        }
        internal static string Support(Settings settings) { return settings != null && settings.Enabled && PublicHttps(settings.SupportUrl) ? settings.SupportUrl : null; }
        internal static Campaign ActiveCampaign(Settings settings, DateTimeOffset now)
        {
            var campaign = settings == null || !settings.Enabled ? null : settings.Sponsor;
            DateTimeOffset start, end;
            if (campaign == null || !PublicHttps(campaign.Url) || !ShortText(campaign.Id, 60) || !ShortText(campaign.Advertiser, 60)
                || !ShortText(campaign.Title, 100) || !ShortText(campaign.Description, 180)
                || !DateTimeOffset.TryParse(campaign.StartsUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out start)
                || !DateTimeOffset.TryParse(campaign.EndsUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out end)
                || end <= start || now < start || now >= end) return null;
            return campaign;
        }
        private static bool ShortText(string text, int max) { return !string.IsNullOrWhiteSpace(text) && text.Length <= max && !text.Any(char.IsControl); }
        /// <summary>Mention affichée une seule fois par écran (pied de page) à la place d'un badge sur chaque lien.</summary>
        internal const string Notice = "Certains liens sont affiliés : commission possible pour ONYX, même prix pour toi.";
        /// <summary>Vrai si au moins une règle d'affiliation peut s'appliquer : c'est elle qui déclenche la mention.</summary>
        internal static bool HasAffiliation(Settings settings)
        {
            return settings != null && settings.Enabled && settings.AffiliateRules != null && settings.AffiliateRules.Any(r =>
                r != null && r.Enabled && !string.IsNullOrWhiteSpace(r.MerchantHost) && (!string.IsNullOrEmpty(r.AppendQuery) || !string.IsNullOrEmpty(r.UrlTemplate)));
        }
        internal static Link Resolve(Settings settings, string original)
        {
            var result = new Link { Url = original };
            if (settings == null || !settings.Enabled || settings.AffiliateRules == null || !PublicHttps(original)) return result;
            var source = new Uri(original);
            foreach (var rule in settings.AffiliateRules)
            {
                if (rule == null || !rule.Enabled || !string.Equals(rule.MerchantHost, source.IdnHost, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrEmpty(rule.AppendQuery))
                {
                    string tagged = AppendParameter(source, rule.AppendQuery);
                    if (tagged == null || !PublicHttps(tagged)) continue;
                    result.Url = tagged; result.Affiliated = true; break;
                }
                string template = rule.UrlTemplate;
                if (string.IsNullOrEmpty(template) || template.Length > 2048 || template.IndexOf("{url}", StringComparison.Ordinal) < 0
                    || template.IndexOf("{url}", StringComparison.Ordinal) != template.LastIndexOf("{url}", StringComparison.Ordinal)) continue;
                string target = template.Replace("{url}", Uri.EscapeDataString(original));
                if (target.Contains('{') || target.Contains('}') || !PublicHttps(target)) continue;
                // Le lien produit complet reste un paramètre : pas de remplacement par une page d'accueil.
                result.Url = target; result.Affiliated = true; break;
            }
            return result;
        }

        /// <summary>Ajoute « nom=valeur » à la requête du lien, en remplaçant un éventuel paramètre
        /// du même nom. Nom et valeur limités à [A-Za-z0-9_-] : rien à encoder, rien à injecter.</summary>
        private static string AppendParameter(Uri source, string parameter)
        {
            int eq = parameter.IndexOf('=');
            if (parameter.Length > 100 || eq <= 0 || eq == parameter.Length - 1 || !parameter.Where((c, i) => i != eq).All(SafeChar)) return null;
            string prefix = parameter.Substring(0, eq + 1);
            var kept = source.Query.TrimStart('?').Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(p => !p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !string.Equals(p, prefix.TrimEnd('='), StringComparison.OrdinalIgnoreCase));
            var builder = new UriBuilder(source) { Query = string.Join("&", kept.Concat(new[] { parameter })) };
            return builder.Uri.AbsoluteUri;
        }
        private static bool SafeChar(char c) { return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-'; }
    }
}
