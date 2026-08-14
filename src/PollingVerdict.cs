using System;

namespace BTOptimizer
{
    /// <summary>
    /// CE QUE DIT VRAIMENT UNE MESURE DE TAUX DE RAPPORT.
    ///
    /// Le testeur d'ONYX calculait déjà deux chiffres — la fréquence MÉDIANE et le meilleur palier
    /// tenu — puis n'en affichait qu'un. C'est l'ÉCART entre les deux qui porte l'information la
    /// plus utile : une souris annoncée à 8000 Hz dont la médiane tombe à 3000 ne rapporte pas à
    /// 8000, elle décroche. Le chiffre était calculé et jeté.
    ///
    /// ET LE HAUT N'EST PAS TOUJOURS MIEUX. Chaque rapport est une interruption matérielle : 8000 Hz
    /// veut dire huit mille interruptions par seconde, traitées en priorité sur un cœur — souvent
    /// le cœur 0, celui-là même où tourne le fil principal du jeu. C'est exactement le mécanisme que
    /// mesure la latence DPC d'ONYX. Sur une machine juste, monter le taux de rapport peut COÛTER
    /// des images au lieu d'en gagner, et l'ancien verdict félicitait tout ce qui dépassait 950 Hz
    /// sans jamais le mentionner.
    ///
    /// Enfin, le gain se compare à ce que l'écran peut montrer : à 200 Hz d'affichage, il se passe
    /// quarante rapports de souris entre deux images. Le bénéfice de 8000 Hz sur 1000 Hz existe
    /// (il porte sur la fraîcheur de la position au moment du rendu), mais il est sans commune
    /// mesure avec le saut de 125 à 1000.
    /// </summary>
    internal static class PollingVerdict
    {
        /// <summary>Paliers standards annoncés par les fabricants.</summary>
        public static readonly int[] Paliers = { 125, 250, 500, 1000, 2000, 4000, 8000 };

        /// <summary>Palier PUR le plus proche d'une mesure.</summary>
        public static int Palier(int hz)
        {
            int meilleur = Paliers[0], ecart = int.MaxValue;
            foreach (int p in Paliers)
            {
                int d = Math.Abs(p - hz);
                if (d < ecart) { ecart = d; meilleur = p; }
            }
            return meilleur;
        }

        /// <summary>
        /// PUR : la souris DÉCROCHE-t-elle ? true quand la fréquence courante est nettement sous le
        /// palier atteint. Le seuil est à 70 % : en dessous, ce n'est plus de la variation de mesure,
        /// c'est un taux que la machine ne tient pas.
        /// </summary>
        public static bool Decroche(int median, int meilleur)
        {
            if (meilleur <= 0 || median <= 0) return false;
            if (meilleur < 900) return false;   // sous 1000 Hz, la variation est normale et sans enjeu
            return median < meilleur * 70 / 100;
        }

        /// <summary>
        /// Verdict PUR. <paramref name="hzEcran"/> = fréquence de l'écran (0 si inconnue) : elle sert
        /// à situer le gain, pas à le nier.
        /// </summary>
        public static string Verdict(int median, int meilleur, int hzEcran)
        {
            if (meilleur <= 0) return "En attente de mouvement continu…";
            int p = Palier(meilleur);

            if (Decroche(median, meilleur))
                return "⚠ Ta souris est réglée sur ~" + p + " Hz mais ne le TIENT PAS : la fréquence "
                     + "retombe à ~" + median + " Hz pendant le mouvement. C'est le signe que la "
                     + "machine n'absorbe pas le flux d'interruptions. Redescends d'un palier ("
                     + PalierEnDessous(p) + " Hz) : un taux stable vaut mieux qu'un taux annoncé. "
                     + "Vérifie aussi que la souris est sur un port USB direct, jamais sur un hub.";

            if (p >= 4000)
            {
                string ecran = hzEcran >= 30
                    ? " Sur ton écran " + hzEcran + " Hz, il se passe environ " + (p / Math.Max(1, hzEcran))
                      + " rapports entre deux images."
                    : "";
                return "✔ " + p + " Hz tenus, et c'est beaucoup." + ecran
                     + " Attention au revers : chaque rapport est une interruption matérielle, et à "
                     + "ce rythme elles s'empilent souvent sur le cœur 0 — celui du fil principal du "
                     + "jeu. Si tu vois des à-coups, mesure la latence DPC et compare avec 1000 Hz "
                     + "avant de conclure que plus haut est mieux.";
            }

            if (p >= 1000)
                return "✔ " + p + " Hz tenus : le palier qui compte est atteint. Moins d'une "
                     + "milliseconde entre deux rapports. Monter plus haut apporte beaucoup moins "
                     + "que le saut depuis 125 Hz, et se paie en interruptions.";

            if (p >= 500)
                return "Correct : ~" + p + " Hz. Beaucoup de souris montent à 1000 Hz — regarde le "
                     + "logiciel du constructeur (Logitech G HUB, Razer Synapse…) ou l'interrupteur "
                     + "sous la souris. C'est le dernier palier où le gain est franchement sensible.";

            if (p >= 250)
                return "⚠ ~" + p + " Hz seulement. Passe à 1000 Hz dans le logiciel de ta souris : "
                     + "c'est le gain d'input le plus net qui existe, et il est gratuit.";

            return "⚠ ~" + p + " Hz — très bas (réglage d'usine, ou branchement lent). Monte à "
                 + "500-1000 Hz dans le logiciel constructeur, et branche la souris sur un port USB "
                 + "direct de la carte mère plutôt que sur un hub ou un port d'écran.";
        }

        /// <summary>Palier PUR immédiatement inférieur (le même si on est déjà au plus bas).</summary>
        public static int PalierEnDessous(int p)
        {
            int precedent = Paliers[0];
            foreach (int t in Paliers)
            {
                if (t >= p) return precedent;
                precedent = t;
            }
            return precedent;
        }
    }
}
