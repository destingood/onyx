using System;
using System.Collections.Generic;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Vérifie <see cref="Clips"/> — bibliothèque des captures : à quel jeu appartient un fichier
    /// (dossier du jeu, sinon nom avant la date, sinon « Autres »), quels fichiers sont des vidéos,
    /// et l'ordre d'affichage.
    /// </summary>
    internal static class ClipsTests
    {
        public static void Tout()
        {
            Banc.Titre("Clips");

            Banc.Egal("sous-dossier direct de la racine", "Fortnite", Clips.JeuDepuisChemin(@"C:\V", @"C:\V\Fortnite\clip.mp4"));
            Banc.Egal("sous-dossier plus profond : le premier niveau compte", "Fortnite", Clips.JeuDepuisChemin(@"C:\V", @"C:\V\Fortnite\Session 2\clip.mp4"));
            Banc.Egal("Game Bar : nom avant la date", "Fortnite", Clips.JeuDepuisChemin(@"C:\V\Captures", @"C:\V\Captures\Fortnite 2026-11-26 21-04-11.mp4"));
            Banc.Egal("NVIDIA : nom avant la date à points", "Counter-Strike 2", Clips.JeuDepuisChemin(@"C:\V", @"C:\V\Counter-Strike 2 2026.11.26 - 21.04.11.02.DVR.mp4"));
            Banc.Egal("dossier d'un logiciel de capture : le niveau suivant est le jeu", "Fortnite", Clips.JeuDepuisChemin(@"C:\Users\L\Videos", @"C:\Users\L\Videos\NVIDIA\Fortnite\Fortnite 2026.11.26 - 21.04.11.02.DVR.mp4"));
            Banc.Egal("dossier d'un logiciel de capture, clip posé à plat : nom avant la date", "Valorant", Clips.JeuDepuisChemin(@"C:\V", @"C:\V\Radeon ReLive\Valorant 2026-11-26 21-04-11.mp4"));
            Banc.Egal("dossier d'un logiciel de capture, sans jeu repérable", "Autres", Clips.JeuDepuisChemin(@"C:\V", @"C:\V\Medal\clip.mp4"));
            Banc.Egal("racine avec barre finale", "Fortnite", Clips.JeuDepuisChemin(@"C:\V\", @"C:\V\Fortnite\clip.mp4"));
            Banc.Egal("casse de la racine ignorée", "Fortnite", Clips.JeuDepuisChemin(@"c:\v", @"C:\V\Fortnite\clip.mp4"));
            Banc.Egal("fichier à la racine sans date", "Autres", Clips.JeuDepuisChemin(@"C:\V", @"C:\V\a.mp4"));
            Banc.Egal("fichier qui n'est qu'une date", "Autres", Clips.JeuDepuisChemin(@"C:\V", @"C:\V\2026-11-26 21-04-11.mp4"));
            Banc.Egal("hors de la racine", "Autres", Clips.JeuDepuisChemin(@"C:\V", @"D:\X\Fortnite\clip.mp4"));
            Banc.Egal("chemins vides", "Autres", Clips.JeuDepuisChemin(null, null));

            Banc.Verifie("vidéo : .MP4 (casse ignorée)", true, Clips.EstVideo("x.MP4"));
            Banc.Verifie("vidéo : .mkv .mov .webm", true, Clips.EstVideo("a.mkv") && Clips.EstVideo("b.mov") && Clips.EstVideo("c.webm"));
            Banc.Verifie("pas une vidéo : .png, sans extension, null", false, Clips.EstVideo("x.png") || Clips.EstVideo("mp4") || Clips.EstVideo(null));

            var a = new Clips.Clip { Chemin = "a", Jeu = "Fortnite", Date = new DateTime(2026, 11, 20) };
            var b = new Clips.Clip { Chemin = "b", Jeu = "Valorant", Date = new DateTime(2026, 11, 26) };
            var c = new Clips.Clip { Chemin = "c", Jeu = "fortnite", Date = new DateTime(2026, 11, 24) };
            List<Clips.Clip> tous = Clips.Trie(new[] { a, b, c }, null);
            Banc.Egal("tri : plus récent d'abord", "b,c,a", string.Join(",", tous.ConvertAll(x => x.Chemin)));
            List<Clips.Clip> fn = Clips.Trie(new[] { a, b, c }, "FORTNITE");
            Banc.Egal("filtre par jeu, casse ignorée", "c,a", string.Join(",", fn.ConvertAll(x => x.Chemin)));
            Banc.Verifie("liste nulle : vide", true, Clips.Trie(null, null).Count == 0);

            // Renommer.
            string err;
            Banc.Egal("nom simple gardé", "Clutch 1v4", Clips.NomPropre("  Clutch 1v4  ", ".mp4", out err));
            Banc.Egal("extension tapée par habitude : retirée", "Clutch", Clips.NomPropre("Clutch.MP4", ".mp4", out err));
            Banc.Egal("points et espaces de fin retirés", "Clutch", Clips.NomPropre("Clutch. . ", ".mp4", out err));
            Banc.Egal("point au milieu gardé", "v1.2 final", Clips.NomPropre("v1.2 final", ".mp4", out err));
            Banc.Verifie("nom vide refusé, avec la raison", true, Clips.NomPropre("   ", ".mp4", out err) == null && err == "Le nom est vide.");
            Banc.Verifie("extension seule refusée", true, Clips.NomPropre(".mp4", ".mp4", out err) == null);
            foreach (char c2 in "\\/:*?\"<>|")
                if (Clips.NomPropre("a" + c2 + "b", ".mp4", out err) != null) Banc.Verifie("caractère interdit accepté : " + c2, true, false);
            Banc.Verifie("caractères interdits refusés", true, Clips.NomPropre("a:b", ".mp4", out err) == null && err.EndsWith(":"));
            Banc.Verifie("tabulation refusée", true, Clips.NomPropre("a\tb", ".mp4", out err) == null);
            Banc.Verifie("nom réservé refusé (CON, com1.txt)", true,
                Clips.NomPropre("con", ".mp4", out err) == null && Clips.NomPropre("COM1.txt", ".mp4", out err) == null);
            Banc.Egal("nom qui commence comme un réservé : accepté", "Console", Clips.NomPropre("Console", ".mp4", out err));
            Banc.Verifie("nom trop long refusé", true, Clips.NomPropre(new string('a', Clips.NomMax + 1), ".mp4", out err) == null);
            Banc.Egal("nom à la longueur maximale accepté", Clips.NomMax.ToString(), Clips.NomPropre(new string('a', Clips.NomMax), ".mp4", out err).Length.ToString());
            Banc.Egal("même dossier, même extension", @"D:\Vidéos\Captures\Clutch.mp4",
                Clips.CheminRenomme(@"D:\Vidéos\Captures\Fortnite 2026-11-26 21-04-11.mp4", "Clutch"));

            // Limite d'espace.
            Banc.Egal("limite relue", "50", Clips.LitLimite(" 50\r\n").ToString());
            Banc.Verifie("limite absente, nulle, négative, absurde ou illisible : aucune", true,
                Clips.LitLimite(null) == 0 && Clips.LitLimite("0") == 0 && Clips.LitLimite("-5") == 0
                && Clips.LitLimite("99999") == 0 && Clips.LitLimite("5,5") == 0 && Clips.LitLimite("dix") == 0);
            Banc.Verifie("le premier choix proposé est « aucune »", true, Clips.LimitesGo[0] == 0);

            var v1 = new Clips.Clip { Chemin = "v1", Date = new DateTime(2026, 1, 1), Octets = 40 };
            var v2 = new Clips.Clip { Chemin = "v2", Date = new DateTime(2026, 3, 1), Octets = 30 };
            var v3 = new Clips.Clip { Chemin = "v3", Date = new DateTime(2026, 2, 1), Octets = 20 };
            var v4 = new Clips.Clip { Chemin = "v4", Date = new DateTime(2026, 4, 1), Octets = 10 };
            var lot = new[] { v4, v1, v2, v3 };
            Banc.Egal("total", "100", Clips.Total(lot).ToString());
            Banc.Verifie("sous la limite ou juste dessus : rien à retirer", true,
                Clips.EnTrop(lot, 100).Count == 0 && Clips.EnTrop(lot, 500).Count == 0);
            Banc.Verifie("aucune limite : rien à retirer", true, Clips.EnTrop(lot, 0).Count == 0 && Clips.EnTrop(lot, -1).Count == 0 && Clips.EnTrop(null, 10).Count == 0);
            Banc.Egal("dépassement de 1 : le plus ancien seulement", "v1", string.Join(",", Clips.EnTrop(lot, 99).ConvertAll(x => x.Chemin)));
            Banc.Egal("les plus anciens d'abord, pas un de plus qu'il ne faut", "v1,v3", string.Join(",", Clips.EnTrop(lot, 45).ConvertAll(x => x.Chemin)));
            Banc.Egal("limite plus petite que le plus récent : tout part", "v1,v3,v2,v4", string.Join(",", Clips.EnTrop(lot, 5).ConvertAll(x => x.Chemin)));
        }
    }
}
