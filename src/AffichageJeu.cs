using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BTOptimizer
{
    /// <summary>Résolution et mode d'affichage demandés dans les préréglages ; null : ne pas changer.</summary>
    internal sealed class ChoixAffichage
    {
        /// <summary>« 1920x1080 ».</summary>
        public string Resolution;
        /// <summary>Valeur telle que le jeu l'écrit (« 1 » pour Fortnite, « Fullscreen » pour Black Ops 6).</summary>
        public string Mode;
        public bool Vide { get { return Resolution == null && Mode == null; } }
    }

    /// <summary>Ce que la fenêtre des préréglages peut proposer pour l'affichage d'un jeu.</summary>
    internal sealed class OptionsAffichage
    {
        /// <summary>« 2560x1440 », de la plus grande à la plus petite ; vide : résolution non proposée.</summary>
        public List<string> Resolutions = new List<string>();
        public string ResolutionActuelle;
        /// <summary>Valeur du jeu et libellé de son menu ; vide : mode non proposé.</summary>
        public List<KeyValuePair<string, string>> Modes = new List<KeyValuePair<string, string>>();
        public string ModeActuel;
        /// <summary>Ce qu'il faut savoir avant de choisir (mode où la résolution ne sert pas, clé absente…).</summary>
        public string Remarque;

        public string LibelleMode(string valeur)
        {
            foreach (var m in Modes) if (m.Key == valeur) return m.Value;
            return valeur;
        }
    }

    /// <summary>
    /// AFFICHAGE DES JEUX — PUR. Les résolutions proposées sont celles que Windows accepte pour
    /// l'écran (la liste que les jeux montrent aussi dans leur menu) ; rien en dessous de 800 × 600.
    /// </summary>
    internal static class AffichageJeu
    {
        private static readonly Regex Format = new Regex(@"^([0-9]{3,5})x([0-9]{3,5})$", RegexOptions.CultureInvariant);

        public static bool Lit(string resolution, out int largeur, out int hauteur)
        {
            largeur = hauteur = 0;
            Match m = Format.Match(resolution ?? "");
            return m.Success
                && int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out largeur)
                && int.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out hauteur)
                && largeur > 0 && hauteur > 0;
        }

        /// <summary>« 2560 × 1440 ».</summary>
        public static string Texte(string resolution)
        {
            int l, h;
            return Lit(resolution, out l, out h) ? l.ToString(CultureInfo.InvariantCulture) + " × " + h.ToString(CultureInfo.InvariantCulture) : resolution;
        }

        /// <summary>Sans doublon, au moins 800 × 600, de la plus grande à la plus petite.</summary>
        public static List<string> Trie(IEnumerable<string> resolutions)
        {
            var vues = new HashSet<string>(StringComparer.Ordinal);
            var t = new List<int[]>();
            if (resolutions != null)
                foreach (string r in resolutions)
                {
                    int l, h;
                    if (!Lit(r, out l, out h) || l < 800 || h < 600 || !vues.Add(l + "x" + h)) continue;
                    t.Add(new[] { l, h });
                }
            t.Sort((a, b) => a[0] != b[0] ? b[0].CompareTo(a[0]) : b[1].CompareTo(a[1]));
            return t.ConvertAll(x => x[0].ToString(CultureInfo.InvariantCulture) + "x" + x[1].ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>Résolutions acceptées par TOUS les écrans (quand on ne sait pas sur lequel le jeu s'affiche).</summary>
        public static HashSet<string> Communes(List<HashSet<string>> parEcran)
        {
            if (parEcran == null || parEcran.Count == 0) return new HashSet<string>(StringComparer.Ordinal);
            var res = new HashSet<string>(parEcran[0], StringComparer.Ordinal);
            for (int i = 1; i < parEcran.Count; i++) res.IntersectWith(parEcran[i]);
            return res;
        }

        /// <summary>Identifiant d'écran tel qu'Unreal l'écrit (« "MONITOR\\SAM793E\\{…}\\0002" ») → celui de Windows.</summary>
        public static string IdEcran(string valeurIni)
        {
            string v = (valeurIni ?? "").Trim();
            if (v.Length >= 2 && v[0] == '"' && v[v.Length - 1] == '"') v = v.Substring(1, v.Length - 2);
            v = v.Replace("\\\\", "\\");
            return v.StartsWith("MONITOR\\", StringComparison.OrdinalIgnoreCase) ? v : null;
        }
    }
}
