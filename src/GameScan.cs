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
    /// jeux, on explique quoi régler. Chaque jeu Steam porte son AppID (pour la jaquette officielle).
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
                "PathOfExile.exe", "PathOfExileSteam.exe", "PathOfExile_x64.exe", // Path of Exile
                "FSD-Win64-Shipping.exe",                      // Deep Rock Galactic
                "DeadByDaylight-Win64-Shipping.exe",           // Dead by Daylight
                "b1-Win64-Shipping.exe",                       // Black Myth: Wukong
                "FactoryGame-Win64-Shipping.exe",              // Satisfactory
                "RobloxPlayerBeta.exe"                        // Roblox (le JOUEUR ; jamais le Studio)
            };
        }

        /// <summary>Exécutable(s) principal(aux) d'un jeu (pour la priorité CPU par jeu via IFEO), ou null
        /// si inconnu. Basé sur les noms distinctifs de PriorityExes.</summary>
        public static string[] ExesFor(string name)
        {
            switch (name)
            {
                case "Counter-Strike 2": return new[] { "cs2.exe" };
                case "VALORANT": return new[] { "VALORANT-Win64-Shipping.exe" };
                case "Fortnite": return new[] { "FortniteClient-Win64-Shipping.exe" };
                case "Overwatch 2": return new[] { "Overwatch.exe" };
                case "Apex Legends": return new[] { "r5apex.exe", "r5apex_dx12.exe" };
                case "Call of Duty (MW / Warzone / BO6)": return new[] { "cod.exe", "cod22-cod.exe", "cod23-cod.exe", "cod24-cod.exe" };
                case "League of Legends": return new[] { "League of Legends.exe" };
                case "Rocket League": return new[] { "RocketLeague.exe" };
                case "Rainbow Six Siege": return new[] { "RainbowSix.exe", "RainbowSix_Vulkan.exe" };
                case "Dota 2": return new[] { "dota2.exe" };
                case "PUBG: BATTLEGROUNDS": return new[] { "TslGame.exe" };
                case "Marvel Rivals": return new[] { "MarvelRivals.exe" };
                case "Helldivers 2": return new[] { "helldivers2.exe" };
                case "Destiny 2": return new[] { "destiny2.exe" };
                case "Rust": return new[] { "RustClient.exe" };
                case "Warframe": return new[] { "Warframe.x64.exe" };
                case "Elden Ring": return new[] { "eldenring.exe" };
                case "Grand Theft Auto V": return new[] { "GTA5.exe", "GTA5_Enhanced.exe" };
                case "Battlefield 2042": return new[] { "BF2042.exe" };
                case "The Finals": return new[] { "DiscoveryClient.exe" };
                case "Cyberpunk 2077": return new[] { "Cyberpunk2077.exe" };
                case "Baldur's Gate 3": return new[] { "bg3.exe", "bg3_dx11.exe" };
                case "Palworld": return new[] { "Palworld-Win64-Shipping.exe" };
                case "Path of Exile": return new[] { "PathOfExile.exe", "PathOfExileSteam.exe", "PathOfExile_x64.exe" };
                case "Deep Rock Galactic": return new[] { "FSD-Win64-Shipping.exe" };
                case "Dead by Daylight": return new[] { "DeadByDaylight-Win64-Shipping.exe" };
                case "Black Myth: Wukong": return new[] { "b1-Win64-Shipping.exe" };
                case "Satisfactory": return new[] { "FactoryGame-Win64-Shipping.exe" };
                default: return null;
            }
        }

        public class GameInfo
        {
            public string Name;          // nom affiché
            public string Uncap;         // comment passer le jeu à 500 FPS
            public bool Detected;        // trouvé sur ce PC
            public int SteamId;          // AppID Steam (0 si hors Steam) — sert à la jaquette officielle
            public string SteamFolder;   // dossier steamapps\common (si jeu Steam)
            public string[] Dirs;        // dossiers d'installation classiques
            public string[] Keywords;    // mots-clés dans les clés de désinstallation
            public string InstallPath;   // dossier d'installation résolu (si trouvé sur disque)
            public string Store;         // launcher résolu (STEAM / EPIC / RIOT / BATTLE.NET) si détecté
        }

        // Fabrique compacte : garde le grand catalogue lisible. uncap en dernier (chaîne la plus longue).
        private static GameInfo G(string name, int appId, string steam, string[] dirs, string[] keys, string uncap)
        {
            return new GameInfo { Name = name, SteamId = appId, SteamFolder = steam, Dirs = dirs, Keywords = keys, Uncap = uncap };
        }

        private static string[] A(params string[] v) { return v; }

        // Conseil générique correct quand le jeu n'a pas de manip' spécifique connue.
        private const string GEN = "Options → Affichage / Graphismes : « Limite d'images/s » → Illimitée (ou 500) ; V-Sync : Désactivée ; NVIDIA Reflex / AMD Anti-Lag : Activé si disponible.";

        /// <summary>Grand catalogue de jeux connus + AppID Steam + réglage FPS (déblocage DANS chaque jeu).</summary>
        public static List<GameInfo> Known()
        {
            return new List<GameInfo>
            {
                // ---- Compétitif / FPS / Battle royale ----
                G("Counter-Strike 2", 730, "Counter-Strike Global Offensive", null, A("Counter-Strike"),
                    "Console : fps_max 0 (illimité). Ou Paramètres → Vidéo → Avancé → « Limite de FPS en partie » : 500+. V-Sync : DÉSACTIVÉ, Reflex : Activé."),
                G("VALORANT", 0, null, A(@"C:\Riot Games\VALORANT"), A("VALORANT"),
                    "Paramètres → Vidéo → Général : « Limiter les FPS – Toujours : Non ». V-Sync : Non, NVIDIA Reflex : Activé."),
                G("Fortnite", 0, null, A(@"C:\Program Files\Epic Games\Fortnite"), A("Fortnite"),
                    "Paramètres vidéo → « Limite d'images/s : Illimitée » (ou 500). Mode de rendu « Performance » pour TENIR 500. V-Sync : Off, Reflex : Activé + Boost."),
                G("Overwatch 2", 2357570, "Overwatch", A(@"C:\Program Files (x86)\Overwatch"), A("Overwatch"),
                    "Options → Vidéo : « Limite de fréquence d'images : Personnalisée » → 500 (moteur jusqu'à 600). V-Sync : Off, Reflex : Activé."),
                G("Apex Legends", 1172470, "Apex Legends", null, A("Apex Legends"),
                    "Le menu plafonne à 300 : option de lancement « +fps_max unlimited » (ou +fps_max 500) dans Steam / EA app. V-Sync : Off."),
                G("Call of Duty (MW / Warzone / BO6)", 1938090, "Call of Duty HQ", A(@"C:\Program Files (x86)\Call of Duty"), A("Call of Duty"),
                    "Paramètres → Graphismes → « Limite d'images par seconde : Personnalisée » → Jeu : 500. V-Sync : Off, Reflex : Activé."),
                G("Rainbow Six Siege", 359550, "Tom Clancy's Rainbow Six Siege", null, A("Rainbow Six"),
                    "Affichage → « Limite d'IPS » au maximum, ou GameSettings.ini → FPSLimit=0 (illimité). V-Sync : Off."),
                G("PUBG: BATTLEGROUNDS", 578080, "PUBG", null, A("PUBG"),
                    "Paramètres → Graphismes : « Fréquence d'images » → Illimitée. V-Sync : Off."),
                G("The Finals", 2073850, "The Finals", null, A("The Finals"),
                    "Paramètres → Vidéo : « Limite de FPS » → 500. V-Sync : Off, Reflex : Activé."),
                G("Marvel Rivals", 2767030, "MarvelRivals", null, A("Marvel Rivals"),
                    "Paramètres → Affichage : « Limite d'images/s » → 500 / Illimitée. V-Sync : Off, Reflex : Activé."),
                G("Team Fortress 2", 440, "Team Fortress 2", null, A("Team Fortress"),
                    "Lance avec -console : fps_max 0 (illimité). V-Sync : Off."),
                G("Battlefield 2042", 1517290, "Battlefield 2042", null, A("Battlefield 2042"),
                    "Options → Vidéo : « Fréquence d'images max » → 500. V-Sync : Off."),
                G("Battlefield V", 1238810, "Battlefield V", null, A("Battlefield V"), GEN),
                G("Titanfall 2", 1237970, "Titanfall2", null, A("Titanfall"),
                    "Option de lancement +fps_max unlimited. V-Sync : Off."),
                G("Halo Infinite", 1240440, "Halo Infinite", null, A("Halo Infinite"),
                    "Paramètres → Vidéo : « Fréquence d'images max » → 500 (menu + jeu). V-Sync : Off."),
                G("Destiny 2", 1085660, "Destiny 2", null, A("Destiny 2"),
                    "Options → Vidéo : « Fréquence d'images max » → Illimitée. V-Sync : Off, Reflex : Activé."),
                G("Escape from Tarkov", 0, null, A(@"C:\Battlestate Games\EFT"), A("Escape from Tarkov", "EFT"), GEN),
                G("Rust", 252490, "Rust", null, A("Rust"),
                    "Console (F1) : fps.limit 0 (illimité), ou Options → Fréquence d'images max. V-Sync : Off."),
                G("NARAKA: BLADEPOINT", 1203220, "NARAKA BLADEPOINT", null, A("NARAKA"), GEN),

                // ---- MOBA / Stratégie ----
                G("League of Legends", 0, null, A(@"C:\Riot Games\League of Legends"), A("League of Legends"),
                    "Options → Vidéo : « Limite d'images/s : Non plafonnée ». (Un cap FIXE proche de l'écran, ex. 500, stabilise le frametime.)"),
                G("Dota 2", 570, "dota 2 beta", null, A("Dota 2"),
                    "Options → Vidéo : décoche « Limiter à la fréquence d'écran » ; règle fps_max via la console dev si besoin. V-Sync : Off."),
                G("Teamfight Tactics", 0, null, A(@"C:\Riot Games\League of Legends"), A("Teamfight"), GEN),
                G("StarCraft II", 0, null, A(@"C:\Program Files (x86)\StarCraft II"), A("StarCraft II"), GEN),
                G("Age of Empires IV", 1466860, "Age of Empires IV", null, A("Age of Empires IV"), GEN),
                G("Sid Meier's Civilization VI", 289070, "Sid Meier's Civilization VI", null, A("Civilization VI"), GEN),
                G("Total War: WARHAMMER III", 1142710, "Total War WARHAMMER III", null, A("WARHAMMER III"), GEN),
                G("RimWorld", 294100, "RimWorld", null, A("RimWorld"), GEN),

                // ---- Riot / Blizzard / MMO ----
                G("Diablo IV", 2344520, null, A(@"C:\Program Files (x86)\Diablo IV"), A("Diablo IV"),
                    "Options → Graphismes : « Fréquence d'images max (premier plan) » → 500. V-Sync : Off."),
                G("World of Warcraft", 0, null, A(@"C:\Program Files (x86)\World of Warcraft"), A("World of Warcraft"),
                    "Système → Avancé : « Fréquence d'images max » → 500 (ou décoché). V-Sync : Désactivée."),
                G("Hearthstone", 0, null, A(@"C:\Program Files (x86)\Hearthstone"), A("Hearthstone"),
                    "Bridé à 60 par défaut : Options → décoche la limite. V-Sync : Off."),
                G("FINAL FANTASY XIV", 39210, "FINAL FANTASY XIV Online", null, A("FINAL FANTASY XIV"),
                    "Config système → décoche « Limiter la fréquence d'images » (+ en arrière-plan). V-Sync : Off."),
                G("Lost Ark", 1599340, "Lost Ark", null, A("Lost Ark"), GEN),
                G("New World", 1063730, "New World", null, A("New World"), GEN),
                G("Path of Exile", 238960, "Path of Exile", null, A("Path of Exile"),
                    "Options → Graphismes : « Fréquence d'images max » → 500, V-Sync Off. Moteur DX12 / Vulkan pour la stabilité."),
                G("Path of Exile 2", 2694490, "Path of Exile 2", null, A("Path of Exile 2"),
                    "Options → Graphismes : « Fréquence d'images max » → 500, V-Sync Off."),
                G("Warframe", 230410, "Warframe", null, A("Warframe"),
                    "Options → Affichage : « Limite de fréquence d'images » → Illimitée (ou décocher). V-Sync : Off."),
                G("War Thunder", 236390, "War Thunder", null, A("War Thunder"), GEN),
                G("Genshin Impact", 0, null, A(@"C:\Program Files\Genshin Impact", @"C:\Program Files\HoYoPlay"), A("Genshin Impact"),
                    "Bridé à 60 FPS (120 sur certaines plateformes). Réduis surtout la latence côté pilote ; V-Sync interne."),
                G("Honkai: Star Rail", 0, null, A(@"C:\Program Files\Star Rail"), A("Star Rail"),
                    "Bridé à 60 FPS. Réduis la latence côté pilote ; V-Sync interne."),
                G("Wuthering Waves", 0, null, A(@"C:\Wuthering Waves"), A("Wuthering Waves"), GEN),

                // ---- Survie / Coop / Sandbox ----
                G("Palworld", 1623730, "Palworld", null, A("Palworld"), GEN),
                G("Helldivers 2", 553850, "Helldivers 2", null, A("Helldivers"),
                    "Options → Affichage : « Fréquence d'images max » → au max. V-Sync : Off."),
                G("Deep Rock Galactic", 548430, "Deep Rock Galactic", null, A("Deep Rock Galactic"), GEN),
                G("Valheim", 892970, "Valheim", null, A("Valheim"), GEN),
                G("Enshrouded", 1203620, "Enshrouded", null, A("Enshrouded"), GEN),
                G("Grounded", 962130, "Grounded", null, A("Grounded"), GEN),
                G("Sons of the Forest", 1326470, "Sons Of The Forest", null, A("Sons of the Forest"), GEN),
                G("The Forest", 242760, "The Forest", null, A("The Forest"), GEN),
                G("Subnautica", 264710, "Subnautica", null, A("Subnautica"), GEN),
                G("No Man's Sky", 275850, "No Man's Sky", null, A("No Man's Sky"), GEN),
                G("Satisfactory", 526870, "Satisfactory", null, A("Satisfactory"), GEN),
                G("ARK: Survival Ascended", 2399830, "ARK Survival Ascended", null, A("Survival Ascended"), GEN),
                G("ARK: Survival Evolved", 346110, "ARK", null, A("ARK: Survival Evolved"), GEN),
                G("DayZ", 221100, "DayZ", null, A("DayZ"), GEN),
                G("Dead by Daylight", 381210, "Dead by Daylight", null, A("Dead by Daylight"),
                    "Plafonné à 120 FPS : règle « Fréquence d'images » sur 120, V-Sync Off (cap interne)."),
                G("Phasmophobia", 739630, "Phasmophobia", null, A("Phasmophobia"), GEN),
                G("Lethal Company", 1966720, "Lethal Company", null, A("Lethal Company"), GEN),
                G("Sea of Thieves", 1172620, "Sea of Thieves", null, A("Sea of Thieves"), GEN),
                G("Minecraft", 0, null, A(Environment.ExpandEnvironmentVariables(@"%APPDATA%\.minecraft")), A("Minecraft"),
                    "Options → Graphismes → « Images/s max : Illimité ». Pour TENIR 500 : mod Sodium (ou OptiFine)."),
                G("Roblox", 0, null, A(Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Roblox")), A("Roblox"),
                    "Bridé à 60 par défaut : utilise le mode hautes performances ou un déverrouilleur de FPS communautaire."),
                G("Terraria", 105600, "Terraria", null, A("Terraria"),
                    "Bridé à 60 (moteur) ; « Frame Skip : Off », fenêtré sans V-Sync. (240 Hz via mods.)"),

                // ---- Solo / AAA ----
                G("Grand Theft Auto V", 271590, "Grand Theft Auto V", null, A("Grand Theft Auto V"),
                    "Pas de cap FPS interne : la V-Sync est le plafond → Désactive-la. Un limiteur externe stabilise le frametime."),
                G("Cyberpunk 2077", 1091500, "Cyberpunk 2077", null, A("Cyberpunk 2077"),
                    "Paramètres → Vidéo : « FPS max » → Illimité, V-Sync → Off. DLSS / Reflex pour tenir le framerate."),
                G("Baldur's Gate 3", 1086940, "Baldurs Gate 3", null, A("Baldur"),
                    "Options → Affichage : décoche V-Sync et le limiteur ; « Limite de FPS » au max."),
                G("Elden Ring", 1245620, "ELDEN RING", null, A("ELDEN RING"),
                    "Bridé à 60 FPS (moteur). Hors compétition, désactive la V-Sync in-game. (Déblocage = solutions tierces, à tes risques.)"),
                G("Sekiro", 814380, "Sekiro", null, A("Sekiro"),
                    "Bridé à 60 FPS (moteur FromSoftware). Désactive la V-Sync ; déblocage = tiers, à tes risques."),
                G("Dark Souls III", 374320, "DARK SOULS III", null, A("DARK SOULS III"),
                    "Bridé à 60 FPS (moteur). V-Sync côté pilote uniquement."),
                G("Black Myth: Wukong", 2358720, "Black Myth Wukong", null, A("Black Myth"), GEN),
                G("Red Dead Redemption 2", 1174180, "Red Dead Redemption 2", null, A("Red Dead Redemption"),
                    "Graphismes → V-Sync Off, pas de triple buffering ; le jeu suit ton écran/limiteur."),
                G("The Witcher 3", 292030, "The Witcher 3", null, A("Witcher 3"),
                    "Options vidéo → décoche « Fréquence d'images max ». V-Sync : Off."),
                G("Starfield", 1716740, "Starfield", null, A("Starfield"), GEN),
                G("Hogwarts Legacy", 990080, "Hogwarts Legacy", null, A("Hogwarts"), GEN),
                G("The Elder Scrolls V: Skyrim SE", 489830, "Skyrim Special Edition", null, A("Skyrim Special Edition"),
                    "Verrouillé ~60 FPS : la physique se dérègle au-delà. Garde 60 ; V-Sync interne (iPresentInterval)."),
                G("Fallout 4", 377160, "Fallout 4", null, A("Fallout 4"),
                    "Physique liée au framerate : au-delà de ~60 elle se dérègle. Garde ~60."),
                G("God of War", 1593500, "God of War", null, A("God of War"), GEN),
                G("Marvel's Spider-Man Remastered", 1817070, "Marvel's Spider-Man Remastered", null, A("Spider-Man Remastered"), GEN),
                G("Warhammer 40,000: Space Marine 2", 2183900, "Warhammer 40,000 Space Marine 2", null, A("Space Marine 2"), GEN),
                G("Monster Hunter World", 582010, "Monster Hunter World", null, A("Monster Hunter World"), GEN),
                G("Monster Hunter Wilds", 2246340, "MonsterHunterWilds", null, A("Monster Hunter Wilds"), GEN),

                // ---- Indés / Roguelites ----
                G("Hades", 1145360, "Hades", null, A("Hades"), GEN),
                G("Hades II", 1145350, "Hades II", null, A("Hades II"), GEN),
                G("Hollow Knight", 367520, "Hollow Knight", null, A("Hollow Knight"), GEN),
                G("Vampire Survivors", 1794680, "Vampire Survivors", null, A("Vampire Survivors"), GEN),
                G("Balatro", 2379780, "Balatro", null, A("Balatro"), GEN),
                G("Stardew Valley", 413150, "Stardew Valley", null, A("Stardew Valley"),
                    "Bridé à 60 FPS (moteur). Désactive la V-Sync du pilote pour réduire la latence."),
                G("It Takes Two", 1426210, "It Takes Two", null, A("It Takes Two"), GEN),

                // ---- Jeux de combat (verrouillés 60 pour le gameplay) ----
                G("TEKKEN 8", 1778820, "TEKKEN 8", null, A("TEKKEN 8"),
                    "Verrouillé à 60 FPS (gameplay). Laisse 60, V-Sync Off côté pilote pour la latence."),
                G("Street Fighter 6", 1364780, "Street Fighter 6", null, A("Street Fighter 6"),
                    "Verrouillé à 60 FPS (gameplay). V-Sync Off côté pilote."),
                G("Mortal Kombat 1", 1971870, "Mortal Kombat 1", null, A("Mortal Kombat 1"),
                    "Verrouillé à 60 FPS (gameplay)."),

                // ---- Sport / Course / Rythme ----
                G("Rocket League", 252950, "rocketleague", A(@"C:\Program Files\Epic Games\rocketleague"), A("Rocket League"),
                    "Le menu plafonne à 250 : TASystemSettings.ini → MaxFPS=500. V-Sync : Off."),
                G("EA SPORTS FC 25", 2669320, "EA SPORTS FC 25", null, A("EA SPORTS FC 25", "EA SPORTS FC"),
                    "Paramètres → « Fréquence d'images » : décoche la limite / V-Sync."),
                G("Forza Horizon 5", 1551360, "ForzaHorizon5", null, A("Forza Horizon 5"), GEN),
                G("Fall Guys", 1097150, "Fall Guys", A(@"C:\Program Files\Epic Games\FallGuys"), A("Fall Guys"), GEN),
                G("osu!", 0, null, A(Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\osu!")), A("osu!"),
                    "Options → « Frame limiter » → Unlimited (ou 1000 fps). V-Sync : Off.")
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

        /// <summary>Racine d'installation de Steam (HKCU\Valve\Steam), ou null.</summary>
        public static string SteamRoot()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                    if (k != null)
                    {
                        string s = Convert.ToString(k.GetValue("SteamPath"));
                        if (!string.IsNullOrEmpty(s)) return s.Replace('/', '\\');
                    }
            }
            catch { }
            return null;
        }

        /// <summary>Tous les dossiers steamapps\common (bibliothèque principale + secondaires).</summary>
        private static List<string> SteamCommonDirs()
        {
            var result = new List<string>();
            try
            {
                string steam = SteamRoot();
                if (string.IsNullOrEmpty(steam)) return result;

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
