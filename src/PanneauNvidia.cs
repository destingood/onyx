using System;
using System.Collections.Generic;
using System.IO;

namespace BTOptimizer
{
    /// <summary>
    /// OUVRIR LE PANNEAU NVIDIA — celui qui existe, pas celui d'il y a dix ans.
    ///
    /// Trois boutons de l'application lançaient « nvcpl.cpl » (guides Streamer, 500 FPS et
    /// Latence). Ce fichier est l'ancien applet du
    /// Panneau de configuration, retiré des pilotes NVIDIA depuis des années. Vérifié sur cette
    /// machine : absent de System32 comme de SysWOW64. L'échec était avalé par un catch et le
    /// bouton ne faisait donc RIEN — sans message, sans trace.
    ///
    /// Ce que NVIDIA installe aujourd'hui, relevé sur cette machine :
    ///   · « NVIDIA App »            → C:\Program Files\NVIDIA Corporation\NVIDIA App\CEF\NVIDIA App.exe
    ///   · « NVIDIA Control Panel »  → application du Store, lancée par son identifiant
    ///
    /// On essaie donc plusieurs pistes, des plus vérifiables (un fichier qui existe) aux plus
    /// incertaines (un identifiant d'application), et on rend compte de l'échec au lieu de le taire.
    /// </summary>
    internal static class PanneauNvidia
    {
        /// <summary>Identifiant de l'application Store du panneau NVIDIA. La partie après le
        /// souligné est l'identifiant d'éditeur de NVIDIA : il est le même sur toutes les
        /// machines, ce n'est pas une valeur propre à ce poste.</summary>
        private const string AumidPanneau =
            "NVIDIACorp.NVIDIAControlPanel_56jybvy8sckqj!NVIDIACorp.NVIDIAControlPanel";

        private const string AumidApp = "com.nvidia.nvapp";

        /// <summary>PUR : les chemins d'exécutables à tester, dans l'ordre.</summary>
        public static List<string> Executables()
        {
            var l = new List<string>();
            foreach (Environment.SpecialFolder dossier in
                     new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
            {
                string racine;
                try { racine = Environment.GetFolderPath(dossier); }
                catch { continue; }
                if (string.IsNullOrEmpty(racine)) continue;
                string nv = Path.Combine(racine, "NVIDIA Corporation");
                l.Add(Path.Combine(nv, @"Control Panel Client\nvcplui.exe"));
                l.Add(Path.Combine(nv, @"NVIDIA App\CEF\NVIDIA App.exe"));
                l.Add(Path.Combine(nv, @"NVIDIA App\NVIDIA App.exe"));
            }
            return l;
        }

        /// <summary>Premier exécutable réellement présent, null si aucun.</summary>
        public static string Trouve()
        {
            foreach (string p in Executables())
                try { if (File.Exists(p)) return p; } catch { }
            return null;
        }

        /// <summary>
        /// Ouvre le panneau NVIDIA. Rend faux si rien n'a pu être lancé — l'appelant doit le dire
        /// à l'utilisateur plutôt que de laisser un bouton sans effet.
        /// </summary>
        public static bool Ouvrir()
        {
            string exe = Trouve();
            if (exe != null && Lance(exe, null)) return true;

            // Applications modernes : on passe par l'explorateur, seul moyen de lancer par
            // identifiant. On ne peut pas vérifier leur réussite — d'où l'ordre : les chemins
            // vérifiables d'abord.
            foreach (string aumid in new[] { AumidPanneau, AumidApp })
                if (Lance("explorer.exe", "shell:AppsFolder\\" + aumid)) return true;

            // Dernier recours : l'applet historique, au cas où une très vieille installation
            // l'aurait encore.
            return Lance("nvcpl.cpl", null);
        }

        private static bool Lance(string fichier, string arguments)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(fichier) { UseShellExecute = true };
                if (!string.IsNullOrEmpty(arguments)) psi.Arguments = arguments;
                return System.Diagnostics.Process.Start(psi) != null;
            }
            catch { return false; }
        }
    }
}
