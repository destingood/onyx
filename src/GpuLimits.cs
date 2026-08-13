using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BTOptimizer
{
    /// <summary>
    /// LIMITE DE PUISSANCE ET LIMITE DE TEMPÉRATURE — les deux curseurs du haut d'Afterburner.
    ///
    /// Ce sont les deux réglages qui décident réellement de la fréquence que la carte tient dans la
    /// durée. Une RTX moderne ne s'arrête pas à une fréquence programmée : elle monte tant qu'elle
    /// a du budget de puissance ET de température, puis redescend dès qu'un des deux plafonds est
    /// touché. Élargir ces plafonds ne « donne » pas de fréquence — ça enlève le frein.
    ///
    /// Ils passent par l'outil officiel du pilote, sans rien installer :
    ///   • nvidia-smi --power-limit=W      (le curseur « Power Limit % » d'Afterburner)
    ///   • nvidia-smi --gpu-target-temp=C  (le curseur « Temp Limit »)
    ///
    /// POURQUOI DES WATTS ET PAS DES POURCENTS. Afterburner affiche 125 %. Le pilote, lui, ne parle
    /// qu'en watts. Le pourcentage est le rapport à la puissance PAR DÉFAUT de la carte — sur la
    /// machine de test : 400 W demandés pour 320 W par défaut, soit exactement les 125 % affichés.
    /// ONYX fait la conversion et affiche les deux, parce que le pourcentage seul ne veut rien dire
    /// d'une carte à l'autre.
    ///
    /// HONNÊTETÉ. Monter ces plafonds ne crée pas de performance là où il n'y en a pas : si la carte
    /// n'atteint jamais ses limites, les élargir ne change RIEN. Ça se paie toujours en
    /// consommation, en chaleur et en bruit. Les bornes ne sont jamais inventées : elles sont lues
    /// sur la carte, et une valeur hors bornes est ramenée dedans plutôt que refusée en silence.
    /// </summary>
    internal static class GpuLimits
    {
        /// <summary>Ce que la carte déclare. Les champs à 0 sont des valeurs NON LUES — jamais des
        /// suppositions.</summary>
        public sealed class Etat
        {
            public int PuissanceW;      // limite actuellement demandée
            public int DefautW;         // puissance de référence (le « 100 % »)
            public int MinW, MaxW;      // bornes acceptées par la carte
            public int TempCibleC;      // limite de température actuelle
            public int TempMaxC;        // température maximale d'exploitation
            public bool Lu;             // false = rien de fiable n'a pu être lu

            /// <summary>Limite de puissance en pourcentage de la référence, façon Afterburner.</summary>
            public int Pourcent { get { return GpuLimits.Pourcent(PuissanceW, DefautW); } }
        }

        // ------------------------------------------------------------------ pur

        /// <summary>Conversion PURE watts → pourcentage de la référence. 0 si la référence est
        /// inconnue : mieux vaut ne rien afficher qu'un pourcentage faux.</summary>
        public static int Pourcent(int watts, int defautW)
        {
            if (watts <= 0 || defautW <= 0) return 0;
            return (int)Math.Round(watts * 100.0 / defautW);
        }

        /// <summary>Conversion PURE pourcentage → watts, RAMENÉE dans les bornes de la carte.
        /// Demander 150 % à une carte qui plafonne à 125 % donne 125 %, pas une erreur.</summary>
        public static int Watts(int pourcent, int defautW, int minW, int maxW)
        {
            if (defautW <= 0) return 0;
            int w = (int)Math.Round(defautW * pourcent / 100.0);
            return Borne(w, minW, maxW);
        }

        /// <summary>Ramène une valeur dans un intervalle. Si l'intervalle est absurde (bornes non
        /// lues), la valeur passe telle quelle — on ne fabrique pas de limite.</summary>
        public static int Borne(int valeur, int min, int max)
        {
            if (min <= 0 || max <= 0 || min > max) return valeur;
            if (valeur < min) return min;
            if (valeur > max) return max;
            return valeur;
        }

        /// <summary>Lecture PURE de la sortie « nvidia-smi -q -d POWER,TEMPERATURE » (testable sans
        /// carte). Les champs absents restent à 0 plutôt que d'être devinés.</summary>
        public static Etat Analyse(string sortie)
        {
            var e = new Etat();
            if (string.IsNullOrEmpty(sortie)) return e;

            e.PuissanceW = Watt(sortie, @"Current Power Limit\s*:\s*([\d.]+)\s*W");
            if (e.PuissanceW == 0) e.PuissanceW = Watt(sortie, @"Requested Power Limit\s*:\s*([\d.]+)\s*W");
            e.DefautW = Watt(sortie, @"Default Power Limit\s*:\s*([\d.]+)\s*W");
            e.MinW = Watt(sortie, @"Min Power Limit\s*:\s*([\d.]+)\s*W");
            e.MaxW = Watt(sortie, @"Max Power Limit\s*:\s*([\d.]+)\s*W");
            e.TempCibleC = Entier(sortie, @"GPU Target Temperature\s*:\s*(\d+)");
            e.TempMaxC = Entier(sortie, @"GPU Max Operating Temp\s*:\s*(\d+)");

            // « Lu » exige le minimum vital : sans référence ni bornes, aucun curseur n'a de sens.
            e.Lu = e.DefautW > 0 && e.MinW > 0 && e.MaxW > 0;
            return e;
        }

        private static int Watt(string s, string motif)
        {
            Match m = Regex.Match(s, motif, RegexOptions.IgnoreCase);
            if (!m.Success) return 0;
            double v;
            if (!double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return 0;
            return v > 0 && v < 2000 ? (int)Math.Round(v) : 0;
        }

        private static int Entier(string s, string motif)
        {
            Match m = Regex.Match(s, motif, RegexOptions.IgnoreCase);
            int v;
            if (!m.Success || !int.TryParse(m.Groups[1].Value, out v)) return 0;
            return v > 0 && v < 200 ? v : 0;
        }

        /// <summary>Traduction PURE d'un échec de nvidia-smi. On ne dit « il faut les droits » que
        /// si le pilote l'a dit.</summary>
        public static string Motif(string sortie, int code)
        {
            string o = (sortie ?? "").ToLowerInvariant();
            if (o.Contains("insufficient permission") || o.Contains("permission"))
                return "Refusé : droits administrateur nécessaires.";
            if (o.Contains("not supported") || o.Contains("unsupported"))
                return "Cette carte n'accepte pas ce réglage.";
            if (o.Contains("out of range") || o.Contains("invalid argument"))
                return "Valeur hors des bornes acceptées par la carte.";
            return "Refusé par le pilote (code " + code + ").";
        }

        // ------------------------------------------------------------------ matériel

        private static string Smi { get { return Sys.Sys32("nvidia-smi.exe"); } }

        public static bool Disponible()
        {
            try { return System.IO.File.Exists(Smi); }
            catch { return false; }
        }

        /// <summary>Lit l'état réel de la carte.</summary>
        public static Etat Lire()
        {
            try
            {
                if (!Disponible()) return new Etat();
                NativeResult r = Sys.Run(Smi, "-q -d POWER,TEMPERATURE");
                return Analyse(r.Output);
            }
            catch { return new Etat(); }
        }

        /// <summary>Applique une limite de puissance, en watts. La valeur est RAMENÉE dans les
        /// bornes de la carte avant l'envoi — on ne laisse pas le pilote refuser pour rien.</summary>
        public static bool AppliquerPuissance(int watts, Action<string, int> log)
        {
            Etat e = Lire();
            if (!e.Lu)
            {
                if (log != null) log("Limites de puissance illisibles : rien n'a été changé.", 3);
                return false;
            }
            int w = Borne(watts, e.MinW, e.MaxW);
            if (w != watts && log != null)
                log("Demande de " + watts + " W ramenée à " + w + " W : c'est la borne de la carte.", 2);
            return Envoie("--power-limit=" + w,
                "Limite de puissance : " + w + " W (" + Pourcent(w, e.DefautW) + " % de la référence "
                + e.DefautW + " W). Plus de budget pour tenir la fréquence — et plus de chaleur.", log);
        }

        /// <summary>Applique une limite de température, en °C.</summary>
        public static bool AppliquerTemp(int celsius, Action<string, int> log)
        {
            Etat e = Lire();
            int max = e.TempMaxC > 0 ? e.TempMaxC : 90;
            int c = Borne(celsius, 60, max);
            if (c != celsius && log != null)
                log("Demande de " + celsius + " °C ramenée à " + c + " °C (maximum d'exploitation de la carte).", 2);
            return Envoie("--gpu-target-temp=" + c,
                "Limite de température : " + c + " °C. La carte réduit sa fréquence à partir de là.", log);
        }

        /// <summary>Rend la puissance à sa valeur d'usine. La température n'a pas de commande de
        /// remise à zéro : on y réécrit donc la valeur d'origine mémorisée par l'appelant.</summary>
        public static bool ReinitialiserPuissance(Action<string, int> log)
        {
            Etat e = Lire();
            if (!e.Lu)
            {
                if (log != null) log("Limites illisibles : remise à zéro annulée.", 3);
                return false;
            }
            return Envoie("--power-limit=" + e.DefautW,
                "Limite de puissance rendue à l'usine : " + e.DefautW + " W.", log);
        }

        private static bool Envoie(string args, string succes, Action<string, int> log)
        {
            try
            {
                NativeResult r = Sys.Run(Smi, args);
                string o = r.Output ?? "";
                // nvidia-smi rend 0 même quand il refuse : le texte fait foi, pas le code de sortie.
                bool refuse = o.IndexOf("Failed", StringComparison.OrdinalIgnoreCase) >= 0
                           || o.IndexOf("Insufficient", StringComparison.OrdinalIgnoreCase) >= 0
                           || o.IndexOf("Unable to", StringComparison.OrdinalIgnoreCase) >= 0;
                if (r.ExitCode != 0 || refuse)
                {
                    if (log != null) log(Motif(o, r.ExitCode), 3);
                    return false;
                }
                if (log != null) log(succes, 2);
                return true;
            }
            catch (Exception ex)
            {
                if (log != null) log("Réglage impossible : " + ex.Message, 3);
                return false;
            }
        }
    }
}
