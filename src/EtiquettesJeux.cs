using System;
using System.Collections.Generic;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// ÉTIQUETTES DE JEUX (logique PURE) : mots choisis par l'utilisateur (« solo », « coop »,
    /// « à finir »…) pour filtrer la bibliothèque. Une ligne par jeu dans bt-jeux-etiquettes.txt :
    /// « clé TAB étiquette1|étiquette2 ». La clé est BibliothequeJeux.Cle : le même jeu garde ses
    /// étiquettes quel que soit le magasin. Comparaison sans tenir compte de la casse.
    /// </summary>
    internal static partial class EtiquettesJeux
    {
        public const int MaxParJeu = 8, MaxJeux = 500, Longueur = 24;

        /// <summary>Saisie « solo, Coop ; à finir » → étiquettes nettoyées, sans doublon, 8 au plus.</summary>
        public static List<string> Analyse(string saisie)
        {
            var res = new List<string>();
            if (string.IsNullOrEmpty(saisie)) return res;
            var vues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string brute in saisie.Split(new[] { ',', ';', '|', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string e = Normalise(brute);
                if (e.Length == 0 || !vues.Add(e)) continue;
                res.Add(e);
                if (res.Count == MaxParJeu) break;
            }
            return res;
        }

        /// <summary>Espaces multiples réduits, « # » de tête retiré, 24 caractères au plus.</summary>
        public static string Normalise(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder();
            bool espace = false;
            foreach (char c in s.Trim().TrimStart('#').Trim())
            {
                if (char.IsWhiteSpace(c) || c == '|' || char.IsControl(c)) { espace = true; continue; }
                if (espace && sb.Length > 0) sb.Append(' ');
                espace = false;
                sb.Append(c);
            }
            string r = sb.ToString();
            return r.Length > Longueur ? r.Substring(0, Longueur).TrimEnd() : r;
        }

        public static string Saisie(IEnumerable<string> l)
        {
            return l == null ? "" : string.Join(", ", l);
        }

        public static Dictionary<string, List<string>> Lire(string texte)
        {
            var res = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(texte)) return res;
            foreach (string brute in texte.Split('\n'))
            {
                string[] c = brute.TrimEnd('\r').Split('\t');
                if (c.Length < 2 || c[0].Length == 0 || res.ContainsKey(c[0]) || res.Count >= MaxJeux) continue;
                List<string> e = Analyse(c[1]);
                if (e.Count > 0) res[c[0]] = e;
            }
            return res;
        }

        /// <summary>Clés triées : le fichier ne change pas d'ordre d'une écriture à l'autre.</summary>
        public static string Ecrire(Dictionary<string, List<string>> d)
        {
            var sb = new StringBuilder();
            if (d == null) return "";
            var cles = new List<string>(d.Keys);
            cles.Sort(StringComparer.Ordinal);
            foreach (string k in cles)
            {
                if (k.Length == 0 || d[k] == null || d[k].Count == 0) continue;
                sb.Append(k).Append('\t').Append(string.Join("|", d[k])).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>Étiquettes du jeu (copie, vide s'il n'en a pas).</summary>
        public static List<string> De(Dictionary<string, List<string>> d, string nomJeu)
        {
            string cle = BibliothequeJeux.Cle(nomJeu);
            List<string> l;
            return d != null && cle.Length > 0 && d.TryGetValue(cle, out l) ? new List<string>(l) : new List<string>();
        }

        /// <summary>Remplace les étiquettes du jeu ; une saisie vide les retire. Faux si la liste est pleine.</summary>
        public static bool Pose(Dictionary<string, List<string>> d, string nomJeu, string saisie)
        {
            string cle = BibliothequeJeux.Cle(nomJeu);
            if (d == null || cle.Length == 0) return false;
            List<string> e = Analyse(saisie);
            if (e.Count == 0) { d.Remove(cle); return true; }
            if (!d.ContainsKey(cle) && d.Count >= MaxJeux) return false;
            d[cle] = e;
            return true;
        }

        /// <summary>Toutes les étiquettes, une fois chacune (première orthographe gardée), par ordre alphabétique.</summary>
        public static List<string> Toutes(Dictionary<string, List<string>> d)
        {
            var vues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (d != null)
            {
                var cles = new List<string>(d.Keys);
                cles.Sort(StringComparer.Ordinal);
                foreach (string k in cles) foreach (string e in d[k]) if (!vues.ContainsKey(e)) vues[e] = e;
            }
            var res = new List<string>(vues.Values);
            res.Sort(StringComparer.CurrentCultureIgnoreCase);
            return res;
        }

        public static bool Porte(List<string> etiquettes, string etiquette)
        {
            if (etiquettes == null || string.IsNullOrEmpty(etiquette)) return false;
            foreach (string e in etiquettes) if (string.Equals(e, etiquette, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Recherche : une étiquette contient le texte tapé (déjà en minuscules).</summary>
        public static bool Contient(List<string> etiquettes, string requete)
        {
            if (etiquettes == null || string.IsNullOrEmpty(requete)) return false;
            foreach (string e in etiquettes) if (e.ToLowerInvariant().Contains(requete)) return true;
            return false;
        }
    }
}
