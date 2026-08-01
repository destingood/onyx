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
            // « composition / ingrédients de mon PC / disque / carte mère… » = matériel, pas alimentaire.
            if (Regex.IsMatch(n, "\\b(pc|cpu|gpu|ram|ssd|hdd|ordi|ordinateur|config|systeme|processeur|disque|ecran|ventilateur|alimentation|boitier|clavier|souris)\\b")
                || n.Contains("carte graphique") || n.Contains("carte mere") || n.Contains("carte son") || n.Contains("carte reseau")) return null;
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
            // Dans ce domaine, « espace » = espace DISQUE : on écarte tout contexte stockage.
            if (n.Contains("disque") || n.Contains("libere") || n.Contains("liberer") || n.Contains("stockage")
                || n.Contains("plein") || n.Contains("ssd") || n.Contains("hdd") || Regex.IsMatch(n, "\\b(go|mo|to)\\b")) return false;
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

        // ---- QUALITÉ DE L'AIR ----
        /// <summary>« qualité de l'air », « pollution à Lyon », « particules fines » → vrai (pas « quel temps »).</summary>
        internal static bool IsAir(string s)
        {
            string n = Deacc((s ?? "").ToLowerInvariant());
            return n.Contains("qualite de l'air") || n.Contains("qualite de l air") || n.Contains("qualite d'air")
                || n.Contains("pollution") || n.Contains("air pollue") || n.Contains("indice atmo")
                || n.Contains("particules fines") || n.Contains("pm2.5") || n.Contains("pm10")
                || (n.Contains("air") && n.Contains("respire"));
        }

        // ---- DISTANCE ENTRE 2 VILLES ----
        internal sealed class Pair { public string A; public string B; }
        /// <summary>« distance entre Paris et Lyon », « combien de km de X à Y » → couple de villes, sinon null.</summary>
        internal static Pair DistanceQuery(string q)
        {
            if (string.IsNullOrEmpty(q)) return null;
            string n = Deacc(q.ToLowerInvariant());
            bool trig = n.Contains("distance") || (n.Contains("km") && n.Contains("entre")) || (n.Contains("combien de km"));
            if (!trig) return null;
            var m = Regex.Match(q, "(?i)(?:entre|de)\\s+(.+?)\\s+(?:et|a|à|->|jusqu'?a|jusqu'?à)\\s+(.+?)\\s*[?.!]*$");
            if (!m.Success) return null;
            string a = m.Groups[1].Value.Trim().Trim('«', '»', '"', '\'', ' ', '.');
            string b = m.Groups[2].Value.Trim().Trim('«', '»', '"', '\'', ' ', '.');
            if (a.Length < 2 || b.Length < 2 || a.Length > 30 || b.Length > 30) return null;   // une ville n'est pas une phrase
            string ad = Deacc(a.ToLowerInvariant()), bd = Deacc(b.ToLowerInvariant());
            const string bad = "\\b(ping|lag|rame|wifi|reseau|serveur|latence|fps|internet|connexion|debit)\\b";
            if (Regex.IsMatch(ad, bad) || Regex.IsMatch(bd, bad)) return null;   // « distance entre moi et le serveur… »
            return new Pair { A = a, B = b };
        }

        // ---- LUNE / SÉISMES / ISS ----
        /// <summary>« phase de la lune », « pleine lune » → vrai (pas « mes lunettes »).</summary>
        internal static bool IsMoon(string s)
        {
            string n = Deacc((s ?? "").ToLowerInvariant());
            return n.Contains("phase de la lune") || n.Contains("pleine lune") || Regex.IsMatch(n, "\\blune\\b");
        }
        /// <summary>« derniers séismes », « tremblement de terre » → vrai (pas « secousses » d'un écran).</summary>
        internal static bool IsQuake(string s)
        {
            string n = Deacc((s ?? "").ToLowerInvariant());
            // « secousse » seul est ambigu (écran qui saccade) → on exige un contexte sismique explicite.
            return n.Contains("seisme") || n.Contains("tremblement de terre") || n.Contains("sismique")
                || n.Contains("secousse tellurique") || n.Contains("secousse sismique");
        }
        /// <summary>« où est l'ISS », « station spatiale » → vrai (pas « la suisse »).</summary>
        internal static bool IsIss(string s)
        {
            string n = Deacc((s ?? "").ToLowerInvariant());
            return Regex.IsMatch(n, "\\biss\\b") || n.Contains("station spatiale");
        }

        // ---- JOURS FÉRIÉS D'UN AUTRE PAYS (Nager.Date) ----
        internal sealed class CountryHit { public string Iso; public string Name; }
        private static readonly (string name, string iso, string disp)[] Countries = new (string, string, string)[]
        {
            ("allemagne","DE","Allemagne"), ("espagne","ES","Espagne"), ("italie","IT","Italie"),
            ("royaume-uni","GB","Royaume-Uni"), ("angleterre","GB","Royaume-Uni"), ("etats-unis","US","États-Unis"),
            ("usa","US","États-Unis"), ("belgique","BE","Belgique"), ("suisse","CH","Suisse"),
            ("canada","CA","Canada"), ("portugal","PT","Portugal"), ("pays-bas","NL","Pays-Bas"),
            ("hollande","NL","Pays-Bas"), ("irlande","IE","Irlande"), ("autriche","AT","Autriche"),
            ("pologne","PL","Pologne"), ("suede","SE","Suède"), ("norvege","NO","Norvège"),
            ("danemark","DK","Danemark"), ("finlande","FI","Finlande"), ("grece","GR","Grèce"),
            ("japon","JP","Japon"), ("bresil","BR","Brésil"), ("mexique","MX","Mexique"),
            ("australie","AU","Australie"), ("luxembourg","LU","Luxembourg"), ("croatie","HR","Croatie"),
            ("hongrie","HU","Hongrie"), ("roumanie","RO","Roumanie"), ("slovaquie","SK","Slovaquie"),
            ("ukraine","UA","Ukraine"), ("nouvelle-zelande","NZ","Nouvelle-Zélande"), ("argentine","AR","Argentine"),
            ("france","FR","France")
        };
        /// <summary>Si « férié(s) » + un pays connu est nommé → code ISO2 + nom, sinon null (→ France par défaut).</summary>
        internal static CountryHit HolidayCountryQuery(string s)
        {
            string n = Deacc((s ?? "").ToLowerInvariant());
            if (!n.Contains("ferie")) return null;
            foreach (var c in Countries)
                if (n.Contains(c.name)) return new CountryHit { Iso = c.iso, Name = c.disp };
            return null;
        }

        // ---- CALCULATRICE (locale, hors-ligne) ----
        /// <summary>« 15% de 240 », « racine de 2 », « 3+4*2 », « combien font 12*8 » → résultat, sinon null.</summary>
        internal static string Calc(string q)
        {
            if (string.IsNullOrEmpty(q)) return null;
            string n = Deacc(q.ToLowerInvariant()).Trim();
            // Retire une éventuelle amorce (« calcule », « combien font »…) PUIS n'accepte que si le reste
            // est TOUT le calcul (ancré ^…$). Sinon « ma RAM tourne à 90% de 16 Go » serait pris pour un calcul.
            string body = Regex.Replace(n, "^(?:calcule(?:r)?|combien\\s+(?:font|fait)|ca\\s+fait\\s+combien|ca\\s+fait|quelle?\\s+est|c'?est\\s+quoi|resultat\\s+de|resultat|=)\\s*", "").Trim();
            var mp = Regex.Match(body, "^(\\d+(?:[.,]\\d+)?)\\s*(?:%|pour ?cent)\\s+de\\s+(\\d+(?:[.,]\\d+)?)\\s*[?.!]*$");
            if (mp.Success) { double x = D(mp.Groups[1].Value), y = D(mp.Groups[2].Value); return "🧮 " + FmtN(y * x / 100) + "  (" + FmtN(x) + " % de " + FmtN(y) + ")"; }
            var mr = Regex.Match(body, "^racine(?:\\s+carree)?\\s+de\\s+(\\d+(?:[.,]\\d+)?)\\s*[?.!]*$");
            if (mr.Success) { double x = D(mr.Groups[1].Value); return x < 0 ? "🧮 racine d'un nombre négatif : indéfini dans les réels." : "🧮 racine de " + FmtN(x) + " = " + FmtN(Math.Sqrt(x)); }
            var mw = Regex.Match(body, "^(\\d+(?:[.,]\\d+)?)\\s*(?:\\^|puissance)\\s*(\\d+(?:[.,]\\d+)?)\\s*[?.!]*$");
            if (mw.Success) { double a = D(mw.Groups[1].Value), b = D(mw.Groups[2].Value); return "🧮 " + FmtN(a) + " puissance " + FmtN(b) + " = " + FmtN(Math.Pow(a, b)); }
            string expr = body.Replace(" ", "").Replace(",", ".");
            if (Regex.IsMatch(expr, "^[0-9+\\-*/().]+$") && Regex.IsMatch(expr, "[+\\-*/]"))
            {
                try
                {
                    var e = new Expr(expr);
                    double d = e.Eval();
                    if (!e.Done() || double.IsNaN(d) || double.IsInfinity(d)) return null;
                    return "🧮 " + body.Replace(".", ",").Trim() + " = " + FmtN(d);
                }
                catch { return null; }
            }
            return null;
        }
        private static double D(string s) { double d; double.TryParse(s.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out d); return d; }
        internal static string FmtN(double v)
        {
            if (Math.Abs(v - Math.Round(v)) < 1e-9 && Math.Abs(v) < 1e15) return ((long)Math.Round(v)).ToString(CultureInfo.InvariantCulture);
            return Math.Round(v, 4).ToString("0.####", CultureInfo.InvariantCulture).Replace('.', ',');
        }
        // mini-évaluateur récursif (+ - * / parenthèses), hors-ligne et sûr (pas d'eval système).
        private sealed class Expr
        {
            private readonly string s; private int i;
            public Expr(string t) { s = t; i = 0; }
            public bool Done() { return i >= s.Length; }
            public double Eval() { return E(); }
            private double E() { double v = T(); while (i < s.Length && (s[i] == '+' || s[i] == '-')) { char op = s[i++]; double r = T(); v = op == '+' ? v + r : v - r; } return v; }
            private double T() { double v = F(); while (i < s.Length && (s[i] == '*' || s[i] == '/')) { char op = s[i++]; double r = F(); v = op == '*' ? v * r : v / r; } return v; }
            private double F()
            {
                if (i < s.Length && s[i] == '(') { i++; double v = E(); if (i < s.Length && s[i] == ')') i++; return v; }
                if (i < s.Length && s[i] == '-') { i++; return -F(); }
                if (i < s.Length && s[i] == '+') { i++; return F(); }
                int st = i; while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
                if (i == st) throw new FormatException();
                return double.Parse(s.Substring(st, i - st), CultureInfo.InvariantCulture);
            }
        }

        // ---- DÉFINITION D'UN MOT (rendue via Wikipédia FR) ----
        /// <summary>« définition de X », « que veut dire X », « que signifie X » → le mot, sinon null.</summary>
        internal static string DefineQuery(string q)
        {
            if (string.IsNullOrEmpty(q)) return null;
            var m = Regex.Match(q, "(?i)(?:d[eé]finition\\s+(?:de\\s+la\\s+|de\\s+l['’]|de\\s+|du\\s+|des\\s+|d['’])?|que\\s+veut\\s+dire\\s+|que\\s+signifie\\s+|signification\\s+(?:de\\s+la\\s+|de\\s+l['’]|de\\s+|du\\s+|d['’])?|d[eé]finis\\s+|sens\\s+du\\s+mot\\s+)(.+?)\\s*[?.!]*$");
            if (!m.Success) return null;
            string w = m.Groups[1].Value.Trim().Trim('«', '»', '"', '\'', ' ', '.', '?', '!');
            w = Regex.Replace(w, "^(?i)(?:le|la|les|un|une|l['’]|du|des|de)\\s+", "");
            return w.Length >= 2 ? w : null;
        }

        // ---- ACTUALITÉS (Google Actualités RSS) ----
        /// <summary>« les actualités » → "" (à la une) ; « actu nvidia » → "nvidia" ; sinon null.</summary>
        internal static string NewsQuery(string q)
        {
            if (string.IsNullOrEmpty(q)) return null;
            string n = Deacc(q.ToLowerInvariant());
            bool isNews = Regex.IsMatch(n, "\\bactu(alit[eé]s?)?\\b") || n.Contains("les news")
                || n.Contains("quoi de neuf") || n.Contains("dernieres nouvelles") || n.Contains("derniere nouvelle")
                || n.Contains("infos du jour") || n.Contains("info du jour") || n.Contains("les nouvelles");
            if (!isNews) return null;
            // « quoi de neuf, mon PC rame » : ne pas détourner une plainte technique vers les actus.
            if (n.Contains("rame") || n.Contains("lag") || n.Contains("plante") || n.Contains("crash") || n.Contains("freeze")
                || n.Contains("bug") || n.Contains("saccade") || Regex.IsMatch(n, "\\b(pc|wifi|ping|pilote|driver|disque|ecran|cpu|gpu|ram|fps|latence)\\b")) return null;
            var m = Regex.Match(q, "(?i)(?:actualit[eé]s?|actu|news|nouvelles?|infos?)\\s+(?:sur\\s+|de\\s+|du\\s+|des\\s+|d['’]|concernant\\s+|a\\s+propos\\s+de\\s+)?(.+?)\\s*[?.!]*$");
            if (m.Success)
            {
                string topic = m.Groups[1].Value.Trim().Trim('«', '»', '"', '\'', ' ', '.', '?', '!');
                string td = Deacc(topic.ToLowerInvariant());
                string[] stop = { "jour", "du jour", "aujourd'hui", "aujourdhui", "maintenant", "recente", "recentes",
                                  "recents", "recent", "en france", "france", "monde", "du monde", "importantes", "importante" };
                if (topic.Length >= 2 && Array.IndexOf(stop, td) < 0) return topic;
            }
            return "";   // actualités générales (à la une)
        }

        // ---- JEUX GRATUITS PC (FreeToGame) ----
        private static readonly Dictionary<string, string> GameGenres = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            {"fps","shooter"},{"tir","shooter"},{"shooter","shooter"},{"tps","third-person"},
            {"mmorpg","mmorpg"},{"mmofps","mmofps"},{"mmo","mmo"},{"moba","moba"},
            {"battle royale","battle-royale"},{"battle-royale","battle-royale"},{"br","battle-royale"},
            {"strategie","strategy"},{"strategy","strategy"},{"rts","strategy"},
            {"course","racing"},{"racing","racing"},{"sport","sports"},{"sports","sports"},
            {"combat","fighting"},{"baston","fighting"},{"fighting","fighting"},
            {"carte","card"},{"cartes","card"},{"card","card"},{"horreur","horror"},{"horror","horror"},
            {"survie","survival"},{"survival","survival"},{"zombie","zombie"},{"zombies","zombie"},
            {"anime","anime"},{"manga","anime"},{"action","action"},{"tower defense","tower-defense"},
            {"action rpg","action-rpg"},{"arpg","action-rpg"},{"aventure","open-world"},{"espace","space"}
        };
        /// <summary>« jeux gratuits », « jeux gratuits fps » → genre FreeToGame ou "" ; sinon null (jamais sur une panne).</summary>
        internal static string FreeGamesQuery(string q)
        {
            if (string.IsNullOrEmpty(q)) return null;
            string n = Deacc(q.ToLowerInvariant());
            bool wantGames = n.Contains("jeux gratuit") || n.Contains("free to play") || n.Contains("free-to-play")
                || Regex.IsMatch(n, "\\bf2p\\b") || n.Contains("jeux free")
                || (n.Contains("jeu") && n.Contains("gratuit") && (n.Contains("montre") || n.Contains("liste")
                    || n.Contains("propose") || n.Contains("recommande") || n.Contains("quel") || n.Contains("des jeux") || n.Contains("un jeu")));
            if (!wantGames) return null;
            // ne JAMAIS détourner une plainte technique vers une liste de jeux.
            if (n.Contains("rame") || n.Contains("lag") || n.Contains("plante") || n.Contains("crash")
                || n.Contains("freeze") || n.Contains("saccade") || n.Contains("marche pas") || n.Contains("bug")) return null;
            foreach (var kv in GameGenres)
                if (Regex.IsMatch(n, "\\b" + Regex.Escape(kv.Key) + "\\b")) return kv.Value;
            return "";
        }

        // ---- LIVRES (Open Library) ----
        /// <summary>« livre harry potter », « un roman de Tolkien » → requête livre, sinon null
        /// (pas « livre sterling » = monnaie, ni l'intérieur de « délivre »).</summary>
        internal static string BookQuery(string q)
        {
            if (string.IsNullOrEmpty(q)) return null;
            string n = Deacc(q.ToLowerInvariant());
            // « livre sterling / turque… » = monnaie ; « livre » + euro/dollar/taux = change, pas un bouquin.
            if (n.Contains("sterling") || n.Contains("livre turque") || n.Contains("livre egyptienne")
                || n.Contains("livre libanaise") || n.Contains("livre syrienne")
                || (n.Contains("livre") && (n.Contains("euro") || n.Contains("dollar") || n.Contains("cours de la livre") || n.Contains("taux") || n.Contains("change")))) return null;
            // \b devant le déclencheur : « délivre », « livrer », « livraison » ne matchent pas.
            var m = Regex.Match(q, "(?i)\\b(?:livres?|bouquins?|romans?)\\s+(?:sur\\s+|de\\s+|intitul[eé]\\s+|qui\\s+parle\\s+de\\s+)?(.+?)\\s*[?.!]*$");
            if (!m.Success) return null;
            string w = m.Groups[1].Value.Trim().Trim('«', '»', '"', '\'', ' ', '.', '?', '!');
            return w.Length >= 2 ? w : null;
        }

        // ---- POKÉMON ----
        /// <summary>« pokémon pikachu », « c'est quoi le pokemon mew » → nom (anglais), sinon null.</summary>
        internal static string PokemonQuery(string q)
        {
            if (string.IsNullOrEmpty(q)) return null;
            var m = Regex.Match(q, "(?i)pok[eé]mon\\s+(?:numero\\s+|n°\\s*)?([\\p{L}0-9\\-']{2,20})");
            if (!m.Success) return null;
            string w = m.Groups[1].Value.Trim().Trim('\'', '-');
            string wd = Deacc(w.ToLowerInvariant());
            if (w.Length < 2 || wd == "est" || wd == "le" || wd == "la" || wd == "un" || wd == "une" || wd == "quoi") return null;
            return w;
        }

        // ---- SÉRIES TV ----
        /// <summary>« série breaking bad », « la série the office » → titre, sinon null (pas « numéro de série »
        /// ni « série DE problèmes »).</summary>
        internal static string ShowQuery(string q)
        {
            if (string.IsNullOrEmpty(q)) return null;
            string n = Deacc(q.ToLowerInvariant());
            if (n.Contains("numero de serie") || n.Contains("cle de serie") || n.Contains("clef de serie")
                || n.Contains("serial") || n.Contains("port serie") || n.Contains("numero serie")
                || n.Contains("mise en serie") || n.Contains("en serie")) return null;
            // « série » = gamme/génération de matériel (carte série RTX, processeur série Ryzen…) → pas une série TV.
            if (n.Contains("carte graphique") || n.Contains("carte mere")
                || Regex.IsMatch(n, "\\b(rtx|gtx|radeon|ryzen|geforce|nvidia|amd|intel|ssd|processeur|gpu|cpu)\\b")) return null;
            // \b devant « série » (évite d'attraper l'intérieur d'un mot) ; « de/des/du/d' » retirés du préfixe.
            var m = Regex.Match(q, "(?i)\\b(?:s[eé]rie(?:\\s+t[eé]l[eé])?|tv\\s*show)\\s+(?:sur\\s+|intitul[eé]e?\\s+)?(.+?)\\s*[?.!]*$");
            if (!m.Success) return null;
            string w = m.Groups[1].Value.Trim().Trim('«', '»', '"', '\'', ' ', '.', '?', '!');
            string wd = Deacc(w.ToLowerInvariant());
            // « série de problèmes / soucis / bugs » n'est pas un titre de série.
            if (wd.StartsWith("de ") || wd.StartsWith("des ") || wd.StartsWith("du ") || wd.StartsWith("d'") || wd.StartsWith("d’") || wd == "tv") return null;
            // capture porteuse d'un symptôme technique → ce n'est pas un titre de série.
            if (Regex.IsMatch(wd, "\\b(chauffe|plante|rame|lag|crash|freeze|bug|saccade|bloque|marche pas)\\b")) return null;
            return w.Length >= 2 ? w : null;
        }

        // ---- PRIX NOBEL ----
        internal sealed class NobelHit { public string Cat; public string CatFr; public string Year; }
        /// <summary>« prix nobel de physique 2023 », « nobel de la paix » → catégorie (+année option), sinon null.</summary>
        internal static NobelHit NobelQuery(string s)
        {
            string n = Deacc((s ?? "").ToLowerInvariant());
            if (!n.Contains("nobel")) return null;
            string cat = null, catFr = null;
            if (n.Contains("physique")) { cat = "phy"; catFr = "physique"; }
            else if (n.Contains("chimie")) { cat = "che"; catFr = "chimie"; }
            else if (n.Contains("medecine") || n.Contains("physiologie")) { cat = "med"; catFr = "médecine"; }
            else if (n.Contains("litterature")) { cat = "lit"; catFr = "littérature"; }
            else if (n.Contains("paix")) { cat = "pea"; catFr = "la paix"; }
            else if (n.Contains("economie")) { cat = "eco"; catFr = "économie"; }
            else return null;
            var my = Regex.Match(n, "\\b(?:19|20)\\d{2}\\b");
            return new NobelHit { Cat = cat, CatFr = catFr, Year = my.Success ? my.Value : null };
        }

        // ---- BILAN MISES À JOUR (diagnostic local, puis proposition — ou non) ----
        /// <summary>« mes pilotes sont à jour ? », « installe les mises à jour », « bilan maj » → vrai.</summary>
        internal static bool IsUpdateCheck(string s)
        {
            string n = Deacc((s ?? "").ToLowerInvariant());
            // MàJ de la mémoire/base du Copilote = autre sujet.
            if (n.Contains("memoire") || n.Contains("connaissance")) return false;
            if (n.Contains("mise a jour") || n.Contains("mises a jour") || n.Contains("mettre a jour")
                || n.Contains("mets a jour") || Regex.IsMatch(n, "\\bmaj\\b") || Regex.IsMatch(n, "\\bupdates?\\b")) return true;
            return n.Contains("a jour") && Regex.IsMatch(n, "\\b(pilote|pilotes|driver|drivers|windows|gpu)\\b");
        }

        /// <summary>Nombre d'applications à mettre à jour d'après la sortie de « winget upgrade ».
        /// Robuste FR/EN : pied « N mises à niveau disponibles » / « N upgrades available », sinon
        /// comptage des lignes du tableau après le séparateur « --- ». −1 si sortie illisible.</summary>
        internal static int WingetCount(string output)
        {
            if (string.IsNullOrWhiteSpace(output)) return -1;
            string flat = Deacc(output.ToLowerInvariant());
            var mf = Regex.Match(flat, "(\\d+)\\s+(?:mises?\\s+a\\s+niveau\\s+disponibles?|upgrades?\\s+available)");
            if (mf.Success) { int c; if (int.TryParse(mf.Groups[1].Value, out c)) return c; }
            var lines = output.Replace("\r", "").Split('\n');
            int sep = -1;
            for (int i = 0; i < lines.Length; i++)
                if (Regex.IsMatch(lines[i], "^-{20,}\\s*$")) { sep = i; break; }
            if (sep < 0) return -1;
            int n = 0;
            for (int i = sep + 1; i < lines.Length; i++)
            {
                string l = lines[i].TrimEnd();
                if (l.Length == 0) break;                                   // fin du tableau
                if (Regex.IsMatch(Deacc(l.ToLowerInvariant()), "disponibles?|available")) break;   // pied
                if (Regex.IsMatch(l, "\\S+\\s{2,}\\S+")) n++;               // au moins 2 colonnes
            }
            return n;
        }

        /// <summary>Marque du GPU d'après son nom WMI → « nvidia » / « amd » / « intel » / null.</summary>
        internal static string GpuVendor(string name)
        {
            string n = Deacc((name ?? "").ToLowerInvariant());
            if (n.Contains("nvidia") || n.Contains("geforce") || Regex.IsMatch(n, "\\b(rtx|gtx|quadro)\\b")) return "nvidia";
            if (n.Contains("amd") || n.Contains("radeon")) return "amd";
            if (n.Contains("intel") || Regex.IsMatch(n, "\\b(arc|iris|uhd)\\b")) return "intel";
            return null;
        }

        // ---- BATTERIE / UPTIME (fonctions locales instantanées) ----
        /// <summary>« il me reste combien de batterie », « niveau de batterie » → vrai.</summary>
        internal static bool IsBattery(string s)
        {
            string n = Deacc((s ?? "").ToLowerInvariant());
            return n.Contains("batterie") || n.Contains("sur secteur");
        }

        /// <summary>« depuis quand mon PC tourne », « uptime » → vrai (pas « depuis quand tu existes »).</summary>
        internal static bool IsUptime(string s)
        {
            string n = Deacc((s ?? "").ToLowerInvariant());
            if (Regex.IsMatch(n, "\\buptime\\b")) return true;
            bool ask = n.Contains("depuis quand") || n.Contains("depuis combien de temps") || n.Contains("ca fait combien de temps");
            bool subj = n.Contains("tourne") || n.Contains("allume") || n.Contains("demarre") || n.Contains("redemarre")
                || Regex.IsMatch(n, "\\b(pc|ordi|ordinateur|machine)\\b");
            return ask && subj;
        }

        // ---- LIBÉRER DE LA PLACE (hibernation / nettoyage profond DISM) ----
        /// <summary>« désactive l'hibernation », « supprime la veille prolongée » → vrai.</summary>
        internal static bool IsHibernateOff(string s)
        {
            string n = Deacc((s ?? "").ToLowerInvariant());
            bool subj = n.Contains("hibernation") || n.Contains("veille prolongee") || n.Contains("hiberfil");
            if (!subj) return false;
            return n.Contains("desactive") || n.Contains("coupe") || n.Contains("enleve") || n.Contains("supprime")
                || n.Contains("vire") || n.Contains("libere") || Regex.IsMatch(n, "\\boff\\b");
        }

        /// <summary>« réactive l'hibernation », « remets la veille prolongée » → vrai (pas « désactive »).</summary>
        internal static bool IsHibernateOn(string s)
        {
            string n = Deacc((s ?? "").ToLowerInvariant());
            bool subj = n.Contains("hibernation") || n.Contains("veille prolongee") || n.Contains("hiberfil");
            if (!subj || IsHibernateOff(s)) return false;   // « désactive » contient « active » → tester Off d'abord
            return n.Contains("reactive") || n.Contains("remet") || n.Contains("retablis") || n.Contains("active")
                || Regex.IsMatch(n, "\\bon\\b");
        }

        /// <summary>« nettoyage profond », « winsxs », « dism » → vrai (pas « nettoie mon pc » = nettoyage normal).</summary>
        internal static bool IsDeepClean(string s)
        {
            string n = Deacc((s ?? "").ToLowerInvariant());
            return n.Contains("nettoyage profond") || n.Contains("winsxs") || Regex.IsMatch(n, "\\bdism\\b")
                || (n.Contains("composants") && n.Contains("windows"));
        }

        /// <summary>La sortie de « Dism /AnalyzeComponentStore » recommande-t-elle un nettoyage ? (FR/EN)</summary>
        internal static bool DismRecommended(string output)
        {
            if (string.IsNullOrEmpty(output)) return false;
            string n = Deacc(output.ToLowerInvariant());
            return Regex.IsMatch(n, "(recommande|recommended)\\s*:\\s*(oui|yes)");
        }

        // ---- SANTÉ DISQUES / JOURNAL / GARDIEN ----
        /// <summary>« état de mes disques », « mon ssd est en bonne santé ? », « smart » → vrai.</summary>
        internal static bool IsDiskHealth(string s)
        {
            string n = Deacc((s ?? "").ToLowerInvariant());
            bool disk = Regex.IsMatch(n, "\\b(ssd|disque|disques|hdd|nvme)\\b");
            bool health = n.Contains("sante") || n.Contains("etat") || Regex.IsMatch(n, "\\bsmart\\b")
                || n.Contains("va mourir") || n.Contains("mort") || n.Contains("fatigue") || n.Contains("usure");
            return (disk && health) || n.Contains("smart de mes disques") || n.Contains("sante disque");
        }

        /// <summary>« qu'est-ce que tu as changé ? », « journal de bord », « historique des actions » → vrai.</summary>
        internal static bool IsJournal(string s)
        {
            string n = Deacc((s ?? "").ToLowerInvariant());
            return n.Contains("journal") || n.Contains("historique des actions") || n.Contains("historique des changements")
                || (n.Contains("qu'est ce que tu as change") || n.Contains("qu est ce que tu as change")
                    || n.Contains("qu'as tu change") || n.Contains("qu as tu change") || n.Contains("tu as change quoi"));
        }

        /// <summary>« gardien », « alertes », « tout va bien ? » → vrai.</summary>
        internal static bool IsGuardian(string s)
        {
            string n = Deacc((s ?? "").ToLowerInvariant());
            return n.Contains("gardien") || Regex.IsMatch(n, "\\balertes?\\b") || n.Contains("tout va bien");
        }

        // ---- PROFIL ONYX (export / restauration) ----
        /// <summary>« exporte mon profil », « sauvegarde mes réglages ONYX » → vrai.</summary>
        internal static bool IsExportProfile(string s)
        {
            string n = Deacc((s ?? "").ToLowerInvariant());
            return (n.Contains("exporte") || n.Contains("export") || n.Contains("sauvegarde"))
                && (n.Contains("profil") || n.Contains("reglages onyx") || n.Contains("mes reglages"));
        }

        /// <summary>« importe mon profil », « restaure mon profil » → vrai (pas « point de restauration »).</summary>
        internal static bool IsImportProfile(string s)
        {
            string n = Deacc((s ?? "").ToLowerInvariant());
            return (n.Contains("importe") || n.Contains("import") || n.Contains("restaure")) && n.Contains("profil");
        }

        // ---- TENDANCE SANTÉ ----
        /// <summary>« score de santé », « tendance », « évolution de mon pc » → vrai.</summary>
        internal static bool IsHealthTrend(string s)
        {
            string n = Deacc((s ?? "").ToLowerInvariant());
            return n.Contains("score de sante") || n.Contains("tendance") || n.Contains("evolution de mon pc")
                || n.Contains("historique de sante") || n.Contains("historique sante");
        }

        // ---- « ÇA MARCHAIT HIER » (diff d'état système) ----
        /// <summary>« qu'est-ce qui a changé sur mon PC », « ça marchait hier » → vrai
        /// (PAS « qu'est-ce que TU as changé » = journal des actions d'ONYX).</summary>
        internal static bool IsWhatChanged(string s)
        {
            string n = Deacc((s ?? "").ToLowerInvariant());
            if (n.Contains("tu as") || n.Contains("t'as change") || n.Contains("t as change")) return false;   // → journal
            if (n.Contains("marchait hier") || n.Contains("marchait avant") || n.Contains("fonctionnait hier")
                || n.Contains("fonctionnait avant") || n.Contains("marchait tres bien")) return true;
            return (n.Contains("qui a change") || n.Contains("quoi a change") || n.Contains("qu'est ce qui a change")
                || n.Contains("qu est ce qui a change")) && Regex.IsMatch(n, "\\b(pc|ordi|ordinateur|systeme|windows)\\b");
        }

        // ---- GOULOT D'ÉTRANGLEMENT CPU / GPU ----
        /// <summary>« c'est mon cpu ou mon gpu qui limite », « bottleneck », « goulot » → vrai.</summary>
        internal static bool IsBottleneck(string s)
        {
            string n = Deacc((s ?? "").ToLowerInvariant());
            if (n.Contains("bottleneck") || n.Contains("goulot")) return true;
            bool both = Regex.IsMatch(n, "\\bcpu\\b") && Regex.IsMatch(n, "\\bgpu\\b");
            bool limit = n.Contains("limite") || n.Contains("bride") || n.Contains("brid")
                || n.Contains("qui bloque") || n.Contains("le plus faible") || n.Contains("maillon");
            return both && limit;
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
