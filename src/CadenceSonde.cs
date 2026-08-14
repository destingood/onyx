using System;

namespace BTOptimizer
{
    /// <summary>
    /// À QUELLE FRÉQUENCE INTERROGER UNE SONDE LENTE.
    ///
    /// La température CPU se lit par WMI, dans l'espace de noms root\WMI (zone thermique ACPI).
    /// Une requête WMI n'est pas un appel de fonction : elle traverse DCOM jusqu'au processus
    /// WmiPrvSE.exe et revient. Mesuré sur cette machine : ~4 ms par requête, même quand elle
    /// échoue.
    ///
    /// Or l'échantillonnage tourne à 1 Hz — y compris l'overlay affiché PENDANT LE JEU. Une
    /// application dont tout le propos est de réduire la latence n'a rien à faire à dépenser
    /// 4 ms de va-et-vient inter-processus par seconde en pleine partie, pour une grandeur qui
    /// bouge sur plusieurs secondes.
    ///
    /// Pire : sur les machines où la zone thermique ACPI n'est pas exposée (courant sur les
    /// PC de bureau), la requête échoue à TOUS les coups. Sans mémoire de cet échec, on paie le
    /// prix fort chaque seconde, indéfiniment, pour un résultat qui ne viendra jamais.
    ///
    /// D'où ces deux règles, séparées de toute entrée-sortie pour rester vérifiables :
    ///   — on ne relit pas plus souvent que la période de base ;
    ///   — chaque échec consécutif double l'attente, jusqu'à un plafond.
    /// </summary>
    internal static class CadenceSonde
    {
        /// <summary>Période de base. Assez courte pour qu'une montée en température reste
        /// visible à l'œil, assez longue pour diviser par deux les allers-retours WMI.</summary>
        public const int PeriodeMs = 2000;

        /// <summary>Attente maximale après échecs répétés. Au-delà, insister ne sert plus à
        /// rien, mais on ne renonce jamais tout à fait : un pilote peut être installé en
        /// cours de route.</summary>
        public const int PlafondMs = 60000;

        /// <summary>
        /// PUR : délai à respecter avant la prochaine lecture, selon le nombre d'échecs
        /// consécutifs. Doublement à chaque échec, plafonné.
        /// </summary>
        public static int DelaiMs(int echecsConsecutifs)
        {
            if (echecsConsecutifs <= 0) return PeriodeMs;
            long d = PeriodeMs;
            for (int i = 0; i < echecsConsecutifs && d < PlafondMs; i++) d *= 2;
            return d > PlafondMs ? PlafondMs : (int)d;
        }

        /// <summary>
        /// PUR : faut-il relire ? <paramref name="ageMs"/> négatif signifie « jamais lu »,
        /// et dans ce cas on lit — sinon la toute première valeur n'arriverait jamais.
        /// </summary>
        public static bool DoitRelire(double ageMs, int echecsConsecutifs)
        {
            if (ageMs < 0) return true;
            return ageMs >= DelaiMs(echecsConsecutifs);
        }
    }
}
