using System;
using System.Collections.Generic;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Bons plans : prix affichés, plafonds inclusifs, lien sûr, filtre, tri et dédoublonnage.
    /// </summary>
    internal static class OffresTests
    {
        static Offre O(string src, string id, string nom, int prix, int remise, params string[] plats)
        { return new Offre { Source = src, Id = id, Nom = nom, PrixCts = prix, Remise = remise, Plateformes = plats }; }

        public static void Tout()
        {
            Banc.Titre("Bons plans : offres, prix, filtre");

            Banc.Verifie("prix 749", true, Offres.Prix(749) == "7,49 €");
            Banc.Verifie("prix rond", true, Offres.Prix(2000) == "20 €");
            Banc.Verifie("prix sous 1 €", true, Offres.Prix(50) == "0,50 €");
            Banc.Verifie("prix gratuit", true, Offres.Prix(0) == "Gratuit");
            Banc.Verifie("prix inconnu", true, Offres.Prix(-1) == "—");
            Banc.Verifie("libellés", true, Offres.LibellePlafond(-1) == "Tous" && Offres.LibellePlafond(0) == "Gratuit" && Offres.LibellePlafond(100) == "1 € max" && Offres.LibellePlafond(2000) == "20 € max");
            Banc.Verifie("plafond inclusif", true, Offres.SousPlafond(O("steam", "1", "A", 1000, 0), 1000));
            Banc.Verifie("au-dessus du plafond", true, !Offres.SousPlafond(O("steam", "1", "A", 1001, 0), 1000));
            Banc.Verifie("gratuit sous 1 €", true, Offres.SousPlafond(O("epic", "1", "A", 0, 100), 100));
            Banc.Verifie("plafond Gratuit refuse 99 centimes", true, !Offres.SousPlafond(O("steam", "1", "A", 99, 0), 0));
            Banc.Verifie("prix inconnu hors plafond", true, !Offres.SousPlafond(O("steam", "1", "A", -1, 0), 2000));
            Banc.Verifie("prix inconnu sans plafond", true, Offres.SousPlafond(O("steam", "1", "A", -1, 0), -1));
            Banc.Verifie("nom normalisé", true, Offres.NomNormalise("  Pokémon™ : Épée & Bouclier ") == "pokemonepeebouclier");
            Banc.Verifie("nom null", true, Offres.NomNormalise(null) == "");
            Banc.Verifie("lien https", true, Offres.LienSur("https://store.steampowered.com/app/730/"));
            Banc.Verifie("lien http", true, Offres.LienSur("http://exemple.fr/a"));
            Banc.Verifie("lien javascript refusé", true, !Offres.LienSur("javascript:alert(1)"));
            Banc.Verifie("lien file refusé", true, !Offres.LienSur("file:///C:/Windows/System32/calc.exe"));
            Banc.Verifie("lien UNC refusé", true, !Offres.LienSur("\\\\serveur\\partage\\x.exe"));
            Banc.Verifie("lien relatif refusé", true, !Offres.LienSur("/app/730") && !Offres.LienSur("") && !Offres.LienSur(null));

            var l = new List<Offre> {
                O("steam", "1", "Zelda-like", 1999, 50, "Steam"), O("steam", "2", "Éclipse", 499, 80, "Steam"),
                O("epic", "3", "Mark of the Deep", 0, 100, "Epic Games"), O("gamerpower", "9", "Mark of the Deep", 0, 100, "PC", "Epic Games"),
                O("steam", "4", "", 100, 10, "Steam"), O("steam", "2", "Éclipse", 499, 80, "Steam") };
            l[0].Tags = new[] { 19, 21 };
            var f = new Offres.Filtre();
            Banc.Verifie("nom vide écarté", true, Offres.Filtrer(l, f).Count == 5);
            f.Texte = "eclipse"; Banc.Verifie("texte sans accent", true, Offres.Filtrer(l, f).Count == 2);
            f = new Offres.Filtre { PlafondCts = 500 }; Banc.Verifie("5 € max", true, Offres.Filtrer(l, f).Count == 4);
            f = new Offres.Filtre { Tag = 19 }; Banc.Verifie("catégorie", true, Offres.Filtrer(l, f).Count == 1);
            f = new Offres.Filtre { Plateforme = "epic games" }; Banc.Verifie("plateforme sans casse", true, Offres.Filtrer(l, f).Count == 2);
            var d = Offres.Dedoublonner(l);
            Banc.Verifie("doublon de clé retiré, gratuit en double : Epic gardé", true, d.Count == 4 && d.Exists(o => o.Source == "epic") && !d.Exists(o => o.Source == "gamerpower"));
            Banc.Verifie("tri remise", true, Offres.Trier(d, "remise")[0].Remise == 100);
            Banc.Verifie("tri prix : inconnu en dernier", true, Offres.Trier(new List<Offre> { O("steam", "1", "A", -1, 0), O("steam", "2", "B", 300, 0) }, "prix")[0].Id == "2");
            Banc.Verifie("tri nom", true, Offres.Trier(new List<Offre> { O("steam", "1", "b", 1, 0), O("steam", "2", "A", 1, 0) }, "nom")[0].Id == "2");
            Banc.Verifie("18 catégories, Action = 19", true, Offres.Categories.Length == 18 && Offres.Categories[0].Key == "Action" && Offres.Categories[0].Value == 19);
            // Revue finale : offres terminées et sources en échec écartées.
            var now = new DateTime(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);
            var finie = O("gamerpower", "1", "Fini", 0, 0); finie.Fin = now.AddHours(-1);
            var active = O("epic", "2", "Actif", 0, 0); active.Fin = now.AddDays(2);
            var sansFin = O("gamerpower", "3", "Sans fin", 0, 0);
            var enCours = Offres.EnCours(new List<Offre> { finie, active, sansFin }, now);
            Banc.Verifie("gratuits : offre terminée écartée", true, enCours.Count == 2 && !enCours.Contains(finie));
            var sains = Offres.SansSources(new List<Offre> { finie, active, sansFin }, new[] { "gamerpower" });
            Banc.Verifie("gratuits : source en échec écartée", true, sains.Count == 1 && sains[0] == active);
        }
    }
}
