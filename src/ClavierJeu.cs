using System;
using System.Collections.Generic;
using System.Globalization;

namespace BTOptimizer
{
    /// <summary>
    /// CLAVIER PENDANT LA PARTIE — PUR. Deux gênes classiques, réglées le temps du Mode Jeu :
    ///  1. la touche Windows, qui ouvre le menu Démarrer et sort du jeu ; bloquée SEULEMENT quand
    ///     le jeu est au premier plan (hors du jeu, elle marche normalement) ;
    ///  2. le raccourci des touches rémanentes (Maj cinq fois) et celui des touches filtres (Maj
    ///     droite tenue 8 s), qui ouvrent une fenêtre en pleine partie. Seul le RACCOURCI est coupé :
    ///     si l'utilisateur se sert de ces fonctions, elles restent actives.
    /// Tout est rendu à la sortie du Mode Jeu. Rien n'est injecté dans le jeu : ONYX lit le clavier
    /// depuis son propre processus (crochet clavier bas niveau de Windows).
    /// </summary>
    internal static partial class ClavierJeu
    {
        public const int VkWinGauche = 0x5B, VkWinDroite = 0x5C;

        /// <summary>Drapeau « raccourci actif » : SKF_HOTKEYACTIVE et FKF_HOTKEYACTIVE valent tous deux 4.</summary>
        public const uint Raccourci = 0x4;

        // ------------------------------------------------------------------ Touche Windows

        /// <summary>
        /// Décide pour chaque touche. Une touche Windows RELÂCHÉE suit le sort de son appui : si
        /// l'appui est passé (hors du jeu) et que le relâchement était bloqué (on est entré dans le
        /// jeu entre-temps), Windows croirait la touche tenue et changerait chaque touche suivante
        /// en raccourci Windows.
        /// </summary>
        public sealed class Filtre
        {
            private bool _gauche, _droite;   // appui bloqué, relâchement à bloquer aussi

            /// <summary>true = la touche est avalée.</summary>
            public bool Bloque(int vk, bool relache, bool injecte, bool jeuDevant)
            {
                if (vk != VkWinGauche && vk != VkWinDroite) return false;
                bool gauche = vk == VkWinGauche;
                if (relache)
                {
                    bool b = gauche ? _gauche : _droite;
                    if (gauche) _gauche = false; else _droite = false;
                    return b;
                }
                // Une touche envoyée par un logiciel (macro, clavier virtuel) n'est pas un doigt qui glisse.
                bool bloque = jeuDevant && !injecte;
                if (gauche) _gauche = bloque; else _droite = bloque;
                return bloque;
            }
        }

        /// <summary>« FortniteClient-Win64-Shipping.exe » → « FortniteClient-Win64-Shipping ».</summary>
        public static string SansExe(string nom)
        {
            if (string.IsNullOrEmpty(nom)) return "";
            nom = nom.Trim();
            return nom.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? nom.Substring(0, nom.Length - 4) : nom;
        }

        private static readonly HashSet<string> JamaisUnJeu = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "explorer", "BTOptimizer", "ONYX", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost",
            "SearchApp", "LockApp", "TextInputHost", "ApplicationFrameHost", "Taskmgr", "dwm",
        };

        /// <summary>
        /// Le jeu est-il devant ? Jeu connu (Mode Jeu automatique) : son processus au premier plan.
        /// Jeu inconnu (Mode Jeu lancé à la main) : une fenêtre plein écran qui n'est ni le bureau,
        /// ni ONYX, ni un élément de Windows.
        /// </summary>
        public static bool JeuDevant(string processusDevant, string jeu, bool pleinEcran)
        {
            string p = SansExe(processusDevant);
            if (p.Length == 0 || JamaisUnJeu.Contains(p)) return false;
            string j = SansExe(jeu);
            if (j.Length > 0) return string.Equals(p, j, StringComparison.OrdinalIgnoreCase);
            return pleinEcran;
        }

        // ------------------------------------------------------------------ Raccourcis d'accessibilité

        public static uint SansRaccourci(uint drapeaux) { return drapeaux & ~Raccourci; }

        /// <summary>
        /// À la sortie : on ne rend QUE le bit du raccourci, tel qu'il était. Le reste des drapeaux
        /// reste celui d'aujourd'hui (l'utilisateur a pu activer les touches rémanentes entre-temps).
        /// </summary>
        public static uint Rendu(uint actuel, uint origine) { return (actuel & ~Raccourci) | (origine & Raccourci); }

        /// <summary>Marqueur de reprise : « remanentes=510 » et/ou « filtres=126 ».</summary>
        public static string EcritEtat(uint? remanentes, uint? filtres)
        {
            var l = new List<string>();
            if (remanentes.HasValue) l.Add("remanentes=" + remanentes.Value.ToString(CultureInfo.InvariantCulture));
            if (filtres.HasValue) l.Add("filtres=" + filtres.Value.ToString(CultureInfo.InvariantCulture));
            return string.Join("\n", l);
        }

        public static void LitEtat(string texte, out uint? remanentes, out uint? filtres)
        {
            remanentes = null; filtres = null;
            if (string.IsNullOrEmpty(texte)) return;
            foreach (string brut in texte.Split('\n'))
            {
                string l = brut.Trim();
                int i = l.IndexOf('=');
                if (i <= 0) continue;
                uint v;
                if (!uint.TryParse(l.Substring(i + 1), NumberStyles.None, CultureInfo.InvariantCulture, out v)) continue;
                string k = l.Substring(0, i);
                if (k == "remanentes") remanentes = v;
                else if (k == "filtres") filtres = v;
            }
        }

        // ------------------------------------------------------------------ Options

        public sealed class Options
        {
            /// <summary>Touche Windows bloquée en jeu. Non par défaut, comme Razer Cortex : elle sert
            /// aussi à Win+G (Xbox Game Bar) et Win+Maj+S (capture), bloqués avec elle.</summary>
            public bool ToucheWindows;
            /// <summary>Raccourcis Maj coupés en jeu. Oui par défaut.</summary>
            public bool Raccourcis = true;
        }

        public static Options LitOptions(string texte)
        {
            var o = new Options();
            if (string.IsNullOrEmpty(texte)) return o;
            foreach (string brut in texte.Split('\n'))
            {
                string l = brut.Trim();
                if (l == "touchewindows=1") o.ToucheWindows = true;
                else if (l == "touchewindows=0") o.ToucheWindows = false;
                else if (l == "raccourcis=1") o.Raccourcis = true;
                else if (l == "raccourcis=0") o.Raccourcis = false;
            }
            return o;
        }

        public static string EcritOptions(Options o)
        {
            return "touchewindows=" + (o.ToucheWindows ? "1" : "0") + "\nraccourcis=" + (o.Raccourcis ? "1" : "0");
        }
    }
}
