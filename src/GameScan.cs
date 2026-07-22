using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace BTOptimizer
{
    /// <summary>
    /// Détection (lecture seule, best-effort) des jeux installés, avec pour chacun la manipulation
    /// exacte qui débloque la limite de FPS. Aucune écriture : on ne touche JAMAIS aux fichiers des
    /// jeux, on explique quoi régler.
    /// </summary>
    internal static class GameScan
    {
        /// <summary>Clé IFEO officielle de Windows (PerfOptions = priorité au lancement, aucune injection).</summary>
        public const string IfeoKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";

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
                "hl2.exe",                                     // Team Fortress 2 / Source
                "TslGame.exe",                                 // PUBG
                "MarvelRivals.exe",                            // Marvel Rivals
                "helldivers2.exe",                             // Helldivers 2
                "destiny2.exe",                                // Destiny 2
                "RustClient.exe",                              // Rust
                "Warframe.x64.exe",                            // Warframe
                "eldenring.exe",                               // Elden Ring
                "GTA5.exe", "GTA5_Enhanced.exe",               // GTA V (Legacy / Enhanced)
                "BF2042.exe",                                  // Battlefield 2042
                "DiscoveryClient.exe",                         // The Finals
                "Cyberpunk2077.exe",                           // Cyberpunk 2077
                "bg3.exe", "bg3_dx11.exe",                     // Baldur's Gate 3
                "Palworld-Win64-Shipping.exe",                 // Palworld
                "PathOfExile.exe", "PathOfExileSteam.exe",     // Path of Exile
                "PathOfExile_x64.exe",                         // Path of Exile (x64)
                "deeprockgalactic.exe", "FSD-Win64-Shipping.exe", // Deep Rock Galactic
                "DeadByDaylight-Win64-Shipping.exe",           // Dead by Daylight
                "b1-Win64-Shipping.exe",                       // Black Myth: Wukong
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
            public string Store;         // launcher résolu (STEAM / EPIC / RIOT / BATTLE.NET) si détecté
        }

        // Fabrique compacte : garde le grand catalogue lisible.
        private static GameInfo G(string name, string uncap, string steam, string[] dirs, string[] keys)
        {
            return new GameInfo { Name = name, Uncap = uncap, SteamFolder = steam, Dirs = dirs, Keywords = keys };
        }

        private static string[] A(params string[] v) { return v; }

        // Conseil générique correct quand le jeu n'a pas de manip' spécifique connue.
        private const string GEN = "Options → Affichage / Graphismes : « Limite d'images/s » → Illimitée (ou 500) ; V-Sync : Désactivée ; NVIDIA Reflex / AMD Anti-Lag : Activé si disponible.";

        /// <summary>Grand catalogue de jeux connus + réglage FPS (le déblocage se fait DANS chaque jeu).</summary>
        public static List<GameInfo> Known()
        {
            return new List<GameInfo>
            {
                // ---- Compétitif / FPS / Battle royale ----
                G("Counter-Strike 2", "Console : fps_max 0 (illimité). Ou Paramètres → Vidéo → Avancé → « Limite de FPS en partie » : 500+. V-Sync : DÉSACTIVÉ, Reflex : Activé.",
                    "Counter-Strike Global Offensive", null, A("Counter-Strike")),
                G("VALORANT", "Paramètres → Vidéo → Général : « Limiter les FPS – Toujours : Non ». V-Sync : Non, NVIDIA Reflex : Activé.",
                    null, A(@"C:\Riot Games\VALORANT"), A("VALORANT")),
                G("Fortnite", "Paramètres vidéo → « Limite d'images/s : Illimitée » (ou 500). Mode de rendu « Performance » pour TENIR 500. V-Sync : Off, Reflex : Activé + Boost.",
                    null, A(@"C:\Program Files\Epic Games\Fortnite"), A("Fortnite")),
                G("Overwatch 2", "Options → Vidéo : « Limite de fréquence d'images : Personnalisée » → 500 (moteur jusqu'à 600). V-Sync : Off, Reflex : Activé.",
                    "Overwatch", A(@"C:\Program Files (x86)\Overwatch"), A("Overwatch")),
                G("Apex Legends", "Le menu plafonne à 300 : option de lancement « +fps_max unlimited » (ou +fps_max 500) dans Steam / EA app. V-Sync : Off.",
                    "Apex Legends", null, A("Apex Legends")),
                G("Call of Duty (MW / Warzone / BO6)", "Paramètres → Graphismes → « Limite d'images par seconde : Personnalisée » → Jeu : 500. V-Sync : Off, Reflex : Activé.",
                    "Call of Duty HQ", A(@"C:\Program Files (x86)\Call of Duty"), A("Call of Duty")),
                G("Rainbow Six Siege", "Affichage → « Limite d'IPS » au maximum, ou GameSettings.ini → FPSLimit=0 (illimité). V-Sync : Off.",
                    "Tom Clancy's Rainbow Six Siege", null, A("Rainbow Six")),
                G("PUBG: BATTLEGROUNDS", "Paramètres → Graphismes : « Fréquence d'images » → Illimitée. V-Sync : Off.",
                    "PUBG", null, A("PUBG")),
                G("The Finals", "Paramètres → Vidéo : « Limite de FPS » → 500. V-Sync : Off, Reflex : Activé.",
                    "The Finals", null, A("The Finals")),
                G("Marvel Rivals", "Paramètres → Affichage : « Limite d'images/s » → 500 / Illimitée. V-Sync : Off, Reflex : Activé.",
                    "MarvelRivals", null, A("Marvel Rivals")),
                G("Team Fortress 2", "Lance avec -console : fps_max 0 (illimité). V-Sync : Off.",
                    "Team Fortress 2", null, A("Team Fortress")),
                G("Battlefield 2042", "Options → Vidéo : « Fréquence d'images max » → 500. V-Sync : Off.",
                    "Battlefield 2042", null, A("Battlefield 2042")),
                G("Battlefield V", GEN, "Battlefield V", null, A("Battlefield V")),
                G("Titanfall 2", "Option de lancement +fps_max unlimited. V-Sync : Off.",
                    "Titanfall2", null, A("Titanfall")),
                G("Halo Infinite", "Paramètres → Vidéo : « Fréquence d'images max » → 500 (menu + jeu). V-Sync : Off.",
                    "Halo Infinite", null, A("Halo Infinite")),
                G("Destiny 2", "Options → Vidéo : « Fréquence d'images max » → Illimitée. V-Sync : Off, Reflex : Activé.",
                    "Destiny 2", null, A("Destiny 2")),
                G("Escape from Tarkov", GEN, null, A(@"C:\Battlestate Games\EFT"), A("Escape from Tarkov", "EFT")),
                G("Rust", "Console (F1) : fps.limit 0 (illimité), ou Options → Fréquence d'images max. V-Sync : Off.",
                    "Rust", null, A("Rust")),
                G("Splitgate 2", GEN, "Splitgate 2", null, A("Splitgate")),
                G("Delta Force", GEN, "Delta Force", null, A("Delta Force")),
                G("NARAKA: BLADEPOINT", GEN, "NARAKA BLADEPOINT", null, A("NARAKA")),

                // ---- MOBA / Stratégie ----
                G("League of Legends", "Options → Vidéo : « Limite d'images/s : Non plafonnée ». (Un cap FIXE proche de l'écran, ex. 500, stabilise le frametime.)",
                    null, A(@"C:\Riot Games\League of Legends"), A("League of Legends")),
                G("Dota 2", "Options → Vidéo : décoche « Limiter à la fréquence d'écran » ; règle fps_max via la console dev si besoin. V-Sync : Off.",
                    "dota 2 beta", null, A("Dota 2")),
                G("Teamfight Tactics", GEN, null, A(@"C:\Riot Games\League of Legends"), A("Teamfight")),
                G("StarCraft II", GEN, null, A(@"C:\Program Files (x86)\StarCraft II"), A("StarCraft II")),
                G("Age of Empires IV", GEN, "Age of Empires IV", null, A("Age of Empires IV")),

                // ---- Battle.net / Riot / MMO ----
                G("Diablo IV", "Options → Graphismes : « Fréquence d'images max (premier plan) » → 500. V-Sync : Off.",
                    null, A(@"C:\Program Files (x86)\Diablo IV"), A("Diablo IV")),
                G("World of Warcraft", "Système → Avancé : « Fréquence d'images max » → 500 (ou décoché). V-Sync : Désactivée.",
                    null, A(@"C:\Program Files (x86)\World of Warcraft"), A("World of Warcraft")),
                G("Hearthstone", "Bridé à 60 par défaut : Options → décoche la limite. V-Sync : Off.",
                    null, A(@"C:\Program Files (x86)\Hearthstone"), A("Hearthstone")),
                G("FINAL FANTASY XIV", "Config système → décoche « Limiter la fréquence d'images » (+ en arrière-plan). V-Sync : Off.",
                    "FINAL FANTASY XIV Online", null, A("FINAL FANTASY XIV")),
                G("Lost Ark", GEN, "Lost Ark", null, A("Lost Ark")),
                G("New World", GEN, "New World", null, A("New World")),
                G("Path of Exile", "Options → Graphismes : « Fréquence d'images max » → 500, V-Sync Off. Moteur DX12 / Vulkan pour la stabilité.",
                    "Path of Exile", null, A("Path of Exile")),
                G("Path of Exile 2", "Options → Graphismes : « Fréquence d'images max » → 500, V-Sync Off.",
                    "Path of Exile 2", null, A("Path of Exile 2")),
                G("Warframe", "Options → Affichage : « Limite de fréquence d'images » → Illimitée (ou décocher). V-Sync : Off.",
                    "Warframe", null, A("Warframe")),
                G("War Thunder", GEN, "War Thunder", null, A("War Thunder")),
                G("Genshin Impact", "Bridé à 60 FPS (120 sur certaines plateformes). Réduis surtout la latence côté pilote ; V-Sync interne.",
                    null, A(@"C:\Program Files\Genshin Impact", @"C:\Program Files\HoYoPlay"), A("Genshin Impact")),
                G("Honkai: Star Rail", "Bridé à 60 FPS. Réduis la latence côté pilote ; V-Sync interne.",
                    null, A(@"C:\Program Files\Star Rail"), A("Star Rail")),
                G("Wuthering Waves", GEN, null, A(@"C:\Wuthering Waves"), A("Wuthering Waves")),

                // ---- Survie / Coop / Sandbox ----
                G("Palworld", GEN, "Palworld", null, A("Palworld")),
                G("Helldivers 2", "Options → Affichage : « Fréquence d'images max » → au max. V-Sync : Off.",
                    "Helldivers 2", null, A("Helldivers")),
                G("Deep Rock Galactic", GEN, "Deep Rock Galactic", null, A("Deep Rock Galactic")),
                G("Valheim", GEN, "Valheim", null, A("Valheim")),
                G("ARK: Survival Ascended", GEN, "ARK Survival Ascended", null, A("Survival Ascended")),
                G("ARK: Survival Evolved", GEN, "ARK", null, A("ARK: Survival Evolved")),
                G("DayZ", GEN, "DayZ", null, A("DayZ")),
                G("Dead by Daylight", "Plafonné à 120 FPS : règle « Fréquence d'images » sur 120, V-Sync Off (cap interne).",
                    "Dead by Daylight", null, A("Dead by Daylight")),
                G("Phasmophobia", GEN, "Phasmophobia", null, A("Phasmophobia")),
                G("Lethal Company", GEN, "Lethal Company", null, A("Lethal Company")),
                G("Sea of Thieves", GEN, "Sea of Thieves", null, A("Sea of Thieves")),
                G("Minecraft", "Options → Graphismes → « Images/s max : Illimité ». Pour TENIR 500 : mod Sodium (ou OptiFine).",
                    null, A(Environment.ExpandEnvironmentVariables(@"%APPDATA%\.minecraft")), A("Minecraft")),
                G("Roblox", "Bridé à 60 par défaut : utilise le mode hautes performances ou un déverrouilleur de FPS communautaire.",
                    null, A(Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Roblox")), A("Roblox")),
                G("Terraria", "Bridé à 60 (moteur) ; « Frame Skip : Off », fenêtré sans V-Sync. (240 Hz via mods.)",
                    "Terraria", null, A("Terraria")),

                // ---- Solo / AAA ----
                G("Grand Theft Auto V", "Pas de cap FPS interne : la V-Sync est le plafond → Désactive-la. Un limiteur externe stabilise le frametime.",
                    "Grand Theft Auto V", null, A("Grand Theft Auto V")),
                G("Cyberpunk 2077", "Paramètres → Vidéo : « FPS max » → Illimité, V-Sync → Off. DLSS / Reflex pour tenir le framerate.",
                    "Cyberpunk 2077", null, A("Cyberpunk 2077")),
                G("Baldur's Gate 3", "Options → Affichage : décoche V-Sync et le limiteur ; « Limite de FPS » au max.",
                    "Baldurs Gate 3", null, A("Baldur")),
                G("Elden Ring", "Bridé à 60 FPS (moteur). Hors compétition, désactive la V-Sync in-game. (Déblocage = solutions tierces, à tes risques.)",
                    "ELDEN RING", null, A("ELDEN RING")),
                G("Sekiro", "Bridé à 60 FPS (moteur FromSoftware). Désactive la V-Sync ; déblocage = tiers, à tes risques.",
                    "Sekiro", null, A("Sekiro")),
                G("Black Myth: Wukong", GEN, "Black Myth Wukong", null, A("Black Myth")),
                G("Red Dead Redemption 2", "Graphismes → V-Sync Off, pas de triple buffering ; le jeu suit ton écran/limiteur.",
                    "Red Dead Redemption 2", null, A("Red Dead Redemption")),
                G("The Witcher 3", "Options vidéo → décoche « Fréquence d'images max ». V-Sync : Off.",
                    "The Witcher 3", null, A("Witcher 3")),
                G("Starfield", GEN, "Starfield", null, A("Starfield")),
                G("Hogwarts Legacy", GEN, "Hogwarts Legacy", null, A("Hogwarts")),
                G("Monster Hunter World", GEN, "Monster Hunter World", null, A("Monster Hunter World")),
                G("Monster Hunter Wilds", GEN, "MonsterHunterWilds", null, A("Monster Hunter Wilds")),

                // ---- Jeux de combat (verrouillés 60 pour le gameplay) ----
                G("TEKKEN 8", "Verrouillé à 60 FPS (gameplay). Laisse 60, V-Sync Off côté pilote pour la latence.",
                    "TEKKEN 8", null, A("TEKKEN 8")),
                G("Street Fighter 6", "Verrouillé à 60 FPS (gameplay). V-Sync Off côté pilote.",
                    "Street Fighter 6", null, A("Street Fighter 6")),
                G("Mortal Kombat 1", "Verrouillé à 60 FPS (gameplay).",
                    "Mortal Kombat 1", null, A("Mortal Kombat 1")),

                // ---- Sport / Course / Rythme ----
                G("Rocket League", "Le menu plafonne à 250 : TASystemSettings.ini → MaxFPS=500. V-Sync : Off.",
                    "rocketleague", A(@"C:\Program Files\Epic Games\rocketleague"), A("Rocket League")),
                G("EA SPORTS FC 25", "Paramètres → « Fréquence d'images » : décoche la limite / V-Sync.",
                    "EA SPORTS FC 25", null, A("EA SPORTS FC 25", "EA SPORTS FC")),
                G("Forza Horizon 5", GEN, "ForzaHorizon5", null, A("Forza Horizon 5")),
                G("Fall Guys", GEN, "Fall Guys", A(@"C:\Program Files\Epic Games\FallGuys"), A("Fall Guys")),
                G("osu!", "Options → « Frame limiter » → Unlimited (ou 1000 fps). V-Sync : Off.",
                    null, A(Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\osu!")), A("osu!"))
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

                    if (g.Detected) g.Store = StoreFromPath(g.InstallPath);
                }
                catch { }
            }
        }

        // Devine le launcher à partir du chemin d'installation résolu (affichage d'une pastille).
        private static string StoreFromPath(string p)
        {
            if (string.IsNullOrEmpty(p)) return null;
            string s = p.ToLowerInvariant();
            if (s.Contains("steamapps")) return "STEAM";
            if (s.Contains("epic games")) return "EPIC";
            if (s.Contains("riot games")) return "RIOT";
            if (s.Contains(@"\overwatch") || s.Contains("world of warcraft") || s.Contains(@"\diablo")
                || s.Contains("call of duty") || s.Contains("hearthstone") || s.Contains("starcraft")
                || s.Contains("battle.net")) return "BATTLE.NET";
            if (s.Contains("battlestate")) return "TARKOV";
            if (s.Contains("hoyoplay") || s.Contains("genshin") || s.Contains("star rail")) return "HOYO";
            return null;
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
