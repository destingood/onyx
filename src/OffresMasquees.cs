using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// JEUX MASQUÉS DES BONS PLANS (logique PURE) : « J'ai déjà ce jeu » ou « Pas pour moi ».
    /// Le jeu disparaît des promotions et des jeux gratuits, sur toutes les boutiques (clé
    /// <see cref="BibliothequeJeux.Cle"/> : sans ™ ni mention d'édition). Les favoris et les
    /// suivis restent affichés : ce sont des choix explicites. Tout se réaffiche depuis « Boutiques ».
    /// Fichier bt-bons-plans-masques.txt : une ligne par jeu, « clé TAB raison TAB nom TAB date ».
    /// </summary>
    internal static class OffresMasquees
    {
        public const int Max = 500;
        public const string Possede = "possede", PasPourMoi = "pas";

        public sealed class Masque { public string Cle, Nom, Raison; public DateTime Quand; }

        public static bool Contient(List<Masque> l, string nom)
        {
            string k = BibliothequeJeux.Cle(nom);
            return l != null && k.Length > 0 && l.Exists(m => m.Cle == k);
        }

        /// <summary>Ajoute le jeu ; faux s'il n'a pas de nom ou s'il est déjà masqué. Au plafond, le plus ancien part.</summary>
        public static bool Ajoute(List<Masque> l, string nom, string raison, DateTime quand)
        {
            string k = BibliothequeJeux.Cle(nom);
            if (l == null || k.Length == 0 || l.Exists(m => m.Cle == k)) return false;
            while (l.Count >= Max)
            {
                int vieux = 0;
                for (int i = 1; i < l.Count; i++) if (l[i].Quand < l[vieux].Quand) vieux = i;
                l.RemoveAt(vieux);
            }
            l.Add(new Masque { Cle = k, Nom = Propre(nom.Trim()), Raison = raison == Possede ? Possede : PasPourMoi, Quand = quand });
            return true;
        }

        public static bool Retire(List<Masque> l, string nomOuCle)
        {
            string k = BibliothequeJeux.Cle(nomOuCle);
            return l != null && k.Length > 0 && l.RemoveAll(m => m.Cle == k) > 0;
        }

        /// <summary>Retire des offres les jeux masqués ; renvoie combien ont été retirées.</summary>
        public static int Filtre(List<Offre> offres, List<Masque> l)
        {
            if (offres == null || l == null || l.Count == 0) return 0;
            var cles = new HashSet<string>(StringComparer.Ordinal);
            foreach (Masque m in l) cles.Add(m.Cle);
            return offres.RemoveAll(o => o != null && cles.Contains(BibliothequeJeux.Cle(o.Nom)));
        }

        public static List<Masque> Lire(string texte)
        {
            var l = new List<Masque>();
            if (string.IsNullOrEmpty(texte)) return l;
            foreach (string brut in texte.Replace("\r", "").Split('\n'))
            {
                string[] c = brut.Split('\t');
                string k = c.Length > 0 ? c[0].Trim() : "";
                if (k.Length == 0 || l.Exists(m => m.Cle == k)) continue;
                DateTime d;
                l.Add(new Masque
                {
                    Cle = k,
                    Raison = c.Length > 1 && c[1].Trim() == Possede ? Possede : PasPourMoi,
                    Nom = c.Length > 2 && c[2].Trim().Length > 0 ? c[2].Trim() : k,
                    Quand = c.Length > 3 && DateTime.TryParse(c[3].Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out d) ? d : DateTime.MinValue
                });
                if (l.Count >= Max) break;
            }
            return l;
        }

        public static string Ecrire(List<Masque> l)
        {
            var sb = new StringBuilder();
            if (l != null)
                foreach (Masque m in l)
                    sb.Append(m.Cle).Append('\t').Append(m.Raison).Append('\t').Append(Propre(m.Nom))
                      .Append('\t').Append(m.Quand.ToString("o", CultureInfo.InvariantCulture)).Append('\n');
            return sb.ToString();
        }

        private static string Propre(string s) { return (s ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' '); }
    }
}
