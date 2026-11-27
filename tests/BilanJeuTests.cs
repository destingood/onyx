using System;
using System.Collections.Generic;

namespace BTOptimizer.Tests
{
    /// <summary>Vérifie <see cref="BilanJeu"/> : cumuls par période, classement, FPS pondérés, écart, série.</summary>
    internal static class BilanJeuTests
    {
        private static JournalJeu.Partie P(string jeu, int j, int h, int s, double fps)
        {
            return new JournalJeu.Partie { Jeu = jeu, Debut = new DateTime(2026, 11, j, h, 0, 0), Secondes = s, FpsMoyen = fps };
        }

        public static void Tout()
        {
            Banc.Titre("Bilan de jeu");
            DateTime maintenant = new DateTime(2026, 11, 27, 20, 0, 0);   // vendredi
            var parties = new List<JournalJeu.Partie>
            {
                P("Fortnite", 27, 18, 3600, 240),
                P("fortnite", 26, 20, 1800, 0),        // non mesurée ; casse différente = même jeu
                P("Counter-Strike 2", 25, 21, 5400, 300),
                P("Fortnite", 21, 15, 1200, 200),      // samedi 21 : premier jour des 7 derniers
                P("Counter-Strike 2", 18, 20, 3000, 280),  // semaine d'avant
                new JournalJeu.Partie { Jeu = "Elden Ring", Debut = new DateTime(2026, 10, 1, 20, 0, 0), Secondes = 1000 },
                P("Fortnite", 30, 10, 999, 0),         // dans le futur : ignorée
                null
            };

            BilanJeu.Resultat r = BilanJeu.Calcule(parties, maintenant, 7);
            Banc.Verifie("7 jours : 3 h 20 sur 4 parties, la semaine d'avant comptée à part", true,
                r.Secondes == 12000 && r.Parties == 4 && r.SecondesAvant == 3000);
            Banc.Verifie("jeux classés par temps, noms rapprochés sans tenir compte de la casse", true,
                r.Jeux.Count == 2 && r.Jeux[0].Jeu == "Fortnite" && r.Jeux[0].Secondes == 6600 && r.Jeux[0].Parties == 3);
            Banc.Verifie("FPS moyens pondérés par la durée, parties non mesurées exclues", true, Math.Abs(r.Jeux[0].FpsMoyen - 230) < 0.01);
            Banc.Verifie("plus longue partie, jour préféré, série de 3 jours", true,
                r.PlusLongue.Jeu == "Counter-Strike 2" && r.JourPrefere == DayOfWeek.Wednesday && r.Serie == 3);
            List<string> l = BilanJeu.Resume(r);
            Banc.Egal("résumé : ligne 1", "3 h 20 de jeu, 4 parties, 2 jeux (+300 % par rapport aux 7 jours d'avant).", l[0]);
            Banc.Egal("résumé : plus longue partie", "Plus longue partie : 1 h 30 sur Counter-Strike 2, le 25/11.", l[1]);
            Banc.Egal("détail d'un jeu", "1 h 50 · 3 parties · 230 FPS en moyenne", BilanJeu.Detail(r.Jeux[0]));

            r = BilanJeu.Calcule(parties, maintenant, 30);
            Banc.Verifie("30 jours : 4 h 10, période d'avant = Elden Ring", true, r.Secondes == 15000 && r.SecondesAvant == 1000);
            r = BilanJeu.Calcule(parties, maintenant, 0);
            Banc.Verifie("tout le journal : 6 parties, pas d'écart", true, r.Parties == 6 && r.SecondesAvant == -1 && BilanJeu.Ecart(r) == null);

            Banc.Verifie("série gardée jusqu'à minuit le lendemain, perdue après", true,
                BilanJeu.Calcule(parties, maintenant.AddDays(1), 7).Serie == 3 && BilanJeu.Calcule(parties, maintenant.AddDays(2), 7).Serie == 0);
            r = BilanJeu.Calcule(parties, new DateTime(2026, 11, 17, 12, 0, 0).AddDays(-30), 7);
            Banc.Egal("période sans partie", "Aucune partie sur les 7 derniers jours.", BilanJeu.Resume(r)[0]);
            Banc.Verifie("journal vide : explication", true, BilanJeu.Resume(BilanJeu.Calcule(null, maintenant, 0))[0].StartsWith("Aucune partie enregistrée"));
            Banc.Verifie("écart négatif", true,
                BilanJeu.Ecart(new BilanJeu.Resultat { Jours = 7, Secondes = 500, SecondesAvant = 1000 }) == "−50 % par rapport aux 7 jours d'avant");
        }
    }
}
