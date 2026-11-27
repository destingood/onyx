using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BTOptimizer
{
    /// <summary>
    /// LISTE DE SOUHAITS GOG (logique PURE). GOG n'a pas d'API publique pour elle, mais la page
    /// publique https://www.gog.com/u/&lt;pseudo&gt;/wishlist embarque ses données (« var gogData =
    /// {…} » : page, totalPages, products[].title, isGame). Liste privée ou pseudo inconnu : 404.
    /// Les titres deviennent des suivis Steam (même jeu cherché sur Steam) : les alertes de prix
    /// restent celles de Steam. 100 produits par page ; une page N n'est gardée que si GOG répond
    /// bien « page N » (paramètre ?page= non documenté).
    /// </summary>
    internal static class ListeSouhaitsGog
    {
        public const int PagesMax = 10;

        public sealed class Page
        {
            public int Numero = 1, Pages = 1, Total;
            public readonly List<string> Titres = new List<string>();
        }

        private static readonly Regex DepuisUrl = new Regex(@"gog\.com/(?:[a-z]{2}(?:-[a-z]{2})?/)?u/([^/?#\s]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Valide = new Regex(@"^[A-Za-z0-9_.\-]{2,40}$", RegexOptions.CultureInvariant);
        private static readonly Regex NonJeu = new Regex(@"\bDLC\b|Soundtrack|\bOST\b|Season Pass|Artbook", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Edition = new Regex(
            @"\s*(?:[:\-–—]\s*)?(?:(?:GOTY|Game of the Year|Complete|Ultimate|Deluxe|Digital Deluxe|Definitive|Enhanced|Gold|Premium|Standard)\s+Edition"
            + @"|[ÉE]dition\s+(?:Ultime|Compl[èe]te|Deluxe|D[ée]finitive|Standard|Gold|GOTY|Jeu de l'ann[ée]e)|Director'?s Cut|GOTY)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Pseudo tapé seul ou adresse de profil collée ; null s'il n'a pas la forme d'un pseudo GOG.</summary>
        public static string Pseudo(string saisie)
        {
            if (string.IsNullOrWhiteSpace(saisie)) return null;
            string s = saisie.Trim();
            Match m = DepuisUrl.Match(s);
            if (m.Success) s = Uri.UnescapeDataString(m.Groups[1].Value);
            return Valide.IsMatch(s) ? s : null;
        }

        public static string Url(string pseudo, int page)
        {
            if (string.IsNullOrEmpty(pseudo)) return null;
            return "https://www.gog.com/u/" + Uri.EscapeDataString(pseudo) + "/wishlist"
                + (page > 1 ? "?page=" + page.ToString(CultureInfo.InvariantCulture) : "");
        }

        /// <summary>Objet JSON qui suit « var gogData = », accolades et chaînes comprises ; null s'il manque.</summary>
        public static string Extrait(string html)
        {
            if (string.IsNullOrEmpty(html)) return null;
            int i = html.IndexOf("var gogData", StringComparison.Ordinal);
            if (i < 0) return null;
            i = html.IndexOf('{', i);
            if (i < 0) return null;
            int prof = 0;
            bool chaine = false;
            for (int k = i; k < html.Length; k++)
            {
                char c = html[k];
                if (chaine)
                {
                    if (c == '\\') k++;
                    else if (c == '"') chaine = false;
                    continue;
                }
                if (c == '"') chaine = true;
                else if (c == '{') prof++;
                else if (c == '}' && --prof == 0) return html.Substring(i, k - i + 1);
            }
            return null;
        }

        /// <summary>Titres des JEUX de la page (isGame faux : DLC ou film, écarté) ; null si la page n'a pas de liste lisible.</summary>
        public static Page Lire(string html)
        {
            string json = Extrait(html);
            if (json == null) return null;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    JsonElement r = doc.RootElement, produits;
                    if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("products", out produits) || produits.ValueKind != JsonValueKind.Array) return null;
                    var p = new Page { Numero = Entier(r, "page", 1), Pages = Math.Max(1, Entier(r, "totalPages", 1)), Total = Entier(r, "totalProducts", 0) };
                    var vus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (JsonElement it in produits.EnumerateArray())
                    {
                        JsonElement t, jeu;
                        if (it.ValueKind != JsonValueKind.Object || !it.TryGetProperty("title", out t) || t.ValueKind != JsonValueKind.String) continue;
                        if (it.TryGetProperty("isGame", out jeu) && jeu.ValueKind == JsonValueKind.False) continue;
                        string titre = (t.GetString() ?? "").Trim();
                        if (NonJeu.IsMatch(titre)) continue;   // GOG marque parfois un DLC « isGame »
                        if (titre.Length > 0 && vus.Add(titre)) p.Titres.Add(titre);
                    }
                    return p;
                }
            }
            catch { return null; }
        }

        /// <summary>
        /// Titre à chercher sur Steam si le titre GOG n'y est pas : sans ™ ® ©, sans année entre
        /// parenthèses, sans nom d'édition (« Cyberpunk 2077 : Édition Ultime » → « Cyberpunk 2077 »).
        /// null si rien ne change ou s'il reste moins de 3 caractères.
        /// </summary>
        public static string Simplifie(string titre)
        {
            if (string.IsNullOrWhiteSpace(titre)) return null;
            string s = Regex.Replace(titre, "[™®©]", "");
            s = Regex.Replace(s, @"\s*\(\d{4}\)\s*$", "");
            s = Edition.Replace(s, "").Trim().TrimEnd(':', '-', '–', '—').Trim();
            return s.Length >= 3 && !string.Equals(s, titre.Trim(), StringComparison.OrdinalIgnoreCase) ? s : null;
        }

        /// <summary>« 12 titres lus sur GOG, 9 trouvés sur Steam. » suivi du compte des suivis et des titres introuvables.</summary>
        public static string Compte(int lus, List<string> introuvables, string compteSuivis, bool tronque)
        {
            int n = introuvables == null ? 0 : introuvables.Count;
            string t = "GOG : " + lus.ToString(CultureInfo.InvariantCulture) + (lus > 1 ? " titres lus, " : " titre lu, ")
                + (lus - n).ToString(CultureInfo.InvariantCulture) + " trouvé" + (lus - n > 1 ? "s" : "") + " sur Steam. " + compteSuivis;
            if (n > 0)
            {
                var ex = introuvables.GetRange(0, Math.Min(3, n));
                t += " Absents de Steam, non suivis : " + string.Join(", ", ex) + (n > 3 ? " (+" + (n - 3).ToString(CultureInfo.InvariantCulture) + ")" : "") + ".";
            }
            if (tronque) t += " Liste longue : seules les premières pages ont pu être lues.";
            return t;
        }

        private static int Entier(JsonElement e, string nom, int defaut)
        {
            JsonElement x; int v;
            return e.TryGetProperty(nom, out x) && x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out v) ? v : defaut;
        }
    }
}
