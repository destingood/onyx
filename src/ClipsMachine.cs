using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// BIBLIOTHÈQUE DE CLIPS — branchement : dossiers balayés (Captures de Windows, Vidéos, dossiers
    /// ajoutés par l'utilisateur dans bt-clips-dossiers.txt), balayage borné, vignettes de
    /// l'Explorateur. Lecture seule : la seule suppression possible passe par la Corbeille
    /// (Storage.RecycleFiles), jamais par File.Delete.
    /// </summary>
    internal static partial class Clips
    {
        private const string FichierDossiers = "bt-clips-dossiers.txt";
        public const int MaxFichiers = 5000;
        private const int ProfondeurMax = 3;

        // FOLDERID_AppCaptures : le dossier « Captures » de la Game Bar (déplaçable par l'utilisateur).
        private static readonly Guid DossierCapturesId = new Guid("EDC0FE71-98D8-4F4A-B920-C8DC133CB165");

        [DllImport("shell32.dll")]
        private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);

        public static string DossierCaptures()
        {
            IntPtr p = IntPtr.Zero;
            try
            {
                if (SHGetKnownFolderPath(DossierCapturesId, 0, IntPtr.Zero, out p) == 0)
                {
                    string s = Marshal.PtrToStringUni(p);
                    if (!string.IsNullOrEmpty(s)) return s;
                }
            }
            catch { }
            finally { if (p != IntPtr.Zero) Marshal.FreeCoTaskMem(p); }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos", "Captures");
        }

        public static List<string> DossiersAjoutes()
        {
            var res = new List<string>();
            try
            {
                string p = AppPaths.File(FichierDossiers);
                if (!File.Exists(p)) return res;
                foreach (string l in File.ReadAllLines(p, Encoding.UTF8))
                    if (l.Trim().Length > 0 && !res.Exists(x => string.Equals(x, l.Trim(), StringComparison.OrdinalIgnoreCase))) res.Add(l.Trim());
            }
            catch { }
            return res;
        }

        public static void AjouteDossier(string dossier)
        {
            if (string.IsNullOrEmpty(dossier)) return;
            List<string> l = DossiersAjoutes();
            if (l.Exists(x => string.Equals(x, dossier, StringComparison.OrdinalIgnoreCase))) return;
            l.Add(dossier);
            File.WriteAllLines(AppPaths.File(FichierDossiers), l, new UTF8Encoding(false));
        }

        /// <summary>Captures, Vidéos, puis dossiers ajoutés ; seulement ceux qui existent, sans doublon.</summary>
        public static List<string> Racines()
        {
            var res = new List<string>();
            var candidats = new List<string> { DossierCaptures() };
            try { candidats.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)); } catch { }
            candidats.AddRange(DossiersAjoutes());
            foreach (string c in candidats)
            {
                if (string.IsNullOrEmpty(c)) continue;
                string n;
                try { n = Path.GetFullPath(c).TrimEnd('\\'); } catch { continue; }
                if (!Directory.Exists(n) || res.Exists(x => string.Equals(x, n, StringComparison.OrdinalIgnoreCase))) continue;
                res.Add(n);
            }
            return res;
        }

        /// <summary>
        /// Vidéos sous les racines, profondeur ≤ 3, au plus <paramref name="maxFichiers"/>. La racine la
        /// plus précise l'emporte (Vidéos\Captures range ses clips d'après le nom, pas en « Captures »).
        /// </summary>
        public static List<Clip> Balaye(IEnumerable<string> racines, int maxFichiers)
        {
            var res = new List<Clip>();
            var vus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ordre = new List<string>(racines ?? new string[0]);
            ordre.Sort((a, b) => b.Length.CompareTo(a.Length));
            foreach (string r in ordre)
            {
                if (res.Count >= maxFichiers) break;
                Parcourt(r, r, 0, maxFichiers, res, vus);
            }
            return res;
        }

        private static void Parcourt(string racine, string dossier, int profondeur, int max, List<Clip> res, HashSet<string> vus)
        {
            if (res.Count >= max) return;
            try
            {
                var d = new DirectoryInfo(dossier);
                if (profondeur > 0 && (d.Attributes & FileAttributes.ReparsePoint) != 0) return;
                foreach (FileInfo f in d.EnumerateFiles())
                {
                    if (res.Count >= max) return;
                    if (!EstVideo(f.Name) || !vus.Add(f.FullName)) continue;
                    res.Add(new Clip { Chemin = f.FullName, Jeu = JeuDepuisChemin(racine, f.FullName), Racine = racine, Date = f.LastWriteTime, Octets = f.Length });
                }
                if (profondeur >= ProfondeurMax) return;
                foreach (DirectoryInfo s in d.EnumerateDirectories())
                    Parcourt(racine, s.FullName, profondeur + 1, max, res, vus);
            }
            catch { }   // dossier protégé ou disparu : on passe
        }

        // ------------------------------------------------------------------ renommer, limite d'espace

        /// <summary>
        /// Renomme le clip dans son dossier sans jamais écraser un autre fichier. Renvoie le nouveau
        /// chemin, ou null avec la raison (fichier ouvert dans un lecteur, nom déjà pris…).
        /// </summary>
        public static string Renomme(string chemin, string nom, out string erreur)
        {
            erreur = null;
            try
            {
                string cible = CheminRenomme(chemin, nom);
                if (string.Equals(cible, chemin, StringComparison.Ordinal)) return chemin;
                // Même nom à la casse près : c'est le même fichier, Windows accepte le changement.
                if (!string.Equals(cible, chemin, StringComparison.OrdinalIgnoreCase) && (File.Exists(cible) || Directory.Exists(cible)))
                { erreur = "Un fichier porte déjà ce nom dans ce dossier."; return null; }
                File.Move(chemin, cible);
                return cible;
            }
            catch (Exception ex) { erreur = ex.Message; return null; }
        }

        private const string FichierLimite = "bt-clips-limite.txt";

        /// <summary>Limite d'espace choisie pour les clips, en Go ; 0 = aucune.</summary>
        public static int LimiteGo()
        {
            try { string p = AppPaths.File(FichierLimite); return File.Exists(p) ? LitLimite(File.ReadAllText(p)) : 0; }
            catch { return 0; }
        }

        public static void EcritLimite(int go)
        {
            File.WriteAllText(AppPaths.File(FichierLimite), go.ToString(System.Globalization.CultureInfo.InvariantCulture), new UTF8Encoding(false));
        }

        // ------------------------------------------------------------------ vignettes de l'Explorateur

        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            [PreserveSig] int GetImage(Taille size, int flags, out IntPtr phbm);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Taille { public int cx, cy; }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(string path, IntPtr pbc,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr h);

        /// <summary>Vignette que l'Explorateur montre pour ce fichier (icône à défaut) ; null si impossible. À appeler sur un fil STA.</summary>
        public static Bitmap Vignette(string chemin, int largeur, int hauteur)
        {
            IShellItemImageFactory f = null;
            IntPtr h = IntPtr.Zero;
            try
            {
                SHCreateItemFromParsingName(chemin, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out f);
                if (f.GetImage(new Taille { cx = largeur, cy = hauteur }, 0, out h) != 0 || h == IntPtr.Zero) return null;
                using (Image img = Image.FromHbitmap(h)) return new Bitmap(img);
            }
            catch { return null; }
            finally
            {
                if (h != IntPtr.Zero) DeleteObject(h);
                if (f != null) try { Marshal.ReleaseComObject(f); } catch { }
            }
        }

        public static string TailleLisible(long octets)
        {
            if (octets >= 1L << 30) return (octets / (double)(1L << 30)).ToString("0.0") + " Go";
            if (octets >= 1L << 20) return (octets / (double)(1L << 20)).ToString("0") + " Mo";
            return Math.Max(1, octets / 1024) + " Ko";
        }
    }
}
