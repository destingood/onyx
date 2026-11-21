using System;

namespace BTOptimizer
{
    /// <summary>
    /// Intégrité de la mémoire (HVCI) : informer, ne jamais couper d'office.
    ///
    /// Fait publié par Microsoft (Windows IT Pro Blog, « Expanding memory integrity protection
    /// across Windows devices », mis à jour le 31 août 2026) : à partir d'octobre 2026, les mises à
    /// jour qualité de Windows activent l'intégrité de la mémoire sur les PC éligibles, mais
    /// seulement là où elle n'est pas configurée et où aucune stratégie ne s'applique. Un PC où elle
    /// a déjà été coupée n'est pas modifié.
    ///
    /// Deux conséquences pour ONYX :
    ///   - « vbs_off » appliqué puis HVCI de nouveau actif : ce n'est PAS cette mise à jour. Le
    ///     constat le dit, au lieu du « annulé par Windows » générique de la dérive ;
    ///   - « vbs_off » n'est jamais ré-appliqué dans le lot « Ré-appliquer » de la dérive : couper
    ///     une protection de sécurité reste un choix explicite, un par un.
    /// « Rétablir » supprime les deux valeurs (retour à « non configuré ») : Windows peut ensuite
    /// réactiver la protection de lui-même, ce qui est l'état d'origine voulu.
    ///
    /// Logique pure, lecture seule.
    /// </summary>
    internal static class IntegriteMemoire
    {
        public const string IdTweak = "vbs_off";

        private const string Choix = " Elle protège le noyau contre les pilotes malveillants et peut coûter des "
                                   + "performances en jeu : à toi de choisir, ONYX ne la coupe pas d'office.";

        /// <summary>Réglage traité par son propre constat : exclu du lot « Ré-appliquer » de la dérive.</summary>
        public static bool AvisSepare(string tweakId)
        {
            return string.Equals(tweakId, IdTweak, StringComparison.Ordinal);
        }

        /// <summary>Texte du constat, ou null si la protection n'est pas active.
        /// coupeeParOnyx : « vbs_off » figure dans la liste des réglages appliqués.</summary>
        public static string Constat(bool active, bool coupeeParOnyx)
        {
            if (!active) return null;
            if (coupeeParOnyx)
                return "Intégrité de la mémoire (HVCI) de nouveau active, alors qu'ONYX l'avait coupée. "
                     + "Ce n'est pas la mise à jour d'octobre 2026 : Microsoft ne touche pas un PC où elle "
                     + "a été coupée. Elle a été remise par Sécurité Windows, une stratégie ou un logiciel."
                     + Choix;
            return "Intégrité de la mémoire (HVCI) active. Depuis octobre 2026, les mises à jour de Windows "
                 + "l'activent d'office sur les PC compatibles où elle n'était pas réglée (Microsoft)."
                 + Choix;
        }
    }
}
