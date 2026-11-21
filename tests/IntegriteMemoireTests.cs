namespace BTOptimizer.Tests
{
    /// <summary>
    /// Intégrité de la mémoire : informer avec la date, laisser le choix, et ne pas accuser la
    /// mise à jour d'octobre 2026 quand ONYX l'avait coupée (Microsoft n'y touche pas).
    /// </summary>
    internal static class IntegriteMemoireTests
    {
        public static void Tout()
        {
            Banc.Titre("Intégrité de la mémoire : information");

            Banc.Verifie("inactive : rien", true, IntegriteMemoire.Constat(false, false) == null);
            Banc.Verifie("inactive après vbs_off : rien", true, IntegriteMemoire.Constat(false, true) == null);

            string defaut = IntegriteMemoire.Constat(true, false);
            Banc.Verifie("active d'office : constat", true, defaut != null);
            Banc.Verifie("active d'office : date citée", true, defaut != null && defaut.Contains("octobre 2026"));
            Banc.Verifie("active d'office : choix laissé", true, defaut != null && defaut.Contains("à toi de choisir"));
            Banc.Verifie("active d'office : pas coupée d'office", true, defaut != null && defaut.Contains("ne la coupe pas d'office"));

            string remise = IntegriteMemoire.Constat(true, true);
            Banc.Verifie("remise après vbs_off : constat", true, remise != null);
            Banc.Verifie("remise : ONYX l'avait coupée", true, remise != null && remise.Contains("ONYX l'avait coupée"));
            Banc.Verifie("remise : la mise à jour n'est pas accusée", true, remise != null && remise.Contains("Ce n'est pas la mise à jour d'octobre 2026"));
            Banc.Verifie("remise : choix laissé", true, remise != null && remise.Contains("à toi de choisir"));
            Banc.Verifie("deux textes différents", true, defaut != remise);

            Banc.Verifie("vbs_off : avis séparé (hors lot de dérive)", true, IntegriteMemoire.AvisSepare("vbs_off"));
            Banc.Verifie("hpet_off : reste dans le lot", false, IntegriteMemoire.AvisSepare("hpet_off"));
            Banc.Verifie("id nul : reste dans le lot", false, IntegriteMemoire.AvisSepare(null));
        }
    }
}
