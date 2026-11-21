using System;
using System.Text.RegularExpressions;

namespace BTOptimizer
{
    /// <summary>
    /// Shaders précompilés (Advanced Shader Delivery, « ASD ») : les jeux compatibles téléchargent
    /// leurs shaders déjà compilés au lieu de les compiler en jeu (saccades des premières minutes).
    /// C'est l'équivalent Windows du cache de shaders que Steam distribue sous Linux.
    ///
    /// Prérequis publiés par Microsoft (DirectX Developer Blog, 21 octobre 2026, « Advanced Shader
    /// Delivery is Available Now Across Hardware ») : Windows 11 24H2 ou plus, et
    ///   - NVIDIA : toutes les RTX, pilote 621.01 ou plus ;
    ///   - AMD : RDNA 1 à 4, Adrenalin 26.6.1 ou plus ;
    ///   - Intel : Arc B570/B580, Core Ultra séries 2 et 3, pilote 32.0.101.9150 ou plus.
    /// (Qualcomm Snapdragon X2 aussi : non traité, ONYX vise les PC x64.)
    ///
    /// Logique pure, lecture seule. Conservatrice : carte non reconnue avec certitude, version
    /// illisible → aucun constat.
    /// </summary>
    internal static class ShadersPrecompiles
    {
        private const int Build24H2 = 26100;
        private const int NvidiaMin = 62101;                        // 621.01
        private static readonly int[] AdrenalinMin = { 26, 6, 1 };
        private static readonly int[] IntelMin = { 32, 0, 101, 9150 };

        private const string Cache = " Ne vide pas le cache de shaders sans raison : il évite de les recompiler "
                                   + "au prochain lancement (saccades).";

        /// <summary>Marque si la carte est éligible avec certitude : « nvidia », « amd », « intel », sinon null.</summary>
        public static string CarteEligible(string gpu)
        {
            if (string.IsNullOrEmpty(gpu)) return null;
            if (Regex.IsMatch(gpu, @"\bRTX\b", RegexOptions.IgnoreCase)
                && gpu.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0) return "nvidia";
            // RDNA 1 à 4 en carte dédiée : RX à 4 chiffres des séries 5000, 6000, 7000, 9000
            // (les RX 550-590 à 3 chiffres sont des Polaris, non éligibles). Puces intégrées
            // RDNA 2 à 3.5 seulement si le nom les désigne (« AMD Radeon(TM) Graphics » : on se tait).
            if (Regex.IsMatch(gpu, @"\bRX\s*[5679]\d{3}\b", RegexOptions.IgnoreCase)
                || Regex.IsMatch(gpu, @"\bRadeon\b.*\b(6[68]0M|7[468]0M|8[689]0M|80[456]0S)\b", RegexOptions.IgnoreCase))
                return "amd";
            // Intel : Arc B570/B580 ; Core Ultra série 2 (Arc 130V/140V, 130T/140T).
            if (gpu.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0
                && Regex.IsMatch(gpu, @"\b(B570|B580|1[34]0[VT])\b", RegexOptions.IgnoreCase)) return "intel";
            return null;
        }

        /// <summary>Windows antérieur à 24H2 avec une carte éligible : constat, sinon null.</summary>
        public static string WindowsTropAncien(int build, string gpu)
        {
            if (build <= 0 || build >= Build24H2 || CarteEligible(gpu) == null) return null;
            return "Windows est antérieur à Windows 11 24H2 : les shaders précompilés (Advanced Shader Delivery) "
                 + "ne sont pas disponibles. Avec 24H2 ou plus, les jeux compatibles téléchargent leurs shaders "
                 + "déjà compilés : moins de saccades et de chargement au premier lancement." + Cache;
        }

        /// <summary>
        /// Windows 24H2 ou plus, carte éligible, pilote trop ancien : constat, sinon null.
        /// <paramref name="versionPilote"/> : version Windows du pilote (WMI) ;
        /// <paramref name="versionAdrenalin"/> : version Adrenalin d'AMD (« 26.6.1 »), null si inconnue.
        /// </summary>
        public static string PiloteTropAncien(int build, string gpu, string versionPilote, string versionAdrenalin)
        {
            if (build < Build24H2) return null;
            string marque = CarteEligible(gpu);
            string min;
            if (marque == "nvidia")
            {
                int v = ProblemesConnus.VersionNvidia(versionPilote);
                if (v < 0 || v >= NvidiaMin) return null;
                min = "NVIDIA 621.01";
            }
            else if (marque == "amd")
            {
                int c = Compare(versionAdrenalin, AdrenalinMin);
                if (c == int.MinValue || c >= 0) return null;
                min = "AMD Adrenalin 26.6.1";
            }
            else if (marque == "intel")
            {
                int c = Compare(versionPilote, IntelMin);
                if (c == int.MinValue || c >= 0) return null;
                min = "Intel 32.0.101.9150";
            }
            else return null;
            return "Le pilote graphique est trop ancien pour les shaders précompilés (Advanced Shader Delivery) : "
                 + "il faut le pilote " + min + " ou plus récent. Les jeux compatibles téléchargent alors leurs "
                 + "shaders déjà compilés : moins de saccades et de chargement au premier lancement." + Cache;
        }

        /// <summary>Compare une version « a.b.c[.d] » à un minimum. &lt;0, 0, &gt;0 ; int.MinValue si illisible.</summary>
        public static int Compare(string version, int[] min)
        {
            if (string.IsNullOrEmpty(version)) return int.MinValue;
            string[] p = version.Trim().Split('.');
            if (p.Length < min.Length) return int.MinValue;
            for (int i = 0; i < min.Length; i++)
            {
                int n;
                if (!int.TryParse(p[i], out n)) return int.MinValue;
                if (n != min[i]) return n < min[i] ? -1 : 1;
            }
            return 0;
        }
    }
}
