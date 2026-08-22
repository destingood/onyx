using System;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Vérifie <see cref="RepartitionCoeurs"/>.
    ///
    /// Le jeu d'essai principal n'est pas inventé : ce sont les chiffres relevés sur la machine
    /// de référence et cités dans « ONYX annonçait répartir des interruptions qui ne peuvent pas
    /// l'être ». C'est justement pour lire ce tableau qu'il fallait encore ouvrir LatencyMon.
    /// </summary>
    internal static class RepartitionCoeursTests
    {
        /// <summary>Le relevé réel : 92 % des DPC sur le cœur 0, sur seize cœurs.</summary>
        private static long[] ReleveReel()
        {
            var e = new long[16];
            e[0] = 360758;
            e[1] = 18952;
            for (int i = 2; i < 16; i++) e[i] = 9836 / 14;   // le reste, réparti
            return e;
        }

        private static long[] BienReparti()
        {
            var e = new long[16];
            for (int i = 0; i < 16; i++) e[i] = 5000;
            return e;
        }

        public static void Tout()
        {
            Comptes();
            Domination();
            RefusDeConclure();
            RapportSurReleveReel();
            RapportSurMachineSaine();
            Tri();
        }

        private static void Comptes()
        {
            Banc.Titre("Comptes de base");

            Banc.Verifie("total d'un tableau nul vaut 0, sans exception",
                true, RepartitionCoeurs.Total(null) == 0);
            Banc.Verifie("total d'un tableau vide vaut 0",
                true, RepartitionCoeurs.Total(new long[0]) == 0);
            Banc.Verifie("total additionne bien", true,
                RepartitionCoeurs.Total(new long[] { 1, 2, 3, 4 }) == 10);

            // Compter les CASES donnerait le nombre de coeurs de la machine ; on veut le nombre
            // de coeurs qui travaillent, et c'est le second qui porte l'information.
            Banc.Verifie("seuls les cœurs ayant vu passer quelque chose sont actifs", true,
                RepartitionCoeurs.CoeursActifs(new long[] { 10, 0, 5, 0, 0 }) == 2);
            Banc.Verifie("aucun cœur actif sur un tableau nul",
                true, RepartitionCoeurs.CoeursActifs(null) == 0);

            Banc.Verifie("part d'un cœur hors bornes vaut 0, sans exception", true,
                RepartitionCoeurs.Part(new long[] { 1, 1 }, 9) == 0);
            Banc.Verifie("part d'un index négatif vaut 0", true,
                RepartitionCoeurs.Part(new long[] { 1, 1 }, -1) == 0);
            Banc.Verifie("part correcte", true,
                Math.Abs(RepartitionCoeurs.Part(new long[] { 3, 1 }, 0) - 0.75) < 1e-9);

            Banc.Verifie("part idéale = 1 / cœurs actifs", true,
                Math.Abs(RepartitionCoeurs.PartIdeale(BienReparti()) - 1.0 / 16) < 1e-9);

            Banc.Verifie("% d'un cœur consommé : 500 ms sur 10 s = 5 %", true,
                Math.Abs(RepartitionCoeurs.PartDuCoeurConsommee(500, 10) - 0.05) < 1e-9);
            Banc.Verifie("durée nulle ne divise pas par zéro", true,
                RepartitionCoeurs.PartDuCoeurConsommee(500, 0) == 0);
        }

        private static void Domination()
        {
            Banc.Titre("Cœur dominant");

            Banc.Verifie("le cœur 0 domine le relevé réel",
                true, RepartitionCoeurs.CoeurDominant(ReleveReel()) == 0);
            Banc.Verifie("... avec plus de 90 % du travail", true,
                RepartitionCoeurs.PartDuDominant(ReleveReel()) > 0.90);
            Banc.Verifie("aucun dominant sur un tableau nul",
                true, RepartitionCoeurs.CoeurDominant(null) == -1);
            Banc.Verifie("aucun dominant quand tout est à zéro",
                true, RepartitionCoeurs.CoeurDominant(new long[8]) == -1);
            Banc.Verifie("le dominant n'est pas forcément le premier", true,
                RepartitionCoeurs.CoeurDominant(new long[] { 1, 2, 99, 3 }) == 2);
        }

        /// <summary>
        /// Le garde-fou le plus important du module. Sur trois cents événements, une répartition
        /// déséquilibrée est du hasard, pas un réglage — et un tableau de pourcentages calculé
        /// là-dessus a exactement l'air aussi sérieux qu'un autre.
        /// </summary>
        private static void RefusDeConclure()
        {
            Banc.Titre("Refus de conclure sur trop peu d'événements");

            var maigre = new long[16];
            maigre[0] = 300;                       // 100 % sur un cœur, mais 300 événements
            Banc.Verifie("100 % sur un cœur mais trop peu d'événements : pas de concentration",
                false, RepartitionCoeurs.EstConcentre(maigre));
            Banc.Verifie("... et aucun rapport", true,
                RepartitionCoeurs.Rapport(new RepartitionCoeurs.Etat {
                    Evenements = maigre, Ms = new double[16], Secondes = 60 }).Length == 0);

            var juste = new long[16];
            juste[0] = RepartitionCoeurs.MiniPourConclure;
            Banc.Verifie("au seuil exact, on conclut", true, RepartitionCoeurs.EstConcentre(juste));

            Banc.Verifie("état nul : aucun rapport, aucune exception",
                true, RepartitionCoeurs.Rapport(null).Length == 0);
            Banc.Verifie("état sans tableau : aucun rapport", true,
                RepartitionCoeurs.Rapport(new RepartitionCoeurs.Etat()).Length == 0);
        }

        private static void RapportSurReleveReel()
        {
            Banc.Titre("Rapport sur le relevé réel de la machine de référence");

            long[] e = ReleveReel();
            var ms = new double[16];
            ms[0] = 5427; ms[1] = 334;             // secondes du relevé LatencyMon, en ms
            string r = RepartitionCoeurs.Rapport(new RepartitionCoeurs.Etat {
                Evenements = e, Ms = ms, Secondes = 60 });

            Banc.Verifie("la concentration est détectée", true, RepartitionCoeurs.EstConcentre(e));
            Banc.Verifie("le rapport nomme le cœur 0", true, r.Contains("CŒUR 0"));
            Banc.Verifie("il donne le % d'un cœur consommé (5 427 ms sur 60 s ≈ 9 %)",
                true, r.Contains("9,0 % du cœur") || r.Contains("9.0 % du cœur"));

            // LE point d'honnetete du module : une concentration n'est PAS forcement un defaut,
            // et surtout pas forcement reparable. Un peripherique a vecteur unique ne peut pas
            // etre etale — c'est precisement ce qu'etablissait le commit d'ou viennent ces
            // chiffres. Le taire enverrait quelqu'un chercher un reglage qui n'existe pas.
            Banc.Verifie("il refuse de présenter ça comme réparable",
                true, r.Contains("CE QUE ÇA NE PROUVE PAS"));
            Banc.Verifie("il renvoie vers le nombre de vecteurs", true, r.Contains("vecteur"));

            Banc.Verifie("aucune ligne ne dépasse 80 colonnes", true, ToutTientEn80(r));
        }

        private static void RapportSurMachineSaine()
        {
            Banc.Titre("Rapport sur une machine bien répartie");

            string r = RepartitionCoeurs.Rapport(new RepartitionCoeurs.Etat {
                Evenements = BienReparti(), Ms = new double[16], Secondes = 60 });

            Banc.Verifie("aucune concentration détectée",
                false, RepartitionCoeurs.EstConcentre(BienReparti()));
            Banc.Verifie("le rapport le dit clairement", true, r.Contains("Réparti"));
            Banc.Verifie("et n'agite pas d'alarme", false, r.Contains("CONCENTRÉ"));
            Banc.Verifie("aucune ligne ne dépasse 80 colonnes", true, ToutTientEn80(r));
        }

        private static void Tri()
        {
            Banc.Titre("Ordre décroissant");

            int[] o = RepartitionCoeurs.OrdreDecroissant(new long[] { 5, 50, 500, 1 });
            Banc.Verifie("le plus chargé en tête", true, o[0] == 2);
            Banc.Verifie("puis le suivant", true, o[1] == 1);
            Banc.Verifie("le moins chargé en queue", true, o[3] == 3);
            Banc.Verifie("tableau nul : tableau vide, aucune exception",
                true, RepartitionCoeurs.OrdreDecroissant(null).Length == 0);
        }

        private static bool ToutTientEn80(string rapport)
        {
            foreach (string ligne in rapport.Split('\n'))
                if (ligne.TrimEnd('\r').Length > 80) return false;
            return true;
        }
    }
}
