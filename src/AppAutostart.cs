using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BTOptimizer
{
    /// <summary>
    /// Démarrage automatique avec Windows + redémarrage de l'explorateur — deux fonctions
    /// reprises du concurrent (page Plan : is/enable/disable_autostart ; fix restart_explorer).
    /// Autostart via la clé Run de l'utilisateur courant (aucun droit admin requis).
    /// </summary>
    internal static class AppAutostart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "Fluide";

        public static bool IsEnabled()
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue(ValueName) != null; }
            catch { return false; }
        }

        /// <summary>Commande de lancement robuste : gère le lancement direct (.exe) ET via l'hôte
        /// dotnet (dossier framework-dependent) pour rester compatible Smart App Control.</summary>
        public static string LaunchCommand()
        {
            string exe = null;
            try { exe = Environment.ProcessPath; } catch { }
            if (string.IsNullOrEmpty(exe)) exe = Application.ExecutablePath;
            if (exe != null && exe.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            {
                string dll = null;
                try { var a = Assembly.GetEntryAssembly(); if (a != null) dll = a.Location; } catch { }
                if (!string.IsNullOrEmpty(dll)) return "\"" + exe + "\" \"" + dll + "\"";
            }
            return "\"" + exe + "\"";
        }

        public static bool SetEnabled(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (k == null) return false;
                    if (on) k.SetValue(ValueName, LaunchCommand());
                    else k.DeleteValue(ValueName, false);
                }
                return true;
            }
            catch { return false; }
        }

        /// <summary>Redémarre explorer.exe : rafraîchit le shell Windows (barre des tâches, icônes)
        /// après des réglages, ou débloque une barre des tâches figée. À exécuter en arrière-plan.</summary>
        public static void RestartExplorer()
        {
            try
            {
                foreach (Process p in Process.GetProcessesByName("explorer"))
                {
                    try { p.Kill(); } catch { }
                }
                System.Threading.Thread.Sleep(700);
                // Windows relance normalement explorer tout seul ; sinon on le relance.
                if (Process.GetProcessesByName("explorer").Length == 0)
                {
                    try { Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true }); } catch { }
                }
            }
            catch { }
        }
    }
}
