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
        public sealed class Clip { public string Chemin, Jeu, Racine; public DateTime Date; public long Octets; }

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

        /// <summary>Ordres d'affichage proposés, dans l'ordre des numéros de <see cref="Trie"/>.</summary>
        public static readonly string[] Ordres = { "Plus récents d'abord", "Plus anciens d'abord", "Nom (A → Z)", "Plus gros d'abord" };

        private static readonly System.Globalization.CompareInfo Francais = System.Globalization.CultureInfo.GetCultureInfo("fr-FR").CompareInfo;

        /// <summary>
        /// Clips du jeu (<paramref name="jeu"/> null = tous) dans l'ordre choisi : 0 plus récents,
        /// 1 plus anciens, 2 nom de fichier (accents et casse ignorés), 3 plus gros. À égalité, le
        /// plus récent passe devant ; un numéro inconnu vaut 0.
        /// </summary>
        public static List<Clip> Trie(IEnumerable<Clip> clips, string jeu, int ordre = 0)
        {
            var res = new List<Clip>();
            if (clips == null) return res;
            foreach (Clip c in clips)
                if (c != null && (jeu == null || string.Equals(c.Jeu, jeu, StringComparison.OrdinalIgnoreCase))) res.Add(c);
            Comparison<Clip> recent = (x, y) => y.Date.CompareTo(x.Date);
            switch (ordre)
            {
                case 1: res.Sort((x, y) => x.Date.CompareTo(y.Date)); break;
                case 2:
                    res.Sort((x, y) =>
                    {
                        int n = Francais.Compare(NomSeul(x.Chemin), NomSeul(y.Chemin), System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace);
                        return n != 0 ? n : recent(x, y);
                    });
                    break;
                case 3: res.Sort((x, y) => { int n = y.Octets.CompareTo(x.Octets); return n != 0 ? n : recent(x, y); }); break;
                default: res.Sort(recent); break;
            }
            return res;
        }

        private static string NomSeul(string chemin)
        {
            try { return Path.GetFileNameWithoutExtension(chemin ?? "") ?? ""; } catch { return chemin ?? ""; }
        }

        // ------------------------------------------------------------------ renommer

        public const int NomMax = 120;

        private static readonly string[] Reserves =
        {
            "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        /// <summary>
        /// Nom de fichier (sans extension) que Windows accepte, ou null avec la raison. L'extension
        /// tapée par habitude est retirée : renommer ne change jamais le type du fichier.
        /// </summary>
        public static string NomPropre(string saisi, string extension, out string erreur)
        {
            erreur = null;
            string n = (saisi ?? "").Trim();
            if (!string.IsNullOrEmpty(extension) && n.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                n = n.Substring(0, n.Length - extension.Length);
            n = n.Trim().TrimEnd('.', ' ');   // Windows retire lui-même les points et espaces de fin
            if (n.Length == 0) { erreur = "Le nom est vide."; return null; }
            if (n.Length > NomMax) { erreur = "Le nom dépasse " + NomMax + " caractères."; return null; }
            foreach (char c in n)
            {
                if (c < 32) { erreur = "Le nom contient un caractère invisible."; return null; }
                if ("\\/:*?\"<>|".IndexOf(c) >= 0) { erreur = "Caractère interdit dans un nom de fichier : " + c; return null; }
            }
            int point = n.IndexOf('.');
            string debut = (point >= 0 ? n.Substring(0, point) : n).Trim();
            if (Array.Exists(Reserves, r => string.Equals(r, debut, StringComparison.OrdinalIgnoreCase)))
            { erreur = "« " + debut + " » est un nom réservé par Windows."; return null; }
            return n;
        }

        /// <summary>Même dossier, même extension, nouveau nom.</summary>
        public static string CheminRenomme(string chemin, string nom)
        {
            string dossier = Path.GetDirectoryName(chemin) ?? "";
            return Path.Combine(dossier, nom + Path.GetExtension(chemin));
        }

        // ------------------------------------------------------------------ déplacer

        private static string SansBarre(string dossier) { return (dossier ?? "").TrimEnd('\\', '/'); }

        /// <summary>
        /// Chemin d'arrivée d'un clip déplacé dans <paramref name="dossier"/> : même nom, ou « nom (2) »,
        /// « nom (3) »… si le nom y est déjà pris — rien n'est jamais écrasé. Null si le clip est déjà
        /// dans ce dossier, ou si aucun nom n'est libre.
        /// </summary>
        public static string CibleDeplacement(string chemin, string dossier, Func<string, bool> existe)
        {
            if (string.IsNullOrEmpty(chemin) || string.IsNullOrEmpty(dossier)) return null;
            if (string.Equals(SansBarre(Path.GetDirectoryName(chemin)), SansBarre(dossier), StringComparison.OrdinalIgnoreCase)) return null;
            string nom = Path.GetFileNameWithoutExtension(chemin), ext = Path.GetExtension(chemin);
            for (int n = 1; n <= 99; n++)
            {
                string cible = Path.Combine(dossier, (n == 1 ? nom : nom + " (" + n + ")") + ext);
                if (existe == null || !existe(cible)) return cible;
            }
            return null;
        }

        /// <summary>
        /// Le dossier est-il déjà balayé : une des racines, ou un de leurs sous-dossiers à
        /// <paramref name="profondeurMax"/> niveaux au plus. Sinon il faut l'ajouter aux dossiers suivis,
        /// faute de quoi un clip qu'on y déplace disparaîtrait de la liste.
        /// </summary>
        public static bool DejaSuivi(string dossier, IEnumerable<string> racines, int profondeurMax)
        {
            string d = SansBarre(dossier);
            if (d.Length == 0 || racines == null) return false;
            foreach (string racine in racines)
            {
                string r = SansBarre(racine);
                if (r.Length == 0) continue;
                if (string.Equals(d, r, StringComparison.OrdinalIgnoreCase)) return true;
                if (!d.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase)) continue;
                if (d.Substring(r.Length + 1).Split('\\', '/').Length <= profondeurMax) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ limite d'espace

        /// <summary>Choix proposés, en Go ; 0 = aucune limite.</summary>
        public static readonly int[] LimitesGo = { 0, 5, 10, 20, 50, 100, 200, 500 };

        /// <summary>Limite relue du fichier de réglage : un entier de Go entre 1 et 10 000, sinon 0 (aucune).</summary>
        public static int LitLimite(string texte)
        {
            int v;
            if (!int.TryParse((texte ?? "").Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out v)) return 0;
            return v >= 1 && v <= 10000 ? v : 0;
        }

        public static long Total(IEnumerable<Clip> clips)
        {
            long t = 0;
            if (clips != null) foreach (Clip c in clips) if (c != null && c.Octets > 0) t += c.Octets;
            return t;
        }

        /// <summary>
        /// Les clips à retirer pour repasser sous la limite : les plus ANCIENS d'abord, et pas un de
        /// plus qu'il ne faut. Limite nulle ou négative : aucun. Cette liste n'est qu'une proposition,
        /// l'envoi à la Corbeille reste confirmé par l'utilisateur.
        /// </summary>
        public static List<Clip> EnTrop(IEnumerable<Clip> clips, long limiteOctets)
        {
            var res = new List<Clip>();
            if (clips == null || limiteOctets <= 0) return res;
            long total = Total(clips);
            if (total <= limiteOctets) return res;
            var anciens = new List<Clip>();
            foreach (Clip c in clips) if (c != null && c.Octets > 0) anciens.Add(c);
            anciens.Sort((x, y) => x.Date.CompareTo(y.Date));
            foreach (Clip c in anciens)
            {
                if (total <= limiteOctets) break;
                res.Add(c); total -= c.Octets;
            }
            return res;
        }
    }
}
