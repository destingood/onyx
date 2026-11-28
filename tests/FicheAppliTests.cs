using BTOptimizer;

namespace BTOptimizer.Tests
{
    /// <summary>Applis de jeu : fiche (winget show) et désinstallation.</summary>
    internal static class FicheAppliTests
    {
        public static void Tout()
        {
            Banc.Titre("Applis de jeu : fiche et désinstallation");
            Banc.Verifie("identifiant winget", true, FicheAppli.IdValide("Discord.Discord"));
            Banc.Verifie("identifiant avec + et -", true, FicheAppli.IdValide("Microsoft.VCRedist.2015+.x64") && FicheAppli.IdValide("CPUID.CPU-Z"));
            Banc.Verifie("espace refusé", false, FicheAppli.IdValide("Discord Discord"));
            Banc.Verifie("option glissée refusée", false, FicheAppli.IdValide("--source"));
            Banc.Verifie("guillemet et esperluette refusés", false, FicheAppli.IdValide("a\"b") || FicheAppli.IdValide("a&calc"));
            Banc.Verifie("vide refusé", false, FicheAppli.IdValide("") || FicheAppli.IdValide(null));
            Banc.Egal("commande de la fiche", "show --id Discord.Discord -e --source winget --accept-source-agreements --disable-interactivity", FicheAppli.ArgsFiche("Discord.Discord"));
            Banc.Egal("commande de désinstallation", "uninstall --id Discord.Discord -e --silent --accept-source-agreements --disable-interactivity", FicheAppli.ArgsDesinstalle("Discord.Discord"));
            bool leve = false;
            try { FicheAppli.ArgsDesinstalle("x y"); } catch (System.ArgumentException) { leve = true; }
            Banc.Verifie("identifiant invalide : aucune commande", true, leve);

            Banc.Verifie("appli installée : désinstallable", true, FicheAppli.PeutDesinstaller(false, true));
            Banc.Verifie("bibliothèque essentielle : jamais", false, FicheAppli.PeutDesinstaller(true, true));
            Banc.Verifie("absente : rien à désinstaller", false, FicheAppli.PeutDesinstaller(false, false));
            Banc.Verifie("refus expliqué (essentielle)", true, FicheAppli.Refus(true, true).Contains("des jeux en dépendent"));
            Banc.Verifie("refus expliqué (absente)", true, FicheAppli.Refus(false, false).Contains("pas installé"));
            Banc.Verifie("aucun refus pour une appli installée", true, FicheAppli.Refus(false, true) == null);

            string sortie = "   - \r\n   \\ \r\n  ██████▒▒▒  50%\r\nFound Discord [Discord.Discord]\r\nVersion: 1.0.9270\r\nPublisher: Discord Inc.\r\nDescription:\r\n"
                + "  Discord is the easiest way to talk.\r\nHomepage: https://discord.com/download\r\nLicense: Proprietary\r\nTags:\r\n  chat\r\n  gaming\r\n"
                + "Installer:\r\n  Installer Type: exe\r\n  Installer Url: https://exemple.invalid/DiscordSetup.exe\r\n  Installer SHA256: 0d03\r\n  Release Date: 2026-11-23\r\n";
            string f = FicheAppli.Nettoie(sortie, "Discord.Discord");
            Banc.Verifie("fiche : commence au paquet", true, f.StartsWith("Found Discord [Discord.Discord]"));
            Banc.Verifie("fiche : version, éditeur, description", true, f.Contains("Version: 1.0.9270") && f.Contains("Publisher: Discord Inc.") && f.Contains("  Discord is the easiest way to talk."));
            Banc.Verifie("fiche : sans mots-clés", false, f.Contains("gaming") || f.Contains("Tags"));
            Banc.Verifie("fiche : sans adresse ni empreinte de l'installeur", false, f.Contains("Installer Url") || f.Contains("SHA256") || f.Contains("Installer Type"));
            Banc.Verifie("fiche : date de publication gardée", true, f.EndsWith("Release Date: 2026-11-23"));
            Banc.Verifie("fiche : sans roue ni barre d'attente", false, f.Contains("█") || f.Contains("\\"));
            Banc.Egal("fiche : sortie vide", "", FicheAppli.Nettoie(null, "X.Y"));
            Banc.Egal("fiche : paquet absent de la sortie, texte gardé", "No package found.", FicheAppli.Nettoie("No package found.\r\n", "X.Y"));

            string src = CatalogueTests.Source("LibsForm.cs");
            Banc.Verifie("désinstallation : confirmation avant winget", true,
                src.IndexOf("Désinstaller « ", System.StringComparison.Ordinal) > 0
                && src.IndexOf("Désinstaller « ", System.StringComparison.Ordinal) < src.IndexOf("FicheAppli.ArgsDesinstalle(", System.StringComparison.Ordinal));
            Banc.Verifie("désinstallation : bibliothèques essentielles protégées", true, src.Contains("FicheAppli.Refus(it.Essential, here)"));
            Banc.Verifie("fiche : lue par winget show", true, src.Contains("FicheAppli.ArgsFiche(it.WingetId)"));
        }
    }
}
