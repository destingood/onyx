namespace BTOptimizer.Tests
{
    /// <summary>Vérifie <see cref="ResidusJeuxRegles"/> : seulement des dossiers d'Unreal Engine, seulement journaux et rapports de plantage.</summary>
    internal static class ResidusJeuxTests
    {
        public static void Tout()
        {
            Banc.Titre("Résidus des jeux");
            Banc.Verifie("Saved\\Config\\Windows : Unreal", true, ResidusJeuxRegles.EstUnreal(new[] { "Windows" }));
            Banc.Verifie("Saved\\Config\\WindowsNoEditor : Unreal", true, ResidusJeuxRegles.EstUnreal(new[] { "CrashReportClient", "WindowsNoEditor" }));
            Banc.Verifie("autre forme : ignoré", false, ResidusJeuxRegles.EstUnreal(new[] { "Profiles", "Settings" }));
            Banc.Verifie("aucun sous-dossier : ignoré", false, ResidusJeuxRegles.EstUnreal(null));
            Banc.Verifie("éditeur (UEFN) : ignoré", false, ResidusJeuxRegles.EstUnreal(new[] { "CrashReportClient", "WindowsEditor" }) || ResidusJeuxRegles.RetenuParNom("UnrealEditorFortnite"));
            Banc.Verifie("lanceur Epic retenu par son nom", true, ResidusJeuxRegles.RetenuParNom("EpicGamesLauncher"));
            Banc.Egal("seulement journaux et rapports de plantage", "Logs|Crashes", string.Join("|", ResidusJeuxRegles.SousDossiers));
            Banc.Egal("nom connu", "Fortnite", ResidusJeuxRegles.NomJeu("FortniteGame"));
            Banc.Egal("suffixe Game retiré", "ShooterX", ResidusJeuxRegles.NomJeu("ShooterXGame"));
            Banc.Egal("nom court gardé", "Game", ResidusJeuxRegles.NomJeu("Game"));
            Banc.Egal("libellé", "Fortnite — rapports de plantage", ResidusJeuxRegles.Libelle("Fortnite", "Crashes"));
            Banc.Egal("libellé des journaux", "Fortnite — journaux", ResidusJeuxRegles.Libelle("Fortnite", "Logs"));
        }
    }
}
