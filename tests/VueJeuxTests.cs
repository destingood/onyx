using System;
using System.Collections.Generic;

namespace BTOptimizer.Tests
{
    /// <summary>Affichage de la bibliothèque : choix gardé, tranches de dernière partie, regroupements.</summary>
    internal static class VueJeuxTests
    {
        private static string Montre(List<VueJeux.Bloc> blocs)
        {
            var l = new List<string>();
            foreach (VueJeux.Bloc b in blocs) l.Add(b.Titre + "=" + string.Join(",", b.Jeux));
            return string.Join(" | ", l);
        }

        public static void Tout()
        {
            Banc.Titre("Bibliothèque — grille ou liste");
            Banc.Verifie("par défaut, la grille", false, VueJeux.Lit("").Liste);
            Banc.Verifie("« liste » est relu", true, VueJeux.Lit("liste;aucun").Liste);
            Banc.Egal("le regroupement est relu", "Etiquette", VueJeux.Lit("LISTE;etiquette").Par.ToString());
            Banc.Egal("un regroupement inconnu vaut « aucun »", "Aucun", VueJeux.Lit("liste;couleur").Par.ToString());
            Banc.Egal("aller-retour du choix", "liste;derniere", VueJeux.Ecrit(VueJeux.Lit("liste;derniere")));
            Banc.Egal("choix absent écrit en grille", "grille;aucun", VueJeux.Ecrit(null));
            Banc.Egal("bouton en grille", "Grille", VueJeux.Bouton(VueJeux.Lit("grille;plateforme")));
            Banc.Egal("bouton en liste regroupée", "Liste · Plateforme", VueJeux.Bouton(VueJeux.Lit("liste;plateforme")));
            Banc.Egal("bouton en liste simple", "Liste", VueJeux.Bouton(VueJeux.Lit("liste")));

            Banc.Titre("Bibliothèque — tranches de dernière partie");
            var now = new DateTime(2026, 11, 27, 22, 0, 0);
            Banc.Egal("aujourd'hui", "0", VueJeux.Tranche(now.AddHours(-20), now).ToString());
            Banc.Egal("hier compte dans les 7 jours", "1", VueJeux.Tranche(now.AddDays(-1), now).ToString());
            Banc.Egal("il y a 6 jours : 7 derniers jours", "1", VueJeux.Tranche(now.AddDays(-6), now).ToString());
            Banc.Egal("il y a 7 jours : 30 derniers jours", "2", VueJeux.Tranche(now.AddDays(-7), now).ToString());
            Banc.Egal("il y a 30 jours : plus ancien", "3", VueJeux.Tranche(now.AddDays(-30), now).ToString());
            Banc.Egal("jamais joué", "4", VueJeux.Tranche(null, now).ToString());

            Banc.Titre("Bibliothèque — regroupements");
            var jeux = new List<VueJeux.Jeu>
            {
                new VueJeux.Jeu { Plateforme = "STEAM", Etiquettes = new List<string> { "coop", "à finir" }, Derniere = now.AddDays(-2) },
                new VueJeux.Jeu { Plateforme = "EPIC", Etiquettes = new List<string>(), Derniere = now },
                new VueJeux.Jeu { Plateforme = "steam", Etiquettes = new List<string> { "Coop", "coop" }, Derniere = null },
                new VueJeux.Jeu { Plateforme = "", Etiquettes = null, Derniere = now.AddDays(-90) }
            };
            Banc.Egal("sans regroupement : un bloc sans titre, ordre gardé", "=0,1,2,3", Montre(VueJeux.Range(jeux, VueJeux.Groupe.Aucun, now)));
            Banc.Egal("par plateforme : ordre alphabétique, casse ignorée, « Autres » pour l'inconnue",
                "Autres  ·  1=3 | EPIC  ·  1=1 | STEAM  ·  2=0,2", Montre(VueJeux.Range(jeux, VueJeux.Groupe.Plateforme, now)));
            Banc.Egal("par étiquette : un jeu sous chacune, sans doublon, « Sans étiquette » à la fin",
                "# à finir  ·  1=0 | # coop  ·  2=0,2 | Sans étiquette  ·  2=1,3", Montre(VueJeux.Range(jeux, VueJeux.Groupe.Etiquette, now)));
            Banc.Egal("par dernière partie : de la plus récente à jamais, tranches vides omises",
                "Aujourd'hui  ·  1=1 | 7 derniers jours  ·  1=0 | Plus ancien  ·  1=3 | Jamais lancé avec ONYX ouvert  ·  1=2",
                Montre(VueJeux.Range(jeux, VueJeux.Groupe.DernierePartie, now)));
            jeux[0].Genre = "RPG"; jeux[2].Genre = "FPS"; jeux[3].Genre = "RPG";
            jeux[0].Editeur = "CD PROJEKT RED"; jeux[2].Editeur = "Valve";
            Banc.Egal("par genre, inconnus à la fin", "FPS  ·  1=2 | RPG  ·  2=0,3 | Genre inconnu  ·  1=1",
                Montre(VueJeux.Range(jeux, VueJeux.Groupe.Genre, now)));
            Banc.Egal("par éditeur", "CD PROJEKT RED  ·  1=0 | Valve  ·  1=2 | Éditeur inconnu  ·  2=1,3",
                Montre(VueJeux.Range(jeux, VueJeux.Groupe.Editeur, now)));
            Banc.Egal("bouton par genre", "Liste · Genre", VueJeux.Bouton(VueJeux.Lit("liste;genre")));
            Banc.Egal("liste vide : aucun bloc", "0", VueJeux.Range(new List<VueJeux.Jeu>(), VueJeux.Groupe.Plateforme, now).Count.ToString());
            Banc.Egal("liste absente : aucun bloc", "0", VueJeux.Range(null, VueJeux.Groupe.Aucun, now).Count.ToString());
        }
    }
}
