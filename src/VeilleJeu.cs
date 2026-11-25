using System;
using System.Collections.Generic;
using System.IO;

namespace BTOptimizer
{
    /// <summary>
    /// VEILLE DE JEU — partie pure (testée au banc). Le minuteur des shells relève les processus UNE
    /// fois par tick, sur un fil de fond (VeilleJeuMachine.cs), et passe ce relevé au Mode Jeu auto,
    /// au profil par jeu et au viseur : plus d'énumération des processus sur le fil d'interface.
    /// </summary>
    internal static partial class VeilleJeu
    {
        /// <summary>Un relevé : application plein écran au premier plan, jeu connu en cours (nom du
        /// processus ou null), noms distincts de tous les processus.</summary>
        public sealed class Releve { public bool PleinEcran; public string JeuConnu; public string[] Noms; }

        /// <summary>Processus qui peuvent couvrir un écran sans être des jeux : surfaces de Windows,
        /// outils de capture, lecteurs vidéo, présentation, bureau à distance.</summary>
        private static readonly HashSet<string> PasDesJeux = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "explorer", "searchhost", "lockapp", "shellexperiencehost", "startmenuexperiencehost", "dwm", "idle",
            "snippingtool", "screenclippinghost", "sharex", "lightshot",
            "vlc", "mpc-hc", "mpc-hc64", "mpc-be64", "mpv", "potplayermini64", "wmplayer",
            "microsoft.media.player", "video.ui",
            "powerpnt", "mstsc"
        };

        /// <summary>
        /// PUR : la fenêtre au premier plan est-elle le plein écran d'un JEU ?
        ///
        /// Couvrir son écran ne suffit pas. Mesuré le 25/11/2026 : l'appli Claude en plein écran
        /// sur un deuxième écran enclenchait le Mode Jeu auto à chaque fois qu'on y travaillait.
        /// Les navigateurs et les applis bâties dessus (Claude, Discord, VS Code…) partagent la
        /// classe de fenêtre « Chrome_WidgetWin » : c'est elle qu'on écarte, plutôt qu'une liste
        /// de noms qui ne serait jamais à jour. Un jeu listé reste reconnu par son nom
        /// (<see cref="JeuConnuParmi"/>), quelle que soit sa fenêtre.
        /// </summary>
        public static bool PleinEcranDeJeu(string processus, string classe, bool couvreEcran)
        {
            if (!couvreEcran || string.IsNullOrEmpty(processus)) return false;
            if (PasDesJeux.Contains(processus.Trim())) return false;
            if (classe != null
                && (classe.StartsWith("Chrome_WidgetWin", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(classe, "MozillaWindowClass", StringComparison.OrdinalIgnoreCase)))
                return false;
            return true;
        }

        private static HashSet<string> _connus;

        /// <summary>Noms des exécutables des jeux reconnus par le MODE JEU AUTO.</summary>
        public static string[] ExesPrioritaires()
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

        /// <summary>Premier nom de la liste qui est un jeu connu (casse ignorée, nom rendu tel quel), ou null.</summary>
        public static string JeuConnuParmi(IEnumerable<string> noms)
        {
            if (noms == null) return null;
            HashSet<string> connus = _connus;
            if (connus == null)
            {
                connus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string exe in ExesPrioritaires()) connus.Add(Path.GetFileNameWithoutExtension(exe));
                _connus = connus;
            }
            foreach (string n in noms) if (n != null && connus.Contains(n)) return n;
            return null;
        }
    }
}
