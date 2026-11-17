namespace BTOptimizer.Tests
{
    /// <summary>
    /// Table des problèmes connus : une mise à jour Windows précise associée à un pilote trop
    /// ancien. Lecture seule ; chaque entrée se tait dès qu'une condition manque.
    /// </summary>
    internal static class ProblemesConnusTests
    {
        private const string Rtx = "NVIDIA GeForce RTX 4080 SUPER";

        public static void Tout()
        {
            Banc.Titre("Problèmes connus (mise à jour Windows + pilote)");

            // ---- Version NVIDIA lue dans la version Windows du pilote
            Banc.Verifie("32.0.15.8194 = 581.94", true, ProblemesConnus.VersionNvidia("32.0.15.8194") == 58194);
            Banc.Verifie("32.0.16.2101 = 621.01", true, ProblemesConnus.VersionNvidia("32.0.16.2101") == 62101);
            Banc.Verifie("31.0.15.5222 = 552.22", true, ProblemesConnus.VersionNvidia("31.0.15.5222") == 55222);
            Banc.Verifie("version illisible : -1", true, ProblemesConnus.VersionNvidia("abc") == -1);
            Banc.Verifie("version absente : -1", true, ProblemesConnus.VersionNvidia(null) == -1);
            Banc.Verifie("trop peu de chiffres : -1", true, ProblemesConnus.VersionNvidia("1.2") == -1);

            // ---- KB5066835 (octobre 2025, builds 26100.6899 et 26200.6899) + NVIDIA avant 581.94
            string k = ProblemesConnus.Constat(26200, 6899, Rtx, "32.0.15.8180");
            Banc.Verifie("25H2 avec KB5066835 + pilote 581.80 : signalé, KB et pilote correctif nommés", true,
                k != null && k.Contains("KB5066835") && k.Contains("581.94"));
            Banc.Verifie("24H2 avec KB5066835 + pilote ancien : signalé", true,
                ProblemesConnus.Constat(26100, 7000, Rtx, "32.0.15.8180") != null);
            Banc.Verifie("pilote 581.94 : rien", true, ProblemesConnus.Constat(26200, 6899, Rtx, "32.0.15.8194") == null);
            Banc.Verifie("pilote plus récent (621.01) : rien", true, ProblemesConnus.Constat(26200, 9974, Rtx, "32.0.16.2101") == null);
            Banc.Verifie("Windows avant la mise à jour (6898) : rien", true, ProblemesConnus.Constat(26200, 6898, Rtx, "32.0.15.8180") == null);
            Banc.Verifie("Windows 23H2 : rien", true, ProblemesConnus.Constat(22631, 9999, Rtx, "32.0.15.8180") == null);
            Banc.Verifie("carte AMD : rien", true, ProblemesConnus.Constat(26200, 6899, "AMD Radeon RX 7900 XT", "32.0.15.8180") == null);
            Banc.Verifie("pilote illisible : rien (pas d'affirmation sans lecture)", true,
                ProblemesConnus.Constat(26200, 6899, Rtx, "") == null);
        }
    }
}
