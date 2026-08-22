using System;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Vérifie <see cref="AutoJeu"/> — la bascule automatique du Mode Jeu.
    ///
    /// C'est exactement le genre de logique qu'on ne peut pas éprouver à la main : reproduire un
    /// alt-tab de six secondes en plein match, vingt fois de suite, pour voir si les services
    /// repartent, n'est pas une vérification, c'est une superstition. Ici la séquence se joue en
    /// quelques appels.
    ///
    /// Le test central est <see cref="PasDeClignotement"/> : il rejoue le défaut réel de l'ancienne
    /// version, qui relâchait au PREMIER relevé sans jeu.
    /// </summary>
    internal static class AutoJeuTests
    {
        /// <summary>Rejoue n relevés d'affilée et rend le dernier geste décidé.</summary>
        private static AutoJeu.Geste Releves(AutoJeu.Etat e, int n, bool jeu, bool pleinEcran)
        {
            AutoJeu.Geste g = AutoJeu.Geste.Rien;
            for (int i = 0; i < n; i++) g = AutoJeu.Decide(e, jeu, pleinEcran);
            return g;
        }

        /// <summary>Un relevé qui, s'il décide, applique la décision — comme le fait le vrai Tick.</summary>
        private static AutoJeu.Geste Joue(AutoJeu.Etat e, bool jeu, bool pleinEcran)
        {
            AutoJeu.Geste g = AutoJeu.Decide(e, jeu, pleinEcran);
            if (g == AutoJeu.Geste.Engager) { e.BoostActif = true; e.ParNous = true; }
            else if (g == AutoJeu.Geste.Relacher) { e.BoostActif = false; e.ParNous = false; }
            return g;
        }

        public static void Tout()
        {
            Engagement();
            PasDeClignotement();
            Relachement();
            ActivationManuelle();
            Robustesse();
        }

        private static void Engagement()
        {
            Banc.Titre("Enclenchement");

            // Un nom d'exécutable de jeu est DISTINCTIF : aucun faux positif possible, donc aucune
            // raison d'attendre. Les premières secondes d'un chargement sont justement celles où
            // le disque travaille le plus.
            var e = new AutoJeu.Etat();
            Banc.Verifie("un jeu connu enclenche dès le premier relevé", true,
                AutoJeu.Decide(e, true, false) == AutoJeu.Geste.Engager);

            // Le plein écran, lui, peut être une vidéo : il doit tenir.
            var f = new AutoJeu.Etat();
            Banc.Verifie("un seul relevé en plein écran n'enclenche pas", true,
                AutoJeu.Decide(f, false, true) == AutoJeu.Geste.Rien);
            Banc.Verifie("deux relevés stables en plein écran enclenchent", true,
                AutoJeu.Decide(f, false, true) == AutoJeu.Geste.Engager);

            // Une vidéo qu'on quitte au bout d'un relevé ne doit rien déclencher.
            var v = new AutoJeu.Etat();
            AutoJeu.Decide(v, false, true);
            AutoJeu.Decide(v, false, false);
            Banc.Verifie("un plein écran fugace ne laisse pas de compteur derrière lui", true,
                AutoJeu.Decide(v, false, true) == AutoJeu.Geste.Rien);

            var d = new AutoJeu.Etat();
            Banc.Verifie("sans jeu ni plein écran, on ne fait rien", true,
                Releves(d, 5, false, false) == AutoJeu.Geste.Rien);
        }

        /// <summary>
        /// LE TEST QUI JUSTIFIE LE MODULE.
        ///
        /// L'ancienne version relâchait au premier relevé sans jeu. Un alt-tab vers le bureau
        /// pendant un chargement relançait donc l'indexation du disque AU MILIEU de la partie,
        /// avant de la ré-arrêter deux secondes plus tard.
        /// </summary>
        private static void PasDeClignotement()
        {
            Banc.Titre("Un alt-tab ne relance pas les services en pleine partie");

            var e = new AutoJeu.Etat();
            Joue(e, true, true);
            Banc.Verifie("le mode jeu est enclenché", true, e.BoostActif);

            // Six secondes au bureau : trois relevés. L'ancienne règle aurait déjà tout relancé.
            for (int i = 0; i < 3; i++)
                Banc.Verifie("relevé " + (i + 1) + " sans jeu : on TIENT", true,
                    Joue(e, false, false) == AutoJeu.Geste.Rien);
            Banc.Verifie("les services sont toujours suspendus", true, e.BoostActif);

            // Le jeu revient au premier plan : le compteur d'absence doit repartir de zéro, sinon
            // trois alt-tabs espacés finiraient par relâcher.
            Joue(e, true, true);
            Banc.Verifie("le retour du jeu remet le compteur d'absence à zéro", true, e.TicksSansJeu == 0);
            Banc.Verifie("et trois nouveaux relevés d'absence ne relâchent toujours pas", true,
                Releves(e, 3, false, false) == AutoJeu.Geste.Rien);
        }

        private static void Relachement()
        {
            Banc.Titre("Relâchement quand le jeu est vraiment fermé");

            var e = new AutoJeu.Etat();
            Joue(e, true, false);

            Banc.Verifie("avant le seuil, rien", true,
                Releves(e, AutoJeu.TicksPourRelacher - 1, false, false) == AutoJeu.Geste.Rien);
            Banc.Verifie("au seuil, on relâche", true,
                Joue(e, false, false) == AutoJeu.Geste.Relacher);
            Banc.Verifie("et les services sont relancés", false, e.BoostActif);
            Banc.Verifie("on ne relâche pas deux fois", true,
                Joue(e, false, false) == AutoJeu.Geste.Rien);

            // Le seuil de relâchement est PLUS HAUT que celui d'enclenchement : se tromper en
            // tenant coûte quelques secondes ; se tromper en relâchant coûte une saccade en jeu.
            Banc.Verifie("le seuil de relâchement est plus haut que celui d'enclenchement", true,
                AutoJeu.TicksPourRelacher > AutoJeu.TicksPourEngager);
        }

        private static void ActivationManuelle()
        {
            Banc.Titre("Une activation manuelle n'appartient pas à l'automatique");

            // L'utilisateur a activé le Mode Jeu lui-même : ParNous reste faux.
            var e = new AutoJeu.Etat { BoostActif = true, ParNous = false };
            Banc.Verifie("l'automatique ne coupe jamais un mode jeu manuel", true,
                Releves(e, AutoJeu.TicksPourRelacher * 3, false, false) == AutoJeu.Geste.Rien);
            Banc.Verifie("et il le laisse actif", true, e.BoostActif);

            // Et il ne cherche pas non plus à le ré-enclencher : il tourne déjà.
            Banc.Verifie("il ne ré-enclenche pas ce qui tourne déjà", true,
                AutoJeu.Decide(e, true, true) == AutoJeu.Geste.Rien);
        }

        private static void Robustesse()
        {
            Banc.Titre("Rien ne casse sur une entrée absente");

            Banc.Verifie("un état nul ne lève pas d'exception", true,
                AutoJeu.Decide(null, true, true) == AutoJeu.Geste.Rien);
        }
    }
}
