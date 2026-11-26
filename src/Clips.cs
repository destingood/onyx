using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace BTOptimizer
{
    /// <summary>
    /// BIBLIOTHÈQUE DE CLIPS (logique PURE) : à quel jeu appartient une capture, quels fichiers
    /// sont des vidéos, dans quel ordre les montrer. Le jeu vient du sous-dossier direct de la
    /// racine (rangement NVIDIA, OBS, ou à la main), sinon du nom de fichier avant la date
    /// (Game Bar : « Fortnite 2026-11-26 21-04-11.mp4 »), sinon « Autres ».
    /// </summary>
    internal static partial class Clips
    {
        public sealed class Clip { public string Chemin, Jeu; public DateTime Date; public long Octets; }

        public const string Autres = "Autres";

        private static readonly string[] Extensions = { ".mp4", ".mkv", ".mov", ".webm" };

        // Dossiers que les logiciels de capture créent sous Vidéos avant celui du jeu.
        private static readonly string[] Logiciels = { "NVIDIA", "Captures", "Radeon ReLive", "AMD", "Medal", "Outplayed", "OBS", "ShadowPlay", "Clips" };

        // Nom avant une date AAAA-MM-JJ ou AAAA.MM.JJ (Game Bar, NVIDIA, OBS).
        private static readonly Regex AvantDate = new Regex(@"^(.*?)[\s_\-]*\d{4}[-.]\d{2}[-.]\d{2}", RegexOptions.CultureInvariant);

        public static bool EstVideo(string chemin)
        {
            if (string.IsNullOrEmpty(chemin)) return false;
            string ext;
            try { ext = Path.GetExtension(chemin); } catch { return false; }
            foreach (string e in Extensions) if (string.Equals(ext, e, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static string JeuDepuisChemin(string racine, string chemin)
        {
            if (string.IsNullOrEmpty(racine) || string.IsNullOrEmpty(chemin)) return Autres;
            string r = racine.TrimEnd('\\', '/') + "\\";
            if (!chemin.StartsWith(r, StringComparison.OrdinalIgnoreCase)) return Autres;
            string reste = chemin.Substring(r.Length);
            int sep = reste.IndexOfAny(new[] { '\\', '/' });
            // Dossier d'un logiciel de capture (Vidéos\NVIDIA\Fortnite\…) : le jeu est au niveau suivant.
            if (sep > 0 && Array.Exists(Logiciels, l => string.Equals(l, reste.Substring(0, sep), StringComparison.OrdinalIgnoreCase)))
            {
                reste = reste.Substring(sep + 1);
                sep = reste.IndexOfAny(new[] { '\\', '/' });
            }
            if (sep > 0) return reste.Substring(0, sep);
            string nom;
            try { nom = Path.GetFileNameWithoutExtension(reste); } catch { return Autres; }
            Match m = AvantDate.Match(nom ?? "");
            string jeu = m.Success ? m.Groups[1].Value.Trim(' ', '-', '_', '.') : "";
            return jeu.Length > 0 ? jeu : Autres;
        }

        /// <summary>Plus récent d'abord ; <paramref name="jeu"/> null = tous les jeux.</summary>
        public static List<Clip> Trie(IEnumerable<Clip> clips, string jeu)
        {
            var res = new List<Clip>();
            if (clips == null) return res;
            foreach (Clip c in clips)
                if (c != null && (jeu == null || string.Equals(c.Jeu, jeu, StringComparison.OrdinalIgnoreCase))) res.Add(c);
            res.Sort((x, y) => y.Date.CompareTo(x.Date));
            return res;
        }
    }
}
