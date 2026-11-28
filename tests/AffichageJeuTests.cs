using System.Collections.Generic;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Vérifie <see cref="AffichageJeu"/> et le choix de résolution et de mode d'affichage dans les
    /// préréglages Fortnite et Black Ops 6 : seulement des valeurs que le fichier accepte, jamais de clé ajoutée.
    /// </summary>
    internal static class AffichageJeuTests
    {
        private const string Ini =
            "[/Script/FortniteGame.FortGameUserSettings]\r\n" +
            "ResolutionSizeX=1920\r\n" +
            "ResolutionSizeY=1080\r\n" +
            "LastUserConfirmedResolutionSizeX=1920\r\n" +
            "LastUserConfirmedResolutionSizeY=1080\r\n" +
            "FullscreenMode=1\r\n" +
            "LastConfirmedFullscreenMode=1\r\n" +
            "PreferredFullscreenMode=1\r\n" +
            "DisplayID=\"MONITOR\\\\SAM793E\\\\{4d36e96e-e325-11ce-bfc1-08002be10318}\\\\0002\"\r\n";

        private const string Cod =
            "Resolution@0;1;2 = 1920x1080\r\n" +
            "DisplayMode@0;1;2 = Fullscreen borderless window // one of Windowed, Fullscreen, Fullscreen borderless window\r\n" +
            "PreferredDisplayMode@0;1;2 = Fullscreen // one of Fullscreen, Fullscreen borderless window\r\n" +
            "Monitor@0;1;2 = DELL S2721DGF\r\n";

        private static string Fn(List<ReglagesFortnite.Changement> c) { return string.Join("|", c.ConvertAll(x => x.Cle + " " + x.Avant + ">" + x.Apres)); }
        private static string Cd(List<ReglagesCod.Changement> c) { return string.Join("|", c.ConvertAll(x => x.Cle + " " + x.Avant + ">" + x.Apres)); }

        public static void Tout()
        {
            Banc.Titre("Résolution et mode d'affichage");

            int l, h;
            Banc.Verifie("format 2560x1440 lu", true, AffichageJeu.Lit("2560x1440", out l, out h) && l == 2560 && h == 1440);
            Banc.Verifie("format inattendu refusé", false, AffichageJeu.Lit("2560 x 1440", out l, out h) || AffichageJeu.Lit("x1440", out l, out h));
            Banc.Egal("texte affiché", "2560 × 1440", AffichageJeu.Texte("2560x1440"));
            Banc.Egal("tri : sans doublon, au moins 800 × 600, de la plus grande à la plus petite", "2560x1440|1920x1080|1280x720|800x600",
                string.Join("|", AffichageJeu.Trie(new[] { "1280x720", "640x480", "2560x1440", "1920x1080", "1920x1080", "800x600", "abc" })));
            var a = new HashSet<string> { "2560x1440", "1920x1080", "1280x720" };
            var b = new HashSet<string> { "1920x1080", "1280x720", "1024x768" };
            Banc.Egal("plusieurs écrans : seulement les résolutions communes", "1920x1080|1280x720",
                string.Join("|", AffichageJeu.Trie(AffichageJeu.Communes(new List<HashSet<string>> { a, b }))));
            Banc.Verifie("aucun écran : liste vide", true, AffichageJeu.Communes(new List<HashSet<string>>()).Count == 0);
            Banc.Egal("identifiant d'écran d'Unreal → celui de Windows", "MONITOR\\SAM793E\\{4d36e96e-e325-11ce-bfc1-08002be10318}\\0002",
                AffichageJeu.IdEcran("\"MONITOR\\\\SAM793E\\\\{4d36e96e-e325-11ce-bfc1-08002be10318}\\\\0002\""));
            Banc.Verifie("identifiant inattendu ignoré", true, AffichageJeu.IdEcran("\\\\.\\DISPLAY1") == null);

            // Fortnite
            List<string> absentes;
            Banc.Egal("Fortnite : résolution actuelle", "1920x1080", ReglagesFortnite.ResolutionActuelle(Ini));
            Banc.Egal("Fortnite : mode actuel", "1", ReglagesFortnite.ModeAffichageActuel(Ini));
            Banc.Egal("Fortnite : écran du jeu", "MONITOR\\SAM793E\\{4d36e96e-e325-11ce-bfc1-08002be10318}\\0002", ReglagesFortnite.IdEcran(Ini));
            Banc.Egal("Fortnite : résolution sur les quatre clés",
                "ResolutionSizeX 1920>2560|ResolutionSizeY 1080>1440|LastUserConfirmedResolutionSizeX 1920>2560|LastUserConfirmedResolutionSizeY 1080>1440",
                Fn(ReglagesFortnite.PrevoitAffichage(Ini, new ChoixAffichage { Resolution = "2560x1440" }, out absentes)));
            Banc.Egal("Fortnite : fenêtré ne touche pas le mode du retour en plein écran",
                "FullscreenMode 1>2|LastConfirmedFullscreenMode 1>2",
                Fn(ReglagesFortnite.PrevoitAffichage(Ini, new ChoixAffichage { Mode = "2" }, out absentes)));
            Banc.Egal("Fortnite : plein écran sur les trois clés",
                "FullscreenMode 1>0|LastConfirmedFullscreenMode 1>0|PreferredFullscreenMode 1>0",
                Fn(ReglagesFortnite.PrevoitAffichage(Ini, new ChoixAffichage { Mode = "0" }, out absentes)));
            Banc.Verifie("Fortnite : mode inconnu refusé", true, ReglagesFortnite.PrevoitAffichage(Ini, new ChoixAffichage { Mode = "3" }, out absentes).Count == 0);
            Banc.Verifie("Fortnite : valeur déjà en place, rien à changer", true,
                ReglagesFortnite.PrevoitAffichage(Ini, new ChoixAffichage { Resolution = "1920x1080", Mode = "1" }, out absentes).Count == 0);
            string sansMode = Ini.Replace("FullscreenMode=1\r\n", "").Replace("LastConfirmedFullscreenMode=1\r\n", "").Replace("PreferredFullscreenMode=1\r\n", "");
            List<ReglagesFortnite.Changement> rien = ReglagesFortnite.PrevoitAffichage(sansMode, new ChoixAffichage { Mode = "0" }, out absentes);
            Banc.Verifie("Fortnite : clé de mode absente → jamais ajoutée, signalée", true, rien.Count == 0 && absentes.Contains("FullscreenMode"));
            List<ReglagesFortnite.Changement> c = ReglagesFortnite.PrevoitAffichage(Ini, new ChoixAffichage { Resolution = "2560x1440", Mode = "0" }, out absentes);
            string apres = ReglagesFortnite.Applique(Ini, c);
            Banc.Verifie("Fortnite : seules les valeurs bougent", true,
                apres == Ini.Replace("=1920\r\n", "=2560\r\n").Replace("=1080\r\n", "=1440\r\n").Replace("Mode=1\r\n", "Mode=0\r\n"));
            Banc.Egal("Fortnite : rétablir rend le fichier exact", Ini, ReglagesFortnite.Retablit(apres, c));

            // Black Ops 6
            string mode;
            Banc.Egal("BO6 : résolution actuelle", "1920x1080", ReglagesCod.ResolutionActuelle(Cod));
            Banc.Egal("BO6 : modes du fichier", "Windowed|Fullscreen|Fullscreen borderless window", string.Join("|", ReglagesCod.ModesAffichage(Cod, out mode)));
            Banc.Egal("BO6 : mode actuel", "Fullscreen borderless window", mode);
            Banc.Egal("BO6 : écran choisi dans le jeu", "DELL S2721DGF", ReglagesCod.Ecran(Cod));
            Banc.Egal("BO6 : libellé du mode", "Plein écran fenêtré", ReglagesCod.NomAffichage("Fullscreen borderless window"));
            Banc.Egal("BO6 : résolution changée",
                "Resolution 1920x1080>2560x1440",
                Cd(ReglagesCod.PrevoitAffichage(Cod, new ChoixAffichage { Resolution = "2560x1440" }, out absentes)));
            Banc.Egal("BO6 : plein écran, le mode préféré est déjà en place",
                "DisplayMode Fullscreen borderless window>Fullscreen",
                Cd(ReglagesCod.PrevoitAffichage(Cod, new ChoixAffichage { Mode = "Fullscreen" }, out absentes)));
            Banc.Egal("BO6 : fenêtré, hors de la liste du mode préféré : seul DisplayMode bouge",
                "DisplayMode Fullscreen borderless window>Windowed",
                Cd(ReglagesCod.PrevoitAffichage(Cod, new ChoixAffichage { Mode = "Windowed" }, out absentes)));
            Banc.Egal("BO6 : plein écran fenêtré suivi par le mode préféré",
                "PreferredDisplayMode Fullscreen>Fullscreen borderless window",
                Cd(ReglagesCod.PrevoitAffichage(Cod, new ChoixAffichage { Mode = "Fullscreen borderless window" }, out absentes)));
            List<ReglagesCod.Changement> hors = ReglagesCod.PrevoitAffichage(Cod, new ChoixAffichage { Mode = "Exclusive" }, out absentes);
            Banc.Verifie("BO6 : mode hors de la liste refusé, signalé", true, hors.Count == 0 && absentes.Contains("DisplayMode"));
            hors = ReglagesCod.PrevoitAffichage(Cod, new ChoixAffichage { Resolution = "2560 x 1440" }, out absentes);
            Banc.Verifie("BO6 : résolution mal formée refusée", true, hors.Count == 0 && absentes.Contains("Resolution"));
            string commentee = Cod.Replace("= 1920x1080\r\n", "= 1920x1080 // auto\r\n");
            Banc.Verifie("BO6 : ligne de résolution inattendue → non proposée", true, ReglagesCod.ResolutionActuelle(commentee) == null);
            Banc.Verifie("BO6 : ligne absente → jamais ajoutée", true,
                ReglagesCod.PrevoitAffichage(Cod.Replace("Resolution@0;1;2 = 1920x1080\r\n", ""), new ChoixAffichage { Resolution = "2560x1440" }, out absentes).Count == 0
                && absentes.Contains("Resolution"));
            List<ReglagesCod.Changement> cc = ReglagesCod.PrevoitAffichage(Cod, new ChoixAffichage { Resolution = "2560x1440", Mode = "Fullscreen" }, out absentes);
            string ecrit = ReglagesCod.Applique(Cod, cc);
            Banc.Egal("BO6 : seules les valeurs bougent",
                Cod.Replace("= 1920x1080\r\n", "= 2560x1440\r\n").Replace("DisplayMode@0;1;2 = Fullscreen borderless window //", "DisplayMode@0;1;2 = Fullscreen //"), ecrit);
            Banc.Egal("BO6 : rétablir rend le fichier exact", Cod, ReglagesCod.Retablit(ecrit, cc));
        }
    }
}
