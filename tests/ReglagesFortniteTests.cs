using System.Collections.Generic;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Vérifie <see cref="ReglagesFortnite"/> — préréglages graphiques écrits dans GameUserSettings.ini :
    /// seules les valeurs visées bougent, octet pour octet, et « Rétablir » ne défait pas ce que le
    /// joueur a changé lui-même dans le menu entre-temps.
    /// </summary>
    internal static class ReglagesFortniteTests
    {
        private const string Ini =
            ";METADATA=(Diff=true, UseCommands=true)\r\n" +
            "[/Script/FortniteGame.FortGameUserSettings]\r\n" +
            "bUseVSync=True\r\n" +
            "bUseNanite=True\r\n" +
            "bMotionBlur=False\r\n" +
            "FrameRateLimit=240.000000\r\n" +
            "\r\n" +
            "[ScalabilityGroups]\r\n" +
            "sg.ResolutionQuality=100\r\n" +
            "sg.ViewDistanceQuality=3\r\n" +
            "sg.AntiAliasingQuality=3\r\n" +
            "sg.ShadowQuality=0\r\n" +
            "sg.GlobalIlluminationQuality=0\r\n" +
            "sg.ReflectionQuality=0\r\n" +
            "sg.PostProcessQuality=0\r\n" +
            "sg.TextureQuality=3\r\n" +
            "sg.EffectsQuality=0\r\n" +
            "sg.FoliageQuality=3\r\n" +
            "sg.ShadingQuality=2\r\n" +
            "sg.LandscapeQuality=2\r\n" +
            "\r\n" +
            "[ChatSettings]\r\n" +
            "bUseVSync=True\r\n" +
            "ChatFontSize=Default\r\n";

        public static void Tout()
        {
            Banc.Titre("Préréglages Fortnite");
            List<string> absentes;

            List<ReglagesFortnite.Changement> c = ReglagesFortnite.Prevoit(Ini, ReglagesFortnite.Mode.Competition, out absentes);
            Banc.Verifie("seulement les écarts : ShadowQuality déjà à 0 n'est pas listé", true,
                !c.Exists(x => x.Cle == "sg.ShadowQuality") && !c.Exists(x => x.Cle == "bMotionBlur"));
            Banc.Verifie("écart listé avec avant et après (TextureQuality 3 → 1)", true,
                c.Exists(x => x.Cle == "sg.TextureQuality" && x.Avant == "3" && x.Apres == "1" && x.Section == "ScalabilityGroups"));
            Banc.Verifie("VSync de la section du jeu, pas celle du chat", true,
                c.FindAll(x => x.Cle == "bUseVSync").Count == 1 && c.Exists(x => x.Cle == "bUseVSync" && x.Section == "/Script/FortniteGame.FortGameUserSettings"));
            Banc.Verifie("aucune clé absente dans un fichier complet", true, absentes.Count == 0);
            Banc.Egal("nombre d'écarts en Compétition", "8", c.Count.ToString());

            string apres = ReglagesFortnite.Applique(Ini, c);
            string attendu = Ini
                .Replace("[/Script/FortniteGame.FortGameUserSettings]\r\nbUseVSync=True", "[/Script/FortniteGame.FortGameUserSettings]\r\nbUseVSync=False")
                .Replace("bUseNanite=True", "bUseNanite=False")
                .Replace("sg.ViewDistanceQuality=3", "sg.ViewDistanceQuality=2")
                .Replace("sg.AntiAliasingQuality=3", "sg.AntiAliasingQuality=0")
                .Replace("sg.TextureQuality=3", "sg.TextureQuality=1")
                .Replace("sg.FoliageQuality=3", "sg.FoliageQuality=0")
                .Replace("sg.ShadingQuality=2", "sg.ShadingQuality=0")
                .Replace("sg.LandscapeQuality=2", "sg.LandscapeQuality=0");
            Banc.Egal("rien d'autre ne bouge (ordre, \\r\\n, [ChatSettings], FrameRateLimit)", attendu, apres);

            string sansLandscape = Ini.Replace("sg.LandscapeQuality=2\r\n", "");
            List<ReglagesFortnite.Changement> c2 = ReglagesFortnite.Prevoit(sansLandscape, ReglagesFortnite.Mode.Competition, out absentes);
            Banc.Verifie("clé absente : listée, pas ajoutée", true,
                absentes.Contains("sg.LandscapeQuality") && !ReglagesFortnite.Applique(sansLandscape, c2).Contains("LandscapeQuality"));

            Banc.Egal("rétablir rend le fichier exact", Ini, ReglagesFortnite.Retablit(apres, c));

            string joueur = apres.Replace("sg.TextureQuality=1", "sg.TextureQuality=3x").Replace("sg.TextureQuality=3x", "sg.TextureQuality=2");
            string retabli = ReglagesFortnite.Retablit(joueur, c);
            Banc.Egal("rétablir respecte le joueur : TextureQuality changé à la main reste", Ini.Replace("sg.TextureQuality=3", "sg.TextureQuality=2"), retabli);

            Banc.Verifie("booléens : casse ignorée (false = False)", true,
                !ReglagesFortnite.Prevoit(Ini.Replace("bMotionBlur=False", "bMotionBlur=false"), ReglagesFortnite.Mode.Qualite, out absentes).Exists(x => x.Cle == "bMotionBlur"));
            Banc.Verifie("Qualité : Nanite reste allumé, ombres à 3", true,
                ReglagesFortnite.Prevoit(Ini, ReglagesFortnite.Mode.Qualite, out absentes).Exists(x => x.Cle == "sg.ShadowQuality" && x.Apres == "3")
                && !ReglagesFortnite.Prevoit(Ini, ReglagesFortnite.Mode.Qualite, out absentes).Exists(x => x.Cle == "bUseNanite"));

            // Deux préréglages à la suite : Rétablir ramène au fichier d'origine.
            List<ReglagesFortnite.Changement> qualite = ReglagesFortnite.Prevoit(apres, ReglagesFortnite.Mode.Qualite, out absentes);
            List<ReglagesFortnite.Changement> etat = ReglagesFortnite.Fusionne(c, qualite);
            string deuxFois = ReglagesFortnite.Applique(apres, qualite);
            Banc.Egal("deux préréglages puis rétablir : fichier d'origine", Ini, ReglagesFortnite.Retablit(deuxFois, etat));
            Banc.Verifie("fusion : clé revenue à son origine retirée de l'état (Nanite)", true, !etat.Exists(x => x.Cle == "bUseNanite"));
            Banc.Egal("ligne lisible", "Textures : Épique → Bas", ReglagesFortnite.Ligne(new ReglagesFortnite.Changement { Cle = "sg.TextureQuality", Avant = "3", Apres = "0" }));
            Banc.Egal("ligne lisible : booléen", "Synchronisation verticale : activé → désactivé",
                ReglagesFortnite.Ligne(new ReglagesFortnite.Changement { Cle = "bUseVSync", Avant = "True", Apres = "False" }));

            List<ReglagesFortnite.Changement> lu =ReglagesFortnite.Lire(ReglagesFortnite.Ecrire(c));
            bool pareil = lu.Count == c.Count;
            for (int i = 0; pareil && i < c.Count; i++)
                pareil = lu[i].Section == c[i].Section && lu[i].Cle == c[i].Cle && lu[i].Avant == c[i].Avant && lu[i].Apres == c[i].Apres;
            Banc.Verifie("aller-retour du fichier d'état", true, pareil);
            Banc.Verifie("fichier d'état abîmé : lignes ignorées", true, ReglagesFortnite.Lire("x\ty\n\n").Count == 0 && ReglagesFortnite.Lire(null).Count == 0);
            Banc.Verifie("lignes \\n seules gérées aussi", true,
                ReglagesFortnite.Applique(Ini.Replace("\r\n", "\n"), c) == attendu.Replace("\r\n", "\n"));
        }
    }
}
