namespace BTOptimizer.Tests
{
    /// <summary>Grille des cartes d'Optimisations : colonnes, positions, rangées, cartes visibles.</summary>
    internal static class GrilleCartesTests
    {
        internal static void Tout()
        {
            Banc.Titre("Grille des cartes d'Optimisations (liste virtualisée)");
            Banc.Egal("300 px : 1 colonne", "1", GrilleCartes.Colonnes(300).ToString());
            Banc.Egal("1300 px : 3 colonnes", "3", GrilleCartes.Colonnes(1300).ToString());
            Banc.Egal("0 px : 1 colonne", "1", GrilleCartes.Colonnes(0).ToString());
            Banc.Egal("736 px : cartes de 352 px", "352", GrilleCartes.LargeurCarte(736).ToString());
            int total;
            var c = GrilleCartes.Place(736, new[] { 100, 140, 90, 90, 120 }, out total);   // 2 colonnes de 352
            Banc.Egal("carte 1 à droite", "368", c[1].X.ToString());
            Banc.Egal("rangée 2 sous la plus haute de la rangée 1", "156", c[2].Y.ToString());
            Banc.Egal("rangée 3", "262", c[4].Y.ToString());
            Banc.Egal("hauteur totale (écart final compris)", "398", total.ToString());
            Banc.Egal("largeur des cases", "352", c[3].W.ToString());
            Banc.Egal("hauteur propre de chaque carte", "90", c[2].H.ToString());
            int a, b;
            GrilleCartes.Visibles(c, 150, 100, out a, out b);
            Banc.Verifie("vue 150–250 : rangée 2 seule", true, a == 2 && b == 3);
            GrilleCartes.Visibles(c, 0, 1000, out a, out b);
            Banc.Verifie("grande vue : toutes les cartes", true, a == 0 && b == 4);
            GrilleCartes.Visibles(new GrilleCartes.Case[0], 0, 500, out a, out b);
            Banc.Verifie("liste vide : aucune carte", true, a == 0 && b == -1);
            GrilleCartes.Visibles(c, 5000, 100, out a, out b);
            Banc.Verifie("défilement au-delà de la fin : aucune carte", true, b < a);
            Banc.Egal("clic dans l'écart entre deux cartes", "-1", GrilleCartes.IndexA(c, 360, 20).ToString());
            Banc.Egal("clic dans la carte 3", "3", GrilleCartes.IndexA(c, 400, 160).ToString());
            Banc.Egal("clic sous une carte plus basse que sa rangée", "-1", GrilleCartes.IndexA(c, 10, 120).ToString());
            var vide = GrilleCartes.Place(736, new int[0], out total);
            Banc.Verifie("aucune carte : hauteur 0", true, vide.Length == 0 && total == 0);
        }
    }
}
