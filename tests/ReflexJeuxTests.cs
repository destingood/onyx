namespace BTOptimizer.Tests
{
    /// <summary>
    /// Reflex et Anti-Lag 2 coupés dans Overwatch, Battlefield 6 et Black Ops 6. Les extraits
    /// viennent des fichiers relus sur le PC de test (21/11/2026) ; on ne signale que « coupé ».
    /// </summary>
    internal static class ReflexJeuxTests
    {
        private const string Rtx = "NVIDIA GeForce RTX 5090";
        private const string Rx = "AMD Radeon RX 9070 XT";

        // Extraits réels (valeurs du PC de test : tout actif).
        private const string Ow = "[Render.13]\r\nFrameRateCap = \"600\"\r\nReflexMode = \"2\"\r\nShowFPSCounter = \"1\"\r\n";
        private const string Bf = "GstRender.AMDLowLatency 1\r\nGstRender.IntelLowLatency 1\r\nGstRender.NvidiaLowLatency 2\r\n";
        private const string Cod = "// Enables AMD Antilag 2.\r\nAmdAntilag2@0;57752;48403 = false\r\n"
                                 + "NvidiaReflex@0;33761;11445 = Enabled // one of Disabled, Enabled, Enabled + boost\r\n";

        public static void Tout()
        {
            Banc.Titre("Reflex et Anti-Lag 2 dans les jeux");

            // ---- Lecture
            Banc.Verifie("Overwatch ReflexMode 2 : actif", true, ReflexJeux.OverwatchReflex(Ow) == true);
            Banc.Verifie("Overwatch ReflexMode 0 : coupé", true, ReflexJeux.OverwatchReflex(Ow.Replace("\"2\"", "\"0\"")) == false);
            Banc.Verifie("Overwatch sans clé : inconnu", true, ReflexJeux.OverwatchReflex("[Render.13]\r\n") == null);
            Banc.Verifie("Overwatch fichier absent : inconnu", true, ReflexJeux.OverwatchReflex(null) == null);

            Banc.Verifie("BF6 NvidiaLowLatency 2 : actif", true, ReflexJeux.Battlefield6Reflex(Bf) == true);
            Banc.Verifie("BF6 NvidiaLowLatency 0 : coupé", true, ReflexJeux.Battlefield6Reflex(Bf.Replace("NvidiaLowLatency 2", "NvidiaLowLatency 0")) == false);
            Banc.Verifie("BF6 AMDLowLatency 1 : actif", true, ReflexJeux.Battlefield6AntiLag(Bf) == true);
            Banc.Verifie("BF6 AMDLowLatency 0 : coupé", true, ReflexJeux.Battlefield6AntiLag(Bf.Replace("AMDLowLatency 1", "AMDLowLatency 0")) == false);
            Banc.Verifie("BF6 IntelLowLatency ne compte pas", true, ReflexJeux.Battlefield6AntiLag("GstRender.IntelLowLatency 0\r\n") == null);

            Banc.Verifie("BO6 Reflex Enabled : actif", true, ReflexJeux.Bo6Reflex(Cod) == true);
            Banc.Verifie("BO6 Reflex Enabled + boost : actif", true, ReflexJeux.Bo6Reflex(Cod.Replace("= Enabled //", "= Enabled + boost //")) == true);
            Banc.Verifie("BO6 Reflex Disabled : coupé", true, ReflexJeux.Bo6Reflex(Cod.Replace("= Enabled //", "= Disabled //")) == false);
            Banc.Verifie("BO6 commentaire « Disabled » ignoré", true, ReflexJeux.Bo6Reflex(Cod) != false);
            Banc.Verifie("BO6 Anti-Lag 2 false : coupé", true, ReflexJeux.Bo6AntiLag2(Cod) == false);
            Banc.Verifie("BO6 Anti-Lag 2 true : actif", true, ReflexJeux.Bo6AntiLag2(Cod.Replace("= false", "= true")) == true);
            Banc.Verifie("BO6 fichier absent : inconnu", true, ReflexJeux.Bo6Reflex(null) == null && ReflexJeux.Bo6AntiLag2(null) == null);

            // ---- Marque
            Banc.Verifie("marque RTX : nvidia", true, ReflexJeux.Marque(Rtx) == "nvidia");
            Banc.Verifie("marque RX : amd", true, ReflexJeux.Marque(Rx) == "amd");
            Banc.Verifie("marque Intel : aucune", true, ReflexJeux.Marque("Intel(R) Arc(TM) B580 Graphics") == null);

            // ---- Constats
            Banc.Verifie("PC de test (RTX, tout actif) : rien", true,
                ReflexJeux.Constat("Overwatch", Rtx, ReflexJeux.OverwatchReflex(Ow), null, null) == null
                && ReflexJeux.Constat("Battlefield 6", Rtx, ReflexJeux.Battlefield6Reflex(Bf), ReflexJeux.Battlefield6AntiLag(Bf), "AMD Low Latency") == null
                && ReflexJeux.Constat("Black Ops 6", Rtx, ReflexJeux.Bo6Reflex(Cod), ReflexJeux.Bo6AntiLag2(Cod), "AMD Anti-Lag 2") == null);

            string nv = ReflexJeux.Constat("Overwatch", Rtx, false, null, null);
            Banc.Verifie("RTX + Reflex coupé : constat", true, nv != null && nv.Contains("NVIDIA Reflex est coupé dans Overwatch"));
            Banc.Verifie("RTX + Anti-Lag coupé : rien (pas sa marque)", true, ReflexJeux.Constat("Black Ops 6", Rtx, true, false, "AMD Anti-Lag 2") == null);

            string amd = ReflexJeux.Constat("Black Ops 6", Rx, true, false, "AMD Anti-Lag 2");
            Banc.Verifie("RX + Anti-Lag 2 coupé : constat", true, amd != null && amd.Contains("Black Ops 6") && amd.Contains("AMD Anti-Lag 2"));
            Banc.Verifie("RX + Reflex coupé : rien", true, ReflexJeux.Constat("Black Ops 6", Rx, false, true, "AMD Anti-Lag 2") == null);
            Banc.Verifie("clé inconnue : rien", true, ReflexJeux.Constat("Overwatch", Rtx, null, null, null) == null);
            Banc.Verifie("carte inconnue : rien", true, ReflexJeux.Constat("Overwatch", null, false, false, null) == null);
        }
    }
}
