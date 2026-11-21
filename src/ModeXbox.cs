using System;
using System.Text.RegularExpressions;

namespace BTOptimizer
{
    /// <summary>
    /// Conseil « mode Xbox » (expérience plein écran de Windows 11) : on joue sans le bureau,
    /// Windows lance moins de processus de fond (l'Explorateur ne tourne pas).
    ///
    /// Ce que disent les mesures (LTT Labs, 14 juin 2026 : Ryzen 5 9600X, RTX 5060 Ti, 2 × 8 Go) :
    ///   - aucun gain de FPS ;
    ///   - 0,2 à 0,3 Go de mémoire engagée en moins en jeu, 0,6 à 1,3 Go au repos ;
    ///   - défauts : manette exigée par endroits, focus des fenêtres, navigation pénible hors jeux.
    /// Le conseil ne vise donc que les PC où cette mémoire compte : 16 Go ou moins, ou une puce
    /// graphique intégrée seule (elle prend sa mémoire dans la RAM). Jamais présenté comme un gain
    /// de FPS.
    ///
    /// Disponibilité (Microsoft, ElevenForum) : Windows 11 24H2 et 25H2 à partir de la révision
    /// 8328 (KB5083631, 30 avril 2026), 26H1 (28000) à partir de 2179. Ailleurs on se tait.
    ///
    /// Logique pure, lecture seule. Réversible par nature : Win+F11 entre et sort du mode.
    /// </summary>
    internal static class ModeXbox
    {
        /// <summary>16 Go installés donnent un peu moins de 16 384 Mo visibles ; 17 000 laisse
        /// passer les 16 Go et écarte les 24 et 32 Go.</summary>
        private const long RamMaxMo = 17000;

        /// <summary>Le mode Xbox est-il proposé par ce Windows ? Build et révision (UBR).</summary>
        public static bool Disponible(int build, int ubr)
        {
            if (build == 26100 || build == 26200) return ubr >= 8328;
            if (build == 28000) return ubr >= 2179;
            return false;   // Insider ou build inconnu : on ne promet rien
        }

        /// <summary>Puce graphique intégrée reconnue avec certitude par son nom.</summary>
        public static bool EstIntegree(string gpu)
        {
            if (string.IsNullOrEmpty(gpu)) return false;
            string g = gpu.Trim();
            if (g.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (g.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // Cartes dédiées : Arc A380/A770, B570/B580, Arc Pro.
                if (Regex.IsMatch(g, @"\b[AB]\d{3}\b") || Regex.IsMatch(g, @"Arc\(TM\)\s+Pro|Arc\s+Pro", RegexOptions.IgnoreCase))
                    return false;
                return Regex.IsMatch(g, @"UHD|Iris|HD Graphics|Intel\(R\) Graphics|Arc\(TM\) Graphics|\b1[34]0[VT]\b",
                                     RegexOptions.IgnoreCase);
            }
            if (g.IndexOf("Radeon", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (Regex.IsMatch(g, @"\bRX\b|Radeon\s*(\(TM\))?\s*Pro\b", RegexOptions.IgnoreCase)) return false;
                return Regex.IsMatch(g, @"^AMD Radeon\s*(\(TM\))?\s*Graphics$", RegexOptions.IgnoreCase)
                    || Regex.IsMatch(g, @"Radeon\s*(\(TM\))?\s*\d{3}M\b", RegexOptions.IgnoreCase)
                    || Regex.IsMatch(g, @"\b80[456]0S\b")
                    || Regex.IsMatch(g, @"Vega\s+\d{1,2}\s+Graphics", RegexOptions.IgnoreCase);
            }
            return false;   // marque inconnue : traitée comme dédiée (on se tait)
        }

        /// <summary>Vrai si toutes les cartes listées sont des puces intégrées (au moins une).</summary>
        public static bool IntegreeSeule(string[] cartes)
        {
            if (cartes == null || cartes.Length == 0) return false;
            foreach (string c in cartes) if (!EstIntegree(c)) return false;
            return true;
        }

        /// <summary>Texte du conseil, ou null quand il n'apporte rien à ce PC.
        /// ramMo : mémoire physique visible (0 = illisible).</summary>
        public static string Conseil(long ramMo, string[] cartes, int build, int ubr)
        {
            if (!Disponible(build, ubr)) return null;
            bool peuDeRam = ramMo > 0 && ramMo <= RamMaxMo;
            bool integree = IntegreeSeule(cartes);
            if (!peuDeRam && !integree) return null;

            string pourquoi = peuDeRam && integree
                ? "Avec " + Go(ramMo) + " de mémoire, dont une part prise par la puce graphique intégrée"
                : peuDeRam ? "Avec " + Go(ramMo) + " de mémoire"
                : "Ta puce graphique intégrée prend sa mémoire dans la RAM";
            return pourquoi + ", le mode Xbox de Windows peut aider : on joue sans le bureau et Windows "
                 + "lance moins de processus de fond. Mesuré par LTT Labs sur un PC 16 Go : 0,2 à 0,3 Go "
                 + "de mémoire en moins en jeu, 0,6 à 1,3 Go au repos, aucun FPS en plus. "
                 + "Paramètres > Jeux > Mode Xbox ; Win+F11 pour entrer et sortir. "
                 + "Défaut : navigation pénible hors des jeux.";
        }

        private static string Go(long mo)
        {
            return Math.Round(mo / 1024.0).ToString("0") + " Go";
        }
    }
}
