using System;
using System.Collections.Generic;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Vérifie <see cref="JournalJeu"/> — le découpage en parties à partir des relevés de la veille
    /// (un toutes les 2 s). Les cas tordus de la vraie vie se rejouent ici en quelques boucles : un
    /// jeu qui plante et se relance, un PC mis en veille au milieu d'une partie, un launcher qui
    /// ouvre l'exécutable trois secondes.
    /// </summary>
    internal static class JournalJeuTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 11, 26, 20, 0, 0);

        /// <summary>Rejoue des relevés de 2 s en 2 s de <paramref name="de"/> à <paramref name="a"/>
        /// secondes (bornes comprises) et rend les parties closes au passage.</summary>
        private static List<JournalJeu.Partie> Joue(JournalJeu.Etat e, string jeu, int de, int a)
        {
            var closes = new List<JournalJeu.Partie>();
            for (int s = de; s <= a; s += 2)
            {
                JournalJeu.Partie p = JournalJeu.Avance(e, jeu, T0.AddSeconds(s));
                if (p != null) closes.Add(p);
            }
            return closes;
        }

        public static void Tout()
        {
            Banc.Titre("Journal de jeu : parties et durées");
            PartieSimple();
            RelanceApresPlantage();
            VeilleDuPc();
            FlashIgnore();
            ChangementDeJeu();
            AllerRetour();
            Cumuls();
            Durees();
            Reconnaissance();
            Resumes();
            Bilans();
        }

        private static void PartieSimple()
        {
            var e = new JournalJeu.Etat();
            List<JournalJeu.Partie> pendant = Joue(e, "Fortnite", 0, 600);
            List<JournalJeu.Partie> jusqua30 = Joue(e, null, 602, 630);
            List<JournalJeu.Partie> apres = Joue(e, null, 632, 632);
            Banc.Verifie("rien n'est clos tant que le jeu tourne", true, pendant.Count == 0);
            Banc.Verifie("30 s d'absence ne closent pas encore la partie", true, jusqua30.Count == 0);
            Banc.Verifie("la partie est close dès que l'absence dépasse 30 s", true, apres.Count == 1);
            Banc.Egal("durée = du premier au dernier relevé vu", "600", apres.Count == 1 ? apres[0].Secondes.ToString() : "");
            Banc.Verifie("début = premier relevé", true, apres.Count == 1 && apres[0].Debut == T0);
            Banc.Verifie("l'état est remis à zéro après la clôture", true, e.Jeu == null);
        }

        private static void RelanceApresPlantage()
        {
            var e = new JournalJeu.Etat();
            var closes = new List<JournalJeu.Partie>();
            closes.AddRange(Joue(e, "Fortnite", 0, 300));
            closes.AddRange(Joue(e, null, 302, 308));
            closes.AddRange(Joue(e, "Fortnite", 310, 610));
            closes.AddRange(Joue(e, null, 612, 642));
            Banc.Verifie("un plantage relancé en 10 s reste UNE partie", true, closes.Count == 1);
            Banc.Egal("la partie couvre les deux moitiés", "610", closes.Count == 1 ? closes[0].Secondes.ToString() : "");
        }

        private static void VeilleDuPc()
        {
            var e = new JournalJeu.Etat();
            Joue(e, "Fortnite", 0, 300);
            JournalJeu.Partie p = JournalJeu.Avance(e, null, T0.AddHours(8));
            Banc.Verifie("au réveil, la partie est close", true, p != null);
            Banc.Egal("elle s'arrête au dernier relevé vu, pas au réveil", "300", p == null ? "" : p.Secondes.ToString());

            // Réveil avec le jeu encore ouvert : la partie d'avant est close, une nouvelle commence.
            var e2 = new JournalJeu.Etat();
            Joue(e2, "Fortnite", 0, 300);
            JournalJeu.Partie p2 = JournalJeu.Avance(e2, "Fortnite", T0.AddHours(8));
            Banc.Verifie("réveil jeu ouvert : la partie d'avant est close", true, p2 != null && p2.Secondes == 300);
            Banc.Verifie("réveil jeu ouvert : une nouvelle partie démarre au réveil", true, e2.Jeu == "Fortnite" && e2.Debut == T0.AddHours(8));
        }

        private static void FlashIgnore()
        {
            var e = new JournalJeu.Etat();
            var closes = new List<JournalJeu.Partie>();
            closes.AddRange(Joue(e, "Fortnite", 0, 40));
            closes.AddRange(Joue(e, null, 42, 120));
            Banc.Verifie("40 s de jeu ne font pas une partie", true, closes.Count == 0);
            Banc.Verifie("l'état est quand même remis à zéro", true, e.Jeu == null);
        }

        private static void ChangementDeJeu()
        {
            var e = new JournalJeu.Etat();
            Joue(e, "Fortnite", 0, 120);
            JournalJeu.Partie p = JournalJeu.Avance(e, "cs2", T0.AddSeconds(122));
            Banc.Verifie("passer à un autre jeu clôt le premier tout de suite", true, p != null && p.Jeu == "Fortnite");
            Banc.Egal("durée du premier jeu", "120", p == null ? "" : p.Secondes.ToString());
            Banc.Verifie("l'état porte le nouveau jeu", true, e.Jeu == "cs2" && e.Debut == T0.AddSeconds(122));
        }

        private static void AllerRetour()
        {
            var parties = new List<JournalJeu.Partie>
            {
                new JournalJeu.Partie { Jeu = "Fortnite", Debut = T0, Secondes = 4320, FpsMoyen = 238.8, FpsBas = 141 },
                new JournalJeu.Partie { Jeu = "Counter\tStrike 2", Debut = T0.AddDays(-1), Secondes = 900 },
            };
            string texte = JournalJeu.Ecrire(parties);
            List<JournalJeu.Partie> relues = JournalJeu.Lire(texte);
            Banc.Verifie("deux parties relues", true, relues.Count == 2);
            Banc.Verifie("champs conservés", true, relues.Count == 2 && relues[0].Jeu == "Fortnite" && relues[0].Debut == T0
                && relues[0].Secondes == 4320 && Math.Abs(relues[0].FpsMoyen - 238.8) < 0.01 && Math.Abs(relues[0].FpsBas - 141) < 0.01);
            Banc.Verifie("une tabulation dans le nom ne casse pas la ligne", true, relues.Count == 2 && relues[1].Jeu == "Counter Strike 2");
            Banc.Verifie("FPS non mesurés relus à 0", true, relues.Count == 2 && relues[1].FpsMoyen == 0 && relues[1].FpsBas == 0);
            Banc.Verifie("format indépendant de la culture (point décimal)", true, texte.Contains("238.8"));

            List<JournalJeu.Partie> abimees = JournalJeu.Lire("ligne cassée\n" + texte + "Fortnite\tpas une date\t12\t0\t0\n\n");
            Banc.Verifie("une ligne abîmée est ignorée, les autres lues", true, abimees.Count == 2);
            Banc.Verifie("texte null : aucune partie, pas d'exception", true, JournalJeu.Lire(null).Count == 0);
        }

        private static void Cumuls()
        {
            var parties = new List<JournalJeu.Partie>
            {
                new JournalJeu.Partie { Jeu = "Fortnite", Debut = T0.AddDays(-10), Secondes = 3600 },
                new JournalJeu.Partie { Jeu = "Fortnite", Debut = T0.AddDays(-2), Secondes = 1800 },
                new JournalJeu.Partie { Jeu = "cs2", Debut = T0.AddDays(-1), Secondes = 600 },
            };
            Banc.Egal("cumul d'un jeu sur 7 jours", "1800", JournalJeu.Secondes(parties, "fortnite", T0.AddDays(-7)).ToString());
            Banc.Egal("cumul d'un jeu au total", "5400", JournalJeu.Secondes(parties, "Fortnite", DateTime.MinValue).ToString());
            Banc.Egal("jeu null = tous les jeux", "2400", JournalJeu.Secondes(parties, null, T0.AddDays(-7)).ToString());
            DateTime? d = JournalJeu.DernierePartie(parties, "FORTNITE");
            Banc.Verifie("dernière partie = fin de la plus récente", true, d == T0.AddDays(-2).AddSeconds(1800));
            Banc.Verifie("jeu jamais joué : pas de date", true, JournalJeu.DernierePartie(parties, "Valorant") == null);
        }

        private static void Durees()
        {
            Banc.Egal("moins d'une minute", "moins d'une minute", JournalJeu.Duree(45));
            Banc.Egal("minutes seules", "45 min", JournalJeu.Duree(2700));
            Banc.Egal("heures et minutes sur deux chiffres", "2 h 05", JournalJeu.Duree(7500));
        }

        private static void Reconnaissance()
        {
            var index = new Dictionary<string, string>
            {
                { JournalJeu.Cle("FortniteClient-Win64-Shipping.exe"), "Fortnite" },
                { JournalJeu.Cle("cs2.exe"), "Counter-Strike 2" },
            };
            Banc.Egal("casse ignorée, avec .exe", "Fortnite", JournalJeu.JeuParmi(new[] { "explorer", "FORTNITECLIENT-WIN64-SHIPPING.EXE" }, index));
            Banc.Egal("sans .exe (noms de processus)", "Counter-Strike 2", JournalJeu.JeuParmi(new[] { "cs2" }, index));
            Banc.Verifie("aucun jeu : null", true, JournalJeu.JeuParmi(new[] { "explorer", "discord" }, index) == null);
            Banc.Verifie("liste null : null, pas d'exception", true, JournalJeu.JeuParmi(null, index) == null);
        }

        private static void Resumes()
        {
            var fps = new List<double>();
            for (int i = 0; i < 594; i++) fps.Add(240);
            for (int i = 0; i < 6; i++) fps.Add(120);
            double moyen, bas;
            JournalJeu.Resume(fps, 143280, 600, out moyen, out bas);
            Banc.Verifie("moyenne = images / secondes", true, Math.Abs(moyen - 238.8) < 0.001);
            Banc.Verifie("au plus bas = 1er centile des relevés par seconde", true, Math.Abs(bas - 120) < 0.001);
            JournalJeu.Resume(fps.GetRange(0, 99), 23760, 99, out moyen, out bas);
            Banc.Verifie("moins de 100 relevés : pas de « plus bas »", true, bas == 0);
            JournalJeu.Resume(new List<double>(), 0, 0, out moyen, out bas);
            Banc.Verifie("aucune mesure : 0 et 0, pas de division par zéro", true, moyen == 0 && bas == 0);
        }

        private static void Bilans()
        {
            var mesuree = new JournalJeu.Partie { Jeu = "Fortnite", Debut = T0, Secondes = 4320, FpsMoyen = 238.8, FpsBas = 141.4 };
            Banc.Egal("bilan avec FPS", "1 h 12 · 238 FPS en moyenne · 141 au plus bas", JournalJeu.Bilan(mesuree));
            var nue = new JournalJeu.Partie { Jeu = "Fortnite", Debut = T0, Secondes = 4320 };
            Banc.Egal("bilan sans mesure", "1 h 12", JournalJeu.Bilan(nue));
            var sansBas = new JournalJeu.Partie { Jeu = "Fortnite", Debut = T0, Secondes = 90, FpsMoyen = 144 };
            Banc.Egal("bilan sans « plus bas »", "1 min · 144 FPS en moyenne", JournalJeu.Bilan(sansBas));
        }
    }
}
