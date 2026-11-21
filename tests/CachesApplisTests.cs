
namespace BTOptimizer.Tests
{
    /// <summary>
    /// Caches d'applications ouvertes. Vider %APPDATA%\discord\Cache pendant que Discord tourne
    /// supprimait les fichiers libres mais laissait l'index verrouillé : Discord affichait ensuite
    /// des icônes d'image cassée (décorations, plaques, bannières) jusqu'à son redémarrage. Même
    /// risque pour les navigateurs et le launcher Epic. Un cache n'est vidé qu'appli fermée.
    /// </summary>
    internal static class CachesApplisTests
    {
        public static void Tout()
        {
            Banc.Titre("Caches d'applications ouvertes");
            string[] ouverts = { "explorer", "Discord", "svchost" };

            Banc.Egal("Discord ouvert : son cache attend", "Discord",
                CachesApplis.Ouvert(CachesApplis.Discord, ouverts));
            Banc.Egal("nom de processus sans tenir compte de la casse", "discord",
                CachesApplis.Ouvert(CachesApplis.Discord, new[] { "discord" }));
            Banc.Egal("Discord fermé : le cache peut être vidé", null,
                CachesApplis.Ouvert(CachesApplis.Discord, new[] { "explorer", "chrome" }));
            Banc.Egal("nom exact, pas un préfixe", null,
                CachesApplis.Ouvert(CachesApplis.Discord, new[] { "DiscordHelperTiers" }));
            Banc.Egal("cible sans propriétaire : jamais bloquée", null,
                CachesApplis.Ouvert(null, ouverts));
            Banc.Egal("Chrome ouvert : son cache attend", "chrome",
                CachesApplis.Ouvert(CachesApplis.Chrome, new[] { "chrome" }));
            Banc.Egal("Edge ouvert : son cache attend", "msedge",
                CachesApplis.Ouvert(CachesApplis.Edge, new[] { "msedge" }));
            Banc.Egal("Firefox ouvert : son cache attend", "firefox",
                CachesApplis.Ouvert(CachesApplis.Firefox, new[] { "firefox" }));

            // Règles winapp2 : le nom de l'entrée dit à quelle appli appartient le cache.
            Banc.Verifie("winapp2 « Discord * » : propriétaire Discord", true,
                CachesApplis.PourEntreeWinapp2("Discord *") == CachesApplis.Discord);
            Banc.Verifie("winapp2 « Google Chrome * » : propriétaire Chrome", true,
                CachesApplis.PourEntreeWinapp2("Google Chrome *") == CachesApplis.Chrome);
            Banc.Verifie("winapp2 « Microsoft Edge * » : propriétaire Edge", true,
                CachesApplis.PourEntreeWinapp2("Microsoft Edge *") == CachesApplis.Edge);
            Banc.Verifie("winapp2 « Mozilla Firefox * » : propriétaire Firefox", true,
                CachesApplis.PourEntreeWinapp2("Mozilla Firefox *") == CachesApplis.Firefox);
            Banc.Verifie("winapp2 entrée sans appli connue : aucun propriétaire", true,
                CachesApplis.PourEntreeWinapp2("7-Zip *") == null);

            string m = CachesApplis.MessageIgnore("Cache Discord", "Discord");
            Banc.Verifie("le message nomme le cache", true, m.Contains("Cache Discord"));
            Banc.Verifie("le message dit quoi faire (fermer l'appli)", true, m.Contains("Ferme Discord"));
        }
    }
}
