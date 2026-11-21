namespace BTOptimizer.Tests
{
    /// <summary>
    /// Sauvegardes hors du Bureau : les sauvegardes automatiques (avant chaque application de
    /// réglages) et l'historique des mesures vont dans Documents\ONYX\Sauvegardes. Au lancement,
    /// ONYX y range ce qu'il avait laissé sur le Bureau, et seulement ça.
    /// </summary>
    internal static class SauvegardesTests
    {
        public static void Tout()
        {
            Banc.Verifie("dossier des sauvegardes : Documents\\ONYX\\Sauvegardes", true,
                Sauvegardes.Dossier(@"C:\Users\Lu\Documents") == @"C:\Users\Lu\Documents\ONYX\Sauvegardes");

            Banc.Verifie("sauvegarde d'ONYX reconnue", true, Sauvegardes.EstSauvegarde("bt-optimizer-backup-20261117-195155"));
            Banc.Verifie("casse indifférente", true, Sauvegardes.EstSauvegarde("BT-Optimizer-Backup-20261121-194432"));
            Banc.Verifie("dossier perso au nom voisin : pas touché", false, Sauvegardes.EstSauvegarde("bt-optimizer-backup-perso"));
            Banc.Verifie("copie renommée par l'utilisateur : pas touchée", false, Sauvegardes.EstSauvegarde("bt-optimizer-backup-20261117-195155 - Copie"));
            Banc.Verifie("autre dossier du Bureau : pas touché", false, Sauvegardes.EstSauvegarde("Mes jeux"));
            Banc.Verifie("nom vide", false, Sauvegardes.EstSauvegarde(""));
            Banc.Verifie("nom absent", false, Sauvegardes.EstSauvegarde(null));

            Banc.Verifie("historique des mesures reconnu", true, Sauvegardes.EstHistorique("bt-optimizer-mesures.txt"));
            Banc.Verifie("rapport exporté à la demande : reste sur le Bureau", false, Sauvegardes.EstHistorique("bt-optimizer-rapport.html"));
        }
    }
}
