using System;
using System.IO;
using System.Text.RegularExpressions;

namespace BTOptimizer
{
    /// <summary>
    /// Où vont les sauvegardes automatiques (une avant chaque application de réglages) et
    /// l'historique des mesures : Documents\ONYX\Sauvegardes, plus le Bureau. Documents et pas
    /// %LOCALAPPDATA%\ONYX : la désinstallation efface ce dernier, et une sauvegarde doit
    /// survivre à ONYX. Les exports demandés (rapport, profil, relevés) restent sur le Bureau.
    /// Partie pure ; le déplacement vit dans Sauvegardes.Machine.cs.
    /// </summary>
    internal static partial class Sauvegardes
    {
        public const string PrefixeDossier = "bt-optimizer-backup-";
        public const string Historique = "bt-optimizer-mesures.txt";

        // Nom exact posé par Sys.ExportBackup : préfixe + yyyyMMdd-HHmmss, rien d'autre.
        private static readonly Regex NomSauvegarde =
            new Regex(@"^bt-optimizer-backup-\d{8}-\d{6}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static string Dossier(string documents)
        {
            return Path.Combine(documents, "ONYX", "Sauvegardes");
        }

        /// <summary>Dossier créé par ONYX sur le Bureau (et seulement lui : un nom voisin ou renommé n'est pas touché).</summary>
        public static bool EstSauvegarde(string nomDossier)
        {
            return !string.IsNullOrEmpty(nomDossier) && NomSauvegarde.IsMatch(nomDossier);
        }

        public static bool EstHistorique(string nomFichier)
        {
            return string.Equals(nomFichier, Historique, StringComparison.OrdinalIgnoreCase);
        }
    }
}
