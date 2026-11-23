using System;
using System.IO;

namespace BTOptimizer
{
    internal static class MonetisationRuntime
    {
        private static readonly Lazy<Monetisation.Settings> _settings = new Lazy<Monetisation.Settings>(Read);
#if BTTEST
        internal static Monetisation.Settings Preview;
#endif
        internal static Monetisation.Settings Settings
        {
            get
            {
#if BTTEST
                if (Preview != null) return Preview;
#endif
                return _settings.Value;
            }
        }
        private static Monetisation.Settings Read()
        {
            try
            {
                using (var stream = typeof(MonetisationRuntime).Assembly.GetManifestResourceStream("onyx-monetisation.json"))
                {
                    if (stream == null) return new Monetisation.Settings();
                    using (var reader = new StreamReader(stream)) return Monetisation.Parse(reader.ReadToEnd());
                }
            }
            catch { return new Monetisation.Settings(); }
        }
        internal static bool IsAffiliate(string url) { return Monetisation.Resolve(Settings, url).Affiliated; }
        /// <summary>Mention unique d'affiliation (pied de page), vide si aucune règle n'est active.</summary>
        internal static string Mention(string before, string after = "")
        {
            return Monetisation.HasAffiliation(Settings) ? before + Monetisation.Notice + after : "";
        }
        internal static void OpenOffer(string url)
        {
            // Seulement sur clic explicite. Aucun pixel, cookie, profil ou compteur envoyé par ONYX.
            PageBonsPlans.Ouvre(Monetisation.Resolve(Settings, url).Url);
        }
    }
}
