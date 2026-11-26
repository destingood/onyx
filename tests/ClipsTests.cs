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
        }
    }
}
