namespace BTOptimizer.Tests
{
    /// <summary>Veille de jeu : reconnaissance d'un jeu connu dans un relevé de processus.</summary>
    internal static class VeilleJeuTests
    {
        internal static void Tout()
        {
            Banc.Titre("Veille de jeu (un relevé de processus par tick)");
            Banc.Egal("jeu connu trouvé", "cs2", VeilleJeu.JeuConnuParmi(new[] { "explorer", "cs2" }));
            Banc.Egal("casse ignorée", "CS2", VeilleJeu.JeuConnuParmi(new[] { "CS2" }));
            Banc.Verifie("aucun jeu", true, VeilleJeu.JeuConnuParmi(new[] { "explorer", "chrome" }) == null);
            Banc.Verifie("liste vide", true, VeilleJeu.JeuConnuParmi(new string[0]) == null);
            Banc.Verifie("liste nulle", true, VeilleJeu.JeuConnuParmi(null) == null);
            Banc.Verifie("nom nul ignoré", true, VeilleJeu.JeuConnuParmi(new string[] { null, "explorer" }) == null);
        }
    }
}
