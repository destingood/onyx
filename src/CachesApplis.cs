using System;
using System.Collections.Generic;

namespace BTOptimizer
{
    /// <summary>
    /// Caches d'applications : on ne vide le cache d'une appli que si elle est FERMÉE.
    ///
    /// Le nettoyage supprime les fichiers un par un. Appli ouverte, ses fichiers verrouillés
    /// (l'index du cache) restent et les autres partent : l'index pointe alors vers des données
    /// disparues. Chez Discord, ça donnait des icônes d'image cassée sur les décorations
    /// d'avatar, les plaques et les bannières, jusqu'au redémarrage de Discord. Les navigateurs
    /// et le launcher Epic (Chromium eux aussi) ont le même cache, donc le même risque.
    ///
    /// Partie pure : quels processus possèdent un cache, et lequel est ouvert. La lecture des
    /// processus vit dans Sys.CleanTargetNow.
    /// </summary>
    internal static class CachesApplis
    {
        public static readonly string[] Discord = { "Discord", "DiscordPTB", "DiscordCanary" };
        public static readonly string[] Chrome = { "chrome" };
        public static readonly string[] Edge = { "msedge" };
        public static readonly string[] Brave = { "brave" };
        public static readonly string[] Firefox = { "firefox" };
        public static readonly string[] Epic = { "EpicGamesLauncher", "EpicWebHelper" };

        /// <summary>Premier processus de <paramref name="proprietaires"/> présent parmi les
        /// processus ouverts (nom exact, casse ignorée), sinon null.</summary>
        public static string Ouvert(string[] proprietaires, IEnumerable<string> ouverts)
        {
            if (proprietaires == null || proprietaires.Length == 0 || ouverts == null) return null;
            foreach (string o in ouverts)
            {
                if (string.IsNullOrEmpty(o)) continue;
                foreach (string p in proprietaires)
                    if (string.Equals(o, p, StringComparison.OrdinalIgnoreCase)) return o;
            }
            return null;
        }

        /// <summary>Propriétaire d'une entrée winapp2, d'après son nom (« Discord * », « Google
        /// Chrome * »…). Null si l'entrée ne concerne aucune de ces applis.</summary>
        public static string[] PourEntreeWinapp2(string nomEntree)
        {
            if (string.IsNullOrEmpty(nomEntree)) return null;
            string n = nomEntree.Trim();
            if (Commence(n, "Discord")) return Discord;
            if (Commence(n, "Google Chrome") || Commence(n, "Chrome")) return Chrome;
            if (Commence(n, "Microsoft Edge") || Commence(n, "Edge")) return Edge;
            if (Commence(n, "Brave")) return Brave;
            if (Commence(n, "Mozilla Firefox") || Commence(n, "Firefox")) return Firefox;
            if (Commence(n, "Epic Games")) return Epic;
            return null;
        }

        private static bool Commence(string nom, string prefixe)
        {
            return nom.StartsWith(prefixe, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Ligne de journal quand un cache est laissé de côté.</summary>
        public static string MessageIgnore(string nomCible, string processus)
        {
            return nomCible + " : laissé tel quel, " + processus + " est ouvert (le vider maintenant "
                 + "casserait ses images jusqu'à son redémarrage). Ferme " + processus
                 + " puis relance le nettoyage.";
        }
    }
}
