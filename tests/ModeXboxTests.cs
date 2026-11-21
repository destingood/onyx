namespace BTOptimizer.Tests
{
    /// <summary>
    /// Conseil mode Xbox : seulement avec 16 Go ou moins ou une puce intégrée seule, sur un
    /// Windows qui propose le mode. Texte honnête : mémoire libérée, aucun FPS en plus.
    /// </summary>
    internal static class ModeXboxTests
    {
        private static readonly string[] Rtx = { "NVIDIA GeForce RTX 5090" };
        private static readonly string[] Apu = { "AMD Radeon(TM) Graphics" };

        public static void Tout()
        {
            Banc.Titre("Conseil mode Xbox");

            // ---- Disponibilité
            Banc.Verifie("24H2 révision 8328 : disponible", true, ModeXbox.Disponible(26100, 8328));
            Banc.Verifie("25H2 révision 8327 : pas encore", false, ModeXbox.Disponible(26200, 8327));
            Banc.Verifie("26H1 révision 2179 : disponible", true, ModeXbox.Disponible(28000, 2179));
            Banc.Verifie("23H2 : non", false, ModeXbox.Disponible(22631, 9999));
            Banc.Verifie("Insider 26300 : on se tait", false, ModeXbox.Disponible(26300, 9999));

            // ---- Puces intégrées
            Banc.Verifie("Radeon(TM) Graphics : intégrée", true, ModeXbox.EstIntegree("AMD Radeon(TM) Graphics"));
            Banc.Verifie("Radeon 780M : intégrée", true, ModeXbox.EstIntegree("AMD Radeon 780M Graphics"));
            Banc.Verifie("Radeon 8060S : intégrée", true, ModeXbox.EstIntegree("AMD Radeon(TM) 8060S Graphics"));
            Banc.Verifie("Vega 8 : intégrée", true, ModeXbox.EstIntegree("AMD Radeon(TM) Vega 8 Graphics"));
            Banc.Verifie("RX 7600 : dédiée", false, ModeXbox.EstIntegree("AMD Radeon RX 7600"));
            Banc.Verifie("RX Vega 56 : dédiée", false, ModeXbox.EstIntegree("Radeon RX Vega 56"));
            Banc.Verifie("Radeon Pro W7900 : dédiée", false, ModeXbox.EstIntegree("AMD Radeon PRO W7900"));
            Banc.Verifie("Intel UHD 770 : intégrée", true, ModeXbox.EstIntegree("Intel(R) UHD Graphics 770"));
            Banc.Verifie("Iris Xe : intégrée", true, ModeXbox.EstIntegree("Intel(R) Iris(R) Xe Graphics"));
            Banc.Verifie("Arc 140V : intégrée", true, ModeXbox.EstIntegree("Intel(R) Arc(TM) 140V GPU (16GB)"));
            Banc.Verifie("Arc(TM) Graphics : intégrée", true, ModeXbox.EstIntegree("Intel(R) Arc(TM) Graphics"));
            Banc.Verifie("Arc B580 : dédiée", false, ModeXbox.EstIntegree("Intel(R) Arc(TM) B580 Graphics"));
            Banc.Verifie("Arc A770 : dédiée", false, ModeXbox.EstIntegree("Intel(R) Arc(TM) A770 Graphics"));
            Banc.Verifie("RTX : dédiée", false, ModeXbox.EstIntegree("NVIDIA GeForce RTX 4060 Laptop GPU"));
            Banc.Verifie("marque inconnue : dédiée", false, ModeXbox.EstIntegree("Moore Threads MTT S80"));
            Banc.Verifie("nom vide : non", false, ModeXbox.EstIntegree(""));

            Banc.Verifie("portable Intel + RTX : pas seule", false,
                ModeXbox.IntegreeSeule(new[] { "Intel(R) UHD Graphics", "NVIDIA GeForce RTX 4060 Laptop GPU" }));
            Banc.Verifie("APU seule : oui", true, ModeXbox.IntegreeSeule(Apu));
            Banc.Verifie("aucune carte : non", false, ModeXbox.IntegreeSeule(new string[0]));
            Banc.Verifie("liste absente : non", false, ModeXbox.IntegreeSeule(null));

            // ---- Conseil
            Banc.Verifie("PC de test (64 Go, RTX, 26300) : rien", true, ModeXbox.Conseil(65000, Rtx, 26300, 7000) == null);
            Banc.Verifie("32 Go + RTX sur 25H2 : rien", true, ModeXbox.Conseil(32600, Rtx, 26200, 8400) == null);
            Banc.Verifie("16 Go sur 23H2 : rien", true, ModeXbox.Conseil(16100, Rtx, 22631, 5000) == null);
            Banc.Verifie("RAM illisible + RTX : rien", true, ModeXbox.Conseil(0, Rtx, 26200, 8400) == null);

            string seize = ModeXbox.Conseil(16100, Rtx, 26200, 8400);
            Banc.Verifie("16 Go + RTX : conseil", true, seize != null);
            Banc.Verifie("16 Go : taille citée", true, seize != null && seize.Contains("16 Go"));
            Banc.Verifie("texte : aucun FPS en plus", true, seize != null && seize.Contains("aucun FPS en plus"));
            Banc.Verifie("texte : sortie Win+F11", true, seize != null && seize.Contains("Win+F11"));

            string apu = ModeXbox.Conseil(32600, Apu, 26100, 8328);
            Banc.Verifie("32 Go + APU : conseil", true, apu != null);
            Banc.Verifie("APU : cause citée", true, apu != null && apu.Contains("puce graphique intégrée"));

            string deux = ModeXbox.Conseil(15400, Apu, 28000, 2200);
            Banc.Verifie("15 Go + APU : les deux causes", true, deux != null && deux.Contains("15 Go") && deux.Contains("intégrée"));
        }
    }
}
