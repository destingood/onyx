namespace BTOptimizer.Tests
{
    /// <summary>
    /// Constats matériels du diagnostic qui ne modifient rien : RAM en simple canal, HAGS coupé
    /// sur une carte à génération d'images, Windows sans l'optimisation des Ryzen 7000/9000.
    /// </summary>
    internal static class ConstatsMaterielTests
    {
        private const string R9950 = "AMD Ryzen 9 9950X3D 16-Core Processor";
        private const string Rtx4070 = "NVIDIA GeForce RTX 4070 SUPER";

        public static void Tout()
        {
            Banc.Titre("Constats matériels (simple canal, HAGS, Windows et Ryzen)");

            // ---- RAM en simple canal
            string s = ConstatsMateriel.SimpleCanal(1, 4, false);
            Banc.Verifie("1 barrette sur 4 emplacements : simple canal signalé", true, s != null && s.Contains("simple canal"));
            Banc.Verifie("2 barrettes : rien", true, ConstatsMateriel.SimpleCanal(2, 4, false) == null);
            Banc.Verifie("4 barrettes : rien", true, ConstatsMateriel.SimpleCanal(4, 4, false) == null);
            Banc.Verifie("lecture ratée (0 barrette) : rien", true, ConstatsMateriel.SimpleCanal(0, 4, false) == null);
            Banc.Verifie("un seul emplacement : rien à corriger, rien", true, ConstatsMateriel.SimpleCanal(1, 1, false) == null);
            Banc.Verifie("emplacements inconnus : signalé quand même", true, ConstatsMateriel.SimpleCanal(1, 0, false) != null);
            Banc.Verifie("fixe : pas de mention de mémoire soudée", true, !s.Contains("soudée"));
            Banc.Verifie("portable : prévient que la mémoire peut être soudée", true,
                ConstatsMateriel.SimpleCanal(1, 2, true).Contains("soudée"));

            // ---- Carte capable de génération d'images DLSS (RTX 40 et 50)
            Banc.Verifie("RTX 4070 SUPER : oui", true, ConstatsMateriel.CarteGenerationImages(Rtx4070));
            Banc.Verifie("RTX 5090 : oui", true, ConstatsMateriel.CarteGenerationImages("NVIDIA GeForce RTX 5090"));
            Banc.Verifie("RTX 4060 de portable : oui", true, ConstatsMateriel.CarteGenerationImages("NVIDIA GeForce RTX 4060 Laptop GPU"));
            Banc.Verifie("RTX 3080 : non", false, ConstatsMateriel.CarteGenerationImages("NVIDIA GeForce RTX 3080"));
            Banc.Verifie("GTX 1650 : non", false, ConstatsMateriel.CarteGenerationImages("NVIDIA GeForce GTX 1650"));
            Banc.Verifie("RX 7900 XT : non", false, ConstatsMateriel.CarteGenerationImages("AMD Radeon RX 7900 XT"));
            Banc.Verifie("RTX A4000 : non", false, ConstatsMateriel.CarteGenerationImages("NVIDIA RTX A4000"));
            Banc.Verifie("nom absent : non", false, ConstatsMateriel.CarteGenerationImages(null));

            // ---- HAGS coupé
            string h = ConstatsMateriel.HagsManquant(Rtx4070, false);
            Banc.Verifie("RTX 40 + HAGS coupé : signalé, carte nommée", true, h != null && h.Contains("RTX 4070"));
            Banc.Verifie("RTX 40 + HAGS actif : rien", true, ConstatsMateriel.HagsManquant(Rtx4070, true) == null);
            Banc.Verifie("RTX 40 + HAGS inconnu : rien (pas d'affirmation sans lecture)", true, ConstatsMateriel.HagsManquant(Rtx4070, null) == null);
            Banc.Verifie("RTX 30 + HAGS coupé : rien", true, ConstatsMateriel.HagsManquant("NVIDIA GeForce RTX 3080", false) == null);

            // ---- Ryzen 7000/9000 (Zen 4 et Zen 5)
            Banc.Verifie("9950X3D : oui", true, ConstatsMateriel.RyzenZen4Ou5(R9950));
            Banc.Verifie("7800X3D : oui", true, ConstatsMateriel.RyzenZen4Ou5("AMD Ryzen 7 7800X3D 8-Core Processor"));
            Banc.Verifie("7600 : oui", true, ConstatsMateriel.RyzenZen4Ou5("AMD Ryzen 5 7600 6-Core Processor"));
            Banc.Verifie("7840HS (portable Zen 4) : oui", true, ConstatsMateriel.RyzenZen4Ou5("AMD Ryzen 7 7840HS w/ Radeon 780M Graphics"));
            Banc.Verifie("7945HX (portable Zen 4) : oui", true, ConstatsMateriel.RyzenZen4Ou5("AMD Ryzen 9 7945HX"));
            Banc.Verifie("7520U (Zen 2 renommé) : non", false, ConstatsMateriel.RyzenZen4Ou5("AMD Ryzen 5 7520U with Radeon Graphics"));
            Banc.Verifie("7735HS (Zen 3+) : non", false, ConstatsMateriel.RyzenZen4Ou5("AMD Ryzen 7 7735HS"));
            Banc.Verifie("5800X3D : non", false, ConstatsMateriel.RyzenZen4Ou5("AMD Ryzen 7 5800X3D 8-Core Processor"));
            Banc.Verifie("Intel : non", false, ConstatsMateriel.RyzenZen4Ou5("Intel(R) Core(TM) i7-14700K"));
            Banc.Verifie("nom absent : non", false, ConstatsMateriel.RyzenZen4Ou5(null));

            // ---- Windows sans l'optimisation Ryzen (24H2, ou KB5041587 sur 22H2/23H2)
            Banc.Verifie("24H2 : rien", true, ConstatsMateriel.WindowsEnRetard(R9950, 26100, 0) == null);
            Banc.Verifie("25H2 : rien", true, ConstatsMateriel.WindowsEnRetard(R9950, 26200, 0) == null);
            Banc.Verifie("23H2 avec KB5041587 : rien", true, ConstatsMateriel.WindowsEnRetard(R9950, 22631, 4112) == null);
            Banc.Verifie("22H2 avec KB5041587 : rien", true, ConstatsMateriel.WindowsEnRetard(R9950, 22621, 4112) == null);
            string w = ConstatsMateriel.WindowsEnRetard(R9950, 22631, 3880);
            Banc.Verifie("23H2 sans la mise à jour : signalé", true, w != null && w.Contains("KB5041587"));
            Banc.Verifie("22H2 juste avant la mise à jour : signalé", true, ConstatsMateriel.WindowsEnRetard(R9950, 22621, 4111) != null);
            Banc.Verifie("21H2 : signalé", true, ConstatsMateriel.WindowsEnRetard(R9950, 22000, 9999) != null);
            Banc.Verifie("Windows 10 : rien (pas une simple mise à jour)", true, ConstatsMateriel.WindowsEnRetard(R9950, 19045, 5000) == null);
            Banc.Verifie("build illisible : rien", true, ConstatsMateriel.WindowsEnRetard(R9950, 0, 0) == null);
            Banc.Verifie("Intel sur 23H2 ancien : rien", true, ConstatsMateriel.WindowsEnRetard("Intel(R) Core(TM) i7-14700K", 22631, 100) == null);

            // ---- Ryzen X3D à deux CCD (V-Cache sur un seul des deux)
            Banc.Verifie("9950X3D : deux CCD", true, ConstatsMateriel.X3DDeuxCcd(R9950));
            Banc.Verifie("9900X3D : deux CCD", true, ConstatsMateriel.X3DDeuxCcd("AMD Ryzen 9 9900X3D 12-Core Processor"));
            Banc.Verifie("7950X3D : deux CCD", true, ConstatsMateriel.X3DDeuxCcd("AMD Ryzen 9 7950X3D 16-Core Processor"));
            Banc.Verifie("7900X3D : deux CCD", true, ConstatsMateriel.X3DDeuxCcd("AMD Ryzen 9 7900X3D 12-Core Processor"));
            Banc.Verifie("9800X3D (un seul CCD) : non", false, ConstatsMateriel.X3DDeuxCcd("AMD Ryzen 7 9800X3D 8-Core Processor"));
            Banc.Verifie("7800X3D (un seul CCD) : non", false, ConstatsMateriel.X3DDeuxCcd("AMD Ryzen 7 7800X3D 8-Core Processor"));
            Banc.Verifie("9950X (sans V-Cache) : non", false, ConstatsMateriel.X3DDeuxCcd("AMD Ryzen 9 9950X 16-Core Processor"));
            Banc.Verifie("variante inconnue (X3D2) : non, on n'extrapole pas", false, ConstatsMateriel.X3DDeuxCcd("AMD Ryzen 9 9950X3D2 16-Core Processor"));
            Banc.Verifie("nom absent : non", false, ConstatsMateriel.X3DDeuxCcd(null));

            // ---- Ce qui empêche le pilote AMD de garer le CCD sans V-Cache
            // Seul le plan est écarté d'office : le déparquage de base (CPMINCORES) ne touche pas le
            // profil du mode Jeu où AMD gare les cœurs ; il vaut 100 % par défaut sur certains PC.
            Banc.Verifie("garde : Performances ultimes et les deux coupures de Game DVR écartés d'office", true,
                string.Join(",", ConstatsMateriel.ConflitsX3D) == "power_ultimate,gamedvr_off,gamedvr_policy_off");
            Banc.Verifie("garde : le déparquage de base n'est pas écarté", false,
                System.Array.IndexOf(ConstatsMateriel.ConflitsX3D, "cpu_unpark_cores") >= 0);
            Banc.Verifie("tout en ordre : rien", true, ConstatsMateriel.X3DSansParcage(R9950, true, null, false, false) == null);
            Banc.Verifie("pas un X3D à deux CCD : rien", true,
                ConstatsMateriel.X3DSansParcage("AMD Ryzen 7 9800X3D 8-Core Processor", false, "Performances ultimes", true, true) == null);
            string pil = ConstatsMateriel.X3DSansParcage(R9950, false, null, false, false);
            Banc.Verifie("pilote absent : signalé, pilote chipset nommé", true, pil != null && pil.Contains("pilote chipset AMD"));
            Banc.Verifie("pilote non lu : rien (pas d'affirmation sans lecture)", true, ConstatsMateriel.X3DSansParcage(R9950, null, null, false, false) == null);
            string plan = ConstatsMateriel.X3DSansParcage(R9950, true, "Performances ultimes", false, false);
            Banc.Verifie("plan Performances ultimes : signalé, plan nommé, Équilibré conseillé", true,
                plan != null && plan.Contains("Performances ultimes") && plan.Contains("Équilibré"));
            Banc.Verifie("mode Jeu coupé : signalé", true, ConstatsMateriel.X3DSansParcage(R9950, true, null, true, false).Contains("mode Jeu"));
            string dvr = ConstatsMateriel.X3DSansParcage(R9950, true, null, false, true);
            Banc.Verifie("Game DVR coupé par stratégie : signalé, Game Bar nommée", true,
                dvr != null && dvr.Contains("Game DVR") && dvr.Contains("Game Bar"));
            string tout = ConstatsMateriel.X3DSansParcage(R9950, false, "Performances élevées", true, true);
            Banc.Verifie("plusieurs causes : toutes citées dans un seul constat", true,
                tout.Contains("pilote chipset AMD") && tout.Contains("Performances élevées") && tout.Contains("mode Jeu") && tout.Contains("Game DVR"));
        }
    }
}
