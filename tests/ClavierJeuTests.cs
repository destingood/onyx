namespace BTOptimizer.Tests
{
    /// <summary>
    /// Vérifie <see cref="ClavierJeu"/> — touche Windows bloquée seulement jeu devant, relâchement
    /// fidèle à l'appui, raccourcis d'accessibilité coupés puis rendus bit pour bit.
    /// </summary>
    internal static class ClavierJeuTests
    {
        public static void Tout()
        {
            Banc.Titre("Clavier pendant la partie");
            const int G = ClavierJeu.VkWinGauche, D = ClavierJeu.VkWinDroite;

            var f = new ClavierJeu.Filtre();
            Banc.Verifie("jeu devant : touche Windows avalée (appui et relâchement)", true, f.Bloque(G, false, false, true) && f.Bloque(G, true, false, true));
            Banc.Verifie("hors du jeu : touche Windows libre", false, f.Bloque(G, false, false, false) || f.Bloque(G, true, false, false));
            Banc.Verifie("autres touches jamais touchées", false, f.Bloque(0x41, false, false, true) || f.Bloque(0x10, false, false, true) || f.Bloque(0x09, false, false, true));
            Banc.Verifie("touche envoyée par un logiciel : laissée passer", false, f.Bloque(D, false, true, true) || f.Bloque(D, true, true, true));

            f = new ClavierJeu.Filtre();
            bool appui = f.Bloque(G, false, false, false);        // appuyée sur le bureau
            bool relache = f.Bloque(G, true, false, true);        // relâchée une fois le jeu devant
            Banc.Verifie("appui passé hors du jeu : relâchement passé aussi (sinon Windows la croit tenue)", false, appui || relache);
            f = new ClavierJeu.Filtre();
            appui = f.Bloque(G, false, false, true);              // appuyée en jeu
            relache = f.Bloque(G, true, false, false);            // relâchée après un Alt+Tab
            Banc.Verifie("appui avalé en jeu : relâchement avalé aussi", true, appui && relache);
            f = new ClavierJeu.Filtre();
            f.Bloque(G, false, false, true);
            Banc.Verifie("gauche et droite suivies séparément", false, f.Bloque(D, true, false, true));

            Banc.Verifie("jeu connu : son processus devant, avec ou sans .exe", true,
                ClavierJeu.JeuDevant("FortniteClient-Win64-Shipping", "FortniteClient-Win64-Shipping.exe", false)
                && ClavierJeu.JeuDevant("fortniteclient-win64-shipping.EXE", "FortniteClient-Win64-Shipping", false));
            Banc.Verifie("jeu connu : une autre fenêtre plein écran ne compte pas", false, ClavierJeu.JeuDevant("vlc", "FortniteClient-Win64-Shipping", true));
            Banc.Verifie("jeu inconnu : plein écran = jeu, fenêtré = non", true,
                ClavierJeu.JeuDevant("Cyberpunk2077", null, true) && !ClavierJeu.JeuDevant("Cyberpunk2077", null, false));
            Banc.Verifie("bureau, ONYX, menu Démarrer : jamais un jeu", false,
                ClavierJeu.JeuDevant("explorer", null, true) || ClavierJeu.JeuDevant("BTOptimizer", null, true)
                || ClavierJeu.JeuDevant("StartMenuExperienceHost", null, true) || ClavierJeu.JeuDevant("", null, true) || ClavierJeu.JeuDevant(null, "x", true));

            Banc.Egal("raccourci coupé, le reste gardé (510 → 506, valeur Windows par défaut)", "506", ClavierJeu.SansRaccourci(510).ToString());
            Banc.Egal("rendu : seul le bit du raccourci revient", "511", ClavierJeu.Rendu(507, 510).ToString());   // rémanentes activées entre-temps (bit 1)
            Banc.Egal("rendu : raccourci absent au départ, absent à la fin", "506", ClavierJeu.Rendu(506, 506).ToString());

            uint? r, fl;
            ClavierJeu.LitEtat(ClavierJeu.EcritEtat(510, 126), out r, out fl);
            Banc.Verifie("marqueur relu", true, r == 510 && fl == 126);
            ClavierJeu.LitEtat(ClavierJeu.EcritEtat(null, 126), out r, out fl);
            Banc.Verifie("marqueur partiel relu", true, r == null && fl == 126);
            ClavierJeu.LitEtat("remanentes=abc\nfiltres=-3\n=4\nxx", out r, out fl);
            Banc.Verifie("marqueur abîmé : rien", true, r == null && fl == null);
            ClavierJeu.LitEtat(null, out r, out fl);
            Banc.Verifie("pas de marqueur : rien", true, r == null && fl == null);

            ClavierJeu.Options o = ClavierJeu.LitOptions(null);
            Banc.Verifie("par défaut : touche Windows libre, raccourcis Maj coupés", true, !o.ToucheWindows && o.Raccourcis);
            o.ToucheWindows = true; o.Raccourcis = false;
            ClavierJeu.Options o2 = ClavierJeu.LitOptions(ClavierJeu.EcritOptions(o).Replace("\n", "\r\n"));
            Banc.Verifie("options relues (fins de ligne Windows comprises)", true, o2.ToucheWindows && !o2.Raccourcis);
        }
    }
}
