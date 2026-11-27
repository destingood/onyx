using System;
using System.Collections.Generic;

namespace BTOptimizer
{
    /// <summary>
    /// EXCLUSIONS DU NETTOYAGE — PUR. Les dossiers (ou fichiers) que l'utilisateur ne veut jamais
    /// voir proposés ni vidés. Une exclusion couvre tout ce qu'elle contient, et protège aussi le
    /// dossier qui la contient : un cache qui renferme un dossier exclu n'est plus vidé du tout,
    /// plutôt que vidé « autour ».
    /// </summary>
    internal static class ExclusionsNettoyage
    {
        public const int Max = 50;

        /// <summary>Chemin complet et propre (« D:\Montages »), ou null si ce n'en est pas un.</summary>
        public static string Normalise(string saisi)
        {
            string c = (saisi ?? "").Trim().Trim('"').Trim().Replace('/', '\\');
            if (c.Length < 3 || c.Length > 400) return null;
            bool lecteur = ((c[0] >= 'A' && c[0] <= 'Z') || (c[0] >= 'a' && c[0] <= 'z')) && c[1] == ':' && c[2] == '\\';
            bool reseau = c.StartsWith(@"\\", StringComparison.Ordinal) && c.Length > 4 && c[2] != '\\' && c.IndexOf('\\', 3) > 3;
            if (!lecteur && !reseau) return null;
            string reste = lecteur ? c.Substring(3) : c.Substring(2);
            foreach (char k in reste) if (k < 32 || "*?\"<>|:".IndexOf(k) >= 0) return null;
            foreach (string seg in reste.Split('\\')) if (seg == "." || seg == "..") return null;
            if (reste.IndexOf(@"\\", StringComparison.Ordinal) >= 0) return null;
            while (c.Length > 3 && c[c.Length - 1] == '\\') c = c.Substring(0, c.Length - 1);
            if (lecteur) c = char.ToUpperInvariant(c[0]) + c.Substring(1);
            return c;
        }

        private static string SansFin(string chemin) { return (chemin ?? "").Replace('/', '\\').TrimEnd('\\'); }

        /// <summary><paramref name="chemin"/> est <paramref name="dossier"/> ou se trouve dedans (casse ignorée, par segments entiers).</summary>
        public static bool Dans(string dossier, string chemin)
        {
            string d = SansFin(dossier), c = SansFin(chemin);
            if (d.Length == 0 || c.Length < d.Length) return false;
            if (!c.StartsWith(d, StringComparison.OrdinalIgnoreCase)) return false;
            return c.Length == d.Length || c[d.Length] == '\\';
        }

        /// <summary>Le chemin est exclu : lui-même, ou un de ses dossiers parents, figure dans la liste.</summary>
        public static bool Couvre(IEnumerable<string> exclus, string chemin)
        {
            if (exclus == null || string.IsNullOrEmpty(chemin)) return false;
            foreach (string e in exclus) if (!string.IsNullOrEmpty(e) && Dans(e, chemin)) return true;
            return false;
        }

        /// <summary>
        /// Le chemin est exclu, OU il contient une exclusion : nettoyer ce dossier supprimerait ce que
        /// l'utilisateur a demandé de garder. C'est ce test qui décide si une cible est proposée.
        /// </summary>
        public static bool Touche(IEnumerable<string> exclus, string chemin)
        {
            if (exclus == null || string.IsNullOrEmpty(chemin)) return false;
            foreach (string e in exclus)
                if (!string.IsNullOrEmpty(e) && (Dans(e, chemin) || Dans(chemin, e))) return true;
            return false;
        }

        /// <summary>
        /// Ajoute une exclusion. Renvoie null si c'est fait, sinon la raison du refus. Les exclusions
        /// que la nouvelle englobe sont retirées : la liste reste courte.
        /// </summary>
        public static string Ajoute(List<string> exclus, string saisi)
        {
            string c = Normalise(saisi);
            if (c == null) return "Chemin non reconnu : il faut un chemin complet, par exemple D:\\Montages.";
            if (Couvre(exclus, c)) return "Déjà exclu : ce dossier, ou un dossier qui le contient, est dans la liste.";
            exclus.RemoveAll(e => Dans(c, e));
            if (exclus.Count >= Max) return "La liste est pleine (" + Max + " exclusions) : retires-en une d'abord.";
            exclus.Add(c);
            return null;
        }
    }
}
