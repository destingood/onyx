using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace BTOptimizer
{
    /// <summary>
    /// PROFIL ONYX : exporte dans UN zip tout ce qui fait « ton » ONYX — mémoire du Copilote,
    /// faits appris, journal de bord, réglages (Discord, Gardien), documents bt-savoir. Après une
    /// réinstallation de Windows : importe le zip, tout revient. (Les optimisations système, elles,
    /// vivent dans Windows — elles se réappliquent en 1 clic via « TOUT optimiser ».)
    /// </summary>
    internal static class Profile
    {
        private static string BaseDir { get { return AppPaths.DataDir; } }

        // Ce qui constitue le profil : les fichiers bt-*.txt / bt-*.md à côté de l'exe.
        private static IEnumerable<string> ProfileFiles()
        {
            var outp = new List<string>();
            try { outp.AddRange(Directory.EnumerateFiles(BaseDir, "bt-*.txt")); } catch { }
            try { outp.AddRange(Directory.EnumerateFiles(BaseDir, "bt-*.md")); } catch { }
            return outp;
        }

        /// <summary>Crée ONYX-profil-AAAAMMJJ-HHmm.zip (Bureau par défaut). Chemin complet, ou null.</summary>
        public static string Export(string destDir = null)
        {
            try
            {
                if (string.IsNullOrEmpty(destDir)) destDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string zip = Path.Combine(destDir, "ONYX-profil-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".zip");
                if (File.Exists(zip)) File.Delete(zip);
                using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
                {
                    int n = 0;
                    foreach (var f in ProfileFiles()) { try { z.CreateEntryFromFile(f, Path.GetFileName(f)); n++; } catch { } }
                    string savoir = Path.Combine(BaseDir, "bt-savoir");
                    if (Directory.Exists(savoir))
                        foreach (var f in Directory.EnumerateFiles(savoir, "*", SearchOption.AllDirectories))
                        { try { z.CreateEntryFromFile(f, "bt-savoir/" + Path.GetFileName(f)); n++; } catch { } }
                    if (n == 0) z.CreateEntry("profil-vide.txt");   // zip valide même sans donnée encore
                }
                return zip;
            }
            catch { return null; }
        }

        /// <summary>Restaure le ONYX-profil-*.zip LE PLUS RÉCENT du Bureau (ou srcDir). L'existant est
        /// d'abord copié dans bt-avant-import-&lt;date&gt;\ — retour arrière toujours possible.</summary>
        public static string ImportNewest(string srcDir = null)
        {
            try
            {
                if (string.IsNullOrEmpty(srcDir)) srcDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string newest = null; DateTime best = DateTime.MinValue;
                foreach (var f in Directory.EnumerateFiles(srcDir, "ONYX-profil-*.zip"))
                {
                    var t = File.GetLastWriteTime(f);
                    if (t > best) { best = t; newest = f; }
                }
                if (newest == null) return null;
                // Filet : l'état ACTUEL est sauvegardé avant d'écraser quoi que ce soit.
                string bak = Path.Combine(BaseDir, "bt-avant-import-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                Directory.CreateDirectory(bak);
                foreach (var f in ProfileFiles()) { try { File.Copy(f, Path.Combine(bak, Path.GetFileName(f)), true); } catch { } }
                string root = Path.GetFullPath(BaseDir);
                using (var z = ZipFile.OpenRead(newest))
                    foreach (var e in z.Entries)
                    {
                        if (string.IsNullOrEmpty(e.Name)) continue;               // dossier pur
                        string rel = e.FullName.Replace('/', Path.DirectorySeparatorChar);
                        string dest = Path.GetFullPath(Path.Combine(BaseDir, rel));
                        if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;   // anti zip-slip
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        e.ExtractToFile(dest, true);
                    }
                return Path.GetFileName(newest);
            }
            catch { return null; }
        }
    }
}
