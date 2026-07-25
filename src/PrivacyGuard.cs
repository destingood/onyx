using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace BTOptimizer
{
    /// <summary>
    /// Garde-fou VIE PRIVÉE (recommandation « filtrage PII ») : détecte les données personnelles
    /// (e-mail, téléphone, carte bancaire, IBAN, n° de sécu) dans un texte AVANT qu'il ne parte vers
    /// un service externe (moteur de recherche web). Tout reste local par défaut ; ce garde évite
    /// qu'une info sensible fuite dans une requête web sans que l'utilisateur s'en rende compte.
    ///
    /// Volontairement PRUDENT côté domaine PC : les adresses IP (ex. 192.168.1.1) ne sont PAS
    /// considérées comme PII — elles sont banales dans une question réseau/gaming.
    /// </summary>
    internal static class PrivacyGuard
    {
        private sealed class Pat { public string Label; public Regex Re; }

        private static readonly Pat[] Pats = new[]
        {
            new Pat { Label = "e-mail",
                      Re = new Regex(@"[\w.+-]+@[\w-]+\.[\w.-]{2,}", RegexOptions.Compiled | RegexOptions.IgnoreCase) },
            new Pat { Label = "numéro de téléphone",
                      Re = new Regex(@"(?<!\d)(?:\+33\s?|0)[1-9](?:[ .\-]?\d{2}){4}(?!\d)", RegexOptions.Compiled) },
            new Pat { Label = "carte bancaire",
                      Re = new Regex(@"\b\d{4}[ \-]?\d{4}[ \-]?\d{4}[ \-]?\d{4}\b", RegexOptions.Compiled) },
            new Pat { Label = "IBAN",
                      Re = new Regex(@"\b[A-Z]{2}\d{2}[A-Z0-9]{11,30}\b", RegexOptions.Compiled) },
            new Pat { Label = "numéro de sécurité sociale",
                      Re = new Regex(@"(?<!\d)[12][ ]?\d{2}[ ]?\d{2}[ ]?\d{2}[ ]?\d{3}[ ]?\d{3}[ ]?\d{2}(?!\d)", RegexOptions.Compiled) },
        };

        /// <summary>Liste (en clair) des types de PII repérés dans le texte, ou vide si aucun.</summary>
        internal static List<string> Detect(string text)
        {
            var found = new List<string>();
            if (string.IsNullOrEmpty(text)) return found;
            foreach (var p in Pats)
            {
                try { if (p.Re.IsMatch(text) && !found.Contains(p.Label)) found.Add(p.Label); }
                catch { }
            }
            return found;
        }

        internal static bool HasPII(string text) { return Detect(text).Count > 0; }
    }
}
