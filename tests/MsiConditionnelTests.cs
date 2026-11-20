namespace BTOptimizer.Tests
{
    /// <summary>
    /// Mode MSI conditionnel : un périphérique déjà en MSI n'est pas réécrit, et « Rétablir » ne
    /// supprime jamais une valeur MSISupported qu'ONYX n'a pas notée.
    /// </summary>
    internal static class MsiConditionnelTests
    {
        public static void Tout()
        {
            Banc.Titre("Mode MSI conditionnel");

            // ---- Lecture de MSISupported (REG_DWORD lu en int)
            Banc.Verifie("1 : déjà en MSI", true, MsiConditionnel.DejaEnMsi(1));
            Banc.Verifie("0 : pas en MSI", false, MsiConditionnel.DejaEnMsi(0));
            Banc.Verifie("valeur absente : pas en MSI", false, MsiConditionnel.DejaEnMsi(null));
            Banc.Verifie("chaîne \"1\" : pas un DWORD, pas en MSI", false, MsiConditionnel.DejaEnMsi("1"));

            // ---- État d'une famille de périphériques (tous, pas seulement le premier)
            Banc.Verifie("aucun périphérique : inconnu", true, MsiConditionnel.TousEnMsi(new object[0]) == null);
            Banc.Verifie("tous à 1 : actif", true, MsiConditionnel.TousEnMsi(new object[] { 1, 1 }) == true);
            Banc.Verifie("iGPU à 1 puis carte dédiée sans valeur : pas actif", true,
                MsiConditionnel.TousEnMsi(new object[] { 1, null }) == false);
            Banc.Verifie("premier sans valeur : pas actif", true, MsiConditionnel.TousEnMsi(new object[] { null, 1 }) == false);

            // ---- Bilan affiché après une activation
            string rien = MsiConditionnel.Bilan(0, 3);
            Banc.Verifie("tout déjà en MSI : « rien à faire », aucun redémarrage annoncé", true,
                rien.Contains("rien à faire") && !rien.Contains("Redémarrage"));
            string mixte = MsiConditionnel.Bilan(1, 2);
            Banc.Verifie("un passé en MSI, deux laissés : les deux comptes et le redémarrage", true,
                mixte.Contains("1 ") && mixte.Contains("2 ") && mixte.Contains("Redémarrage"));
            Banc.Verifie("aucun périphérique : le dit", true, MsiConditionnel.Bilan(0, 0).Contains("aucun"));
        }
    }
}
