namespace BTOptimizer.Tests
{
    /// <summary>
    /// Shaders précompilés (Advanced Shader Delivery) : Windows 24H2 ou plus et pilote minimum
    /// par marque. Lecture seule ; le constat se tait dès qu'une condition ne se lit pas.
    /// </summary>
    internal static class ShadersPrecompilesTests
    {
        private const string Rtx = "NVIDIA GeForce RTX 5090";

        public static void Tout()
        {
            Banc.Titre("Shaders précompilés (Advanced Shader Delivery)");

            // ---- Cartes éligibles
            Banc.Verifie("RTX : nvidia", true, ShadersPrecompiles.CarteEligible(Rtx) == "nvidia");
            Banc.Verifie("GTX 1660 : non éligible", true, ShadersPrecompiles.CarteEligible("NVIDIA GeForce GTX 1660 SUPER") == null);
            Banc.Verifie("RX 7900 XTX : amd", true, ShadersPrecompiles.CarteEligible("AMD Radeon RX 7900 XTX") == "amd");
            Banc.Verifie("RX 5700 XT (RDNA 1) : amd", true, ShadersPrecompiles.CarteEligible("AMD Radeon RX 5700 XT") == "amd");
            Banc.Verifie("RX 9070 XT (RDNA 4) : amd", true, ShadersPrecompiles.CarteEligible("AMD Radeon RX 9070 XT") == "amd");
            Banc.Verifie("RX 580 (Polaris) : non éligible", true, ShadersPrecompiles.CarteEligible("Radeon RX 580 Series") == null);
            Banc.Verifie("Radeon 890M : amd", true, ShadersPrecompiles.CarteEligible("AMD Radeon(TM) 890M Graphics") == "amd");
            Banc.Verifie("« AMD Radeon(TM) Graphics » : on se tait", true, ShadersPrecompiles.CarteEligible("AMD Radeon(TM) Graphics") == null);
            Banc.Verifie("Arc B580 : intel", true, ShadersPrecompiles.CarteEligible("Intel(R) Arc(TM) B580 Graphics") == "intel");
            Banc.Verifie("Arc 140V : intel", true, ShadersPrecompiles.CarteEligible("Intel(R) Arc(TM) 140V GPU (16GB)") == "intel");
            Banc.Verifie("Arc A770 : non éligible", true, ShadersPrecompiles.CarteEligible("Intel(R) Arc(TM) A770 Graphics") == null);
            Banc.Verifie("carte absente : null", true, ShadersPrecompiles.CarteEligible(null) == null);

            // ---- Windows
            string w = ShadersPrecompiles.WindowsTropAncien(22631, Rtx);
            Banc.Verifie("23H2 + RTX : signalé, 24H2 nommé, conseil sur le cache", true,
                w != null && w.Contains("24H2") && w.Contains("cache de shaders"));
            Banc.Verifie("24H2 + RTX : rien", true, ShadersPrecompiles.WindowsTropAncien(26100, Rtx) == null);
            Banc.Verifie("23H2 + GTX : rien (Windows à jour n'y changerait rien)", true,
                ShadersPrecompiles.WindowsTropAncien(22631, "NVIDIA GeForce GTX 1080") == null);
            Banc.Verifie("build illisible : rien", true, ShadersPrecompiles.WindowsTropAncien(0, Rtx) == null);

            // ---- Pilote
            Banc.Verifie("PC de test (25H2, RTX, 621.01) : rien", true,
                ShadersPrecompiles.PiloteTropAncien(26200, Rtx, "32.0.16.2101", null) == null);
            string p = ShadersPrecompiles.PiloteTropAncien(26200, Rtx, "32.0.15.8194", null);
            Banc.Verifie("25H2 + RTX + 581.94 : signalé, 621.01 nommé", true, p != null && p.Contains("621.01"));
            Banc.Verifie("23H2 : le pilote n'est pas jugé (constat Windows à la place)", true,
                ShadersPrecompiles.PiloteTropAncien(22631, Rtx, "32.0.15.8194", null) == null);
            Banc.Verifie("version NVIDIA illisible : rien", true, ShadersPrecompiles.PiloteTropAncien(26200, Rtx, "abc", null) == null);
            Banc.Verifie("AMD Adrenalin 26.5.2 : signalé", true,
                ShadersPrecompiles.PiloteTropAncien(26200, "AMD Radeon RX 7800 XT", "32.0.21025.1024", "26.5.2") != null);
            Banc.Verifie("AMD Adrenalin 26.6.1 : rien", true,
                ShadersPrecompiles.PiloteTropAncien(26200, "AMD Radeon RX 7800 XT", "32.0.21025.1024", "26.6.1") == null);
            Banc.Verifie("AMD 27.1.1 : rien", true,
                ShadersPrecompiles.PiloteTropAncien(26200, "AMD Radeon RX 7800 XT", null, "27.1.1") == null);
            Banc.Verifie("AMD version Adrenalin inconnue : rien", true,
                ShadersPrecompiles.PiloteTropAncien(26200, "AMD Radeon RX 7800 XT", "32.0.21025.1024", null) == null);
            Banc.Verifie("Intel 32.0.101.8509 : signalé", true,
                ShadersPrecompiles.PiloteTropAncien(26200, "Intel(R) Arc(TM) B580 Graphics", "32.0.101.8509", null) != null);
            Banc.Verifie("Intel 32.0.101.9150 : rien", true,
                ShadersPrecompiles.PiloteTropAncien(26200, "Intel(R) Arc(TM) B580 Graphics", "32.0.101.9150", null) == null);
            Banc.Verifie("carte non éligible : rien", true,
                ShadersPrecompiles.PiloteTropAncien(26200, "NVIDIA GeForce GTX 1660", "32.0.15.5222", null) == null);

            // ---- Comparaison de versions
            Banc.Verifie("26.10.1 > 26.6.1 (numérique, pas texte)", true, ShadersPrecompiles.Compare("26.10.1", new[] { 26, 6, 1 }) > 0);
            Banc.Verifie("version trop courte : illisible", true, ShadersPrecompiles.Compare("26.6", new[] { 26, 6, 1 }) == int.MinValue);
        }
    }
}
