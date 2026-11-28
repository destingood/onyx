using BTOptimizer;

namespace BTOptimizer.Tests
{
    /// <summary>Page sur laquelle ONYX s'ouvre.</summary>
    internal static class PageDemarrageTests
    {
        public static void Tout()
        {
            Banc.Titre("Page d'ouverture au choix");
            Banc.Egal("sans fichier : Centre de contrôle", "0", PageDemarrage.Lit(null).ToString());
            Banc.Egal("page choisie relue", "2", PageDemarrage.Lit("2\r\n").ToString());
            Banc.Egal("dernière page", "10", PageDemarrage.Lit("10").ToString());
            Banc.Egal("hors bornes : Centre de contrôle", "0", PageDemarrage.Lit("11").ToString());
            Banc.Egal("négatif ou texte : Centre de contrôle", "0|0", PageDemarrage.Lit("-1") + "|" + PageDemarrage.Lit("jeux"));
            Banc.Egal("écriture", "9", PageDemarrage.Ecrit(9));
            Banc.Egal("écriture hors bornes", "0", PageDemarrage.Ecrit(42));
            Banc.Egal("aller-retour", "7", PageDemarrage.Lit(PageDemarrage.Ecrit(7)).ToString());
            Banc.Egal("la page 2 est « Mes jeux »", "Mes jeux", PageDemarrage.Noms[2]);

            string src = CatalogueTests.Source("DashboardForm.cs");
            Banc.Verifie("autant de noms que de pages", true, src.Contains("new FpsPage[" + PageDemarrage.Noms.Length + "]"));
            Banc.Verifie("la barre de gauche prend ses noms ici", true, src.Contains("string[] tips = PageDemarrage.Noms;"));
            Banc.Verifie("ouverture : la page choisie", true, src.Contains("ShowPage(PageOuverture)"));
            Banc.Verifie("tests d'interface et captures : toujours le Centre de contrôle", true,
                src.Contains("BT_UITEST\") == \"1\" || Environment.GetEnvironmentVariable(\"BT_UISHOT\") != null) return 0;"));
        }
    }
}
