using System;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BTOptimizer
{
    /// <summary>
    /// DÉMARRAGE AUTOMATIQUE AVEC WINDOWS — et pourquoi la clé « Run » ne pouvait pas marcher.
    ///
    /// L'ancienne version inscrivait ONYX dans HKCU\...\Run, avec ce commentaire : « aucun droit
    /// admin requis ». C'était exactement le problème. ONYX déclare dans son manifeste qu'il EXIGE
    /// les droits administrateur — et une entrée « Run » est traitée à l'ouverture de session dans
    /// le contexte NON élevé de l'utilisateur. Windows ne peut pas l'élever à ce moment-là : il
    /// n'affiche aucune demande d'autorisation au démarrage, et passe simplement l'entrée.
    ///
    /// Résultat : l'entrée existait, elle était marquée « activée » dans le Gestionnaire des tâches,
    /// et l'application ne démarrait jamais. Aucun message, aucune erreur — le pire cas.
    ///
    /// LA BONNE MÉTHODE est une tâche planifiée déclenchée à l'ouverture de session, créée avec le
    /// niveau d'exécution le plus élevé. C'est le seul chemin qui lance une application élevée sans
    /// demander d'autorisation à chaque démarrage. Le projet l'utilisait DÉJÀ pour son gardien de
    /// profil : la machinerie était là, l'autostart ne s'en servait pas.
    ///
    /// Le lancement est retardé de trente secondes. Une application dont le sujet est la latence n'a
    /// aucune raison de se disputer le disque et le processeur avec le reste de l'ouverture de
    /// session : elle n'a rien d'urgent à faire dans les premières secondes.
    /// </summary>
    internal static class AppAutostart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "ONYX";
        private const string LegacyName = "Fluide";     // renommage v14.54
        private const string Tache = "ONYXDemarrage";

        /// <summary>Retard au démarrage, en minutes:secondes (format attendu par schtasks).</summary>
        private const string Retard = "0000:30";

        // ------------------------------------------------------------------ pur

        /// <summary>Argument /tr de schtasks pour un exécutable donné. PUR — c'est la partie qui se
        /// trompe le plus facilement : le chemin doit rester entouré de guillemets À L'INTÉRIEUR de
        /// l'argument, sinon un dossier contenant une espace casse la tâche en silence.</summary>
        public static string ArgumentTache(string exe)
        {
            if (string.IsNullOrEmpty(exe)) return "";
            return "\"\\\"" + exe + "\\\"\"";
        }

        /// <summary>Commande de lancement PURE, pour la clé Run héritée et les diagnostics. Gère le
        /// lancement direct (.exe) ET via l'hôte dotnet (dossier dépendant du runtime).</summary>
        public static string LaunchCommand()
        {
            string exe = Executable();
            if (exe != null && exe.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            {
                string dll = null;
                try { var a = Assembly.GetEntryAssembly(); if (a != null) dll = a.Location; } catch { }
                if (!string.IsNullOrEmpty(dll)) return "\"" + exe + "\" \"" + dll + "\"";
            }
            return "\"" + exe + "\"";
        }

        private static string Executable()
        {
            string exe = null;
            try { exe = Environment.ProcessPath; } catch { }
            if (string.IsNullOrEmpty(exe)) exe = Application.ExecutablePath;
            return exe;
        }

        // ------------------------------------------------------------------ machine

        /// <summary>La tâche planifiée existe-t-elle ?</summary>
        private static bool TacheExiste()
        {
            try { return Sys.Run(Sys.Sys32("schtasks.exe"), "/query /tn " + Tache).ExitCode == 0; }
            catch { return false; }
        }

        /// <summary>Supprime les vieilles entrées « Run », qui ne pouvaient de toute façon pas
        /// lancer une application élevée. Les laisser entretiendrait l'illusion que c'est actif.</summary>
        private static void PurgeRun()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    try { k.DeleteValue(ValueName, false); } catch { }
                    try { k.DeleteValue(LegacyName, false); } catch { }
                }
            }
            catch { }
        }

        public static bool IsEnabled()
        {
            return TacheExiste();
        }

        /// <summary>
        /// Active ou désactive le démarrage automatique. Rend false si la tâche n'a pas pu être
        /// créée — et dans ce cas on ne retombe PAS sur la clé « Run » : elle ne marcherait pas, et
        /// afficher « activé » sans que ça démarre est précisément le défaut qu'on corrige.
        /// </summary>
        public static bool SetEnabled(bool on)
        {
            PurgeRun();   // dans les deux sens : cette entrée n'a jamais rien lancé
            if (!on)
            {
                try { Sys.Run(Sys.Sys32("schtasks.exe"), "/delete /f /tn " + Tache); }
                catch { }
                return !TacheExiste();
            }

            string exe = Executable();
            if (string.IsNullOrEmpty(exe)) return false;
            try
            {
                NativeResult r = Sys.Run(Sys.Sys32("schtasks.exe"),
                    "/create /f /rl HIGHEST /sc ONLOGON /delay " + Retard
                    + " /tn " + Tache + " /tr " + ArgumentTache(exe));
                if (r.ExitCode == 0) return true;

                // Certaines versions de schtasks refusent /delay avec ONLOGON : on retente sans.
                r = Sys.Run(Sys.Sys32("schtasks.exe"),
                    "/create /f /rl HIGHEST /sc ONLOGON /tn " + Tache + " /tr " + ArgumentTache(exe));
                return r.ExitCode == 0;
            }
            catch { return false; }
        }

        /// <summary>Redémarre l'explorateur Windows (dépannage d'interface).</summary>
        public static void RestartExplorer(Action<string, int> log = null)
        {
            try
            {
                foreach (var p in System.Diagnostics.Process.GetProcessesByName("explorer"))
                    try { p.Kill(); } catch { }
                System.Threading.Thread.Sleep(600);
                try { System.Diagnostics.Process.Start(System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")); }
                catch { }
                if (log != null) log("Explorateur Windows redémarré.", 1);
            }
            catch (Exception ex) { if (log != null) log("Redémarrage de l'explorateur impossible : " + ex.Message, 3); }
        }
    }
}
