using System;
using System.Collections.Generic;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Vérifie les fonctions de la 16.07 reprises de Razer Cortex : options de lancement par jeu
    /// (<see cref="OptionsJeu"/>), nom des captures d'écran (<see cref="CaptureEcran"/>), courbe des
    /// FPS (<see cref="JournalJeu.PointsFps"/>) et alerte « la promo se termine » (<see cref="Suivi"/>).
    /// </summary>
    internal static class LancementTests
    {
        public static void Tout()
        {
            Options();
            Captures();
            Courbe();
            FinDePromo();
        }

        private static void Options()
        {
            Banc.Titre("Jeux : options de lancement");
            var l = new List<OptionsJeu.Options>();
            Banc.Verifie("pose", true, OptionsJeu.Pose(l, "Counter-Strike 2", "CS2", "", "-novid\t-high", ""));
            Banc.Egal("arguments nettoyés", "-novid -high", OptionsJeu.Trouve(l, "Counter-Strike 2").Arguments);
            Banc.Egal("nom affiché", "CS2", OptionsJeu.NomAffiche(l, "Counter-Strike 2"));
            Banc.Egal("même jeu, autre édition", "CS2", OptionsJeu.NomAffiche(l, "Counter-Strike™ 2"));
            Banc.Egal("jeu sans options", "Fortnite", OptionsJeu.NomAffiche(l, "Fortnite"));

            OptionsJeu.Pose(l, "Fortnite", "Fortnite", "", "", "");
            Banc.Verifie("nom identique au nom détecté : rien à garder", true, OptionsJeu.Trouve(l, "Fortnite") == null);

            OptionsJeu.Pose(l, "Control", "", @"D:\Jeux\Control\Control_DX12.exe", "", "");
            List<OptionsJeu.Options> relu = OptionsJeu.Lire(OptionsJeu.Ecrire(l));
            Banc.Egal("relu : nombre", "2", relu.Count.ToString());
            Banc.Egal("relu : exécutable", @"D:\Jeux\Control\Control_DX12.exe", OptionsJeu.Trouve(relu, "CONTROL™").Cible);

            OptionsJeu.Pose(l, "Control", "", "", "", "");
            Banc.Verifie("options vidées : jeu retiré", true, OptionsJeu.Trouve(l, "Control") == null);
            Banc.Egal("ligne cassée ignorée", "0", OptionsJeu.Lire("abc\tx\n\n").Count.ToString());

            Banc.Egal("Steam sans arguments", "-applaunch 730", OptionsJeu.ArgsSteam(730, "  "));
            Banc.Egal("Steam avec arguments", "-applaunch 730 -novid", OptionsJeu.ArgsSteam(730, "-novid"));
            Banc.Egal("dossier choisi", @"E:\X", OptionsJeu.DossierDepart(@" E:\X ", @"D:\J\a.exe"));
            Banc.Egal("dossier de l'exécutable", @"D:\J", OptionsJeu.DossierDepart("", @"D:\J\a.exe"));
        }

        private static void Captures()
        {
            Banc.Titre("Jeux : capture d'écran");
            var t = new DateTime(2026, 11, 26, 21, 4, 11);
            Banc.Egal("format Game Bar", "Fortnite 2026-11-26 21-04-11.png", CaptureEcran.NomFichier("Fortnite", t));
            Banc.Egal("caractères interdits", "Half-Life  Alyx 2026-11-26 21-04-11.png", CaptureEcran.NomFichier("Half-Life: Alyx", t));
            Banc.Egal("sans jeu", "Capture 2026-11-26 21-04-11.png", CaptureEcran.NomFichier(null, t));
            Banc.Egal("bibliothèque de clips : jeu retrouvé", "Fortnite",
                Clips.JeuDepuisChemin(@"C:\V", @"C:\V\" + CaptureEcran.NomFichier("Fortnite", t)));
            var pris = new HashSet<string> { "a.png", "a (2).png" };
            Banc.Egal("nom libre", "a (3).png", CaptureEcran.Libre("a.png", pris.Contains));
            Banc.Egal("nom déjà libre", "b.png", CaptureEcran.Libre("b.png", pris.Contains));
        }

        private static void Courbe()
        {
            Banc.Titre("Jeux : courbe des FPS");
            var t = new DateTime(2026, 11, 1, 20, 0, 0);
            var parties = new List<JournalJeu.Partie>();
            for (int i = 0; i < 30; i++)
                parties.Add(new JournalJeu.Partie { Jeu = "X", Debut = t.AddDays(-i), Secondes = 600, FpsMoyen = i % 3 == 0 ? 0 : 100 + i });
            List<JournalJeu.Partie> pts = JournalJeu.PointsFps(parties, 12);
            Banc.Egal("12 points au plus", "12", pts.Count.ToString());
            Banc.Verifie("parties non mesurées écartées", true, pts.TrueForAll(p => p.FpsMoyen > 0));
            Banc.Verifie("du plus ancien au plus récent", true, pts[0].Debut < pts[11].Debut);
            Banc.Verifie("les plus récentes gardées", true, pts[11].Debut == t.AddDays(-1));
            Banc.Egal("liste nulle", "0", JournalJeu.PointsFps(null, 12).Count.ToString());
        }

        private static void FinDePromo()
        {
            Banc.Titre("Bons plans : la promo se termine");
            var now = new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);
            Func<int, DateTime?, Offre> cs = (r, fin) => new Offre { Source = "steam", Id = "730", SteamAppId = 730, Nom = "Counter-Strike 2",
                Url = "https://store.steampowered.com/app/730/", PrixCts = 1000 * (100 - r) / 100, PrixOrigineCts = 1000, Remise = r, Fin = fin };
            var jeux = new List<JeuSuivi> { new JeuSuivi { AppId = 730, Nom = "Counter-Strike 2", RemiseNotifiee = 50 } };

            Banc.Egal("fin lointaine : rien", "0", Suivi.Decide(jeux, new List<Offre> { cs(50, now.AddDays(5)) }, null, now).Count.ToString());
            List<Suivi.Alerte> a = Suivi.Decide(jeux, new List<Offre> { cs(50, now.AddHours(20)) }, null, now);
            Banc.Egal("moins de 48 h : une alerte", "1", a.Count.ToString());
            Banc.Verifie("titre", true, a.Count == 1 && a[0].Titre.Contains("se termine bientôt"));
            Banc.Egal("pas deux fois pour la même promo", "0", Suivi.Decide(jeux, new List<Offre> { cs(50, now.AddHours(20)) }, null, now.AddHours(6)).Count.ToString());

            List<JeuSuivi> relus = Suivi.Lire(Suivi.Ecrire(jeux));
            Banc.Egal("relu : fin notifiée gardée", jeux[0].FinNotifiee, relus[0].FinNotifiee);
            Banc.Egal("ancien format à 4 colonnes", "", Suivi.Lire("730\t50\t\tCounter-Strike 2\n")[0].FinNotifiee);
            Banc.Egal("ancien format : nom intact", "Counter-Strike 2", Suivi.Lire("730\t50\t\tCounter-Strike 2\n")[0].Nom);

            Banc.Egal("promo suivante : nouvelle alerte", "1", Suivi.Decide(jeux, new List<Offre> { cs(50, now.AddDays(30).AddHours(20)) }, null, now.AddDays(30)).Count.ToString());
            Banc.Egal("promo finie : rien", "0", Suivi.Decide(jeux, new List<Offre> { cs(50, now.AddHours(-1)) }, null, now.AddDays(31)).Count.ToString());

            // Nouvelle remise plus forte ET fin proche : une seule alerte, qui donne la date de fin.
            var j2 = new List<JeuSuivi> { new JeuSuivi { AppId = 730, Nom = "Counter-Strike 2", RemiseNotifiee = 0 } };
            List<Suivi.Alerte> b = Suivi.Decide(j2, new List<Offre> { cs(75, now.AddHours(10)) }, null, now);
            Banc.Egal("remise + fin proche : une seule alerte", "1", b.Count.ToString());
            Banc.Verifie("remise + fin proche : date dans le texte", true, b.Count == 1 && b[0].Texte.Contains("jusqu'au"));
            Banc.Egal("remise + fin proche : pas de rappel ensuite", "0", Suivi.Decide(j2, new List<Offre> { cs(75, now.AddHours(10)) }, null, now.AddHours(6)).Count.ToString());
        }
    }
}
