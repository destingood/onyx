using System;

namespace BTOptimizer
{
    /// <summary>
    /// LES BORNES DU CURSEUR DE POWER LIMIT — celles du pilote, pas celles qu'on suppose.
    ///
    /// Le curseur avait un minimum écrit en dur à 50 %. Sa valeur de départ, elle, était calculée
    /// à partir du power limit courant et bornée UNIQUEMENT PAR LE HAUT :
    ///
    ///     _plBar.Value = Math.Min(_plBar.Maximum, (int)Math.Round(100.0 * PowerCur / PowerDefault));
    ///
    /// Il suffisait donc que le power limit courant soit sous 50 % du défaut pour que le
    /// composant refuse la valeur et lève une exception — dans le CONSTRUCTEUR, c'est-à-dire que
    /// la fenêtre Overclock ne s'ouvrait plus du tout.
    ///
    /// Ce n'est pas une hypothèse : sur la RTX 4080 SUPER de cette machine, le pilote autorise
    /// 150 W pour un défaut de 320 W, soit 47 %. Un utilisateur qui bride sa carte au minimum —
    /// pour le bruit, la chaleur, ou parce qu'un autre outil l'a fait — perdait l'accès à la
    /// fenêtre, sans message et sans comprendre pourquoi.
    ///
    /// Au passage, ce 50 % en dur empêchait aussi d'atteindre le vrai minimum du pilote. Les
    /// bornes viennent maintenant de la carte elle-même.
    /// </summary>
    internal static class BornesPuissance
    {
        /// <summary>Amplitude retenue quand la carte ne dit rien d'exploitable : le curseur reste
        /// utilisable autour de 100 % au lieu de partir dans des valeurs absurdes.</summary>
        public const int MaxParDefaut = 120;

        /// <summary>
        /// PUR : bornes et position du curseur, en pourcentage du power limit par défaut.
        ///
        /// Garantit trois choses, quelles que soient les valeurs rendues par le pilote :
        /// <paramref name="minPct"/> &lt; <paramref name="maxPct"/>, la valeur est DANS l'intervalle,
        /// et aucune n'est négative. Sans quoi le composant lève et la fenêtre ne s'ouvre pas.
        /// </summary>
        public static void Calcule(double wattsMin, double wattsDefaut, double wattsMax, double wattsCourant,
                                   out int minPct, out int maxPct, out int valPct)
        {
            if (wattsDefaut <= 0 || double.IsNaN(wattsDefaut) || double.IsInfinity(wattsDefaut))
            {
                minPct = 100; maxPct = MaxParDefaut; valPct = 100;
                return;
            }

            // Plancher : on ARRONDIT VERS LE BAS pour que le minimum réel du pilote reste
            // atteignable (47,3 % doit donner 47, pas 48 — sinon la borne est inaccessible).
            minPct = wattsMin > 0 ? (int)Math.Floor(100.0 * wattsMin / wattsDefaut) : 50;
            if (minPct < 1) minPct = 1;
            if (minPct > 100) minPct = 100;   // une carte dont le minimum dépasse le défaut : on ne bloque pas au-dessus de 100

            // Plafond : vers le HAUT, même raison.
            maxPct = wattsMax > 0 ? (int)Math.Ceiling(100.0 * wattsMax / wattsDefaut) : MaxParDefaut;
            if (maxPct < MaxParDefaut) maxPct = MaxParDefaut;
            if (maxPct <= minPct) maxPct = minPct + 1;

            // Position : bornée DES DEUX CÔTÉS. C'est l'oubli qui faisait tout échouer.
            valPct = wattsCourant > 0 ? (int)Math.Round(100.0 * wattsCourant / wattsDefaut) : 100;
            if (valPct < minPct) valPct = minPct;
            if (valPct > maxPct) valPct = maxPct;
        }

        /// <summary>Écart toléré entre ce qu'on demande et ce que la carte rend : nvidia-smi
        /// arrondit, et le pilote peut ajuster de quelques watts.</summary>
        public const double ToleranceWatts = 2.0;

        /// <summary>
        /// PUR : le message à afficher APRÈS avoir tenté d'écrire le power limit.
        ///
        /// L'ancien code annonçait « Power limit GPU appliqué » sans condition — alors qu'il
        /// venait de relire la carte deux lignes plus haut. La vérité était disponible et
        /// inutilisée. Ici, on ne confirme que ce que la carte confirme.
        ///
        /// <paramref name="wattsObserves"/> ≤ 0 signifie qu'on n'a pas réussi à relire la carte :
        /// on ne dit alors ni « appliqué » ni « échoué », parce qu'on ne sait pas.
        /// </summary>
        public static string Confirmation(int wattsDemandes, double wattsObserves, bool commandeAcceptee)
        {
            if (wattsObserves <= 0)
                return commandeAcceptee
                    ? "Commande envoyée à la carte, mais impossible de relire son état pour le confirmer."
                    : "La carte a REFUSÉ le changement de power limit, et son état n'a pas pu être relu.";

            bool atteint = Math.Abs(wattsObserves - wattsDemandes) <= ToleranceWatts;
            if (atteint)
                return "Power limit appliqué : la carte confirme " + wattsObserves.ToString("0") + " W "
                     + "(fréquences gérées par le pilote).";

            return "ÉCHEC : la carte annonce toujours " + wattsObserves.ToString("0") + " W, pas les "
                 + wattsDemandes + " W demandés."
                 + (commandeAcceptee
                    ? " La commande a été acceptée mais n'a pas pris effet — certaines cartes, notamment sur portable, refusent ce réglage."
                    : " La commande a été refusée (élévation manquante, ou carte qui n'autorise pas ce réglage).");
        }
    }
}
