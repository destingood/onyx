using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Actions partagées sur un jeu, utilisées par la fiche détaillée ET par « Jeux dormants ».
    /// Regroupées ici pour qu'il n'existe QU'UNE seule implémentation de la désinstallation :
    /// c'est l'opération la plus risquée de l'app, elle ne doit pas exister en double.
    /// </summary>
    internal static class GameActions
    {
        /// <summary>
        /// Lance le désinstalleur OFFICIEL du jeu, après confirmation. ONYX ne supprime jamais
        /// de fichiers lui-même : seul l'éditeur sait quoi retirer et quoi CONSERVER (sauvegardes,
        /// profils, services). Renvoie true si un désinstalleur a bien été lancé.
        /// </summary>
        public static bool Uninstall(IWin32Window owner, string name, int steamAppId)
        {
            string via = steamAppId > 0 ? "Steam" : "le désinstalleur du jeu";
            if (MessageBox.Show(owner,
                    "Désinstaller « " + name + " » ?\n\n"
                    + "ONYX ne supprime aucun fichier lui-même : il ouvre " + via + ", qui fera le "
                    + "ménage proprement (fichiers, clés de registre, services).\n\n"
                    + "Si tu veux seulement ne plus le voir dans la liste, utilise « Masquer ».",
                    "Désinstaller", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                return false;

            try
            {
                if (steamAppId > 0)
                {
                    Process.Start(new ProcessStartInfo("steam://uninstall/" + steamAppId) { UseShellExecute = true });
                    return true;
                }

                string cmd = GameLibrary.UninstallCommand(name);
                if (!string.IsNullOrEmpty(cmd))
                {
                    // Lance le désinstalleur DIRECTEMENT (sans passer par cmd.exe). Faire lancer
                    // cmd.exe avec une commande dynamique par un exe non signé est un motif que
                    // Smart App Control / SmartScreen fichent comme suspect (« living off the land »).
                    string exe, args;
                    SplitCommand(cmd, out exe, out args);
                    Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true });
                    return true;
                }

                // Battle.net / EA app / Xbox ne declarent souvent aucun desinstalleur : on
                // l'explique au lieu d'echouer en silence, puis on ouvre la page Windows.
                MessageBox.Show(owner,
                    "Ce jeu ne déclare pas de désinstalleur dans Windows — c'est fréquent pour "
                    + "Battle.net, l'EA app et le Xbox Store.\n\n"
                    + "Désinstalle-le depuis son launcher, ou depuis la fenêtre Windows qui va s'ouvrir.",
                    "Désinstaller", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Process.Start(new ProcessStartInfo("ms-settings:appsfeatures") { UseShellExecute = true });
                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, "Impossible d'ouvrir le désinstalleur : " + ex.Message,
                    "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        /// <summary>Découpe une ligne de commande « "chemin\exe" args » en exe + arguments,
        /// pour lancer le désinstalleur directement (sans cmd.exe).</summary>
        private static void SplitCommand(string cmd, out string exe, out string args)
        {
            cmd = (cmd ?? "").Trim();
            if (cmd.StartsWith("\""))
            {
                int end = cmd.IndexOf('"', 1);
                if (end > 0) { exe = cmd.Substring(1, end - 1); args = cmd.Substring(end + 1).Trim(); return; }
            }
            int sp = cmd.IndexOf(' ');
            if (sp > 0) { exe = cmd.Substring(0, sp); args = cmd.Substring(sp + 1).Trim(); }
            else { exe = cmd; args = ""; }
        }

        /// <summary>Ouvre le dossier d'installation dans l'Explorateur.</summary>
        public static void OpenFolder(string installDir)
        {
            try
            {
                if (string.IsNullOrEmpty(installDir) || !System.IO.Directory.Exists(installDir)) return;
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + installDir + "\"") { UseShellExecute = true });
            }
            catch { }
        }
    }
}
