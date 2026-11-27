using System.Collections.Generic;

namespace BTOptimizer.Tests
{
    /// <summary>Vérifie <see cref="Manette"/> : directions, fronts montants, répétition, déplacement dans la grille.</summary>
    internal static class ManetteTests
    {
        private static string S(List<Manette.Geste> l) { return string.Join(",", l); }

        public static void Tout()
        {
            Banc.Titre("Mode manette");
            Banc.Verifie("croix prioritaire sur le stick", true, Manette.Direction(Manette.CroixGauche, 30000, 0) == Manette.Geste.Gauche);
            Banc.Verifie("stick : axe dominant, Y positif = haut", true,
                Manette.Direction(0, 20000, 25000) == Manette.Geste.Haut && Manette.Direction(0, -30000, 9000) == Manette.Geste.Gauche
                && Manette.Direction(0, 0, -20000) == Manette.Geste.Bas);
            Banc.Verifie("stick sous mi-course : rien", true, Manette.Direction(0, 15000, -15000) == Manette.Geste.Aucun);

            var e = new Manette.Etat();
            Banc.Egal("premier relevé avec A déjà enfoncé : rien", "", S(Manette.Avance(e, Manette.BoutonA, 0, 0, 0)));
            Banc.Egal("A maintenu : rien", "", S(Manette.Avance(e, Manette.BoutonA, 0, 0, 30)));
            Manette.Avance(e, 0, 0, 0, 60);
            Banc.Egal("A pressé de nouveau : Valider", "Valider", S(Manette.Avance(e, Manette.BoutonA, 0, 0, 90)));
            Banc.Egal("B, LB et RB ensemble", "Retour,Precedent,Suivant",
                S(Manette.Avance(e, (ushort)(Manette.BoutonA | Manette.BoutonB | Manette.BoutonLB | Manette.BoutonRB), 0, 0, 120)));

            Manette.Avance(e, 0, 0, 0, 150);
            Banc.Egal("bouton Menu : mode console", "Menu", S(Manette.Avance(e, Manette.BoutonMenu, 0, 0, 180)));
            Banc.Egal("bouton Menu maintenu : une seule bascule", "", S(Manette.Avance(e, Manette.BoutonMenu, 0, 0, 900)));
            Banc.Verifie("mode console branché (page, Échap, F11, fermeture)", true,
                CatalogueTests.Source("PageGames.cs").Contains("Geste.Menu) { Host.BasculeConsole()")
                && CatalogueTests.Source("DashboardForm.cs").Contains("keyData == Keys.F11 || (keyData == Keys.Escape && _console")
                && CatalogueTests.Source("DashboardForm.cs").Contains("if (_console) BasculeConsole(); } catch { } try { WindowBounds.Save"));

            e = new Manette.Etat();
            Manette.Avance(e, 0, 0, 0, 0);
            Banc.Egal("droite pressée : un pas", "Droite", S(Manette.Avance(e, Manette.CroixDroite, 0, 0, 100)));
            Banc.Egal("maintenue 300 ms : pas de répétition", "", S(Manette.Avance(e, Manette.CroixDroite, 0, 0, 400)));
            Banc.Egal("maintenue 400 ms : répétition", "Droite", S(Manette.Avance(e, Manette.CroixDroite, 0, 0, 500)));
            Banc.Egal("puis toutes les 120 ms", "|Droite", S(Manette.Avance(e, Manette.CroixDroite, 0, 0, 600)) + "|" + S(Manette.Avance(e, Manette.CroixDroite, 0, 0, 620)));
            Banc.Egal("relâchée puis stick vers le bas", "Bas", S(Manette.Avance(e, 0, 0, -32000, 700)));

            e = new Manette.Etat();
            Manette.Avance(e, Manette.CroixHaut, 0, 0, 0);
            Banc.Egal("direction déjà tenue au premier relevé : jamais répétée", "", S(Manette.Avance(e, Manette.CroixHaut, 0, 0, 5000)));

            // Grille de 10 jeux sur 4 colonnes : lignes 0-3, 4-7, 8-9.
            Banc.Verifie("rien de choisi : première case", true, Manette.Deplace(-1, 10, 4, Manette.Geste.Bas) == 0);
            Banc.Verifie("bords gauche et droit arrêtent", true,
                Manette.Deplace(4, 10, 4, Manette.Geste.Gauche) == 4 && Manette.Deplace(3, 10, 4, Manette.Geste.Droite) == 3 && Manette.Deplace(9, 10, 4, Manette.Geste.Droite) == 9);
            Banc.Verifie("haut et bas d'une ligne", true, Manette.Deplace(5, 10, 4, Manette.Geste.Haut) == 1 && Manette.Deplace(1, 10, 4, Manette.Geste.Bas) == 5 && Manette.Deplace(1, 10, 4, Manette.Geste.Haut) == 1);
            Banc.Verifie("bas vers une dernière ligne incomplète : sa dernière case", true, Manette.Deplace(7, 10, 4, Manette.Geste.Bas) == 9);
            Banc.Verifie("bas depuis la dernière ligne : on reste", true, Manette.Deplace(8, 10, 4, Manette.Geste.Bas) == 8);
            Banc.Verifie("grille vide", true, Manette.Deplace(0, 0, 4, Manette.Geste.Bas) == -1);
        }
    }
}
