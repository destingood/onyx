using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// OPTIONS DE LANCEMENT PAR JEU (logique PURE) : nom affiché, exécutable, arguments et dossier
    /// de départ choisis par l'utilisateur. Une ligne par jeu dans bt-jeux-options.txt :
    /// « clé TAB nom TAB exécutable TAB arguments TAB dossier ». La clé est BibliothequeJeux.Cle :
    /// le même jeu garde ses options quel que soit le magasin ou l'édition.
    /// </summary>
    internal static partial class OptionsJeu
    {
        public const int Max = 500;

        public sealed class Options
        {
            public string Cle = "", Nom = "", Cible = "", Arguments = "", Dossier = "";
            public bool Vide { get { return Nom.Length == 0 && Cible.Length == 0 && Arguments.Length == 0 && Dossier.Length == 0; } }
        }

        /// <summary>Tabulations et retours à la ligne remplacés par une espace, bords nettoyés.</summary>
        public static string Nettoie(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
        }

        public static List<Options> Lire(string texte)
        {
            var res = new List<Options>();
            if (string.IsNullOrEmpty(texte)) return res;
            var vues = new HashSet<string>();
            foreach (string brute in texte.Split('\n'))
            {
                string[] c = brute.TrimEnd('\r').Split('\t');
                if (c.Length < 5 || c[0].Length == 0 || !vues.Add(c[0])) continue;
                var o = new Options { Cle = c[0], Nom = Nettoie(c[1]), Cible = Nettoie(c[2]), Arguments = Nettoie(c[3]), Dossier = Nettoie(c[4]) };
                if (!o.Vide && res.Count < Max) res.Add(o);
            }
            return res;
        }

        public static string Ecrire(IEnumerable<Options> l)
        {
            var sb = new StringBuilder();
            if (l == null) return "";
            foreach (Options o in l)
            {
                if (o == null || o.Cle.Length == 0 || o.Vide) continue;
                sb.Append(o.Cle).Append('\t').Append(Nettoie(o.Nom)).Append('\t').Append(Nettoie(o.Cible)).Append('\t')
                  .Append(Nettoie(o.Arguments)).Append('\t').Append(Nettoie(o.Dossier)).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>Options du jeu, ou null s'il n'en a pas.</summary>
        public static Options Trouve(List<Options> l, string nomJeu)
        {
            string cle = BibliothequeJeux.Cle(nomJeu);
            if (l == null || cle.Length == 0) return null;
            return l.Find(x => x.Cle == cle);
        }

        /// <summary>Remplace les options du jeu ; des options vides les retirent. Faux si la liste est pleine.</summary>
        public static bool Pose(List<Options> l, string nomJeu, string nom, string cible, string arguments, string dossier)
        {
            string cle = BibliothequeJeux.Cle(nomJeu);
            if (l == null || cle.Length == 0) return false;
            var o = new Options { Cle = cle, Nom = Nettoie(nom), Cible = Nettoie(cible), Arguments = Nettoie(arguments), Dossier = Nettoie(dossier) };
            // Un nom affiché identique au nom détecté n'est pas un changement.
            if (string.Equals(o.Nom, Nettoie(nomJeu), StringComparison.Ordinal)) o.Nom = "";
            int i = l.FindIndex(x => x.Cle == cle);
            if (o.Vide) { if (i >= 0) l.RemoveAt(i); return true; }
            if (i >= 0) { l[i] = o; return true; }
            if (l.Count >= Max) return false;
            l.Add(o);
            return true;
        }

        /// <summary>Nom choisi par l'utilisateur, sinon le nom détecté.</summary>
        public static string NomAffiche(List<Options> l, string nomJeu)
        {
            Options o = Trouve(l, nomJeu);
            return o != null && o.Nom.Length > 0 ? o.Nom : nomJeu;
        }

        /// <summary>Arguments de steam.exe pour lancer le jeu avec des arguments : « -applaunch ID args ».</summary>
        public static string ArgsSteam(int appId, string arguments)
        {
            string a = Nettoie(arguments);
            return "-applaunch " + appId.ToString(CultureInfo.InvariantCulture) + (a.Length > 0 ? " " + a : "");
        }

        /// <summary>Dossier de départ : celui choisi, sinon le dossier de l'exécutable.</summary>
        public static string DossierDepart(string choisi, string exe)
        {
            string d = Nettoie(choisi);
            if (d.Length > 0) return d;
            try { return string.IsNullOrEmpty(exe) ? "" : (Path.GetDirectoryName(exe) ?? ""); } catch { return ""; }
        }
    }
}
