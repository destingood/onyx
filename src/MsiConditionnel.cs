using System.Collections.Generic;

namespace BTOptimizer
{
    /// <summary>
    /// Mode MSI conditionnel (ajout 4 de docs/plan-parite-linux.md). Sur un PC récent, le pilote
    /// met déjà MSISupported à 1 sur la carte graphique, l'USB, le NVMe et le réseau (RigPolice,
    /// 19/09/2026) : ONYX n'écrit alors rien. Pur : testé par le banc.
    /// </summary>
    public static class MsiConditionnel
    {
        /// <summary>Valeur MSISupported lue dans le registre (REG_DWORD lu en int) : 1 = déjà en MSI.</summary>
        public static bool DejaEnMsi(object valeur)
        {
            return valeur is int && (int)valeur == 1;
        }

        /// <summary>true = tous en MSI, false = au moins un sans, null = aucun périphérique.</summary>
        public static bool? TousEnMsi(IEnumerable<object> valeurs)
        {
            bool vu = false;
            foreach (object v in valeurs)
            {
                vu = true;
                if (!DejaEnMsi(v)) return false;
            }
            return vu ? (bool?)true : null;
        }

        /// <summary>Phrase de bilan après une activation : ecrits = passés en MSI, deja = laissés tels quels.</summary>
        public static string Bilan(int ecrits, int deja)
        {
            if (ecrits == 0 && deja == 0) return "MSI : aucun périphérique concerné.";
            if (ecrits == 0) return "MSI déjà actif sur " + deja + " périphérique(s) : rien à faire, rien n'a été écrit.";
            return "MSI activé sur " + ecrits + " périphérique(s)"
                + (deja > 0 ? ", déjà actif sur " + deja + " (non touchés)" : "") + ". Redémarrage requis.";
        }
    }
}
