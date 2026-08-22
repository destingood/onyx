using System;
using System.Collections.Generic;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Vérifie <see cref="ChaineEntree"/> — la chaîne du clic au pixel.
    ///
    /// Ce banc porte sur deux choses, et la seconde compte plus que la première.
    ///
    ///   Les FORMULES : une demi-période d'attente, une période par image en file. Elles tiennent
    ///   en une ligne, donc elles se vérifient à la main — et c'est justement pour ça qu'il faut
    ///   les figer ici : une formule évidente est celle qu'on modifie sans y penser.
    ///
    ///   Les ORDRES DE GRANDEUR, qui sont l'argument du module. « Monter la souris de 500 à
    ///   1000 Hz rapporte dix fois moins que retirer une image en attente » n'est pas une opinion :
    ///   c'est un calcul, et il est écrit noir sur blanc ci-dessous. Le jour où quelqu'un change un
    ///   coefficient, c'est ce test qui tombe.
    /// </summary>
    internal static class ChaineEntreeTests
    {
        /// <summary>La machine de référence : écran 240 Hz, souris à 1000 Hz, deux images en file.</summary>
        private static ChaineEntree.Releve Reference()
        {
            return new ChaineEntree.Releve
            {
                SourisHz = 1000, EcranHz = 240, EcranHzMax = 240, ImagesEnAttente = 2
            };
        }

        public static void Tout()
        {
            Formules();
            ChaineComplete();
            RienDInvente();
            OrdresDeGrandeur();
            LeviersClasses();
            MesureSouris();
        }

        private static void Formules()
        {
            Banc.Titre("Les trois formules");

            // Un rapport toutes les millisecondes : le geste tombe au hasard dans l'intervalle,
            // donc on attend en moyenne une demi-milliseconde.
            Banc.Verifie("1000 Hz coûtent 0,5 ms d'attente moyenne", true,
                Math.Abs(ChaineEntree.MsAttente(1000) - 0.5) < 0.0001);
            Banc.Verifie("… et 1 ms au pire", true,
                Math.Abs(ChaineEntree.MsPeriode(1000) - 1.0) < 0.0001);
            Banc.Verifie("125 Hz coûtent 4 ms d'attente moyenne", true,
                Math.Abs(ChaineEntree.MsAttente(125) - 4.0) < 0.0001);

            Banc.Verifie("une fréquence nulle ne divise pas par zéro", true,
                ChaineEntree.MsAttente(0) == 0);
            Banc.Verifie("une fréquence négative non plus", true,
                ChaineEntree.MsPeriode(-5) == 0);

            // Deux images en attente à 240 im/s = deux périodes de 4,1667 ms.
            Banc.Verifie("2 images à 240 im/s coûtent 8,33 ms", true,
                Math.Abs(ChaineEntree.MsFileRendu(2, 240) - 8.3333) < 0.001);
            Banc.Verifie("zéro image ne coûte rien", true, ChaineEntree.MsFileRendu(0, 240) == 0);

            // Le plafond commande la cadence quand il existe ; sinon l'écran, en meilleur cas.
            var r = Reference();
            Banc.Verifie("sans plafond, on chiffre sur la fréquence d'écran", true,
                ChaineEntree.FpsRetenu(r) == 240);
            r.PlafondFps = 141;
            Banc.Verifie("avec un plafond, c'est lui qui commande", true,
                ChaineEntree.FpsRetenu(r) == 141);
        }

        private static void ChaineComplete()
        {
            Banc.Titre("La chaîne");

            List<ChaineEntree.Maillon> c = ChaineEntree.Construit(Reference());
            Banc.Verifie("les cinq maillons sont là, même ceux qu'on ne sait pas mesurer", true, c.Count == 5);

            // 0,5 (souris) + 8,333 (deux images) + 2,083 (écran) = 10,92 ms
            double t = ChaineEntree.TotalMoyen(c);
            Banc.Verifie("le budget moyen vaut 10,9 ms", true, Math.Abs(t - 10.9166) < 0.01);

            // La dalle n'est pas mesurable : elle doit être comptée comme manquante, pas oubliée.
            Banc.Verifie("la dalle est déclarée non mesurée", true, ChaineEntree.NonMesures(c) >= 1);

            // Un budget bâti sur UN maillon sur trois ne doit pas s'annoncer comme un budget :
            // constaté sur la machine de référence, où seul l'écran était lisible — « 1,00 ms »
            // s'affichait en gros et en doré, comme un excellent résultat.
            Banc.Verifie("trois maillons chiffrés font un budget crédible", true,
                ChaineEntree.BudgetCredible(c));
            var seul = new ChaineEntree.Releve { EcranHz = 500, EcranHzMax = 500 };
            List<ChaineEntree.Maillon> cs = ChaineEntree.Construit(seul);
            Banc.Verifie("un seul maillon chiffré n'en fait pas un", false,
                ChaineEntree.BudgetCredible(cs));
            Banc.Verifie("et le constat refuse d'annoncer un total", true,
                ChaineEntree.Constat(seul).Contains("pas encore de budget"));
            Banc.Verifie("le compte des maillons chiffrés est juste", true,
                ChaineEntree.Chiffres(c) == 3 && ChaineEntree.Chiffres(cs) == 1);

            Banc.Verifie("un relevé nul ne produit aucun maillon, sans exception", true,
                ChaineEntree.Construit(null).Count == 0);
            Banc.Verifie("et son budget vaut zéro", true, ChaineEntree.Budget(null) == 0);
        }

        /// <summary>Le module ne doit jamais compléter ce qu'il ignore.</summary>
        private static void RienDInvente()
        {
            Banc.Titre("Ce qu'on ignore reste vide");

            // Souris jamais mesurée : le maillon existe, mais sans chiffre.
            var r = new ChaineEntree.Releve { EcranHz = 240, EcranHzMax = 240 };
            List<ChaineEntree.Maillon> c = ChaineEntree.Construit(r);
            ChaineEntree.Maillon souris = Trouve(c, ChaineEntree.NomSouris);
            Banc.Verifie("une souris non mesurée n'a pas de millisecondes", true, !souris.Ms.HasValue);
            Banc.Verifie("et elle le dit", true,
                souris.Source == ChaineEntree.Source.NonMesure);

            // Le pire temps noyau ne rejoint JAMAIS le budget moyen : c'est un maximum observé.
            var avec = new ChaineEntree.Releve { EcranHz = 240, EcranHzMax = 240, DpcPireMs = 3.2 };
            Banc.Verifie("le pire temps noyau ne gonfle pas le budget moyen", true,
                Math.Abs(ChaineEntree.Budget(avec) - ChaineEntree.Budget(r)) < 0.0001);
            ChaineEntree.Maillon noyau = Trouve(ChaineEntree.Construit(avec), ChaineEntree.NomNoyau);
            Banc.Verifie("mais il apparaît en pire cas", true,
                noyau.MsPire.HasValue && Math.Abs(noyau.MsPire.Value - 3.2) < 0.0001);
            Banc.Verifie("et jamais en moyenne", true, !noyau.Ms.HasValue);

            // File de rendu inconnue : pas de levier chiffré, mais le coût par image est dit.
            Banc.Verifie("une file inconnue ne devient pas un levier chiffré", true,
                ChaineEntree.Leviers(r).Count == 0);
            string piste = ChaineEntree.PisteFileRendu(r);
            Banc.Verifie("elle devient une piste, chiffrée par image", true,
                piste != null && piste.Contains("4,17 ms"));
            Banc.Verifie("une file déjà connue n'a pas de piste séparée", true,
                ChaineEntree.PisteFileRendu(Reference()) == null);
        }

        /// <summary>
        /// L'ARGUMENT DU MODULE, mis en chiffres.
        ///
        /// Sur la machine de référence, le levier dont parlent tous les forums (le taux de rapport)
        /// rapporte dix fois moins que celui dont personne ne parle (la file de rendu).
        /// </summary>
        private static void OrdresDeGrandeur()
        {
            Banc.Titre("Le levier dont on parle rapporte dix fois moins que l'autre");

            var r = Reference();

            // Retirer une image en attente : 2 → 1 à 240 im/s.
            ChaineEntree.Releve moinsUneImage = r.Copie(); moinsUneImage.ImagesEnAttente = 1;
            double gainImage = ChaineEntree.Gain(r, moinsUneImage);
            Banc.Verifie("retirer une image en attente rend 4,17 ms", true,
                Math.Abs(gainImage - 4.1666) < 0.01);

            // Monter la souris de 1000 à 8000 Hz.
            ChaineEntree.Releve souris8k = r.Copie(); souris8k.SourisHz = 8000;
            double gainSouris = ChaineEntree.Gain(r, souris8k);
            Banc.Verifie("passer la souris de 1000 à 8000 Hz rend 0,44 ms", true,
                Math.Abs(gainSouris - 0.4375) < 0.001);

            Banc.Verifie("l'image en attente rapporte au moins neuf fois plus que la souris", true,
                gainImage > gainSouris * 9);

            // Un levier qui n'améliore rien ne rend pas un gain négatif.
            Banc.Verifie("un changement qui dégrade ne rend jamais un gain négatif", true,
                ChaineEntree.Gain(r, souris8k) >= 0 && ChaineEntree.Gain(souris8k, r) == 0);
        }

        private static void LeviersClasses()
        {
            Banc.Titre("Les leviers, classés par ce qu'ils rendent");

            // Écran bridé à 60 Hz alors qu'il monte à 240, souris à 500 Hz, deux images en file.
            var r = new ChaineEntree.Releve
            {
                SourisHz = 500, EcranHz = 60, EcranHzMax = 240, ImagesEnAttente = 2
            };
            List<ChaineEntree.Levier> l = ChaineEntree.Leviers(r);
            Banc.Verifie("les trois leviers sont proposés", true, l.Count == 3);

            // Passer de 60 à 240 Hz raccourcit l'attente d'affichage ET la période d'image : c'est
            // pour ça que le gain se calcule en rejouant le budget, pas avec une formule par levier.
            Banc.Egal("le plus payant est la fréquence d'affichage", "Fréquence d'affichage", l[0].Nom);
            Banc.Verifie("les gains sont bien décroissants", true,
                l[0].GainMs >= l[1].GainMs && l[1].GainMs >= l[2].GainMs);
            Banc.Egal("et le moins payant est le taux de rapport", "Taux de rapport", l[2].Nom);

            Banc.Verifie("le gain de l'écran dépasse 20 ms sur ce cas", true, l[0].GainMs > 20);
            Banc.Verifie("chaque levier connaît sa part du budget", true,
                l[0].Part > 0 && l[0].Part <= 1);

            // Une machine déjà bien réglée ne doit se voir proposer RIEN.
            var propre = new ChaineEntree.Releve
            {
                SourisHz = 1000, EcranHz = 240, EcranHzMax = 240, ImagesEnAttente = 1
            };
            Banc.Verifie("une machine déjà réglée ne reçoit aucun levier", true,
                ChaineEntree.Leviers(propre).Count == 0);
            Banc.Verifie("et le constat le dit au lieu d'inventer un conseil", true,
                ChaineEntree.Constat(propre).Contains("Aucun levier"));

            // Un écran à 60 Hz qui NE MONTE PAS plus haut n'est pas un levier : on ne propose pas
            // d'acheter un écran, on propose des réglages.
            var bride = new ChaineEntree.Releve { SourisHz = 1000, EcranHz = 60, EcranHzMax = 60, ImagesEnAttente = 1 };
            Banc.Verifie("un écran déjà à son maximum n'est pas un levier", true,
                ChaineEntree.Leviers(bride).Count == 0);
        }

        private static void MesureSouris()
        {
            Banc.Titre("La mesure de souris, retenue au lieu d'être jetée");

            DateTime quand;
            Banc.Verifie("une ligne bien formée rend la fréquence", true,
                ChaineEntree.RelitSouris("1000\t2026-08-22 14:05:09", out quand) == 1000);
            Banc.Verifie("et sa date", true, quand.Year == 2026 && quand.Month == 8);

            Banc.Verifie("une ligne vide ne rend rien", true, ChaineEntree.RelitSouris("", out quand) == 0);
            Banc.Verifie("une ligne abîmée non plus", true, ChaineEntree.RelitSouris("n'importe quoi", out quand) == 0);
            Banc.Verifie("une fréquence nulle est refusée", true, ChaineEntree.RelitSouris("0\t2026-01-01", out quand) == 0);
            Banc.Verifie("une ligne sans date reste exploitable", true,
                ChaineEntree.RelitSouris("500", out quand) == 500);
        }

        private static ChaineEntree.Maillon Trouve(List<ChaineEntree.Maillon> c, string nom)
        {
            foreach (ChaineEntree.Maillon m in c) if (m.Nom == nom) return m;
            return new ChaineEntree.Maillon();
        }
    }
}
