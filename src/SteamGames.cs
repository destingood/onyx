using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace BTOptimizer
{
    /// <summary>
    /// JEUX STEAM INSTALLÉS + VÉRIFICATION DES FICHIERS. Quand un jeu plante au lancement, crashe en
    /// boucle ou a subi un disque plein / une coupure de courant, LA solution officielle est de faire
    /// vérifier ses fichiers par Steam — mais personne ne sait où c'est. ONYX liste les jeux installés
    /// (toutes bibliothèques, y compris sur d'autres disques) et lance la vérification en un clic
    /// (steam://validate/&lt;appid&gt;). Rien n'est supprimé : Steam re-télécharge ce qui manque.
    /// </summary>
    internal static class SteamGames
    {
        public sealed class Game { public string AppId; public string Name; public long SizeBytes; public string Dir; }

        /// <summary>Dossier d'installation de Steam, ou null s'il n'est pas installé.</summary>
        public static string SteamPath()
        {
            foreach (var view in new[] { Microsoft.Win32.RegistryView.Registry32, Microsoft.Win32.RegistryView.Registry64 })
            {
                try
                {
                    using (var b = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, view))
                    using (var k = b.OpenSubKey(@"SOFTWARE\Valve\Steam"))
                    {
                        if (k == null) continue;
                        string p = Convert.ToString(k.GetValue("InstallPath"));
                        if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) return p;
                    }
                }
                catch { }
            }
            foreach (var p in new[] { @"C:\Program Files (x86)\Steam", @"C:\Program Files\Steam" })
                if (Directory.Exists(p)) return p;
            return null;
        }

        /// <summary>Chemins « steamapps » de TOUTES les bibliothèques (Steam éclate souvent les jeux
        /// sur plusieurs disques). PUR à partir du contenu de libraryfolders.vdf → testable.</summary>
        public static List<string> ParseLibraryPaths(string vdfContent, string defaultSteamPath)
        {
            var outp = new List<string>();
            try
            {
                if (!string.IsNullOrEmpty(defaultSteamPath))
                    outp.Add(Path.Combine(defaultSteamPath, "steamapps"));
                if (!string.IsNullOrEmpty(vdfContent))
                    foreach (Match m in Regex.Matches(vdfContent, "\"path\"\\s*\"([^\"]+)\""))
                    {
                        string p = m.Groups[1].Value.Replace("\\\\", "\\");
                        string sa = Path.Combine(p, "steamapps");
                        if (!outp.Contains(sa)) outp.Add(sa);
                    }
            }
            catch { }
            return outp;
        }

        /// <summary>Lit un appmanifest_*.acf. PUR → testable sans Steam.</summary>
        public static Game ParseManifest(string acfContent)
        {
            try
            {
                if (string.IsNullOrEmpty(acfContent)) return null;
                var id = Regex.Match(acfContent, "\"appid\"\\s*\"(\\d+)\"", RegexOptions.IgnoreCase);
                var nm = Regex.Match(acfContent, "\"name\"\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);
                if (!id.Success || !nm.Success) return null;
                long size = 0;
                var sz = Regex.Match(acfContent, "\"SizeOnDisk\"\\s*\"(\\d+)\"", RegexOptions.IgnoreCase);
                if (sz.Success) long.TryParse(sz.Groups[1].Value, out size);
                string name = nm.Groups[1].Value.Trim();
                if (name.Length == 0) return null;
                return new Game { AppId = id.Groups[1].Value, Name = name, SizeBytes = size };
            }
            catch { return null; }
        }

        // Composants techniques installés par Steam : ce ne sont pas des jeux.
        private static bool IsRedistributable(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            return n.Contains("redistributable") || n.Contains("steamworks") || n.Contains("proton")
                || n.Contains("steam linux runtime") || n.Contains("directx");
        }

        /// <summary>Tous les jeux Steam installés, triés du plus gros au plus petit.</summary>
        public static List<Game> Installed()
        {
            var games = new List<Game>();
            try
            {
                string steam = SteamPath();
                if (steam == null) return games;
                string vdf = null;
                try
                {
                    string f = Path.Combine(steam, @"steamapps\libraryfolders.vdf");
                    if (File.Exists(f)) vdf = File.ReadAllText(f);
                }
                catch { }
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var lib in ParseLibraryPaths(vdf, steam))
                {
                    if (!Directory.Exists(lib)) continue;
                    foreach (var acf in Directory.GetFiles(lib, "appmanifest_*.acf"))
                    {
                        Game g = null;
                        try { g = ParseManifest(File.ReadAllText(acf)); } catch { }
                        if (g == null || IsRedistributable(g.Name) || !seen.Add(g.AppId)) continue;
                        g.Dir = lib;
                        games.Add(g);
                    }
                }
                games.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));
            }
            catch { }
            return games;
        }

        /// <summary>Demande à Steam de VÉRIFIER les fichiers du jeu (rien n'est supprimé : Steam
        /// re-télécharge uniquement ce qui est corrompu ou manquant).</summary>
        public static bool Validate(string appId)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("steam://validate/" + appId) { UseShellExecute = true });
                return true;
            }
            catch { return false; }
        }

        public static string Human(long bytes)
        {
            if (bytes >= 1073741824L) return Math.Round(bytes / 1073741824.0, 1) + " Go";
            if (bytes >= 1048576L) return (bytes / 1048576L) + " Mo";
            return bytes + " o";
        }
    }
}
