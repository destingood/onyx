using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// JAQUETTES PERSONNALISÉES : image choisie par l'utilisateur pour un jeu, copiée en PNG dans
    /// le dossier de données d'ONYX (jaquettes\&lt;empreinte de la clé&gt;.png). Elle passe avant la
    /// jaquette officielle. « Jaquette d'origine » met la copie à la Corbeille.
    /// </summary>
    internal static class JaquettesPerso
    {
        private const int HauteurMax = 1200;   // au-delà, l'image est réduite (mémoire, tuiles)
        private static readonly Dictionary<string, Image> _cache = new Dictionary<string, Image>(StringComparer.Ordinal);
        private static readonly object _verrou = new object();

        private static string Dossier { get { return AppPaths.File("jaquettes"); } }

        private static string Chemin(string nomJeu)
        {
            string cle = BibliothequeJeux.Cle(nomJeu);
            if (cle.Length == 0) return null;
            byte[] h = SHA1.HashData(Encoding.UTF8.GetBytes(cle));
            return Path.Combine(Dossier, Convert.ToHexString(h, 0, 10).ToLowerInvariant() + ".png");
        }

        public static bool A(string nomJeu)
        {
            string p = Chemin(nomJeu);
            try { return p != null && File.Exists(p); } catch { return false; }
        }

        /// <summary>Jaquette choisie, ou null. Lue une fois puis gardée en mémoire (fichier non verrouillé).</summary>
        public static Image Pour(string nomJeu)
        {
            string p = Chemin(nomJeu);
            if (p == null) return null;
            lock (_verrou)
            {
                Image img;
                if (_cache.TryGetValue(p, out img)) return img;
                img = null;
                try
                {
                    if (File.Exists(p))
                        using (var ms = new MemoryStream(File.ReadAllBytes(p)))
                        using (var lu = Image.FromStream(ms)) img = new Bitmap(lu);
                }
                catch { img = null; }
                _cache[p] = img;
                return img;
            }
        }

        /// <summary>Copie l'image choisie (PNG, JPG, BMP, GIF, WebP si Windows sait la lire). Lève une exception lisible en cas d'échec.</summary>
        public static void Pose(string nomJeu, string source)
        {
            string p = Chemin(nomJeu);
            if (p == null) throw new InvalidOperationException("jeu sans nom.");
            Bitmap copie;
            using (var ms = new MemoryStream(File.ReadAllBytes(source)))
            using (var lu = Image.FromStream(ms))
            {
                double k = lu.Height > HauteurMax ? (double)HauteurMax / lu.Height : 1.0;
                int w = Math.Max(1, (int)Math.Round(lu.Width * k)), h = Math.Max(1, (int)Math.Round(lu.Height * k));
                copie = new Bitmap(w, h);
                using (Graphics g = Graphics.FromImage(copie))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(lu, new Rectangle(0, 0, w, h));
                }
            }
            Directory.CreateDirectory(Dossier);
            string tmp = p + ".tmp";
            using (copie) copie.Save(tmp, ImageFormat.Png);
            File.Move(tmp, p, true);
            Oublie(p);
        }

        /// <summary>Retour à la jaquette officielle : la copie part à la Corbeille.</summary>
        public static bool Retire(string nomJeu, IntPtr fenetre)
        {
            string p = Chemin(nomJeu);
            if (p == null || !File.Exists(p)) return true;
            bool ok = Storage.RecycleFiles(new List<string> { p }, fenetre) && !File.Exists(p);
            Oublie(p);
            return ok;
        }

        private static void Oublie(string p)
        {
            // L'ancienne image peut encore être peinte ce tour-ci : pas de Dispose, le ramasse-miettes s'en charge.
            lock (_verrou) { _cache.Remove(p); }
        }
    }
}
