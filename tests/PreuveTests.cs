using System;
using System.Collections.Generic;

namespace BTOptimizer.Tests
{
    /// <summary>Optimisation prouvée : verdict, recherche du fautif, attente après redémarrage.</summary>
    internal static class PreuveTests
    {
        private static Preuve.Mesure M(double charge, double pire, double sMoy, double sMax, bool etw = true)
        {
            return new Preuve.Mesure { ChargeUsParS = charge, PireUs = pire, SleepMoyMs = sMoy, SleepMaxMs = sMax, Etw = etw };
        }

        public static void Tout()
        {
            Banc.Titre("Preuve — verdict");
            var reference = M(1000, 400, 1.05, 2.0);

            // Écarts sous la marge de bruit : neutre, dans un sens comme dans l'autre.
            Banc.Verifie("bruit léger = neutre", true, Preuve.Juge(reference, M(1100, 450, 1.10, 2.5)) == Preuve.Verdict.Neutre);
            Banc.Verifie("baisse légère = neutre", true, Preuve.Juge(reference, M(900, 350, 1.00, 1.5)) == Preuve.Verdict.Neutre);

            // Charge : il faut +15 % ET +50 µs/s.
            Banc.Verifie("charge +20 % = régression", true, Preuve.Juge(reference, M(1200, 400, 1.05, 2.0)) == Preuve.Verdict.Regression);
            Banc.Verifie("charge +50 % sur 20 µs/s = bruit", true,
                Preuve.Juge(M(20, 400, 1.05, 2.0), M(30, 400, 1.05, 2.0)) == Preuve.Verdict.Neutre);
            Banc.Verifie("charge −20 % = gain", true, Preuve.Juge(reference, M(800, 400, 1.05, 2.0)) == Preuve.Verdict.Gain);

            // Pire DPC/ISR : +30 % ET +150 µs.
            Banc.Verifie("pire +200 µs (+50 %) = régression", true, Preuve.Juge(reference, M(1000, 600, 1.05, 2.0)) == Preuve.Verdict.Regression);
            Banc.Verifie("pire +200 µs sur 2000 µs (+10 %) = bruit", true,
                Preuve.Juge(M(1000, 2000, 1.05, 2.0), M(1000, 2200, 1.05, 2.0)) == Preuve.Verdict.Neutre);

            // Sleep(1) : seuils absolus.
            Banc.Verifie("Sleep moyen +0,2 ms = régression", true, Preuve.Juge(reference, M(1000, 400, 1.25, 2.0)) == Preuve.Verdict.Regression);
            Banc.Verifie("Sleep pire +1,5 ms = régression", true, Preuve.Juge(reference, M(1000, 400, 1.05, 3.5)) == Preuve.Verdict.Regression);

            // Aux bornes : il faut dépasser LES DEUX marges (relative et absolue).
            Banc.Verifie("charge +16 % mais +48 µs/s = bruit", true,
                Preuve.Juge(M(300, 400, 1.05, 2.0), M(348, 400, 1.05, 2.0)) == Preuve.Verdict.Neutre);
            Banc.Verifie("charge +60 µs/s mais +6 % = bruit", true,
                Preuve.Juge(reference, M(1060, 400, 1.05, 2.0)) == Preuve.Verdict.Neutre);
            Banc.Verifie("charge +16 % et +160 µs/s = régression", true,
                Preuve.Juge(reference, M(1160, 400, 1.05, 2.0)) == Preuve.Verdict.Regression);
            Banc.Verifie("Sleep moyen +0,14 ms = bruit", true, Preuve.Juge(reference, M(1000, 400, 1.19, 2.0)) == Preuve.Verdict.Neutre);
            Banc.Verifie("Sleep pire +0,9 ms = bruit", true, Preuve.Juge(reference, M(1000, 400, 1.05, 2.9)) == Preuve.Verdict.Neutre);

            // Gains sur chaque indicateur.
            Banc.Verifie("pire −200 µs (−50 %) = gain", true, Preuve.Juge(reference, M(1000, 200, 1.05, 2.0)) == Preuve.Verdict.Gain);
            Banc.Verifie("Sleep moyen −0,5 ms = gain", true, Preuve.Juge(M(1000, 400, 1.55, 2.0), M(1000, 400, 1.05, 2.0)) == Preuve.Verdict.Gain);
            Banc.Verifie("Sleep pire −5 ms = gain", true, Preuve.Juge(M(1000, 400, 1.05, 7.0), M(1000, 400, 1.05, 2.0)) == Preuve.Verdict.Gain);

            // Une valeur abîmée (NaN) ne compte ni comme gain ni comme régression.
            Banc.Verifie("NaN ignoré", true, Preuve.Juge(reference, M(double.NaN, 400, 1.05, 2.0)) == Preuve.Verdict.Neutre);

            // Un gain ne compense jamais une régression.
            Banc.Verifie("gain + régression = régression", true, Preuve.Juge(reference, M(500, 400, 1.05, 3.5)) == Preuve.Verdict.Regression);

            // Indicateurs inconnus (-1) ignorés ; ETW contre sans ETW non comparable.
            Banc.Verifie("indicateur inconnu ignoré", true,
                Preuve.Juge(M(-1, -1, 1.05, 2.0, false), M(-1, -1, 1.05, 2.0, false)) == Preuve.Verdict.Neutre);
            Banc.Verifie("ETW contre sans ETW = non comparable", true,
                Preuve.Juge(reference, M(1000, 400, 1.05, 2.0, false)) == Preuve.Verdict.Incomparable);
            Banc.Verifie("mesure absente = non comparable", true, Preuve.Juge(null, reference) == Preuve.Verdict.Incomparable);

            Banc.Titre("Preuve — recherche du fautif");
            HashSet<string> mauvais = new HashSet<string> { "c", "f" };
            int essais = 0;
            Func<List<string>, bool> essai = lot => { essais++; foreach (string x in lot) if (mauvais.Contains(x)) return false; return true; };
            // (essai lit « mauvais » à chaque appel : on peut le réassigner entre deux recherches)
            var fautifs = Preuve.TrouveFautifs(new List<string> { "a", "b", "c", "d", "e", "f", "g", "h" }, essai);
            fautifs.Sort();
            Banc.Egal("deux fautifs isolés", "c,f", string.Join(",", fautifs));
            // Deux fautifs sur 8 : chaque moitié puis chaque quart fautif est ré-essayé → 10 essais.
            Banc.Verifie("deux fautifs sur 8 : 10 essais", true, essais == 10);

            // Un seul fautif sur 8 : 2 essais par niveau, 3 niveaux → 6, moins qu'un par un (8).
            mauvais = new HashSet<string> { "e" };
            essais = 0;
            var unSeul = Preuve.TrouveFautifs(new List<string> { "a", "b", "c", "d", "e", "f", "g", "h" }, essai);
            Banc.Egal("un fautif sur 8 isolé", "e", string.Join(",", unSeul));
            Banc.Verifie("un fautif sur 8 : 6 essais, moins qu'un par un", true, essais == 6);
            mauvais = new HashSet<string> { "c", "f" };

            essais = 0;
            var seul = Preuve.TrouveFautifs(new List<string> { "x" }, essai);
            Banc.Egal("lot d'un seul : fautif sans essai", "x", string.Join(",", seul));
            Banc.Verifie("lot d'un seul : zéro essai", true, essais == 0);

            // Aucune moitié ne régresse seule : la régression du lot était du bruit, rien n'est retiré.
            var rien = Preuve.TrouveFautifs(new List<string> { "a", "b", "d", "e" }, essai);
            Banc.Verifie("bruit : rien retiré", true, rien.Count == 0);
            Banc.Verifie("lot vide : rien", true, Preuve.TrouveFautifs(new List<string>(), essai).Count == 0);

            Banc.Titre("Preuve — attente après redémarrage");
            var a = new Preuve.Attente
            {
                Applique = new DateTime(2026, 11, 17, 14, 30, 0, DateTimeKind.Local),
                Reference = M(1234.5, 678.25, 1.0625, 2.5),
                Ids = new List<string> { "hpet_off", "vbs_off" }
            };
            var relu = Preuve.Lit(Preuve.Ecrit(a));
            Banc.Verifie("relu non nul", true, relu != null);
            if (relu != null)
            {
                Banc.Egal("ids relus", "hpet_off,vbs_off", string.Join(",", relu.Ids));
                Banc.Verifie("date relue", true, relu.Applique == a.Applique);
                Banc.Verifie("charge relue exacte", true, relu.Reference.ChargeUsParS == 1234.5);
                Banc.Verifie("sleep relu exact", true, relu.Reference.SleepMoyMs == 1.0625);
                Banc.Verifie("etw relu", true, relu.Reference.Etw);
                Banc.Verifie("mesurable après redémarrage", true, Preuve.AttenteMesurable(relu, a.Applique.AddMinutes(2)));
                Banc.Verifie("pas mesurable sans redémarrage", false, Preuve.AttenteMesurable(relu, a.Applique.AddMinutes(-5)));
            }
            Banc.Verifie("texte vide = rien", true, Preuve.Lit("") == null);
            Banc.Verifie("texte abîmé = rien", true, Preuve.Lit("n'importe quoi\nsans=sens") == null);
            Banc.Verifie("sans réglage = rien", true, Preuve.Lit("applique=2026-11-17T14:30:00\n") == null);
            Banc.Verifie("fin de ligne CRLF tolérée", true,
                Preuve.Lit("applique=2026-11-17T14:30:00\r\nid=hpet_off\r\n") != null);

            Banc.Titre("Preuve — validation en jeu");
            // 6000 images à 10 ms (100 FPS) dont 60 à 40 ms : 1 % low = 1000 / 40 = 25 FPS.
            var fts = new List<double>();
            for (int i = 0; i < 5940; i++) fts.Add(10);
            for (int i = 0; i < 60; i++) fts.Add(40);
            Preuve.MesureJeu j = Preuve.CalculeJeu("jeu.exe", fts);
            Banc.Verifie("images comptées", true, j.Images == 6000);
            Banc.Verifie("durée = somme des frametimes", true, Math.Abs(j.DureeS - 61.8) < 1e-9);
            Banc.Verifie("FPS moyen = images / temps", true, Math.Abs(j.FpsMoy - 6000 / 61.8) < 1e-9);
            Banc.Verifie("1 % low = pire 1 %", true, Math.Abs(j.Low1 - 25) < 1e-9);
            Banc.Verifie("frametimes nuls ou NaN ignorés", true,
                Preuve.CalculeJeu("x", new List<double> { 10, 0, double.NaN, -3, 10 }).Images == 2);
            Banc.Verifie("aucune image : invalide", false, Preuve.JeuValide(Preuve.CalculeJeu("x", new List<double>())));

            Func<double, double, Preuve.MesureJeu> J = (moy, low) =>
                new Preuve.MesureJeu { Jeu = "jeu.exe", FpsMoy = moy, Low1 = low, Images = 6000, DureeS = 60 };
            var av = J(144, 90);
            Banc.Verifie("jeu : « avant » sans 1 % low = incomparable", true, Preuve.JugeJeu(J(144, -1), J(144, 90)) == Preuve.Verdict.Incomparable);
            Banc.Verifie("jeu : bruit = neutre", true, Preuve.JugeJeu(av, J(140, 85)) == Preuve.Verdict.Neutre);
            Banc.Verifie("jeu : 1 % low −12 % = régression", true, Preuve.JugeJeu(av, J(144, 79)) == Preuve.Verdict.Regression);
            Banc.Verifie("jeu : FPS moyen −8 % = régression", true, Preuve.JugeJeu(av, J(132, 90)) == Preuve.Verdict.Regression);
            Banc.Verifie("jeu : 1 % low +15 % = gain", true, Preuve.JugeJeu(av, J(144, 104)) == Preuve.Verdict.Gain);
            Banc.Verifie("jeu : gain moyen + chute du low = régression", true, Preuve.JugeJeu(av, J(170, 70)) == Preuve.Verdict.Regression);
            var autre = J(144, 90); autre.Jeu = "autre.exe";
            Banc.Verifie("jeu différent = non comparable", true, Preuve.JugeJeu(av, autre) == Preuve.Verdict.Incomparable);
            var court = J(144, 90); court.DureeS = 20;
            Banc.Verifie("mesure trop courte = non comparable", true, Preuve.JugeJeu(av, court) == Preuve.Verdict.Incomparable);
            var peu = J(144, 90); peu.Images = 500;
            Banc.Verifie("trop peu d'images = non comparable", true, Preuve.JugeJeu(av, peu) == Preuve.Verdict.Incomparable);

            j.Quand = new DateTime(2026, 11, 17, 18, 0, 0, DateTimeKind.Local);
            Preuve.MesureJeu jr = Preuve.LitJeu(Preuve.EcritJeu(j));
            Banc.Verifie("mesure en jeu relue", true, jr != null && jr.Jeu == "jeu.exe" && jr.Images == 6000
                && jr.Low1 == j.Low1 && jr.FpsMoy == j.FpsMoy && jr.Quand == j.Quand);
            Banc.Verifie("mesure en jeu abîmée = rien", true, Preuve.LitJeu("jeu=x\n") == null);

            var preuve = new Preuve.Attente { Applique = j.Quand.AddMinutes(10), Ids = new List<string> { "a" } };
            Banc.Verifie("preuve après la mesure : mesure APRÈS", true, Preuve.MesureApres(j, preuve));
            preuve.Applique = j.Quand.AddMinutes(-10);
            Banc.Verifie("preuve avant la mesure : refaire AVANT", false, Preuve.MesureApres(j, preuve));
            Banc.Verifie("pas de mesure avant : AVANT", false, Preuve.MesureApres(null, preuve));
            Banc.Verifie("pas de preuve : AVANT", false, Preuve.MesureApres(j, null));
        }
    }
}
