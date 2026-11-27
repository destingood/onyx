using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BTOptimizer
{
    /// <summary>Serveurs Discord sur disque (bt-discord-serveurs.txt, dossier de données d'ONYX).</summary>
    internal static class ServeursDiscordReglages
    {
        private const string Fichier = "bt-discord-serveurs.txt";

        public static List<ServeursDiscord.Serveur> Lire()
        {
            try
            {
                string chemin = AppPaths.File(Fichier);
                if (File.Exists(chemin)) return ServeursDiscord.Lire(File.ReadAllText(chemin, Encoding.UTF8));
            }
            catch { }
            return new List<ServeursDiscord.Serveur>();
        }

        /// <summary>Écrit la liste (fichier temporaire puis remplacement) ; faux si l'écriture échoue.</summary>
        public static bool Enregistre(IEnumerable<ServeursDiscord.Serveur> liste)
        {
            try
            {
                string chemin = AppPaths.File(Fichier), tmp = chemin + ".tmp";
                File.WriteAllText(tmp, ServeursDiscord.Ecrire(liste), new UTF8Encoding(false));
                File.Move(tmp, chemin, true);
                return true;
            }
            catch { return false; }
        }
    }
}
