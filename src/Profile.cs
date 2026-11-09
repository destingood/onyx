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

        /// <summary>
        /// Tri PUR d'une entrée de zip : rend le chemin relatif à restaurer, ou null si l'entrée
        /// n'a pas la forme de ce qu'Export écrit — « bt-*.txt » / « bt-*.md » à la racine, ou un
        /// fichier directement sous « bt-savoir/ ». Tout le reste est ignoré : chemins remontants,
        /// sous-dossiers inattendus, autres extensions à la racine.
        /// </summary>
        public static string SafeEntry(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return null;
            string n = fullName.Replace('\\', '/');
            if (n.StartsWith("/") || n.Contains("..") || n.Contains(":")) return null;
            string[] parts = n.Split('/');
            foreach (string part in parts)
                if (part.Length == 0 || part.Trim() != part || part.EndsWith(".")
                    || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
            if (parts.Length == 1)
            {
                string f = parts[0];
                bool ok = f.StartsWith("bt-", StringComparison.OrdinalIgnoreCase)
                       && (f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".md", StringComparison.OrdinalIgnoreCase));
                return ok ? f : null;
            }
            if (parts.Length == 2 && parts[0].Equals("bt-savoir", StringComparison.OrdinalIgnoreCase))
                return "bt-savoir" + Path.DirectorySeparatorChar + parts[1];
            return null;
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
                        // On ne restaure QUE ce qu'un export produit. Le dossier de données est
                        // celui de l'exécutable : sans ce tri, un zip « de profil » reçu d'un tiers
                        // pouvait y déposer n'importe quel fichier — une DLL, un fichier de
                        // configuration du runtime — chargé ensuite par une app lancée en admin.
                        string rel = SafeEntry(e.FullName);
                        if (rel == null) continue;
                        string dest = Path.GetFullPath(Path.Combine(BaseDir, rel));
                        string rootSep = root.EndsWith(Path.DirectorySeparatorChar.ToString()) ? root : root + Path.DirectorySeparatorChar;
                        if (!dest.StartsWith(rootSep, StringComparison.OrdinalIgnoreCase)) continue;   // anti zip-slip
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        e.ExtractToFile(dest, true);
                    }
                return Path.GetFileName(newest);
            }
            catch { return null; }
        }
    }
}
