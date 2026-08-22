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
        public sealed class Game
        {
            public string AppId; public string Name; public long SizeBytes; public string Dir;
            public long LastPlayedUnix;            // 0 = jamais lancé
            public DateTime? LastPlayed
            {
                get
                {
                    if (LastPlayedUnix <= 0) return null;
                    try { return DateTimeOffset.FromUnixTimeSeconds(LastPlayedUnix).LocalDateTime; } catch { return null; }
                }
            }
            /// <summary>Jours depuis la dernière partie ; −1 = jamais lancé.</summary>
            public int DaysIdle
            {
                get { var d = LastPlayed; return d == null ? -1 : (int)(DateTime.Now - d.Value).TotalDays; }
            }
        }

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
                long last = 0;
                var lp = Regex.Match(acfContent, "\"LastPlayed\"\\s*\"(\\d+)\"", RegexOptions.IgnoreCase);
                if (lp.Success) long.TryParse(lp.Groups[1].Value, out last);
                string name = nm.Groups[1].Value.Trim();
                if (name.Length == 0) return null;
                return new Game { AppId = id.Groups[1].Value, Name = name, SizeBytes = size, LastPlayedUnix = last };
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

        /// <summary>« Les jeux qui dorment » : installés mais pas lancés depuis N jours (ou jamais),
        /// du plus gros au plus petit. PUR sur une liste donnée → testable.</summary>
        public static List<Game> Dormant(List<Game> all, int idleDays, long minBytes)
        {
            var outp = new List<Game>();
            if (all == null) return outp;
            foreach (var g in all)
            {
                if (g == null || g.SizeBytes < minBytes) continue;
                int d = g.DaysIdle;
                if (d < 0 || d >= idleDays) outp.Add(g);   // jamais lancé, ou dort depuis longtemps
            }
            outp.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));
            return outp;
        }

        /// <summary>Espace total récupérable si on désinstallait cette liste.</summary>
        public static long TotalBytes(List<Game> games)
        {
            long t = 0;
            if (games != null) foreach (var g in games) if (g != null) t += g.SizeBytes;
            return t;
        }

        /// <summary>Texte pour le chat : les gros jeux qui dorment (le plus gros levier d'espace
        /// disque chez un joueur). Ne désinstalle RIEN — informe et laisse décider.</summary>
        public static string DormantText(int idleDays)
        {
            if (SteamPath() == null) return null;
            var all = Installed();
            if (all.Count == 0) return null;
            var dorm = Dormant(all, idleDays, 5L * 1073741824L);   // au moins 5 Go, sinon ça ne vaut pas le clic
            if (dorm.Count == 0)
                return "🎮 Jeux Steam : " + all.Count + " installés, aucun gros jeu inactif depuis " + (idleDays / 30)
                     + " mois. Rien d'évident à libérer de ce côté.";
            var sb = new System.Text.StringBuilder();
            sb.Append("🎮 Gros jeux qui DORMENT (pas lancés depuis ").Append(idleDays / 30).Append(" mois ou jamais) — ")
              .Append(Human(TotalBytes(dorm))).Append(" récupérables :\n");
            int n = 0;
            foreach (var g in dorm)
            {
                if (n++ >= 8) break;
                int d = g.DaysIdle;
                sb.Append("• ").Append(g.Name).Append("  —  ").Append(Human(g.SizeBytes))
                  .Append(d < 0 ? "  (JAMAIS lancé)" : "  (dernière partie il y a " + (d / 30) + " mois)").Append('\n');
            }
            if (dorm.Count > 8) sb.Append("…et ").Append(dorm.Count - 8).Append(" autre(s).\n");
            sb.Append("Désinstaller un jeu ne perd PAS ta progression (sauvegardes dans le cloud Steam) et il se "
                    + "réinstalle quand tu veux. C'est de loin le plus gros levier d'espace disque chez un joueur.");
            return sb.ToString();
        }

        /// <summary>« Mes jeux sont-ils sur SSD ? » — regroupe les jeux par disque et signale ceux
        /// installés sur un support LENT (chargements 3-5× plus longs sur mécanique). PUR sur les
        /// entrées fournies → testable sans matériel.</summary>
        public static string StorageText(List<Game> games, System.Collections.Generic.Dictionary<char, Diagnostics.DriveKind> kinds)
        {
            if (games == null || games.Count == 0) return null;
            var byDrive = new System.Collections.Generic.Dictionary<char, List<Game>>();
            foreach (var g in games)
            {
                if (g == null || string.IsNullOrEmpty(g.Dir) || g.Dir.Length < 2 || g.Dir[1] != ':') continue;
                char dl = char.ToUpperInvariant(g.Dir[0]);
                if (!byDrive.ContainsKey(dl)) byDrive[dl] = new List<Game>();
                byDrive[dl].Add(g);
            }
            if (byDrive.Count == 0) return null;

            var sb = new System.Text.StringBuilder();
            sb.Append("💽 Où sont installés tes jeux (le support change TOUT sur les temps de chargement) :\n");
            var slow = new List<Game>();
            long slowBytes = 0;
            foreach (var kv in byDrive)
            {
                Diagnostics.DriveKind k = null;
                kinds.TryGetValue(kv.Key, out k);
                long size = TotalBytes(kv.Value);
                string label = k != null ? k.Label : "type inconnu";
                string icon = k == null ? "• " : (k.MediaType == 3 ? "🐌 " : k.IsNvme ? "⚡ " : "✅ ");
                sb.Append(icon).Append(kv.Key).Append(": — ").Append(kv.Value.Count).Append(" jeu(x), ")
                  .Append(Human(size)).Append("  (").Append(label);
                if (k != null && !string.IsNullOrEmpty(k.Name)) sb.Append(", ").Append(k.Name);
                sb.Append(")\n");
                if (k != null && k.MediaType == 3) { slow.AddRange(kv.Value); slowBytes += size; }
            }

            if (slow.Count > 0)
            {
                slow.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));
                sb.Append("\n🐌 ").Append(slow.Count).Append(" jeu(x) sur disque MÉCANIQUE (").Append(Human(slowBytes))
                  .Append(") — chargements 3 à 5× plus longs, et des micro-freezes à l'ouverture des zones :\n");
                int n = 0;
                foreach (var g in slow) { if (n++ >= 5) break; sb.Append("   • ").Append(g.Name).Append("  (").Append(Human(g.SizeBytes)).Append(")\n"); }
                sb.Append("À faire (GRATUIT, sans re-télécharger) : dans Steam, clic droit sur le jeu → Propriétés → "
                        + "« Fichiers installés » → « Déplacer le dossier d'installation » → choisis ton SSD.");
            }
            else
            {
                sb.Append("\n✅ Aucun jeu sur disque mécanique : tes chargements sont déjà au mieux de ce que ta config permet.");
                bool anySata = false;
                foreach (var kv in byDrive)
                {
                    Diagnostics.DriveKind k;
                    if (kinds.TryGetValue(kv.Key, out k) && k != null && k.IsSsd && !k.IsNvme) anySata = true;
                }
                if (anySata) sb.Append("\n(Nuance : les jeux sur SSD SATA chargent ~2× moins vite que sur NVMe — utile à savoir "
                                     + "si tu veux placer TON jeu principal sur le disque le plus rapide.)");
            }
            return sb.ToString();
        }

        /// <summary>Version machine : lit les jeux installés et les types de disques.</summary>
        public static string StorageText()
        {
            try { return StorageText(Installed(), Diagnostics.DriveTypes()); }
            catch { return null; }
        }

        /// <summary>Ouvre la désinstallation Steam du jeu (Steam demande confirmation lui-même).</summary>
        public static bool Uninstall(string appId)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("steam://uninstall/" + appId) { UseShellExecute = true });
                return true;
            }
            catch { return false; }
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
