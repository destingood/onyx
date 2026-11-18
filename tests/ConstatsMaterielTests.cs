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
            // Windows annonce la vitesse JEDEC (5600) : sans la référence, une RAM 6000 EXPO
            // remise à 5600 par une mise à jour du BIOS passait pour « à sa vitesse nominale ».
            Banc.Titre("Vitesse annoncée par la référence des barrettes");
            Banc.Verifie("Crucial Pro CP16G60C36U5B", true, ConstatsMateriel.VitesseAnnoncee("CP16G60C36U5B.M8D3") == 6000);
            Banc.Verifie("Corsair CMK32GX5M2B6000C36", true, ConstatsMateriel.VitesseAnnoncee("CMK32GX5M2B6000C36") == 6000);
            Banc.Verifie("G.Skill F5-6400J3239G16G", true, ConstatsMateriel.VitesseAnnoncee("F5-6400J3239G16G") == 6400);
            Banc.Verifie("G.Skill DDR4 F4-3600C16-8GTZNC", true, ConstatsMateriel.VitesseAnnoncee("F4-3600C16-8GTZNC") == 3600);
            Banc.Verifie("Kingston Fury KF560C36-16", true, ConstatsMateriel.VitesseAnnoncee("KF560C36-16") == 5600);
            Banc.Verifie("TeamGroup FF3D516G6000HC38A01", true, ConstatsMateriel.VitesseAnnoncee("FF3D516G6000HC38A01") == 6000);
            Banc.Verifie("référence JEDEC sans vitesse lisible", true, ConstatsMateriel.VitesseAnnoncee("M425R2GA3BB0-CQKOD") == 0);
            Banc.Verifie("référence vide", true, ConstatsMateriel.VitesseAnnoncee(null) == 0);

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
            // ---- BIOS de plus d'un an sur une plateforme AM5 (conseil, jamais de mise à jour)
            Banc.Verifie("9950X3D : AM5", true, ConstatsMateriel.RyzenAm5(R9950));
            Banc.Verifie("7600 : AM5", true, ConstatsMateriel.RyzenAm5("AMD Ryzen 5 7600 6-Core Processor"));
            Banc.Verifie("7500F : AM5", true, ConstatsMateriel.RyzenAm5("AMD Ryzen 5 7500F 6-Core Processor"));
            Banc.Verifie("8700G : AM5", true, ConstatsMateriel.RyzenAm5("AMD Ryzen 7 8700G w/ Radeon 780M Graphics"));
            Banc.Verifie("PRO 8700GE : AM5", true, ConstatsMateriel.RyzenAm5("AMD Ryzen 7 PRO 8700GE"));
            Banc.Verifie("7840HS (portable) : non", false, ConstatsMateriel.RyzenAm5("AMD Ryzen 7 7840HS w/ Radeon 780M Graphics"));
            Banc.Verifie("9955HX (portable) : non", false, ConstatsMateriel.RyzenAm5("AMD Ryzen 9 9955HX"));
            Banc.Verifie("5800X3D (AM4) : non", false, ConstatsMateriel.RyzenAm5("AMD Ryzen 7 5800X3D 8-Core Processor"));
            Banc.Verifie("Intel : non", false, ConstatsMateriel.RyzenAm5("Intel(R) Core(TM) i7-14700K"));
            var maintenant = new System.DateTime(2026, 11, 17);
            string bios = ConstatsMateriel.BiosAncienAm5(R9950, new System.DateTime(2025, 6, 30), maintenant);
            Banc.Verifie("BIOS de 2025 sur AM5 : signalé, date citée, pas de mise à jour automatique", true,
                bios != null && bios.Contains("30/06/2025") && bios.Contains("ne met jamais"));
            Banc.Verifie("BIOS de 4 mois : rien", true,
                ConstatsMateriel.BiosAncienAm5(R9950, new System.DateTime(2026, 6, 30), maintenant) == null);
            Banc.Verifie("BIOS d'un an tout juste : rien", true,
                ConstatsMateriel.BiosAncienAm5(R9950, new System.DateTime(2025, 11, 18), maintenant) == null);
            Banc.Verifie("date illisible : rien", true, ConstatsMateriel.BiosAncienAm5(R9950, null, maintenant) == null);
            Banc.Verifie("portable avec vieux BIOS : rien", true,
                ConstatsMateriel.BiosAncienAm5("AMD Ryzen 7 7840HS", new System.DateTime(2023, 1, 1), maintenant) == null);

            // ---- Resizable BAR coupé (taille BAR1 lue par nvidia-smi : 256 Mio = coupé)
            const string SmiOn = "    FB Memory Usage\r\n        Total                                          : 16376 MiB\r\n"
                               + "    BAR1 Memory Usage\r\n        Total                                          : 16384 MiB\r\n        Used : 16355 MiB\r\n";
            const string SmiOff = "    FB Memory Usage\n        Total : 8192 MiB\n    BAR1 Memory Usage\n        Total : 256 MiB\n        Used : 7 MiB\n";
            Banc.Verifie("BAR1 lue (actif) : 16384", true, ConstatsMateriel.Bar1Mio(SmiOn) == 16384);
            Banc.Verifie("BAR1 lue (coupé) : 256, pas le Total de la VRAM", true, ConstatsMateriel.Bar1Mio(SmiOff) == 256);
            Banc.Verifie("sortie sans BAR1 : inconnu", true, ConstatsMateriel.Bar1Mio("NVIDIA-SMI has failed") == null);
            Banc.Verifie("sortie absente : inconnu", true, ConstatsMateriel.Bar1Mio(null) == null);
            string rebar = ConstatsMateriel.ReBarCoupe("NVIDIA GeForce RTX 3070", 256);
            Banc.Verifie("RTX 3070 à 256 Mio : signalé, BIOS nommé", true, rebar != null && rebar.Contains("Resizable BAR") && rebar.Contains("BIOS"));
            Banc.Verifie("RTX 4080 SUPER actif : rien", true, ConstatsMateriel.ReBarCoupe(Rtx4070, 16384) == null);
            Banc.Verifie("RTX 2080 (pas de ReBAR chez NVIDIA) : rien", true, ConstatsMateriel.ReBarCoupe("NVIDIA GeForce RTX 2080", 256) == null);
            Banc.Verifie("GTX 1650 : rien", true, ConstatsMateriel.ReBarCoupe("NVIDIA GeForce GTX 1650", 256) == null);
            Banc.Verifie("BAR1 inconnue : rien", true, ConstatsMateriel.ReBarCoupe(Rtx4070, null) == null);

            // ---- Reflex coupé dans Fortnite (LatencyTweak2 : 0 coupé, 1 activé, 2 activé + boost)
            const string IniOn = "[/Script/FortniteGame.FortGameUserSettings]\r\nbShowFPS=True\r\nLatencyTweak2=2\r\n[DriverCache]\r\n";
            const string IniOff = "[/Script/FortniteGame.FortGameUserSettings]\nLatencyTweak2=0\n";
            Banc.Verifie("Fortnite : LatencyTweak2=2 lu", true, ConstatsMateriel.ReflexFortnite(IniOn) == 2);
            Banc.Verifie("Fortnite : LatencyTweak2=0 lu", true, ConstatsMateriel.ReflexFortnite(IniOff) == 0);
            Banc.Verifie("Fortnite : clé absente, inconnu", true, ConstatsMateriel.ReflexFortnite("[/Script/FortniteGame.FortGameUserSettings]\nbShowFPS=True\n") == null);
            Banc.Verifie("Fortnite : fichier absent, inconnu", true, ConstatsMateriel.ReflexFortnite(null) == null);
            Banc.Verifie("Fortnite : LatencyTweak2 ne se confond pas avec une autre clé", true,
                ConstatsMateriel.ReflexFortnite("[x]\nOldLatencyTweak2=0\nLatencyTweak2=1\n") == 1);
            string refl = ConstatsMateriel.ReflexCoupeFortnite(Rtx4070, 0);
            Banc.Verifie("Reflex coupé + NVIDIA : signalé, mesure et limite citées", true,
                refl != null && refl.Contains("Reflex") && refl.Contains("82") && refl.Contains("saturée"));
            Banc.Verifie("Reflex activé : rien", true, ConstatsMateriel.ReflexCoupeFortnite(Rtx4070, 1) == null);
            Banc.Verifie("Reflex activé + boost : rien", true, ConstatsMateriel.ReflexCoupeFortnite(Rtx4070, 2) == null);
            Banc.Verifie("carte AMD : rien (Reflex est propre à NVIDIA)", true, ConstatsMateriel.ReflexCoupeFortnite("AMD Radeon RX 7900 XT", 0) == null);
            Banc.Verifie("réglage inconnu : rien", true, ConstatsMateriel.ReflexCoupeFortnite(Rtx4070, null) == null);

            string tout = ConstatsMateriel.X3DSansParcage(R9950, false, "Performances élevées", true, true);
            Banc.Verifie("plusieurs causes : toutes citées dans un seul constat", true,
                tout.Contains("pilote chipset AMD") && tout.Contains("Performances élevées") && tout.Contains("mode Jeu") && tout.Contains("Game DVR"));
        }
    }
}
