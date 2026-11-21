using System;
using System.Collections.Generic;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Bons plans : fichiers de favoris et de jeux suivis, règle de notification (une seule par promo).
    /// </summary>
    internal static class SuiviTests
    {
        public static void Tout()
        {
            Banc.Titre("Bons plans : favoris et jeux suivis");

            var d0 = new DateTime(2026, 11, 21);
            var fav = new List<Favori>();
            var o = new Offre { Source = "steam", Id = "2909400", SteamAppId = 2909400, Nom = "FINAL\tFANTASY\r\nVII", Url = "https://store.steampowered.com/app/2909400/", PrixCts = 749, PrixOrigineCts = 4999, Remise = 85 };
            Banc.Verifie("favori ajouté", true, Favoris.Bascule(fav, Favoris.DepuisOffre(o, d0)) && Favoris.Contient(fav, "steam:2909400"));
            string txt = Favoris.Ecrire(fav);
            Banc.Verifie("écriture : une ligne, 5 champs", true, txt.Split('\n').Length == 2 && txt.TrimEnd('\n').Split('\t').Length == 5);
            var relu = Favoris.Lire(txt);
            Banc.Verifie("relecture", true, relu.Count == 1 && relu[0].Cle == "steam:2909400" && relu[0].Titre == "FINAL FANTASY  VII" && relu[0].Date == d0 && relu[0].Url == o.Url);
            Banc.Verifie("favori retiré", true, !Favoris.Bascule(fav, Favoris.DepuisOffre(o, d0)) && fav.Count == 0);
            Banc.Verifie("fichier abîmé : lignes valides gardées", true, Favoris.Lire("tronquée\tx\n\n" + txt + "a\tb\tpas-une-date\thttps://x.fr\tT\n").Count == 2);
            Banc.Verifie("fichier vide ou null", true, Favoris.Lire("").Count == 0 && Favoris.Lire(null).Count == 0);

            var jeux = new List<JeuSuivi>();
            Banc.Verifie("suivre un jeu déjà en promo : pas d'alerte immédiate", true, Suivi.Ajoute(jeux, o) && jeux[0].RemiseNotifiee == 85);
            Banc.Verifie("déjà suivi", true, !Suivi.Ajoute(jeux, o));
            Banc.Verifie("hors Steam refusé", true, !Suivi.Ajoute(jeux, new Offre { Source = "epic", Id = "x", Nom = "X" }));
            Func<int, Offre> fiche = r => new Offre { Source = "steam", Id = "2909400", SteamAppId = 2909400, Nom = "FINAL FANTASY VII REBIRTH", Url = o.Url, PrixCts = r == 0 ? 4999 : 4999 * (100 - r) / 100, PrixOrigineCts = 4999, Remise = r };
            var aucune = new List<Offre>();
            Banc.Verifie("même promo : rien", true, Suivi.Decide(jeux, new List<Offre> { fiche(85) }, aucune).Count == 0);
            Banc.Verifie("nom mis à jour depuis la fiche", true, jeux[0].Nom == "FINAL FANTASY VII REBIRTH");
            Banc.Verifie("fiche absente : rien, rien ne réarme", true, Suivi.Decide(jeux, aucune, aucune).Count == 0 && jeux[0].RemiseNotifiee == 85);
            Banc.Verifie("fin de promo : réarme sans alerte", true, Suivi.Decide(jeux, new List<Offre> { fiche(0) }, aucune).Count == 0 && jeux[0].RemiseNotifiee == 0);
            var a = Suivi.Decide(jeux, new List<Offre> { fiche(50) }, aucune);
            Banc.Verifie("nouvelle promo : une alerte", true, a.Count == 1 && a[0].Titre == "FINAL FANTASY VII REBIRTH : −50 %" && a[0].Texte.Contains("au lieu de 49,99 € sur Steam") && a[0].Url == o.Url && a[0].AppId == 2909400);
            Banc.Verifie("contrôle suivant : pas de doublon", true, Suivi.Decide(jeux, new List<Offre> { fiche(50) }, aucune).Count == 0);
            Banc.Verifie("promo plus forte : nouvelle alerte", true, Suivi.Decide(jeux, new List<Offre> { fiche(75) }, aucune).Count == 1);
            Banc.Verifie("promo plus faible : rien", true, Suivi.Decide(jeux, new List<Offre> { fiche(60) }, aucune).Count == 0);
            var relus = Suivi.Lire(Suivi.Ecrire(jeux));
            Banc.Verifie("suivis : aller-retour", true, relus.Count == 1 && relus[0].AppId == 2909400 && relus[0].RemiseNotifiee == 75 && relus[0].Nom == "FINAL FANTASY VII REBIRTH");
            Banc.Verifie("redémarrage : pas de doublon", true, Suivi.Decide(relus, new List<Offre> { fiche(75) }, aucune).Count == 0);
            // Jeux installés : remise inconnue à l'ajout, amorcée sans alerte au premier contrôle.
            var inst = new List<JeuSuivi>();
            Func<int, Offre> cs = r => new Offre { Source = "steam", Id = "730", SteamAppId = 730, Nom = "Counter-Strike 2", Url = "https://store.steampowered.com/app/730/", PrixCts = 1000 * (100 - r) / 100, PrixOrigineCts = 1000, Remise = r };
            Banc.Verifie("jeu installé : ajouté à amorcer", true, Suivi.AjouteInstalle(inst, 730, "CS2") && inst[0].RemiseNotifiee == Suivi.AAmorcer && !Suivi.AjouteInstalle(inst, 730, "CS2"));
            Banc.Verifie("jeu installé : fiche absente, reste à amorcer", true, Suivi.Decide(inst, aucune, aucune).Count == 0 && inst[0].RemiseNotifiee == Suivi.AAmorcer);
            Banc.Verifie("jeu installé : à amorcer conservé au redémarrage", true, Suivi.Lire(Suivi.Ecrire(inst))[0].RemiseNotifiee == Suivi.AAmorcer);
            Banc.Verifie("jeu installé déjà en promo : amorcé sans alerte", true, Suivi.Decide(inst, new List<Offre> { cs(50) }, aucune).Count == 0 && inst[0].RemiseNotifiee == 50);
            Banc.Verifie("jeu installé : promo plus forte ensuite, une alerte", true, Suivi.Decide(inst, new List<Offre> { cs(75) }, aucune).Count == 1);
            var g = new List<Offre> { new Offre { Source = "epic", Id = "1f9a", Nom = "Final Fantasy VII: Rebirth", PrixCts = 0, Remise = 100, Plateformes = new[] { "Epic Games" }, Fin = new DateTime(2026, 11, 26, 16, 0, 0, DateTimeKind.Utc), Url = "https://store.epicgames.com/fr/p/x" } };
            var ag = Suivi.Decide(relus, aucune, g);
            Banc.Verifie("gratuit ailleurs : alerte", true, ag.Count == 1 && ag[0].Titre == "FINAL FANTASY VII REBIRTH est gratuit" && ag[0].Texte.StartsWith("Sur Epic Games jusqu'au 26/11"));
            Banc.Verifie("gratuit : pas de doublon", true, Suivi.Decide(relus, aucune, g).Count == 0);
            Banc.Verifie("jeu gratuit non suivi : rien", true, Suivi.Decide(relus, aucune, new List<Offre> { new Offre { Source = "epic", Id = "z", Nom = "Autre", PrixCts = 0 } }).Count == 0);
            var plein = new List<JeuSuivi>(); for (int i = 1; i <= Suivi.Max; i++) plein.Add(new JeuSuivi { AppId = i, Nom = "J" + i });
            Banc.Verifie("50 jeux au plus", true, !Suivi.Ajoute(plein, o));
            Banc.Verifie("retirer", true, Suivi.Retire(plein, 3) && plein.Count == 49 && !Suivi.Retire(plein, 3));
            var cinq = new List<Suivi.Alerte>(); for (int i = 0; i < 5; i++) cinq.Add(new Suivi.Alerte { Titre = "T" + i });
            var r3 = Suivi.Regroupe(cinq, 3);
            Banc.Verifie("regroupement", true, r3.Count == 3 && r3[1].Titre == "T1" && r3[2].Titre == "3 autres jeux suivis en promo" && Suivi.Regroupe(cinq, 5).Count == 5);
            Banc.Verifie("suivis abîmés", true, Suivi.Lire("abc\t0\t\tX\n12\n730\t10\t\tCounter-Strike 2\n").Count == 1);
        }
    }
}
