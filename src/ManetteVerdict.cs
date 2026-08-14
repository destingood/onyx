using System;

namespace BTOptimizer
{
    /// <summary>
    /// CE QUE LA MESURE DE MANETTE PEUT DIRE, ET CE QU'ELLE NE PEUT PAS.
    ///
    /// L'ancien verdict déduisait le TYPE DE CONNEXION du seul débit mesuré :
    ///
    ///     « ≈ 125 Hz — typique du Bluetooth. Branche la manette en USB. »
    ///
    /// Or ce débit baisse pour d'autres raisons : l'utilisateur a peu bougé les sticks, la manette
    /// s'est mise en veille, la fenêtre de mesure est tombée sur un moment creux. Une manette DÉJÀ
    /// filaire pouvait donc se voir conseiller de passer en filaire — un conseil impossible à
    /// suivre, sur un diagnostic inventé.
    ///
    /// XInput sait pourtant répondre : XInputGetCapabilities porte un drapeau « sans fil ». La
    /// liaison est vérifiée (les slots vides rendent bien 1167). On lit donc au lieu de deviner, et
    /// quand la lecture échoue, on se tait sur ce point plutôt que d'inventer.
    ///
    /// Deuxième honnêteté nécessaire : ce chiffre est le taux de CHANGEMENTS D'ÉTAT vus par
    /// XInput, pas le taux de rapport de la manette elle-même. Windows a sa propre cadence, et
    /// elle plafonne ce qu'on peut observer. Annoncer « ta manette fait 250 Hz » alors qu'elle en
    /// fait peut-être 1000 derrière un plafond logiciel serait une conclusion de trop.
    /// </summary>
    internal static class ManetteVerdict
    {
        /// <summary>Valeur rendue par la mesure quand elle a ÉCHOUÉ, à distinguer d'un zéro
        /// qui signifie « aucun changement capté ».</summary>
        public const int Echec = -1;

        /// <summary>PUR : le titre du cadran. Ce n'est pas « le taux de la manette ».</summary>
        public const string Libelle = "CHANGEMENTS D'ÉTAT VUS PAR WINDOWS";

        /// <summary>
        /// PUR : le verdict.
        ///
        /// <paramref name="sansFil"/> n'est pris en compte que si <paramref name="capaciteLue"/>
        /// est vrai — sans quoi on ne dit rien du type de liaison.
        /// </summary>
        public static string Verdict(int hz, bool sansFil, bool capaciteLue)
        {
            if (hz == Echec)
                return "La mesure n'a pas abouti (la manette a-t-elle été débranchée ?). Relance-la.";
            if (hz <= 0)
                return "Aucun changement d'état capté : relance et BOUGE les sticks pendant toute la mesure. "
                     + "Une manette immobile ne rapporte rien, quel que soit son taux réel.";

            string niveau = hz >= 400
                ? "Très réactif : Windows voit " + hz + " changements par seconde."
                : hz >= 200
                ? "Réactif : Windows voit " + hz + " changements par seconde."
                : "Windows ne voit que " + hz + " changements par seconde — c'est peu.";

            // Le conseil ne dépend QUE de ce qu'on a réellement lu.
            string conseil;
            if (!capaciteLue)
                conseil = " Le type de liaison n'a pas pu être lu, donc aucun conseil de branchement ici.";
            else if (sansFil)
                conseil = " Windows indique une liaison SANS FIL. Un câble USB (ou le dongle propriétaire "
                        + "plutôt que le Bluetooth) réduit l'attente entre ton geste et le jeu.";
            else
                conseil = " Windows indique une liaison FILAIRE : de ce côté, il n'y a plus rien à gagner."
                        + (hz < 200 ? " Un chiffre bas vient alors plutôt de la mesure — bouge davantage les sticks." : "");

            return niveau + conseil
                 + "\n\nÀ savoir : ce chiffre est ce que Windows expose, pas le taux de rapport de la manette. "
                 + "Windows a sa propre cadence, et elle plafonne ce qu'on peut observer — une manette plus "
                 + "rapide ne se verra pas au-delà.";
        }
    }
}
