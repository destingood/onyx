using System;
using System.Collections.Generic;

namespace BTOptimizer
{
    /// <summary>
    /// LE SEUL ENDROIT OÙ L'INVENTAIRE TOUCHE VRAIMENT À UN SERVICE.
    ///
    /// <see cref="InventaireServices"/> décide ; cette classe agit. La séparation n'est pas une
    /// coquetterie d'architecture : elle permet de vérifier au banc d'essai que le module refuse
    /// bien de toucher à un anticheat, SANS arrêter d'anticheat pour le prouver.
    ///
    /// Elle porte aussi le second filet. <see cref="Interdit"/> consulte <see cref="ServiceGuard"/>,
    /// la table des services que Windows réclame et qu'ONYX a déjà eu à réparer chez des gens. Le
    /// catalogue de l'inventaire ne la recopie pas — deux tables qui disent la même chose finissent
    /// par ne plus la dire, et c'est la machine de quelqu'un qui paie l'écart.
    /// </summary>
    internal sealed class ActionsServices : InventaireServices.IActions
    {
        private static readonly HashSet<string> Proteges = Recense();

        private static HashSet<string> Recense()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ServiceGuard.Regle r in ServiceGuard.Regles)
                if (r != null && !string.IsNullOrEmpty(r.Service)) set.Add(r.Service);
            return set;
        }

        /// <summary>
        /// Fait connaître les noms du garde-fou au CLASSEMENT, pas seulement au geste.
        ///
        /// Interdire au dernier moment corrigerait l'action et pas la proposition : ONYX aurait
        /// affiché « arrêtable », l'utilisateur aurait coché, et le refus serait arrivé après.
        /// À appeler avant toute analyse — c'est fait ici, et par le Mode Jeu quand il détecte.
        /// </summary>
        public static void Amorce()
        {
            InventaireServices.ProtegeAussi(Proteges);

            // Et dans l'autre sens : les sondes matérielles que le Mode Jeu arrête depuis toujours
            // (SondesMaterielles) doivent être reconnues ici. Sans ce raccord, ONYX suspendait
            // CCleanerPerformanceOptimizerService d'un côté en le déclarant « tiers inconnu » de
            // l'autre — relevé sur la machine de référence.
            InventaireServices.SuspendableAussi(SondesMaterielles.ServicesSondes);
        }

        public ActionsServices() { Amorce(); }

        public void Configure(string service, string typeDemarrage, bool arrete, bool demarre)
        {
            Sys.ConfigureService(service, typeDemarrage, arrete, demarre);
        }

        public void Arrete(string service) { Sys.StopService(service); }

        public void Demarre(string service) { Sys.StartService(service); }

        public bool Interdit(string service)
        {
            return !string.IsNullOrEmpty(service) && Proteges.Contains(service.Trim());
        }
    }
}
