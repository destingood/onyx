using System.Collections.Generic;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Vérifie <see cref="ObjectifFps"/> — conseil d'un cran à partir d'une MESURE en jeu, limite du
    /// jeu signalée avant tout, et lecture de la limite d'images de Fortnite et Cyberpunk.
    /// </summary>
    internal static class ObjectifFpsTests
    {
        private static string Nom(int m) { return new[] { "Compétition", "Performance", "Équilibré", "Qualité" }[m]; }

        public static void Tout()
        {
            Banc.Titre("FPS visés");

            Banc.Verifie("mode actuel : celui qui n'a rien à changer", true,
                ObjectifFps.ModeActuel(new[] { 5, 0, 3, 9 }) == 1 && ObjectifFps.ModeActuel(new[] { 2, 4, 3, 9 }) == null && ObjectifFps.ModeActuel(null) == null);

            ObjectifFps.Avis a = ObjectifFps.Juge("Fortnite", 98, 71, 144, 2, 0, false, Nom);
            Banc.Verifie("objectif manqué en Équilibré : un cran plus léger (Performance)", true, a.Conseil == ObjectifFps.Conseil.Baisser && a.Mode == 1);
            Banc.Egal("texte du conseil", "Objectif manqué : 98 FPS moyens pour 144 visés (1 % low : 71). Essaie « Performance », un cran plus léger, puis remesure.", a.Texte);

            a = ObjectifFps.Juge("Fortnite", 98, 71, 144, 0, 0, false, Nom);
            Banc.Verifie("manqué déjà en Compétition : hors de portée des réglages graphiques, aucun mode conseillé", true,
                a.Conseil == ObjectifFps.Conseil.HorsDePortee && a.Mode == null && a.Texte.Contains("DLSS"));

            a = ObjectifFps.Juge("Fortnite", 98, 71, 144, null, 0, false, Nom);
            Banc.Verifie("réglages perso et objectif manqué : Performance proposé", true, a.Conseil == ObjectifFps.Conseil.Baisser && a.Mode == 1 && a.Texte.Contains("aucun mode"));

            a = ObjectifFps.Juge("Fortnite", 190, 140, 144, 1, 0, false, Nom);
            Banc.Verifie("32 % de marge en Performance : un cran plus beau (Équilibré)", true, a.Conseil == ObjectifFps.Conseil.Monter && a.Mode == 2 && a.Texte.Contains("32 %"));
            a = ObjectifFps.Juge("Fortnite", 160, 120, 144, 1, 0, false, Nom);
            Banc.Verifie("11 % de marge seulement : objectif atteint, on ne bouge pas", true, a.Conseil == ObjectifFps.Conseil.Atteint && a.Mode == null);
            a = ObjectifFps.Juge("Fortnite", 400, 300, 144, 3, 0, false, Nom);
            Banc.Verifie("déjà en Qualité : rien au-dessus", true, a.Conseil == ObjectifFps.Conseil.Atteint);
            a = ObjectifFps.Juge("Fortnite", 237, 200, 144, 1, 240, false, Nom);
            Banc.Verifie("FPS collés à la limite du jeu : pas de montée conseillée (marge inconnue)", true, a.Conseil == ObjectifFps.Conseil.Atteint && a.Texte.Contains("marge réelle est inconnue"));

            a = ObjectifFps.Juge("Cyberpunk 2077", 60, 50, 144, 2, 60, false, Nom);
            Banc.Verifie("limite du jeu sous l'objectif : signalée, aucun mode conseillé", true, a.Conseil == ObjectifFps.Conseil.Limite && a.Mode == null);
            Banc.Egal("obstacle : limite du jeu", "Cyberpunk 2077 est limité à 60 FPS dans ses propres réglages : l'objectif de 144 est au-dessus. Monte la limite d'images dans le jeu (ONYX n'y touche pas).",
                ObjectifFps.Obstacle("Cyberpunk 2077", 144, 60, false));
            Banc.Verifie("pas d'obstacle si la limite est au-dessus ou absente", true,
                ObjectifFps.Obstacle("X", 144, 240, false) == null && ObjectifFps.Obstacle("X", 144, 0, false) == null);
            Banc.Verifie("objectif borné de 30 à 500", true, ObjectifFps.Borne(5) == 30 && ObjectifFps.Borne(9000) == 500);

            var d = ObjectifFps.Lire(ObjectifFps.Ecrire(new Dictionary<string, int> { { "Fortnite", 240 }, { "Cyberpunk 2077", 9000 } }));
            Banc.Verifie("objectifs relus (et bornés)", true, d.Count == 2 && d["fortnite"] == 240 && d["Cyberpunk 2077"] == 500);
            Banc.Verifie("fichier abîmé : lignes ignorées", true, ObjectifFps.Lire("x\n\ty\nz\tabc\n").Count == 0 && ObjectifFps.Lire(null).Count == 0);

            bool vs;
            int lim = ReglagesFortnite.LimiteImages("[/Script/FortniteGame.FortGameUserSettings]\r\nbUseVSync=True\r\nFrameRateLimit=240.000000\r\n", out vs);
            Banc.Verifie("Fortnite : limite 240 et synchro verticale lues", true, lim == 240 && vs);
            lim = ReglagesFortnite.LimiteImages("[/Script/FortniteGame.FortGameUserSettings]\r\nbUseVSync=False\r\nFrameRateLimit=0.000000\r\n", out vs);
            Banc.Verifie("Fortnite : limite 0 = aucune", true, lim == 0 && !vs);

            string cp = "{\"data\":[{\"group_name\":\"/video/display\",\"options\":["
                      + "{\"name\":\"VSync\",\"type\":\"string_list\",\"value\":\"UI-Settings-Video-QualitySetting-Off\",\"index\":0,\"values\":[\"UI-Settings-Video-QualitySetting-Off\"]},"
                      + "{\"name\":\"MaximumFPS_OnOff\",\"type\":\"bool\",\"value\":true},"
                      + "{\"name\":\"MaximumFPS_Value\",\"type\":\"int\",\"value\":120}]}]}";
            lim = ReglagesCyberpunk.LimiteImages(cp, out vs);
            Banc.Verifie("Cyberpunk : limite 120 active, synchro coupée", true, lim == 120 && !vs);
            lim = ReglagesCyberpunk.LimiteImages(cp.Replace("\"value\":true", "\"value\":false"), out vs);
            Banc.Verifie("Cyberpunk : limite désactivée = aucune", true, lim == 0);
        }
    }
}
