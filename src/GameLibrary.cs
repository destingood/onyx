using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace BTOptimizer
{
    /// <summary>
    /// Scanner générique multi-plateforme : énumère TOUS les jeux réellement installés
    /// (pas seulement un catalogue connu) en lisant les manifestes / le registre LOCAUX de
    /// chaque launcher. Aucune connexion réseau, aucun login. Chaque scanner est isolé : un
    /// launcher absent ou un fichier cassé n'empêche jamais les autres de fonctionner.
    /// </summary>
    internal static class GameLibrary
    {
        public sealed class InstalledGame
        {
            public string Name;         // nom affiché
            public string Launcher;     // STEAM / EPIC / GOG / UBISOFT / EA / BATTLE.NET / XBOX / RIOT
            public string InstallDir;   // dossier d'installation (peut être null)
            public string Exe;          // exécutable principal si connu (sinon null)
            public int SteamAppId;      // AppID Steam pour la jaquette officielle (0 sinon)
        }

        /// <summary>Agrège tous les launchers puis dédoublonne par (launcher + nom).</summary>
        public static List<InstalledGame> ScanAll()
        {
            var all = new List<InstalledGame>();
            var scanners = new Func<List<InstalledGame>>[]
            {
                ScanSteam, ScanEpic, ScanGog, ScanUbisoft, ScanRiot, ScanXbox, ScanEaGames, ScanInstalledPrograms
            };
            foreach (Func<List<InstalledGame>> scan in scanners)
            {
                try { all.AddRange(scan() ?? new List<InstalledGame>()); }
                catch { }   // un launcher qui casse ne doit jamais faire tomber le scan complet
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<InstalledGame>();
            foreach (InstalledGame g in all)
            {
                if (string.IsNullOrWhiteSpace(g.Name)) continue;
                string key = (g.Launcher ?? "") + "|" + g.Name.Trim().ToLowerInvariant();
                if (seen.Add(key)) result.Add(g);
            }
            BackfillSteamIds(result);
            result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        /// <summary>
        /// Beaucoup de jeux Steam sont AUSSI déclarés dans le registre, mais sans AppID : ils
        /// s'affichaient donc avec un placeholder au lieu de leur jaquette. Si leur dossier
        /// d'installation vit dans une bibliothèque Steam, on retrouve l'AppID via le dossier
        /// (« steamapps\common\<dossier> ») déjà résolu par le scan Steam.
        /// </summary>
        private static void BackfillSteamIds(List<InstalledGame> games)
        {
            var byFolder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (InstalledGame g in games)
            {
                if (g.SteamAppId <= 0 || string.IsNullOrEmpty(g.InstallDir)) continue;
                string leaf = SafeLeaf(g.InstallDir);
                if (leaf.Length > 0 && !byFolder.ContainsKey(leaf)) byFolder[leaf] = g.SteamAppId;
            }
            if (byFolder.Count == 0) return;

            foreach (InstalledGame g in games)
            {
                if (g.SteamAppId > 0 || string.IsNullOrEmpty(g.InstallDir)) continue;
                if (g.InstallDir.IndexOf(@"steamapps\common", StringComparison.OrdinalIgnoreCase) < 0) continue;
                int id;
                if (byFolder.TryGetValue(SafeLeaf(g.InstallDir), out id)) g.SteamAppId = id;
            }
        }

        private static string SafeLeaf(string p)
        {
            try { return Path.GetFileName(p.TrimEnd('\\', '/')) ?? ""; }
            catch { return ""; }
        }

        // Écartés : désinstalleurs, anti-triche, utilitaires — jamais le jeu lui-même.
        private static readonly string[] ExeNoise =
        {
            "unins", "uninstall", "crashhandler", "crashreport", "crashpad", "anticheat",
            "vcredist", "directx", "dxsetup", "setup", "redist", "config", "settings", "cleanup", "helper"
        };

        /// <summary>
        /// Devine l'exécutable principal d'un jeu : le plus GROS .exe à la racine du dossier
        /// (puis un niveau en dessous), hors désinstalleurs / anti-triche / utilitaires.
        /// Heuristique simple mais fiable : le binaire du jeu pèse toujours bien plus lourd.
        /// Sert à la fois au bouton « Lancer » et à l'extraction de l'icône du jeu.
        /// </summary>
        public static string GuessMainExe(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return null;
            try { if (!Directory.Exists(dir)) return null; } catch { return null; }

            string[] subs;
            try { subs = Directory.GetDirectories(dir); } catch { subs = new string[0]; }
            var scan = new string[subs.Length + 1];
            scan[0] = dir;
            Array.Copy(subs, 0, scan, 1, subs.Length);

            string best = null; long bestLen = 0;
            foreach (string d in scan)
            {
                string[] files;
                try { files = Directory.GetFiles(d, "*.exe"); } catch { continue; }
                foreach (string f in files)
                {
                    string leaf = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                    bool noisy = false;
                    foreach (string n in ExeNoise) if (leaf.Contains(n)) { noisy = true; break; }
                    if (noisy) continue;
                    long len = 0;
                    try { len = new FileInfo(f).Length; } catch { }
                    if (len > bestLen) { bestLen = len; best = f; }
                }
            }
            return best;
        }

        // =========================================================== STEAM (.acf)
        private static List<InstalledGame> ScanSteam()
        {
            var games = new List<InstalledGame>();
            string root = GameScan.SteamRoot();
            if (root == null) return games;

            foreach (string lib in SteamLibraries(root))
            {
                string apps = Path.Combine(lib, "steamapps");
                if (!Directory.Exists(apps)) continue;
                foreach (string acf in Directory.GetFiles(apps, "appmanifest_*.acf"))
                {
                    try
                    {
                        string txt = File.ReadAllText(acf);
                        string name = VdfValue(txt, "name");
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        string dir = VdfValue(txt, "installdir");
                        int id; int.TryParse(VdfValue(txt, "appid"), out id);
                        // On saute les outils/redist Steam (Steamworks Common Redistributables, etc.).
                        if (name.IndexOf("Redistributable", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        string install = dir != null ? Path.Combine(apps, "common", dir) : null;
                        games.Add(new InstalledGame { Name = name, Launcher = "STEAM", InstallDir = install, SteamAppId = id });
                    }
                    catch { }
                }
            }
            return games;
        }

        // Bibliothèque principale + secondaires (libraryfolders.vdf).
        private static IEnumerable<string> SteamLibraries(string root)
        {
            var libs = new List<string> { root };
            try
            {
                string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf))
                    foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s*\"([^\"]+)\""))
                    {
                        string p = m.Groups[1].Value.Replace("\\\\", "\\");
                        if (Directory.Exists(p)) libs.Add(p);
                    }
            }
            catch { }
            return libs.Distinct(StringComparer.OrdinalIgnoreCase);
        }

        // Extrait "clé" "valeur" d'un fichier VDF/ACF (format KeyValues de Valve).
        private static string VdfValue(string txt, string key)
        {
            Match m = Regex.Match(txt, "\"" + Regex.Escape(key) + "\"\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : null;
        }

        // =========================================================== EPIC (.item JSON)
        private static List<InstalledGame> ScanEpic()
        {
            var games = new List<InstalledGame>();
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                @"Epic\EpicGamesLauncher\Data\Manifests");
            if (!Directory.Exists(dir)) return games;

            foreach (string item in Directory.GetFiles(dir, "*.item"))
            {
                try
                {
                    using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(item)))
                    {
                        JsonElement r = doc.RootElement;
                        string name = GetStr(r, "DisplayName");
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        games.Add(new InstalledGame
                        {
                            Name = name,
                            Launcher = "EPIC",
                            InstallDir = GetStr(r, "InstallLocation"),
                            Exe = GetStr(r, "LaunchExecutable")
                        });
                    }
                }
                catch { }
            }
            return games;
        }

        private static string GetStr(JsonElement e, string prop)
        {
            JsonElement v;
            return e.TryGetProperty(prop, out v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        }

        // =========================================================== GOG (registre)
        private static List<InstalledGame> ScanGog()
        {
            var games = new List<InstalledGame>();
            try
            {
                using (RegistryKey baseK = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
                using (RegistryKey root = baseK.OpenSubKey(@"SOFTWARE\GOG.com\Games"))
                {
                    if (root == null) return games;
                    foreach (string sub in root.GetSubKeyNames())
                        using (RegistryKey g = root.OpenSubKey(sub))
                        {
                            if (g == null) continue;
                            string name = Convert.ToString(g.GetValue("gameName"));
                            if (string.IsNullOrWhiteSpace(name)) continue;
                            games.Add(new InstalledGame
                            {
                                Name = name,
                                Launcher = "GOG",
                                InstallDir = Convert.ToString(g.GetValue("path")),
                                Exe = Convert.ToString(g.GetValue("exe"))
                            });
                        }
                }
            }
            catch { }
            return games;
        }

        // =========================================================== UBISOFT CONNECT (registre)
        private static List<InstalledGame> ScanUbisoft()
        {
            var games = new List<InstalledGame>();
            try
            {
                using (RegistryKey baseK = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
                using (RegistryKey installs = baseK.OpenSubKey(@"SOFTWARE\Ubisoft\Launcher\Installs"))
                {
                    if (installs == null) return games;
                    foreach (string id in installs.GetSubKeyNames())
                        using (RegistryKey g = installs.OpenSubKey(id))
                        {
                            string dir = g == null ? null : Convert.ToString(g.GetValue("InstallDir"));
                            if (string.IsNullOrWhiteSpace(dir)) continue;
                            dir = dir.Replace('/', '\\').TrimEnd('\\');
                            // Le registre Ubisoft ne stocke pas le nom lisible → on prend le dossier.
                            games.Add(new InstalledGame { Name = Path.GetFileName(dir), Launcher = "UBISOFT", InstallDir = dir });
                        }
                }
            }
            catch { }
            return games;
        }

        // =========================================================== RIOT (dossiers)
        private static List<InstalledGame> ScanRiot()
        {
            var games = new List<InstalledGame>();
            foreach (string drive in FixedDrives())
            {
                string root = Path.Combine(drive, "Riot Games");
                if (!Directory.Exists(root)) continue;
                foreach (string sub in Directory.GetDirectories(root))
                {
                    string name = Path.GetFileName(sub);
                    if (name.IndexOf("Riot Client", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    games.Add(new InstalledGame { Name = name, Launcher = "RIOT", InstallDir = sub });
                }
            }
            return games;
        }

        // =========================================================== EA APP / ORIGIN (marqueur fichier)
        // Les jeux installés par l'EA app ne déclarent RIEN dans le registre de désinstallation
        // (vérifié sur Battlefield 6 : aucune clé, aucun éditeur). En revanche chacun contient
        // « __Installer\installerdata.xml » — marqueur fiable et propre à EA/Origin. On teste ce
        // marqueur sur les dossiers de 1er niveau de chaque disque + les conteneurs habituels.
        private static readonly string[] EaContainers =
        {
            @"Program Files\EA Games", @"Program Files (x86)\EA Games",
            @"Program Files\Origin Games", @"Program Files (x86)\Origin Games",
            "EA Games", "Origin Games", "Games"
        };

        private static List<InstalledGame> ScanEaGames()
        {
            var games = new List<InstalledGame>();
            var candidates = new List<string>();
            foreach (string drive in FixedDrives())
            {
                candidates.AddRange(SafeDirs(drive));                                   // ex. F:\Battlefield 6
                foreach (string c in EaContainers) candidates.AddRange(SafeDirs(Path.Combine(drive, c)));
            }
            foreach (string dir in candidates)
            {
                try
                {
                    if (!File.Exists(Path.Combine(dir, "__Installer", "installerdata.xml"))) continue;
                    string name = Path.GetFileName(dir);
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    games.Add(new InstalledGame { Name = name, Launcher = "EA", InstallDir = dir });
                }
                catch { }
            }
            return games;
        }

        private static string[] SafeDirs(string path)
        {
            try { return Directory.Exists(path) ? Directory.GetDirectories(path) : new string[0]; }
            catch { return new string[0]; }
        }

        // =========================================================== PROGRAMMES INSTALLÉS (registre)
        // Rattrape TOUT ce qui n'a pas de manifeste propre : Battle.net (éditeur Blizzard), EA, et
        // surtout les jeux posés par un simple setup.exe (hors launcher). On lit HKLM 32+64 ET HKCU
        // (beaucoup de jeux s'enregistrent par utilisateur — c'était le trou du scanner précédent).
        // On exige une InstallLocation (signal fort « vrai logiciel installé ») et on filtre le
        // bruit (pilotes, runtimes, utilitaires système) pour ne pas polluer la bibliothèque.
        private static readonly string[] NoisePublishers =
        {
            // Systèmes / pilotes / suites bureautiques
            "Microsoft", "Intel", "NVIDIA", "Advanced Micro Devices", "Realtek", "Google", "Apple",
            "Oracle", "Adobe", "Mozilla", "VideoLAN", "Samsung", "Xiaomi", "SafeNet", "Nmap Project",
            // Outils de développement / runtimes
            "Docker", "Git Development", "JetBrains", "Python", "Eclipse Adoptium", "Anysphere",
            "opencode", "Astral Software", "BurntSushi", "Gyan", "jrsoftware", "Ollama", "namazso",
            // Utilitaires système / tweak / monitoring
            "Piriform", "CPUID", "Crystal Dew World", "Resplendence", "NoVirusThanks", "Parsec",
            "Denuvo", "Malwarebytes", "Dropbox", "Bitsum", "TechPowerUp", "Antibody Software",
            "TeamViewer", "OCCT", "Logitech", "Razer", "Corsair",
            // Applis grand public (pas des jeux)
            "Opera Software", "Discord Inc", "spikehd", "TikTok", "Smart Code OOD", "TechEnClair", "yanis",
            // Nos propres logiciels / concurrents (ne pas s'auto-lister dans la bibliothèque)
            "BT Optimizer", "Fluide", "FPSDoctor"
        };

        // Noms exacts sans éditeur exploitable. Correspondance EXACTE obligatoire : un filtre
        // « contient » sur « bun » ou « uv » massacrerait de vrais jeux (« Bunny », « Survival »…).
        private static readonly string[] NoiseExactNames =
        {
            "bun", "uv", "cursor (user)", "opencode", "tauri-app", "ffmpeg", "npcap"
        };

        private static readonly string[] NoiseNames =
        {
            "driver", "runtime", "redistributable", "redistribuable", "sdk", "toolkit", "framework",
            "visual c++", ".net", "language pack", "module linguistique", "service pack", "webview",
            "vcredist", "directx", "physx", "anti-cheat", "anticheat", "antivirus", "winrar",
            "7-zip", "notepad", "visual studio", "afterburner", "hwinfo", "hwmonitor", "cpu-z",
            "gpu-z", "crystaldisk", "ccleaner", "latencymon", "onedrive"
        };

        // Launchers eux-mêmes : exclus en correspondance EXACTE seulement, pour ne pas éliminer
        // un vrai jeu (ex. « The Seven Deadly Sins: Origin » ne doit pas sauter à cause d'« Origin »).
        private static readonly string[] LauncherApps =
        {
            "battle.net", "ea app", "ea desktop", "origin", "epic games launcher", "ubisoft connect",
            "uplay", "gog galaxy", "steam", "riot client", "rockstar games launcher"
        };

        private static List<InstalledGame> ScanInstalledPrograms()
        {
            var games = new List<InstalledGame>();
            const string Un = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
            const string Un32 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

            var roots = new List<RegistryKey>();
            try { roots.Add(Registry.LocalMachine.OpenSubKey(Un)); } catch { }
            try { roots.Add(Registry.LocalMachine.OpenSubKey(Un32)); } catch { }
            try { roots.Add(Registry.CurrentUser.OpenSubKey(Un)); } catch { }
            try { roots.Add(Registry.CurrentUser.OpenSubKey(Un32)); } catch { }

            foreach (RegistryKey k in roots)
            {
                if (k == null) continue;
                try
                {
                    foreach (string sub in k.GetSubKeyNames())
                        using (RegistryKey e = k.OpenSubKey(sub))
                        {
                            if (e == null) continue;
                            string name = Convert.ToString(e.GetValue("DisplayName"));
                            string loc = Convert.ToString(e.GetValue("InstallLocation"));
                            string pub = Convert.ToString(e.GetValue("Publisher")) ?? "";
                            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(loc)) continue;
                            if (Convert.ToString(e.GetValue("SystemComponent")) == "1") continue;

                            // Clé ORPHELINE : le jeu a été désinstallé mais son entrée de registre
                            // subsiste (constaté sur DARK SOULS REMASTERED et Disney Dreamlight
                            // Valley, dont le dossier steamapps\common n'existe plus). Sans ce
                            // test on affiche des jeux fantômes, sans icône ni jaquette.
                            bool locExists;
                            try { locExists = Directory.Exists(loc); } catch { locExists = false; }
                            if (!locExists) continue;
                            string trimmed = name.Trim();
                            if (LauncherApps.Any(a => string.Equals(trimmed, a, StringComparison.OrdinalIgnoreCase))) continue;
                            if (NoiseExactNames.Any(a => string.Equals(trimmed, a, StringComparison.OrdinalIgnoreCase))) continue;
                            if (NoisePublishers.Any(p => pub.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                            if (NoiseNames.Any(n => trimmed.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                            games.Add(new InstalledGame
                            {
                                Name = trimmed,
                                Launcher = LauncherFor(loc, pub),
                                InstallDir = loc
                            });
                        }
                }
                catch { }
                finally { try { k.Dispose(); } catch { } }
            }
            return games;
        }

        // Devine la plateforme depuis le chemin d'installation ou l'éditeur.
        // NB : on se fie à l'ÉDITEUR pour Blizzard, car le dossier peut s'appeler n'importe comment.
        private static string LauncherFor(string loc, string pub)
        {
            string l = (loc ?? "").ToLowerInvariant();
            string p = (pub ?? "").ToLowerInvariant();
            if (p.Contains("blizzard")) return "BATTLE.NET";
            if (l.Contains("steamapps")) return "STEAM";
            if (l.Contains("epic games")) return "EPIC";
            if (l.Contains("gog")) return "GOG";
            if (l.Contains("ubisoft")) return "UBISOFT";
            if (p.Contains("electronic arts") || l.Contains("ea games") || l.Contains("origin games")) return "EA";
            if (l.Contains("riot games")) return "RIOT";
            return "PC";
        }

        // =========================================================== XBOX / GAME PASS
        // Les jeux Xbox/GamePass s'installent dans un dossier "XboxGames" par disque
        // (+ éventuellement un Content\). On énumère ces dossiers (best-effort, sans WinRT).
        private static List<InstalledGame> ScanXbox()
        {
            var games = new List<InstalledGame>();
            foreach (string drive in FixedDrives())
            {
                string root = Path.Combine(drive, "XboxGames");
                if (!Directory.Exists(root)) continue;
                foreach (string sub in Directory.GetDirectories(root))
                {
                    string name = Path.GetFileName(sub);
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    if (string.Equals(name, "GameSave", StringComparison.OrdinalIgnoreCase)) continue;  // dossier système, pas un jeu
                    string content = Path.Combine(sub, "Content");
                    games.Add(new InstalledGame
                    {
                        Name = name,
                        Launcher = "XBOX",
                        InstallDir = Directory.Exists(content) ? content : sub
                    });
                }
            }
            return games;
        }

        // ----------------------------------------------------------- utilitaires
        private static IEnumerable<string> FixedDrives()
        {
            DriveInfo[] drives;
            try { drives = DriveInfo.GetDrives(); }
            catch { yield break; }
            foreach (DriveInfo d in drives)
            {
                bool ok = false;
                try { ok = d.DriveType == DriveType.Fixed && d.IsReady; } catch { }
                if (ok) yield return d.RootDirectory.FullName;
            }
        }
    }
}
