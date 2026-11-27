using System.Collections.Generic;
using BTOptimizer;

namespace BTOptimizer.Tests
{
    /// <summary>Mes serveurs Discord : liens d'invitation collés par l'utilisateur.</summary>
    internal static class ServeursDiscordTests
    {
        public static void Tout()
        {
            Banc.Titre("Mes serveurs Discord");
            Banc.Egal("lien court", "fortnite", ServeursDiscord.Code("https://discord.gg/fortnite"));
            Banc.Egal("lien long, paramètres retirés", "Ab-12cD", ServeursDiscord.Code("  https://www.discord.com/invite/Ab-12cD?event=1 "));
            Banc.Egal("sans https", "abc123", ServeursDiscord.Code("discord.gg/abc123/"));
            Banc.Egal("ancien domaine", "abc123", ServeursDiscord.Code("http://discordapp.com/invite/abc123"));
            Banc.Egal("code seul", "abc123", ServeursDiscord.Code("abc123"));
            Banc.Verifie("autre site refusé", true,
                ServeursDiscord.Code("https://evil.com/discord.gg/abc") == null && ServeursDiscord.Code("discord.gg.evil.com/abc") == null
                && ServeursDiscord.Code("https://discord.com/channels/1/2") == null);
            Banc.Verifie("code invalide refusé", true,
                ServeursDiscord.Code("discord.gg/") == null && ServeursDiscord.Code("a b") == null && ServeursDiscord.Code("x") == null
                && ServeursDiscord.Code("discord.gg/" + new string('a', 33)) == null && ServeursDiscord.Code("abc;calc") == null);
            Banc.Verifie("lien ouvert toujours sur discord.gg", true, ServeursDiscord.Lien("abc") == "https://discord.gg/abc");

            var l = new List<ServeursDiscord.Serveur>();
            string err;
            Banc.Verifie("ajout avec nom", true, ServeursDiscord.Ajoute(l, "  Mon\tclan  ", "discord.gg/Clan42", out err) && l[0].Nom == "Mon clan");
            Banc.Verifie("ajout sans nom : le code sert de nom", true, ServeursDiscord.Ajoute(l, "", "discord.gg/abc123", out err) && l[1].Nom == "abc123");
            Banc.Verifie("doublon refusé", false, ServeursDiscord.Ajoute(l, "x", "https://discord.com/invite/Clan42", out err));
            Banc.Verifie("même code, autre casse : serveur différent", true, ServeursDiscord.Ajoute(l, "", "discord.gg/clan42", out err));
            Banc.Verifie("lien invalide : raison donnée", true, !ServeursDiscord.Ajoute(l, "", "bonjour !", out err) && err.Contains("discord.gg"));
            Banc.Egal("nom borné", ServeursDiscord.NomMax.ToString(), ServeursDiscord.NomPropre(new string('n', 200), "c").Length.ToString());

            List<ServeursDiscord.Serveur> relu = ServeursDiscord.Lire(ServeursDiscord.Ecrire(l) + "ligne pourrie\tx\r\n\r\nabc123\tdoublon\r\n");
            Banc.Egal("aller-retour fichier, lignes invalides et doublons écartés", "Clan42:Mon clan|abc123:abc123|clan42:clan42",
                string.Join("|", relu.ConvertAll(s => s.Code + ":" + s.Nom)));

            var pleine = new List<ServeursDiscord.Serveur>();
            for (int i = 0; i < ServeursDiscord.Max; i++) ServeursDiscord.Ajoute(pleine, "", "code" + i, out err);
            Banc.Verifie("liste plafonnée", true, !ServeursDiscord.Ajoute(pleine, "", "encore1", out err) && pleine.Count == ServeursDiscord.Max);
            Banc.Verifie("branché dans le menu Système", true, CatalogueTests.Source("DashboardForm.cs").Contains("new ServeursDiscordForm()"));
        }
    }
}
