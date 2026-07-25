using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BTOptimizer
{
    /// <summary>
    /// Petits utilitaires « pour plein de choses » (idées piochées dans public-apis/public-apis) :
    ///  - conversions d'UNITÉS en LOCAL (déterministe, hors-ligne, zéro hallucination) ;
    ///  - détection DEVISES / CRYPTO / JOURS FÉRIÉS (les appels réseau sont dans LiveData).
    /// </summary>
    internal static class UtilityTools
    {
        internal sealed class Triple { public double Amount; public string A; public string B; }

        // « 100 km en miles », « 20°c en f », « combien fait 50 kg en livres »
        private static readonly Regex Rx = new Regex(
            @"(\d+(?:[.,]\d+)?)\s*([a-z°/²]{1,14})\s+(?:en|vers|to|->|=)\s+([a-z°/²]{1,14})",
            RegexOptions.Compiled);

        internal static Triple Parse3(string q)
        {
            if (string.IsNullOrEmpty(q)) return null;
            var m = Rx.Match(Deacc(q.ToLowerInvariant()));
            if (!m.Success) return null;
            double amt;
            if (!double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out amt)) return null;
            return new Triple { Amount = amt, A = m.Groups[2].Value, B = m.Groups[3].Value };
        }

        // ---- UNITÉS (local, exact) ----
        // catégorie + facteur vers l'unité de base de la catégorie.
        private static readonly Dictionary<string, KeyValuePair<string, double>> Lin = Build();
        private static Dictionary<string, KeyValuePair<string, double>> Build()
        {
            var d = new Dictionary<string, KeyValuePair<string, double>>(StringComparer.Ordinal);
            void L(string cat, double f, params string[] ks) { foreach (var k in ks) d[k] = new KeyValuePair<string, double>(cat, f); }
            // longueur (base = m)
            L("len", 1000, "km", "kilometre", "kilometres"); L("len", 1, "m", "metre", "metres");
            L("len", 0.01, "cm", "centimetre", "centimetres"); L("len", 0.001, "mm", "millimetre", "millimetres");
            L("len", 1609.344, "mile", "miles", "mi"); L("len", 0.3048, "pied", "pieds", "ft", "feet");
            L("len", 0.0254, "pouce", "pouces", "inch", "in"); L("len", 0.9144, "yard", "yards");
            // masse (base = kg)
            L("mass", 1, "kg", "kilo", "kilos", "kilogramme", "kilogrammes"); L("mass", 0.001, "g", "gramme", "grammes");
            L("mass", 1000, "tonne", "tonnes", "t"); L("mass", 0.453592, "livre", "livres", "lb", "lbs", "pound", "pounds");
            L("mass", 0.0283495, "once", "onces", "oz");
            // vitesse (base = km/h)
            L("speed", 1, "kmh", "km/h"); L("speed", 1.609344, "mph"); L("speed", 3.6, "ms", "m/s", "mps");
            L("speed", 1.852, "noeud", "noeuds", "kt");
            // volume (base = L)
            L("vol", 1, "l", "litre", "litres"); L("vol", 0.001, "ml"); L("vol", 0.01, "cl"); L("vol", 0.1, "dl");
            L("vol", 3.78541, "gal", "gallon", "gallons");
            return d;
        }

        private static string TempKind(string u)
        {
            switch (u)
            {
                case "c": case "°c": case "celsius": case "degre": case "degres": return "C";
                case "f": case "°f": case "fahrenheit": return "F";
                case "k": case "°k": case "kelvin": return "K";
                default: return null;
            }
        }
        private static double ToC(double v, string k) { return k == "F" ? (v - 32) * 5 / 9 : k == "K" ? v - 273.15 : v; }
        private static double FromC(double c, string k) { return k == "F" ? c * 9 / 5 + 32 : k == "K" ? c + 273.15 : c; }

        internal static double? ConvertUnit(double v, string a, string b)
        {
            string ta = TempKind(a), tb = TempKind(b);
            if (ta != null && tb != null) return FromC(ToC(v, ta), tb);
            if (ta != null || tb != null) return null;   // température vs autre : incompatible
            KeyValuePair<string, double> fa, fb;
            if (!Lin.TryGetValue(a, out fa) || !Lin.TryGetValue(b, out fb)) return null;
            if (fa.Key != fb.Key) return null;            // catégories différentes
            return v * fa.Value / fb.Value;
        }

        /// <summary>Conversion d'unités LOCALE (« 100 km en miles »), ou null si non applicable.</summary>
        internal static string LocalUnit(string q)
        {
            var t = Parse3(q);
            if (t == null) return null;
            double? r = ConvertUnit(t.Amount, t.A, t.B);
            if (r == null) return null;
            return "🔢 " + Fmt(t.Amount) + " " + t.A + " = " + Fmt(r.Value) + " " + t.B + ". (calcul local, exact)";
        }

        // ---- DEVISES ----
        private static readonly Dictionary<string, string> Cur = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            {"euro","EUR"},{"euros","EUR"},{"eur","EUR"},{"€","EUR"},
            {"dollar","USD"},{"dollars","USD"},{"usd","USD"},{"$","USD"},
            {"livre","GBP"},{"livres","GBP"},{"gbp","GBP"},{"sterling","GBP"},{"£","GBP"},
            {"yen","JPY"},{"yens","JPY"},{"jpy","JPY"},{"franc","CHF"},{"francs","CHF"},{"chf","CHF"},
            {"cad","CAD"},{"aud","AUD"},{"yuan","CNY"},{"cny","CNY"},{"roupie","INR"},{"inr","INR"},
            {"real","BRL"},{"brl","BRL"},{"rouble","RUB"},{"rub","RUB"}
        };

        /// <summary>Si la question est « montant devise en devise » → codes ISO + montant, sinon null.</summary>
        internal static Triple Currency(string q)
        {
            var t = Parse3(q);
            if (t == null) return null;
            string a, b;
            if (!Cur.TryGetValue(t.A, out a) || !Cur.TryGetValue(t.B, out b)) return null;
            return new Triple { Amount = t.Amount, A = a, B = b };
        }

        // ---- JOURS FÉRIÉS ----
        internal static bool IsHoliday(string s)
        {
            string n = Deacc((s ?? "").ToLowerInvariant());
            return n.Contains("ferie") || n.Contains("feries");   // « férié(s) » désaccentué
        }

        // ---- CRYPTO ----
        private static readonly Dictionary<string, string> Coins = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            {"bitcoin","bitcoin"},{"btc","bitcoin"},{"ethereum","ethereum"},{"eth","ethereum"},
            {"dogecoin","dogecoin"},{"doge","dogecoin"},{"solana","solana"},{"sol","solana"},
            {"cardano","cardano"},{"ada","cardano"},{"litecoin","litecoin"},{"ltc","litecoin"},
            {"ripple","ripple"},{"xrp","ripple"},{"bnb","binancecoin"},{"binance","binancecoin"}
        };

        /// <summary>Id CoinGecko si la question demande le PRIX d'une crypto connue, sinon null.</summary>
        internal static string CryptoId(string s)
        {
            string n = Deacc(s.ToLowerInvariant());
            bool price = n.Contains("prix") || n.Contains("cours") || n.Contains("vaut") || n.Contains("coute") || n.Contains("combien");
            if (!price) return null;
            foreach (var kv in Coins)
                if (Regex.IsMatch(n, "\\b" + Regex.Escape(kv.Key) + "\\b")) return kv.Value;
            return null;
        }

        // ---- TRADUCTION ----
        internal sealed class TransJob { public string Text; public string From; public string To; }
        private static readonly Dictionary<string, string> Langs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            {"anglais","en"},{"english","en"},{"francais","fr"},{"espagnol","es"},{"allemand","de"},
            {"italien","it"},{"portugais","pt"},{"neerlandais","nl"},{"russe","ru"},{"japonais","ja"},
            {"chinois","zh"},{"coreen","ko"},{"arabe","ar"},{"turc","tr"},{"polonais","pl"},
            {"suedois","sv"},{"grec","el"},{"hindi","hi"},{"latin","la"}
        };

        /// <summary>« traduis <texte> en <langue> » → texte + langues, sinon null.</summary>
        internal static TransJob TranslateJob(string q)
        {
            if (string.IsNullOrEmpty(q)) return null;
            var m = Regex.Match(q, "(?i)\\btradui(?:s|t|re)?\\b\\s+(?:moi\\s+)?(.+?)\\s+en\\s+([A-Za-zÀ-ÿ]+)");
            if (!m.Success) m = Regex.Match(q, "(?i)\\btraduction\\s+(?:de\\s+)?(.+?)\\s+en\\s+([A-Za-zÀ-ÿ]+)");
            if (!m.Success) return null;
            string text = m.Groups[1].Value.Trim().Trim('«', '»', '"', '\'', ' ');
            if (text.Length < 1) return null;
            string to;
            if (!Langs.TryGetValue(Deacc(m.Groups[2].Value.ToLowerInvariant()), out to)) return null;
            string from = (to == "fr") ? "en" : "fr";   // heuristique : sinon on part du français
            return new TransJob { Text = text, From = from, To = to };
        }

        // ---- MON IP / SOLEIL ----
        internal static bool IsMyIp(string s)
        {
            string n = Deacc((s ?? "").ToLowerInvariant());
            return n.Contains("mon ip") || n.Contains("mon adresse ip") || n.Contains("quelle est mon ip")
                || n.Contains("mon adresse i.p") || n.Contains("adresse ip publique");
        }
        internal static bool IsSun(string s)
        {
            string n = Deacc((s ?? "").ToLowerInvariant());
            return n.Contains("coucher du soleil") || n.Contains("lever du soleil")
                || n.Contains("soleil se couche") || n.Contains("soleil se leve")
                || (n.Contains("soleil") && (n.Contains("heure") || n.Contains("couche") || n.Contains("leve")));
        }

        // ---- PRODUIT / NUTRITION (Open Food Facts) ----
        /// <summary>« nutriscore du nutella », « calories du coca », « composition du X » → nom du produit, sinon null.</summary>
        internal static string FoodQuery(string q)
        {
            if (string.IsNullOrEmpty(q)) return null;
            string n = Deacc(q.ToLowerInvariant());
            bool food = n.Contains("nutriscore") || n.Contains("nutri-score") || n.Contains("nutri score")
                || n.Contains("open food facts") || n.Contains("openfoodfacts") || n.Contains("additif")
                || n.Contains("composition") || n.Contains("nutritionnel") || n.Contains("calorie")
                || n.Contains("ingredient") || Regex.IsMatch(n, "\\bnova\\b");
            if (!food) return null;
            // Produit = ce qui suit une préposition, en fin de phrase.
            var m = Regex.Match(q, "(?i)(?:de\\s+la\\s+|de\\s+l['’]|du\\s+|des\\s+|de\\s+|d['’]|dans\\s+(?:le|la|les|l['’])\\s*|sur\\s+|pour\\s+|produit\\s+)([\\p{L}0-9][\\p{L}0-9 '’&.\\-]{1,40})\\s*[?.!]*$");
            if (!m.Success) return null;
            string prod = m.Groups[1].Value.Trim().Trim('?', '.', '!', ' ');
            if (prod.Length < 2) return null;
            string pd = Deacc(prod.ToLowerInvariant());
            // rejette le cas où on n'a capté qu'un mot déclencheur (« c'est quoi le nutriscore »).
            if (pd == "calories" || pd == "composition" || pd == "nutriscore" || pd == "nova"
                || pd == "additifs" || pd == "ingredients" || pd == "nutrition") return null;
            return prod;
        }

        // ---- CODE POSTAL → VILLE (Zippopotam) ----
        /// <summary>« code postal 75001 », « quelle ville pour 69001 », ou « 16000 » seul → code à 5 chiffres, sinon null.</summary>
        internal static string PostalQuery(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            string n = Deacc(s.ToLowerInvariant());
            var m = Regex.Match(s, "\\b(\\d{5})\\b");
            if (!m.Success) return null;
            bool ctx = n.Contains("code postal") || n.Contains("quelle ville") || n.Contains("quelle commune")
                || n.Contains("ville de") || n.Contains("ville du") || n.Contains("commune")
                || n.Contains("c'est ou") || n.Contains("c est ou") || n.Contains("ou se trouve")
                || Regex.IsMatch(n.Trim(), "^\\d{5}$");
            return ctx ? m.Groups[1].Value : null;
        }

        // ---- PHOTO ASTRO DU JOUR (NASA APOD) ----
        /// <summary>« photo du jour de la nasa », « image astro », « apod » → vrai (mais pas « c'est quoi la nasa »).</summary>
        internal static bool IsApod(string s)
        {
            string n = Deacc((s ?? "").ToLowerInvariant());
            bool wantPhoto = n.Contains("photo") || n.Contains("image") || n.Contains("cliche");
            return n.Contains("apod")
                || (n.Contains("nasa") && (wantPhoto || n.Contains("du jour")))
                || (wantPhoto && (n.Contains("astro") || n.Contains("espace") || n.Contains("cosmos")));
        }

        // ---- AVIONS EN VOL (OpenSky) ----
        /// <summary>« combien d'avions au-dessus de moi », « avions dans le ciel » → vrai (pas « mode avion »).</summary>
        internal static bool IsFlights(string s)
        {
            string n = Deacc((s ?? "").ToLowerInvariant());
            if (n.Contains("mode avion")) return false;   // réglage Windows, pas le trafic aérien
            return n.Contains("avion") || n.Contains("trafic aerien") || n.Contains("aerien")
                || (n.Contains("vol") && n.Contains("ciel"));
        }

        // ---- DETTE PUBLIQUE DE LA FRANCE ----
        /// <summary>« quelle est la dette de la France », « dette publique » → vrai (pas « j'ai des dettes »).</summary>
        internal static bool IsDebt(string s)
        {
            string n = Deacc((s ?? "").ToLowerInvariant());
            if (!n.Contains("dette")) return false;
            return n.Contains("france") || n.Contains("francaise") || n.Contains("publique")
                || n.Contains("nationale") || n.Contains("pays") || n.Contains("etat") || n.Contains("pib");
        }

        // ---- helpers ----
        internal static string Fmt(double v)
        {
            double r = Math.Round(v, 4);
            string s = r.ToString("0.####", CultureInfo.InvariantCulture);
            return s.Replace('.', ',');
        }

        private static string Deacc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            try
            {
                string f = s.Normalize(NormalizationForm.FormD);
                var sb = new StringBuilder(f.Length);
                foreach (char c in f)
                    if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                        sb.Append(c);
                return sb.ToString();
            }
            catch { return s; }
        }
    }
}
