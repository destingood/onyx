
namespace BTOptimizer.Tests
{
    /// <summary>
    /// Image de la présence Discord. La 15.87 envoyait l'adresse de l'icône de l'application
    /// (256 px) : Discord ne l'a pas affichée, la page blanche est restée. La grande image est
    /// désormais la ressource « logo » (1024 × 1024) déposée dans le portail développeur, Rich
    /// Presence → Art Assets : la voie documentée par Discord.
    /// </summary>
    internal static class DiscordImageTests
    {
        public static void Tout()
        {
            string a = DiscordPresence.Assets();
            Banc.Verifie("grande image = ressource « logo » du portail Discord", true,
                a.Contains("\"large_image\":\"logo\""));
            Banc.Verifie("plus d'adresse d'icône de 256 px (non affichée par Discord)", false,
                a.Contains("\"large_image\":\"https://"));
            Banc.Verifie("texte au survol gardé", true, a.Contains("\"large_text\":\"ONYX"));
        }
    }
}
