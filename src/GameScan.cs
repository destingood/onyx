using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace BTOptimizer
{
    /// <summary>
    /// Détection (lecture seule, best-effort) des jeux compétitifs installés, avec pour
    /// chacun la manipulation exacte qui débloque la limite de FPS. Aucune écriture :
    /// on ne touche JAMAIS aux fichiers des jeux, on explique quoi régler.
    /// </summary>
    internal static class GameScan
    {
        /// <summary>Clé IFEO officielle de Windows (PerfOptions = priorité au lancement, aucune injection).</summary>
        public const string IfeoKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";

        /// <summary>
        /// Exécutables des jeux compétitifs pour la priorité CPU « Haute » (PerfOptions).
        /// javaw.exe (Minecraft) est volontairement exclu : trop générique (toutes les applis Java).
        /// </summary>
        /// <summary>Nom du processus d'un jeu connu en cours d'exécution, ou null. Pour le MODE JEU AUTO précis.</summary>
        public static string RunningKnownGame()
        {
            try
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string exe in PriorityExes()) names.Add(Path.GetFileNameWithoutExtension(exe));
                foreach (Process p in Process.GetProcesses())
                {
                    try { if (names.Contains(p.ProcessName)) return p.ProcessName; }
                    catch { }
                    finally { try { p.Dispose(); } catch { } }
                }
            }
            catch { }
            return null;
        }

        public static string[] PriorityExes()
        {
            // Noms d'exécutables DISTINCTIFS uniquement (évite les faux positifs du MODE JEU AUTO).
            return new[]
            {
                "cs2.exe",                                     // Counter-Strike 2
                "VALORANT-Win64-Shipping.exe",                 // VALORANT
                "FortniteClient-Win64-Shipping.exe",           // Fortnite
                "Overwatch.exe",                               // Overwatch 2
                "r5apex.exe", "r5apex_dx12.exe",               // Apex Legends (DX11 / DX12)
                "cod.exe", "cod22-cod.exe", "cod23-cod.exe", "cod24-cod.exe", // Call of Duty (HQ / MWII / MWIII / BO6)
                "League of Legends.exe",                       // League of Legends
                "RocketLeague.exe",                            // Rocket League
                "RainbowSix.exe", "RainbowSix_Vulkan.exe",     // Rainbow Six Siege (DX11 / Vulkan)
                "dota2.exe",                                   // Dota 2
                "TslGame.exe",                                 // PUBG
                "MarvelRivals.exe",                            // Marvel Rivals
                "helldivers2.exe",                             // Helldivers 2
                "destiny2.exe",                                // Destiny 2
                "RustClient.exe",                              // Rust
                "Warframe.x64.exe",                            // Warframe
                "eldenring.exe",                               // Elden Ring
                "GTA5.exe", "GTA5_Enhanced.exe",               // GTA V (Legacy / Enhanced)
                "BF2042.exe",                                  // Battlefield 2042
                "FactoryGame-Win64-Shipping.exe"               // Satisfactory
            };
        }

        public class GameInfo
        {
            public string Name;          // nom affiché
            public string Uncap;         // comment passer le jeu à 500 FPS
            public bool Detected;        // trouvé sur ce PC
            public string SteamFolder;   // dossier steamapps\common (si jeu Steam)
            public string[] Dirs;        // dossiers d'installation classiques
            public string[] Keywords;    // mots-clés dans les clés de désinstallation
            public string InstallPath;   // dossier d'installation résolu (si trouvé sur disque)
        }

        /// <summary>Jeux connus + réglage FPS exact (le déblocage se fait DANS chaque jeu).</summary>
        public static List<GameInfo> Known()
        {
            return new List<GameInfo>
            {
                new GameInfo
                {
                    Name = "Counter-Strike 2",
                    Uncap = "Console : fps_max 0 (illimité). Ou Paramètres → Vidéo → Avancé → « Limite de FPS en partie » : 500+. V-Sync : DÉSACTIVÉ, Reflex : Activé.",
                    SteamFolder = "Counter-Strike Global Offensive",
                    Keywords = new[] { "Counter-Strike" }
                },
                new GameInfo
                {
                    Name = "VALORANT",
                    Uncap = "Paramètres → Vidéo → Général : « Limiter les FPS – Toujours : Non » (aucune limite active). V-Sync : Non, NVIDIA Reflex : Activé.",
                    Dirs = new[] { @"C:\Riot Games\VALORANT" },
                    Keywords = new[] { "VALORANT" }
                },
                new GameInfo
                {
                    Name = "Fortnite",
                    Uncap = "Paramètres vidéo → « Limite d'images/s : Illimitée » (ou 500). Pour TENIR 500 : mode de rendu « Performance ». V-Sync : Off, Reflex : Activé + Boost.",
                    Dirs = new[] { @"C:\Program Files\Epic Games\Fortnite" },
                    Keywords = new[] { "Fortnite" }
                },
                new GameInfo
                {
                    Name = "Overwatch 2",
                    Uncap = "Options → Vidéo : « Limite de fréquence d'images : Personnalisée » → 500 (le moteur accepte jusqu'à 600). V-Sync : Off, Reflex : Activé.",
                    SteamFolder = "Overwatch",
                    Dirs = new[] { @"C:\Program Files (x86)\Overwatch" },
                    Keywords = new[] { "Overwatch" }
                },
                new GameInfo
                {
                    Name = "Apex Legends",
                    Uncap = "Le menu plafonne à 300 : option de lancement « +fps_max unlimited » (ou +fps_max 500) dans Steam / EA app. V-Sync : Off.",
                    SteamFolder = "Apex Legends",
                    Keywords = new[] { "Apex Legends" }
                },
                new GameInfo
                {
                    Name = "Call of Duty (MW / Warzone)",
                    Uncap = "Paramètres → Graphismes → « Limite d'images par seconde : Personnalisée » → Jeu : 500. V-Sync : Off, Reflex : Activé.",
                    Dirs = new[] { @"C:\Program Files (x86)\Call of Duty" },
                    Keywords = new[] { "Call of Duty" }
                },
                new GameInfo
                {
                    Name = "League of Legends",
                    Uncap = "Options → Vidéo : « Limite d'images par seconde : Non plafonnée ». (Un cap FIXE proche de l'écran, ex. 500, donne un frametime plus stable.)",
                    Dirs = new[] { @"C:\Riot Games\League of Legends" },
                    Keywords = new[] { "League of Legends" }
                },
                new GameInfo
                {
                    Name = "Rocket League",
                    Uncap = "Le menu plafonne à 250 : ferme le jeu puis édite Documents\\My Games\\Rocket League\\TAGame\\Config\\TASystemSettings.ini → MaxFPS=500. V-Sync : Off.",
                    SteamFolder = "rocketleague",
                    Dirs = new[] { @"C:\Program Files\Epic Games\rocketleague" },
                    Keywords = new[] { "Rocket League" }
                },
                new GameInfo
                {
                    Name = "Rainbow Six Siege",
                    Uncap = "Affichage → « Limite d'IPS » au maximum, ou Documents\\My Games\\Rainbow Six - Siege\\<profil>\\GameSettings.ini → FPSLimit=0 (illimité). V-Sync : Off.",
                    SteamFolder = "Tom Clancy's Rainbow Six Siege",
                    Keywords = new[] { "Rainbow Six" }
                },
                new GameInfo
                {
                    Name = "Minecraft",
                    Uncap = "Options → Graphismes → « Images par seconde max : Illimité ». Pour TENIR 500 : mod Sodium (ou OptiFine).",
                    Dirs = new[] { Environment.ExpandEnvironmentVariables(@"%APPDATA%\.minecraft") },
                    Keywords = new[] { "Minecraft" }
                }
            };
        }

        /// <summary>Marque Detected sur chaque jeu trouvé (Steam, dossiers connus, clés de désinstallation).</summary>
        public static void Detect(List<GameInfo> games)
        {
            List<string> steamCommons = SteamCommonDirs();
            HashSet<string> uninstall = UninstallNames();

            foreach (GameInfo g in games)
            {
                try
                {
                    if (g.SteamFolder != null)
                        foreach (string common in steamCommons)
                        {
                            string full = Path.Combine(common, g.SteamFolder);
                            if (Directory.Exists(full)) { g.Detected = true; g.InstallPath = full; break; }
                        }

                    if (!g.Detected && g.Dirs != null)
                        foreach (string d in g.Dirs)
                            if (!string.IsNullOrEmpty(d) && Directory.Exists(d)) { g.Detected = true; g.InstallPath = d; break; }

                    if (!g.Detected && g.Keywords != null)
                        foreach (string name in uninstall)
                        {
                            foreach (string kw in g.Keywords)
                                if (name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) { g.Detected = true; break; }
                            if (g.Detected) break;
                        }
                }
                catch { }
            }
        }

        /// <summary>Tous les dossiers steamapps\common (bibliothèque principale + secondaires).</summary>
        private static List<string> SteamCommonDirs()
        {
            var result = new List<string>();
            try
            {
                string steam = null;
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                    if (k != null) steam = Convert.ToString(k.GetValue("SteamPath"));
                if (string.IsNullOrEmpty(steam)) return result;
                steam = steam.Replace('/', '\\');

                AddCommon(result, steam);

                string vdf = Path.Combine(steam, @"steamapps\libraryfolders.vdf");
                if (File.Exists(vdf))
                {
                    string text = File.ReadAllText(vdf);
                    foreach (Match m in Regex.Matches(text, "\"path\"\\s+\"([^\"]+)\""))
                        AddCommon(result, m.Groups[1].Value.Replace("\\\\", "\\"));
                }
            }
            catch { }
            return result;
        }

        private static void AddCommon(List<string> list, string steamRoot)
        {
            try
            {
                string common = Path.Combine(steamRoot, @"steamapps\common");
                if (!Directory.Exists(common)) return;
                foreach (string s in list)
                    if (string.Equals(s, common, StringComparison.OrdinalIgnoreCase)) return;
                list.Add(common);
            }
            catch { }
        }

        /// <summary>Noms des programmes installés (clés de désinstallation 64/32 bits + utilisateur).</summary>
        private static HashSet<string> UninstallNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] roots =
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };
            foreach (string root in roots)
            {
                CollectNames(Registry.LocalMachine, root, names);
                CollectNames(Registry.CurrentUser, root, names);
            }
            return names;
        }

        private static void CollectNames(RegistryKey hive, string root, HashSet<string> names)
        {
            try
            {
                using (RegistryKey k = hive.OpenSubKey(root))
                {
                    if (k == null) return;
                    foreach (string sub in k.GetSubKeyNames())
                    {
                        try
                        {
                            using (RegistryKey s = k.OpenSubKey(sub))
                            {
                                if (s == null) continue;
                                string name = Convert.ToString(s.GetValue("DisplayName"));
                                if (!string.IsNullOrEmpty(name)) names.Add(name);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }
    }
}
