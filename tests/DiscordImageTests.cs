namespace BTOptimizer.Tests
{
    /// <summary>
    /// Image de la présence Discord. L'application Discord d'ONYX n'a aucune ressource « logo »
    /// (Art Assets vide) : demander « logo » donnait une image blanche chez les amis. La grande
    /// image est donc une adresse https (Discord ne résout pas les adresses http), l'icône de
    /// l'application ONYX servie par Discord lui-même.
    /// </summary>
    internal static class DiscordImageTests
    {
        public static void Tout()
        {
            string a = DiscordPresence.Assets();
            Banc.Verifie("grande image = adresse https, pas un nom de ressource absent", true,
                a.Contains("\"large_image\":\"https://"));
            Banc.Verifie("plus de ressource « logo » (inexistante dans l'application ONYX)", false,
                a.Contains("\"large_image\":\"logo\""));
            Banc.Verifie("icône de l'application ONYX, servie par Discord", true,
                DiscordPresence.ImageLogo.StartsWith("https://cdn.discordapp.com/app-icons/" + DiscordPresence.AppIdParDefaut + "/"));
            Banc.Verifie("texte au survol gardé", true, a.Contains("\"large_text\":\"ONYX"));
        }
    }
}
