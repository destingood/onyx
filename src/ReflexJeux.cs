using System;
using System.Text.RegularExpressions;

namespace BTOptimizer
{
    /// <summary>
    /// Reflex (NVIDIA) et Anti-Lag 2 (AMD) coupés dans le jeu : lecture seule des fichiers de
    /// réglages, en plus de Fortnite (ConstatsMateriel.ReflexFortnite). Un jeu n'est ajouté
    /// qu'après lecture de son fichier sur une vraie installation (PC de test, 21/11/2026).
    ///
    /// Clés relues :
    ///   - Overwatch : Documents\Overwatch\Settings\Settings_v0.ini, « ReflexMode = "2" »
    ///     (section [Render.13]) ;
    ///   - Battlefield 6 : Documents\Battlefield 6\settings\PROFSAVEbf6mp_profile,
    ///     « GstRender.NvidiaLowLatency 2 » et « GstRender.AMDLowLatency 1 » ;
    ///   - Black Ops 6 : Documents\Call of Duty BO6\players\s.1.0.cod24.txt0,
    ///     « NvidiaReflex@… = Enabled // one of Disabled, Enabled, Enabled + boost » et
    ///     « AmdAntilag2@… = false » : valeurs documentées par le fichier lui-même.
    /// Overwatch et Battlefield 6 : 0 = coupé est une HYPOTHÈSE tirée de l'ordre du menu
    /// (Désactivé, Activé, Activé + Boost), non publiée par l'éditeur. On ne signale donc que 0 ;
    /// toute autre valeur vaut « actif ».
    ///
    /// Logique pure. Chaque lecteur rend true (actif), false (coupé) ou null (clé absente).
    /// </summary>
    internal static class ReflexJeux
    {
        private static readonly Regex OwReflex = new Regex(@"^\s*ReflexMode\s*=\s*""?(\d+)""?", RegexOptions.Multiline);
        private static readonly Regex BfNvidia = new Regex(@"^\s*GstRender\.NvidiaLowLatency\s+(\d+)", RegexOptions.Multiline);
        private static readonly Regex BfAmd = new Regex(@"^\s*GstRender\.AMDLowLatency\s+(\d+)", RegexOptions.Multiline);
        private static readonly Regex CodReflex = new Regex(@"^\s*NvidiaReflex@[^=\r\n]*=\s*(Disabled|Enabled)",
                                                            RegexOptions.Multiline | RegexOptions.IgnoreCase);
        private static readonly Regex CodAntiLag = new Regex(@"^\s*AmdAntilag2@[^=\r\n]*=\s*(true|false)\b",
                                                             RegexOptions.Multiline | RegexOptions.IgnoreCase);

        public static bool? OverwatchReflex(string ini) { return Zero(OwReflex, ini); }
        public static bool? Battlefield6Reflex(string profil) { return Zero(BfNvidia, profil); }
        public static bool? Battlefield6AntiLag(string profil) { return Zero(BfAmd, profil); }

        public static bool? Bo6Reflex(string txt)
        {
            Match m = txt == null ? null : CodReflex.Match(txt);
            if (m == null || !m.Success) return null;
            return !m.Groups[1].Value.Equals("Disabled", StringComparison.OrdinalIgnoreCase);
        }

        public static bool? Bo6AntiLag2(string txt)
        {
            Match m = txt == null ? null : CodAntiLag.Match(txt);
            if (m == null || !m.Success) return null;
            return m.Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>« nvidia », « amd » ou null d'après le nom de la carte.</summary>
        public static string Marque(string gpu)
        {
            if (string.IsNullOrEmpty(gpu)) return null;
            if (gpu.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0) return "nvidia";
            if (gpu.IndexOf("Radeon", StringComparison.OrdinalIgnoreCase) >= 0
                || gpu.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0) return "amd";
            return null;
        }

        /// <summary>Constat quand la fonction de la marque de la carte est coupée dans le jeu ;
        /// null sinon (actif, clé absente, autre marque).
        /// nomAmd : nom de l'option AMD tel que le jeu le donne (« Anti-Lag 2 » pour Black Ops 6, dont le
        /// fichier le nomme ; « faible latence AMD » pour Battlefield 6, dont la clé ne dit pas laquelle).</summary>
        public static string Constat(string jeu, string gpu, bool? reflex, bool? antiLag, string nomAmd)
        {
            string marque = Marque(gpu);
            if (marque == "nvidia" && reflex == false)
                return "NVIDIA Reflex est coupé dans " + jeu + ". Quand la carte graphique est saturée, il "
                     + "réduit nettement la latence (TechPowerUp : 82 → 35 ms dans Overwatch) ; sinon, il ne "
                     + "change presque rien. Active-le dans les réglages vidéo du jeu (option « NVIDIA Reflex »).";
            if (marque == "amd" && antiLag == false)
                return "Dans " + jeu + ", l'option AMD de faible latence (" + (nomAmd ?? "Anti-Lag") + ") est coupée. "
                     + "Comme Reflex, elle réduit la latence quand la carte graphique est saturée ; sinon, elle "
                     + "ne change presque rien. Active-la dans les réglages vidéo du jeu.";
            return null;
        }

        private static bool? Zero(Regex r, string texte)
        {
            if (texte == null) return null;
            Match m = r.Match(texte);
            int v;
            if (!m.Success || !int.TryParse(m.Groups[1].Value, out v)) return null;
            return v != 0;
        }
    }
}
