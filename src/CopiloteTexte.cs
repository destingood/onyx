using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BTOptimizer
{
    /// <summary>Compréhension du texte du Copilote (pur, testé) : normalisation, langage texto,
    /// correspondance tolérante aux fautes, oui / non courts.</summary>
    internal static class CopiloteTexte
    {
        /// <summary>Texte prêt pour le routage : minuscules, accents à plat, texto développé.</summary>
        public static string Prepare(string q) { return Expand(Norm(q)); }

        // --- Correspondance TOLÉRANTE AUX FAUTES DE FRAPPE --------------------------------------
        //  D'abord la sous-chaîne exacte (comme avant), sinon mot à mot avec une distance
        //  d'édition bornée : « ecrqn » → « ecran », « grqtuit » → « gratuit », « conexion » →
        //  « connexion ». Mots courts (< 5 lettres) : exact seulement, pour éviter les contresens.
        public static bool Has(string s, params string[] ks)
        {
            string[] w = null;
            foreach (var k in ks)
            {
                if (k.IndexOf(' ') >= 0)
                {
                    // Phrase : sous-chaîne exacte, sinon tous les mots présents (ordre libre).
                    if (s.Contains(k)) return true;
                    if (w == null) w = SplitWords(s);
                    if (FuzzyKey(w, k)) return true;
                    continue;
                }
                // Mot simple : JAMAIS de sous-chaîne globale — « blague » contiendrait « lag ».
                if (w == null) w = SplitWords(s);
                if (FuzzyWord(w, k)) return true;
            }
            return false;
        }

        public static string[] SplitWords(string s)
        {
            return s.Split(new[] { ' ', '\'', '-', ',', '.', ';', ':', '!', '?', '(', ')', '"', '/', '\n', '\r', '\t' },
                           StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>Clé simple → un mot du message ressemble ; clé à plusieurs mots → chaque mot
        /// de la clé est présent (peu importe l'ordre : « plein disque » vaut « disque plein »).</summary>
        public static bool FuzzyKey(string[] words, string key)
        {
            if (key.IndexOf(' ') < 0) return FuzzyWord(words, key);
            foreach (var part in key.Split(' '))
                if (part.Length > 0 && !FuzzyWord(words, part)) return false;
            return true;
        }

        public static bool FuzzyWord(string[] words, string k)
        {
            int tol = k.Length >= 8 ? 2 : k.Length >= 5 ? 1 : 0;
            foreach (var w in words)
            {
                if (w == k) return true;
                // Sous-chaîne INTERNE réservée aux clés longues (« surchauffe » ⊃ « chauffe ») :
                // sur les clés courtes elle créait des contresens (« blague » ⊃ « lag »).
                if (k.Length >= 5 && w.Length > k.Length && w.Contains(k)) return true;
                if (k.Length >= 3 && k.Length <= 4 && w.Length > k.Length && w.StartsWith(k, StringComparison.Ordinal)) return true; // « lags », « dlls »
                if (UnitHit(w, k)) return true;                                       // « 60hz », « 144fps », « 1000hz »
                if (tol > 0 && Math.Abs(w.Length - k.Length) <= tol && Lev(w, k, tol) <= tol) return true;
            }
            return false;
        }

        // Nombre collé à son unité : « 60hz » pour la clé « hz », « 240fps » pour « fps ».
        public static bool UnitHit(string w, string k)
        {
            if (k.Length > 3 || w.Length <= k.Length || !w.EndsWith(k, StringComparison.Ordinal)) return false;
            for (int i = 0; i < w.Length - k.Length; i++) if (!char.IsDigit(w[i])) return false;
            return true;
        }

        /// <summary>Distance d'édition (Levenshtein) avec sortie anticipée au-delà de 'max'.</summary>
        public static int Lev(string a, string b, int max)
        {
            int n = a.Length, m = b.Length;
            var prev = new int[m + 1]; var cur = new int[m + 1];
            for (int j = 0; j <= m; j++) prev[j] = j;
            for (int i = 1; i <= n; i++)
            {
                cur[0] = i; int best = cur[0];
                for (int j = 1; j <= m; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    int v = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                    cur[j] = v; if (v < best) best = v;
                }
                if (best > max) return max + 1;
                var t = prev; prev = cur; cur = t;
            }
            return prev[m];
        }

        // --- « oui » / « non » : formes courtes uniquement, comparées lettres seules (« Vas-y ! »,
        //     « d'accord », « ok stp »…). Une vraie phrase ne matche pas et suit le routage normal. ---
        public static string Squash(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s) if (char.IsLetter(c)) sb.Append(c);
            return sb.ToString();
        }

        private static readonly System.Text.RegularExpressions.Regex YesRx = new System.Text.RegularExpressions.Regex(
            "^(oui|ouais|ouaip|yes|yep|yeah|ok|okay|oki|daccord|dacc|vasy|go|fonce|lance|faisle|fais|ouvre|carrement|volontiers|banco|camarche|cestparti|allonsy|allezy|allez|montre|montremoi|jeveuxbien|pourquoipas|evidemment|grave)"
          + "(oui|ouais|ok|vasy|go|lance|faisle|fais|ouvre|montre|stp|silteplait|svp|merci|le|la|ca|moi|donc|maintenant|toutdesuite)*$");
        private static readonly System.Text.RegularExpressions.Regex NoRx = new System.Text.RegularExpressions.Regex(
            "^(non|nan|nope|no|pasmaintenant|pastoutdesuite|pasencore|plustard|laisse|laissetomber|annule|annuler|stop|arrete|surtoutpas)"
          + "(non|nan|laisse|laissetomber|tomber|merci|stp|silteplait|svp)*$");

        public static bool IsYes(string s) { string x = Squash(s); return x.Length > 0 && x.Length <= 40 && YesRx.IsMatch(x); }
        public static bool IsNo(string s) { string x = Squash(s); return x.Length > 0 && x.Length <= 40 && NoRx.IsMatch(x); }

        // --- Développement du langage « texto » : chaque abréviation courante → sa forme pleine,
        //     AVANT tout le routage (les règles, le lexique et les conseils en profitent tous).
        //     Ne remplace QUE des tokens entiers, connus et non ambigus dans un contexte d'aide PC.
        private static readonly System.Collections.Generic.Dictionary<string, string> Abbr =
            new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal)
        {
            // politesse / salutations
            {"slt","salut"},{"cc","coucou"},{"bjr","bonjour"},{"bsr","bonsoir"},{"wsh","salut"},{"yo","salut"},
            {"stp","s'il te plait"},{"svp","s'il te plait"},{"mrc","merci"},{"dsl","desole"},{"bnj","bonjour"},
            // oui / non
            {"wi","oui"},{"ui","oui"},{"ouai","oui"},{"ouais","oui"},{"nn","non"},{"nan","non"},{"vi","oui"},
            // mots interrogatifs
            {"pk","pourquoi"},{"pq","pourquoi"},{"pkoi","pourquoi"},{"prq","pourquoi"},{"koi","quoi"},{"kwa","quoi"},
            {"ki","qui"},{"kan","quand"},{"kand","quand"},{"cmt","comment"},{"comen","comment"},{"komen","comment"},
            {"komin","comment"},{"cb","combien"},{"cbien","combien"},
            // pronoms / verbes fréquents
            {"g","j'ai"},{"jai","j'ai"},{"j","je"},{"chui","je suis"},{"chuis","je suis"},{"shui","je suis"},
            {"ta","tu as"},{"tas","tu as"},{"ya","il y a"},{"yaa","il y a"},{"jv","je vais"},{"jvai","je vais"},
            {"jvais","je vais"},{"jpe","je peux"},{"jsp","je sais pas"},{"jpp","je n'en peux plus"},{"ta's","tu as"},
            // abréviations courantes
            {"bcp","beaucoup"},{"tjs","toujours"},{"tjr","toujours"},{"tjrs","toujours"},{"tt","tout"},
            {"mtn","maintenant"},{"ct","c'etait"},{"tkt","t'inquiete"},{"askip","a ce qu'il parait"},
            {"qqch","quelque chose"},{"qqn","quelqu'un"},{"qd","quand"},{"ms","mais"},{"pcq","parce que"},
            {"pck","parce que"},{"prkoi","pourquoi"},{"tmp","temps"},{"bg","beau gosse"},
            // problème / négations / tech
            {"pb","probleme"},{"pbm","probleme"},{"pblm","probleme"},{"prob","probleme"},{"probl","probleme"},
            {"souci","souci"},{"ordi","pc"},{"ordinateur","pc"},{"pc","pc"},{"maj","mise a jour"},
            {"dl","telechargement"},{"tel","telecharger"},{"pa","pas"},{"pö","pas"},{"po","pas"},{"pu","plus"},
            {"plu","plus"},{"rien","rien"},{"marche","marche"},{"fonctionne","fonctionne"},
            // ça va & co (renforce la politesse, même isolé)
            {"cava","ca va"},{"sava","ca va"},{"cv","ca va"},{"savapa","ca va pas"},{"cvpa","ca va pas"},
            // --- 2e vague : adverbes, temps, accords ---
            {"vrmt","vraiment"},{"vrm","vraiment"},{"vraimen","vraiment"},{"grv","grave"},{"tro","trop"},
            {"tr","trop"},{"jms","jamais"},{"jame","jamais"},{"tjt","toujours"},{"auj","aujourd'hui"},
            {"ajd","aujourd'hui"},{"dmain","demain"},{"enfait","en fait"},{"enfai","en fait"},{"anfin","enfin"},
            {"fo","faut"},{"fau","faut"},{"vazy","vas y"},{"vazi","vas y"},{"jariv","j'arrive"},
            {"psk","parce que"},{"pask","parce que"},{"parceke","parce que"},{"tfk","tu fais"},{"tufe","tu fais"},
            {"jte","je te"},{"jtai","je t'ai"},{"cetai","c'etait"},{"quand","quand"},{"dak","ok"},{"dac","ok"},
            {"dacc","ok"},{"okey","ok"},{"okay","ok"},{"oke","ok"},{"nikel","nickel"},{"trkl","tranquille"},
            {"pf","bof"},{"pff","bof"},{"bref","bref"},
            // --- 3e vague : vocabulaire panne (variantes de saisie → mot canonique) ---
            {"lague","lag"},{"laggue","lag"},{"lagge","lag"},{"laggs","lag"},{"lagg","lag"},{"laag","lag"},
            {"freez","freeze"},{"frize","freeze"},{"frise","freeze"},{"fige","freeze"},{"gele","freeze"},
            {"bugg","bug"},{"buggue","bug"},{"bugue","bug"},{"boggue","bug"},{"boque","bug"},{"beug","bug"},
            {"plante","plante"},{"plente","plante"},{"crash","crash"},{"krash","crash"},{"crache","crash"},
            {"mouline","rame"},{"patine","rame"},{"ramme","rame"},{"ramette","rame"},
            {"saccade","saccade"},{"sacade","saccade"},{"stotter","stutter"},{"stutt","stutter"},
            {"reboot","redemarre"},{"restart","redemarre"},{"redemare","redemarre"},{"bloque","bloque"},
            {"lenteur","lent"},{"lag","lag"},{"co","connexion"},{"deco","deconnecte"},{"deconecte","deconnecte"},
            {"chaud","chaud"},{"brulant","chaud"},{"cramme","chaud"},{"fournaise","chaud"},{"bruyant","bruyant"},
            {"ventilo","ventilateur"},{"ventilos","ventilateur"},{"screen","ecran"},{"moniteur","ecran"},
            {"ecran","ecran"},{"clavié","clavier"},{"souri","souris"},{"soury","souris"},{"micro","micro"},
            {"manette","manette"},{"drivers","pilotes"},{"driver","pilote"},
            // --- 4e vague : orthographes courantes des mots de panne ---
            {"bsod","ecran bleu"},{"bluescreen","ecran bleu"},{"ecranbleu","ecran bleu"},
            {"rezo","reseau"},{"reso","reseau"},{"wi-fi","wifi"},{"wiifi","wifi"},
            {"connection","connexion"},{"conection","connexion"},{"conexion","connexion"},
            {"y'a","il y a"},{"t'as","tu as"},
        };

        public static string Expand(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf(' ') < 0 && s.Length > 12) return s;
            string[] w = s.Split(' ');
            var sb = new StringBuilder(s.Length + 16);
            for (int i = 0; i < w.Length; i++)
            {
                if (w[i].Length == 0) continue;
                if (sb.Length > 0) sb.Append(' ');
                // La ponctuation collée (« pb? », « slt, ») reste autour du mot développé.
                int a = 0, b = w[i].Length;
                while (a < b && Ponctuation(w[i][a])) a++;
                while (b > a && Ponctuation(w[i][b - 1])) b--;
                string coeur = w[i].Substring(a, b - a), rep;
                sb.Append(w[i], 0, a).Append(Abbr.TryGetValue(coeur, out rep) ? rep : coeur).Append(w[i], b, w[i].Length - b);
            }
            return sb.ToString();
        }

        private static bool Ponctuation(char c) { return ",.;:!?()\"«»…".IndexOf(c) >= 0; }

        private static readonly Regex Repetees = new Regex(@"(\p{L})\1{2,}", RegexOptions.CultureInvariant);
        private static readonly Regex Blancs = new Regex(@"\s+", RegexOptions.CultureInvariant);

        /// <summary>Minuscules, accents et ligatures à plat, apostrophes unifiées, espaces réduits, lettres
        /// répétées pour insister ramenées à une (« laaaag » → « lag ») ; adresses et nombres intacts.</summary>
        public static string Norm(string x)
        {
            if (string.IsNullOrEmpty(x)) return "";
            string t = x.ToLowerInvariant()
                .Replace('’', '\'').Replace('‘', '\'').Replace('ʼ', '\'').Replace('`', '\'').Replace('´', '\'')
                .Replace("œ", "oe").Replace("æ", "ae").Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(t.Length);
            foreach (char c in t)
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            string[] mots = Blancs.Replace(sb.ToString().Normalize(NormalizationForm.FormC), " ").Trim().Split(' ');
            for (int i = 0; i < mots.Length; i++)
                if (mots[i].IndexOfAny(new[] { '.', '/', '@' }) < 0) mots[i] = Repetees.Replace(mots[i], "$1");
            return string.Join(" ", mots);
        }
    }
}
