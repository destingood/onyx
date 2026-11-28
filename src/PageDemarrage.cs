using System.Globalization;

namespace BTOptimizer
{
    /// <summary>
    /// Page sur laquelle ONYX s'ouvre (pur). Par défaut le Centre de contrôle ; l'utilisateur peut
    /// choisir une autre page dans Outils avancés &gt; Système &gt; ONYX. Le fichier garde le numéro
    /// de la page ; un fichier absent, illisible ou hors bornes rend le Centre de contrôle.
    /// </summary>
    internal static class PageDemarrage
    {
        /// <summary>Les pages du menu de gauche, dans l'ordre de la barre.</summary>
        public static readonly string[] Noms =
        {
            "Centre de contrôle", "Optimiser Windows", "Mes jeux", "Santé & entretien", "Performances", "Mes badges",
            "Assistant PC", "Matériel en direct", "Nettoyage & disques", "Bons plans", "Actualités"
        };

        public static int Lit(string texte)
        {
            int v;
            if (texte == null || !int.TryParse(texte.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out v)) return 0;
            return v < Noms.Length ? v : 0;
        }

        public static string Ecrit(int page)
        {
            return (page >= 0 && page < Noms.Length ? page : 0).ToString(CultureInfo.InvariantCulture);
        }
    }
}
