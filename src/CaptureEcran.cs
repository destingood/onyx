using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// CAPTURE D'ÉCRAN EN JEU (logique PURE) : nom du fichier, au format de la Game Bar
    /// (« Fortnite 2026-11-26 21-04-11.png ») pour que la bibliothèque de clips retrouve le jeu.
    /// </summary>
    internal static partial class CaptureEcran
    {
        public const string SansJeu = "Capture";
        private const int NomMax = 80;

        public static string NomFichier(string jeu, DateTime quand)
        {
            var sb = new StringBuilder();
            char[] interdits = Path.GetInvalidFileNameChars();
            foreach (char c in (jeu ?? "").Trim())
                sb.Append(Array.IndexOf(interdits, c) >= 0 || char.IsControl(c) ? ' ' : c);
            string nom = sb.ToString().Trim(' ', '.');
            if (nom.Length > NomMax) nom = nom.Substring(0, NomMax).TrimEnd(' ', '.');
            if (nom.Length == 0) nom = SansJeu;
            return nom + " " + quand.ToString("yyyy'-'MM'-'dd HH'-'mm'-'ss", CultureInfo.InvariantCulture) + ".png";
        }

        /// <summary>Nom libre dans le dossier : « … (2).png », « … (3).png » si la seconde est déjà prise.</summary>
        public static string Libre(string nom, Func<string, bool> existe)
        {
            if (existe == null || !existe(nom)) return nom;
            string baseNom = nom.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? nom.Substring(0, nom.Length - 4) : nom;
            for (int i = 2; i < 100; i++)
            {
                string n = baseNom + " (" + i.ToString(CultureInfo.InvariantCulture) + ").png";
                if (!existe(n)) return n;
            }
            return baseNom + " " + Guid.NewGuid().ToString("N").Substring(0, 6) + ".png";
        }
    }
}
