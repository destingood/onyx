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
            Tuiles();
            Mesures();
        }

        private static void Mesures()
        {
            // 240 FPS pendant 2 min, un relevé toutes les 2 s (compteur cumulé d'images du processus).
            var m = new JournalJeu.Mesure();
            for (int i = 0; i <= 60; i++) m.Ajoute(10, 1000 + i * 480L, 240, T0.AddSeconds(i * 2));
            var p = new JournalJeu.Partie { Jeu = "cs2", Debut = T0, Secondes = 120 };
            m.Remplit(p);
            Banc.Verifie("mesure : images comptées entre relevés", true, m.Images == 28800 && Math.Abs(m.Secondes - 120) < 0.001);
            Banc.Verifie("mesure : moyenne remplie dans la partie", true, Math.Abs(p.FpsMoyen - 240) < 0.001);
            Banc.Verifie("mesure : 61 relevés, pas de « plus bas » sous 100", true, p.FpsBas == 0);

            // Relance du jeu : nouveau PID, compteur reparti de zéro. Rien de négatif, rien d'inventé.
            var r = new JournalJeu.Mesure();
            r.Ajoute(10, 5000, 100, T0);
            r.Ajoute(10, 5200, 100, T0.AddSeconds(2));
            r.Ajoute(22, 50, 100, T0.AddSeconds(4));
            r.Ajoute(22, 250, 100, T0.AddSeconds(6));
            Banc.Verifie("mesure : changement de PID, pas d'images négatives", true, r.Images == 400 && Math.Abs(r.Secondes - 4) < 0.001);

            // Trou (alt-tab, jeu sans image) : l'intervalle n'est pas compté.
            var t = new JournalJeu.Mesure();
            t.Ajoute(10, 0, 60, T0);
            t.Rien();
            t.Ajoute(10, 600, 60, T0.AddSeconds(10));
            t.Ajoute(10, 720, 60, T0.AddSeconds(12));
            Banc.Verifie("mesure : un relevé sans image coupe la continuité", true, t.Images == 120 && Math.Abs(t.Secondes - 2) < 0.001);
            var g = new JournalJeu.Mesure();
            g.Ajoute(10, 0, 60, T0);
            g.Ajoute(10, 900, 60, T0.AddSeconds(15));
            Banc.Verifie("mesure : plus de 5 s entre deux relevés, intervalle ignoré", true, g.Images == 0 && g.Secondes == 0);
        }

        private static void Tuiles()
        {
            DateTime now = T0;
            Banc.Egal("tuile : heures arrondies, jour même", "12 h · aujourd'hui", JournalJeu.Tuile(12 * 3600 + 1700, T0.AddHours(-3), now));
            Banc.Egal("tuile : moins d'une heure, hier", "45 min · hier", JournalJeu.Tuile(2700, T0.AddDays(-1), now));
            Banc.Egal("tuile : il y a N j", "3 h · il y a 2 j", JournalJeu.Tuile(3 * 3600, T0.AddDays(-2).AddHours(-1), now));
            Banc.Verifie("tuile : jamais joué = null", true, JournalJeu.Tuile(0, null, now) == null);
            var parties = new List<JournalJeu.Partie>
            {
                new JournalJeu.Partie { Jeu = "Fortnite", Debut = T0, Secondes = 600 },
                new JournalJeu.Partie { Jeu = "cs2", Debut = T0, Secondes = 300 },
            };
            List<JournalJeu.Partie> f = JournalJeu.Filtre(parties, n => n == "cs2");
            Banc.Verifie("filtre : garde les parties du jeu", true, f.Count == 1 && f[0].Jeu == "cs2");
            Banc.Verifie("filtre : entrées nulles, liste vide", true, JournalJeu.Filtre(null, n => true).Count == 0);
            Banc.Verifie("dernière partie, jeu null = toutes", true, JournalJeu.DernierePartie(parties, null) == T0.AddSeconds(600));

            var histo = new List<JournalJeu.Partie>();
            for (int i = 0; i < 7; i++) histo.Add(new JournalJeu.Partie { Jeu = "Fortnite", Debut = T0.AddDays(-i), Secondes = 3600 });
            histo.Reverse();
            List<JournalJeu.Partie> d = JournalJeu.Dernieres(histo, 5);
            Banc.Verifie("dernières : 5 au plus, la plus récente d'abord", true, d.Count == 5 && d[0].Debut == T0 && d[4].Debut == T0.AddDays(-4));
            Banc.Egal("ligne d'une partie", "26/11 20:00 · 1 h 12 · 238 FPS en moyenne · 141 au plus bas",
                JournalJeu.LignePartie(new JournalJeu.Partie { Jeu = "Fortnite", Debut = T0, Secondes = 4320, FpsMoyen = 238.8, FpsBas = 141.4 }));
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

            // Exe livrés DANS le dossier d'un jeu mais qui tournent sans lui (vu sur Darktide : le
            // runtime WebView2 de Windows, actif en permanence, comptait comme une partie sans fin).
            Banc.Verifie("index : runtime WebView2 exclu", true, JournalJeu.ExeExclu("msedgewebview2"));
            Banc.Verifie("index : sous-processus CEF exclu", true, JournalJeu.ExeExclu("cefsharp.browsersubprocess"));
            Banc.Verifie("index : rapport de plantage exclu", true, JournalJeu.ExeExclu("unitycrashhandler64"));
            Banc.Verifie("index : java exclu", true, JournalJeu.ExeExclu("javaw"));
            Banc.Verifie("index : l'exe du jeu est gardé", false, JournalJeu.ExeExclu("cs2"));
            Banc.Verifie("index : exe Unreal du jeu gardé", false, JournalJeu.ExeExclu("darktide"));
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
