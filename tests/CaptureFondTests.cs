namespace BTOptimizer.Tests
{
    /// <summary>
    /// Vérifie <see cref="CaptureFond"/> — état de la capture des 30 dernières secondes de Windows
    /// (« Enregistrer ce qui s'est passé ») d'après le registre, et l'explication donnée au joueur.
    /// </summary>
    internal static class CaptureFondTests
    {
        public static void Tout()
        {
            Banc.Titre("Capture des 30 dernières secondes");

            Banc.Egal("stratégie AllowGameDVR = 0 : bloquée", "BloqueeParStrategie", CaptureFond.Juge(0, 1, 1).ToString());
            Banc.Egal("captures d'applis coupées", "Coupee", CaptureFond.Juge(null, 0, 1).ToString());
            Banc.Egal("historique coupé", "Coupee", CaptureFond.Juge(null, 1, 0).ToString());
            Banc.Egal("historique absent (défaut Windows : coupé)", "Coupee", CaptureFond.Juge(null, 1, null).ToString());
            Banc.Egal("tout allumé", "Active", CaptureFond.Juge(null, 1, 1).ToString());
            Banc.Egal("captures d'applis absentes (défaut Windows : permises)", "Active", CaptureFond.Juge(null, null, 1).ToString());
            Banc.Egal("stratégie à 1 : ne bloque pas", "Active", CaptureFond.Juge(1, 1, 1).ToString());

            string onyx = CaptureFond.Explique(CaptureFond.Etat.BloqueeParStrategie, true);
            Banc.Verifie("bloquée par ONYX : nomme le réglage et son bouton", true,
                onyx.Contains("Désactiver Game DVR") && onyx.Contains("Rétablir"));
            string autre = CaptureFond.Explique(CaptureFond.Etat.BloqueeParStrategie, false);
            Banc.Verifie("bloquée par autre chose qu'ONYX : le dit", true, autre.Contains("stratégie") && !autre.Contains("Désactiver Game DVR"));
            Banc.Verifie("coupée : rappelle le raccourci", true, CaptureFond.Explique(CaptureFond.Etat.Coupee, false).Contains("Win+Alt+G"));
            Banc.Verifie("active : coût non mesuré, pas de chiffre", true, CaptureFond.Explique(CaptureFond.Etat.Active, false).Contains("non mesuré"));
        }
    }
}
