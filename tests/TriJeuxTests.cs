using System;
using System.Collections.Generic;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Vérifie <see cref="TriJeux"/> — ordre de la bibliothèque : favoris en tête, puis ordre
    /// alphabétique ou « joués récemment » (dernière partie du journal, jamais joués à la fin).
    /// </summary>
    internal static class TriJeuxTests
    {
        private static string Ordre(List<TriJeux.Entree> l, bool recents)
        {
            l.Sort((a, b) => TriJeux.Compare(a, b, recents));
            var noms = new List<string>();
            foreach (TriJeux.Entree e in l) noms.Add(e.Nom);
            return string.Join(",", noms);
        }

        public static void Tout()
        {
            Banc.Titre("Bibliothèque : favoris et joués récemment");

            var t = new DateTime(2026, 11, 26, 20, 0, 0);
            Func<List<TriJeux.Entree>> jeux = () => new List<TriJeux.Entree>
            {
                new TriJeux.Entree { Nom = "Valorant", Derniere = t.AddDays(-3) },
                new TriJeux.Entree { Nom = "apex", Derniere = null },
                new TriJeux.Entree { Nom = "Fortnite", Derniere = t.AddHours(-2) },
                new TriJeux.Entree { Nom = "Zelda", Favori = true, Derniere = null },
                new TriJeux.Entree { Nom = "Brawl", Derniere = t.AddDays(-40) },
            };

            Banc.Egal("alphabétique, favori en tête, casse ignorée", "Zelda,apex,Brawl,Fortnite,Valorant", Ordre(jeux(), false));
            Banc.Egal("joués récemment : favori, puis du plus récent au plus ancien, jamais joué à la fin",
                "Zelda,Fortnite,Valorant,Brawl,apex", Ordre(jeux(), true));

            var deux = new List<TriJeux.Entree>
            {
                new TriJeux.Entree { Nom = "B", Favori = true, Derniere = t.AddDays(-9) },
                new TriJeux.Entree { Nom = "A", Favori = true, Derniere = t.AddDays(-1) },
                new TriJeux.Entree { Nom = "C", Favori = false, Derniere = t },
            };
            Banc.Egal("deux favoris : rangés entre eux par date en mode récent", "A,B,C", Ordre(deux, true));

            var egaux = new List<TriJeux.Entree>
            {
                new TriJeux.Entree { Nom = "Halo", Derniere = t },
                new TriJeux.Entree { Nom = "Doom", Derniere = t },
            };
            Banc.Egal("même date : départage par le nom", "Doom,Halo", Ordre(egaux, true));
            Banc.Verifie("nom absent : pas d'exception", true,
                TriJeux.Compare(new TriJeux.Entree(), new TriJeux.Entree { Nom = "X" }, false) < 0);
        }
    }
}
