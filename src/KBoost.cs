using System;

namespace BTOptimizer
{
    /// <summary>
    /// FRÉQUENCES GPU VERROUILLÉES AU MAXIMUM — l'équivalent du « K-Boost » d'EVGA.
    ///
    /// Au repos et dans les scènes légères, le pilote fait redescendre la fréquence de la carte
    /// pour économiser. Quand la charge repart, il faut quelques millisecondes pour remonter — et
    /// ces montées/descentes se voient sur les creux d'images plus que sur la moyenne. Verrouiller
    /// la fréquence supprime ces transitions : la carte reste en haut, tout le temps.
    ///
    /// EVGA appelait ça K-Boost et passait par un service maison. On fait la même chose avec
    /// l'outil officiel du pilote, sans rien installer : nvidia-smi --lock-gpu-clocks. Vérifié
    /// supporté sur RTX 40 (l'ancienne méthode --applications-clocks, elle, est marquée obsolète
    /// par NVIDIA sur les GeForce).
    ///
    /// HONNÊTETÉ SUR CE QUE ÇA RAPPORTE : ce n'est PAS un gain de FPS moyen. Une carte déjà à 100 %
    /// d'utilisation tourne déjà à sa fréquence maximale — il n'y a rien à gagner. Le bénéfice est
    /// sur la RÉGULARITÉ, quand la charge varie. En échange : plus de consommation, plus de chaleur,
    /// plus de bruit de ventilateurs, en permanence. C'est pourquoi ce réglage n'est dans AUCUN
    /// preset : il se choisit, il ne s'applique pas par défaut.
    /// </summary>
    internal static class KBoost
    {
        /// <summary>Plancher du verrouillage, en pourcentage de la fréquence maximale. On ne
        /// verrouille pas min = max : laisser une petite plage évite que le pilote refuse la
        /// consigne quand la carte doit se protéger (température, limite de puissance).</summary>
        private const int PlancherPourcent = 70;

        private static string EtatPath { get { return AppPaths.File("bt-kboost.txt"); } }

        /// <summary>Fréquence graphique maximale de la carte, en MHz. 0 si illisible.</summary>
        public static int FrequenceMax()
        {
            try
            {
                NativeResult r = Sys.Run(Sys.Sys32("nvidia-smi.exe"), "--query-gpu=clocks.max.graphics --format=csv,noheader,nounits");
                return LitMhz(r.Output);
            }
            catch { return 0; }
        }

        /// <summary>Fréquence graphique actuelle, en MHz. 0 si illisible.</summary>
        public static int FrequenceActuelle()
        {
            try
            {
                NativeResult r = Sys.Run(Sys.Sys32("nvidia-smi.exe"), "--query-gpu=clocks.current.graphics --format=csv,noheader,nounits");
                return LitMhz(r.Output);
            }
            catch { return 0; }
        }

        /// <summary>Lecture PURE d'une sortie nvidia-smi en MHz (testable sans matériel).</summary>
        public static int LitMhz(string sortie)
        {
            if (string.IsNullOrEmpty(sortie)) return 0;
            foreach (var ligne in sortie.Replace("\r", "").Split('\n'))
            {
                string s = ligne.Trim();
                if (s.Length == 0) continue;
                var m = System.Text.RegularExpressions.Regex.Match(s, @"^(\d{3,5})");
                if (m.Success)
                {
                    int v;
                    if (int.TryParse(m.Groups[1].Value, out v) && v > 0) return v;
                }
            }
            return 0;
        }

        /// <summary>Consigne de verrouillage PURE : « min,max » à partir de la fréquence maximale.
        /// Chaîne vide si la fréquence est absurde — on ne devine jamais une consigne.</summary>
        public static string Consigne(int freqMax)
        {
            if (freqMax < 300 || freqMax > 10000) return "";
            int min = freqMax * PlancherPourcent / 100;
            return min + "," + freqMax;
        }

        public static bool Disponible()
        {
            try { return System.IO.File.Exists(Sys.Sys32("nvidia-smi.exe")) && FrequenceMax() > 0; }
            catch { return false; }
        }

        /// <summary>Verrouille les fréquences au maximum de la carte.</summary>
        public static bool Activer(Action<string, int> log)
        {
            int max = FrequenceMax();
            string consigne = Consigne(max);
            if (consigne.Length == 0)
            {
                if (log != null) log("Fréquences GPU illisibles : verrouillage annulé (aucune consigne inventée).", 3);
                return false;
            }
            try
            {
                NativeResult r = Sys.Run(Sys.Sys32("nvidia-smi.exe"), "--lock-gpu-clocks=" + consigne);
                if (r.ExitCode != 0)
                {
                    string o = (r.Output ?? "").ToLowerInvariant();
                    if (log != null)
                        log(o.Contains("permission")
                            ? "Verrouillage refusé : droits administrateur nécessaires."
                            : "Verrouillage refusé par le pilote (code " + r.ExitCode + ").", 3);
                    return false;
                }
            }
            catch (Exception ex)
            {
                if (log != null) log("Verrouillage impossible : " + ex.Message, 3);
                return false;
            }
            try { System.IO.File.WriteAllText(EtatPath, consigne + "\n"); } catch { }
            if (log != null)
                log("Fréquences GPU verrouillées à " + max + " MHz. Plus de montées/descentes : les creux "
                  + "d'images se lissent. En échange : consommation, chaleur et bruit en hausse, en permanence.", 2);
            return true;
        }

        /// <summary>Rend la main au pilote.</summary>
        public static bool Desactiver(Action<string, int> log)
        {
            try
            {
                NativeResult r = Sys.Run(Sys.Sys32("nvidia-smi.exe"), "--reset-gpu-clocks");
                try { if (System.IO.File.Exists(EtatPath)) System.IO.File.Delete(EtatPath); } catch { }
                if (r.ExitCode != 0)
                {
                    if (log != null) log("Le pilote a refusé la remise à zéro (code " + r.ExitCode + ").", 2);
                    return false;
                }
                if (log != null) log("Fréquences GPU rendues au pilote : la carte redescend de nouveau au repos.", 1);
                return true;
            }
            catch (Exception ex)
            {
                if (log != null) log("Remise à zéro impossible : " + ex.Message, 3);
                return false;
            }
        }

        /// <summary>
        /// true = verrouillé, false = libre, null = indéterminé (pas de GPU NVIDIA, outil absent).
        /// On se fie à la mémoire de l'app : nvidia-smi n'expose pas d'état de verrouillage
        /// interrogeable de façon fiable sur GeForce, et deviner à partir de la fréquence courante
        /// donnerait un faux positif dès que la carte est sous charge.
        /// </summary>
        public static bool? Etat()
        {
            try
            {
                if (!Disponible()) return null;
                return System.IO.File.Exists(EtatPath);
            }
            catch { return null; }
        }
    }
}
