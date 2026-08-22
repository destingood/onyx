using System;

namespace BTOptimizer
{
    /// <summary>
    /// LES RAISONS DE BRIDAGE DU GPU NVIDIA, EN UN SEUL LANCEMENT AU LIEU DE SIX.
    ///
    /// Le panneau thermique interrogeait nvidia-smi UN CHAMP À LA FOIS : six lancements de
    /// processus par cycle, mesurés à ~39 ms pièce, soit près d'un quart de seconde de création
    /// de processus toutes les deux secondes — pendant que l'utilisateur regarde justement s'il
    /// est bridé, c'est-à-dire au pire moment.
    ///
    /// C'était fait ainsi pour une bonne raison : nvidia-smi a renommé ces champs
    /// (« clocks_throttle_reasons » → « clocks_event_reasons »), et demander un champ inconnu
    /// invalide la requête entière. Un champ à la fois, c'était la façon sûre.
    ///
    /// La bonne réponse n'est pas d'abandonner le regroupement, c'est d'essayer une famille de
    /// noms puis l'autre : un lancement sur pilote récent, deux sur pilote ancien.
    ///
    /// DEUX PIÈGES, tous deux vérifiés sur machine réelle :
    ///   — un champ invalide sort en CODE DE RETOUR 0. Tester le code ne suffit pas, il faut
    ///     lire la réponse.
    ///   — la réponse groupée s'écrit « Active, Not Active, Not Active ». Chercher « active »
    ///     dans la ligne entière donnerait « pas de bridage » alors qu'il y en a un. On découpe
    ///     donc AVANT de juger — c'est tout l'objet de ce module.
    /// </summary>
    internal static class NvBridage
    {
        /// <summary>Les trois raisons surveillées, dans l'ordre où elles sont demandées.</summary>
        public static readonly string[] Champs =
        {
            "hw_thermal_slowdown",
            "sw_thermal_slowdown",
            "hw_power_brake_slowdown"
        };

        /// <summary>PUR : l'argument --query-gpu pour une famille de noms donnée.</summary>
        public static string Requete(string prefixe)
        {
            if (string.IsNullOrEmpty(prefixe)) return "";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < Champs.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(prefixe).Append('.').Append(Champs[i]);
            }
            return sb.ToString();
        }

        /// <summary>PUR : nvidia-smi a-t-il rejeté la requête ? Il le dit dans sa sortie tout en
        /// rendant un code de retour 0, donc c'est bien la sortie qu'il faut lire.</summary>
        public static bool Rejetee(string sortie)
        {
            if (string.IsNullOrEmpty(sortie)) return true;
            string s = sortie.ToLowerInvariant();
            return s.Contains("not a valid field")
                || s.Contains("invalid combination")
                || s.Contains("unrecognized");
        }

        /// <summary>PUR : une valeur isolée signale-t-elle un bridage actif ?</summary>
        public static bool Actif(string valeur)
        {
            if (string.IsNullOrEmpty(valeur)) return false;
            string v = valeur.Trim().ToLowerInvariant();
            return v.Contains("active") && !v.Contains("not active");
        }

        /// <summary>
        /// PUR : découpe la ligne rendue par nvidia-smi en un drapeau par champ.
        /// Rend null si la ligne est inexploitable — jamais un tableau partiel, qui ferait
        /// passer un champ manquant pour un champ « pas de bridage ».
        /// </summary>
        public static bool[] Analyse(string sortie)
        {
            if (Rejetee(sortie)) return null;
            string ligne = sortie.Replace("\r", "").Split('\n')[0];
            string[] parts = ligne.Split(',');
            if (parts.Length < Champs.Length) return null;

            var r = new bool[Champs.Length];
            for (int i = 0; i < Champs.Length; i++) r[i] = Actif(parts[i]);
            return r;
        }

        /// <summary>PUR : y a-t-il bridage THERMIQUE ? (matériel ou logiciel)</summary>
        public static bool Thermique(bool[] d) { return d != null && (d[0] || d[1]); }

        /// <summary>PUR : y a-t-il bridage par LIMITE DE PUISSANCE ?</summary>
        public static bool Puissance(bool[] d) { return d != null && d[2]; }
    }
}
