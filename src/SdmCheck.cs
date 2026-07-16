using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using Microsoft.Win32;

namespace BTOptimizer
{
    /// <summary>
    /// Détection et neutralisation de « Samsung Display Manager » (SDM), l'appli
    /// compagnon des moniteurs Samsung récents (Odyssey, ViewFinity, Smart Monitor).
    /// Inutile pour CoreSync (l'éclairage est calculé par le moniteur) mais tourne en
    /// fond avec un runtime IA (ONNX) embarqué. Son installeur propose aussi le
    /// service « MAPT » (pont réseau B2B via la prise LAN du moniteur) : sans intérêt
    /// à la maison et source potentielle de perturbations réseau.
    /// </summary>
    internal static class SdmCheck
    {
        public class Status
        {
            public int Running;             // processus SDM / MaptHelper en cours
            public bool StartupEnabled;     // Run, dossier Démarrage ou tâche planifiée
            public bool Installed;          // dossier Program Files présent
            public bool MaptInstalled;      // service MAPT enregistré
            public bool MaptRunning;        // service MAPT en cours d'exécution
            public string MaptServiceName;  // nom réel du service (pour l'arrêt)
            public List<string> Tasks = new List<string>(); // tâches planifiées SDM
        }

        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunKey32 = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
        private static readonly string[] ProcNames = { "samsungdisplaymanager", "mapthelper" };

        public static Status Probe()
        {
            var st = new Status();
            st.Installed = InstallDir() != null;

            List<Process> procs = FindProcesses();
            st.Running = procs.Count;
            foreach (Process p in procs) { try { p.Dispose(); } catch { } }

            ProbeMaptService(st);

            // Démarrage automatique : seulement si le logiciel est présent (évite
            // d'énumérer les tâches planifiées sur les machines non concernées).
            if (st.Installed || st.Running > 0 || st.MaptInstalled)
            {
                if (HasRunEntry(Registry.CurrentUser, RunKey) || HasRunEntry(Registry.LocalMachine, RunKey)
                    || HasRunEntry(Registry.LocalMachine, RunKey32) || FindStartupLinks().Count > 0)
                    st.StartupEnabled = true;
                st.Tasks = FindScheduledTasks();
                if (st.Tasks.Count > 0) st.StartupEnabled = true;
            }
            return st;
        }

        /// <summary>Ferme SDM, coupe son démarrage automatique et désactive MAPT. Retourne le nombre d'actions.</summary>
        public static int Disable(Action<string, int> log)
        {
            int actions = 0;

            foreach (Process p in FindProcesses())
            {
                try
                {
                    string n = p.ProcessName;
                    p.Kill();
                    p.WaitForExit(3000);
                    actions++;
                    if (log != null) log("SDM : processus " + n + " (PID " + p.Id + ") arrêté.", 1);
                }
                catch (Exception ex) { if (log != null) log("SDM : arrêt du processus impossible (" + ex.Message + ").", 3); }
                finally { try { p.Dispose(); } catch { } }
            }

            actions += RemoveRunEntries(Registry.CurrentUser, RunKey, log);
            actions += RemoveRunEntries(Registry.LocalMachine, RunKey, log);
            actions += RemoveRunEntries(Registry.LocalMachine, RunKey32, log);

            // Raccourcis des dossiers Démarrage : renommés en .disabled (réversible)
            foreach (string lnk in FindStartupLinks())
            {
                try
                {
                    File.Move(lnk, lnk + ".disabled");
                    actions++;
                    if (log != null) log("SDM : raccourci de démarrage désactivé (" + Path.GetFileName(lnk) + ").", 1);
                }
                catch (Exception ex) { if (log != null) log("SDM : raccourci non désactivé (" + ex.Message + ").", 3); }
            }

            foreach (string task in FindScheduledTasks())
            {
                NativeResult r = Sys.Run(Sys.Sys32("schtasks.exe"), "/change /tn \"" + task + "\" /disable");
                if (r.ExitCode == 0)
                {
                    actions++;
                    if (log != null) log("SDM : tâche planifiée « " + task + " » désactivée.", 1);
                }
                else if (log != null) log("SDM : tâche « " + task + " » non désactivée (code " + r.ExitCode + ").", 3);
            }

            // Service MAPT : arrêt + démarrage désactivé (réversible : start= demand)
            var st = new Status();
            ProbeMaptService(st);
            if (st.MaptServiceName != null)
            {
                if (st.MaptRunning) Sys.StopService(st.MaptServiceName);
                NativeResult r = Sys.Run(Sys.Sys32("sc.exe"), "config \"" + st.MaptServiceName + "\" start= disabled");
                if (r.ExitCode == 0)
                {
                    actions++;
                    if (log != null) log("SDM : service MAPT (" + st.MaptServiceName + ") arrêté et désactivé.", 1);
                }
                else if (log != null) log("SDM : service MAPT non désactivé (code " + r.ExitCode + ").", 3);
            }

            return actions;
        }

        // ------------------------------------------------------------------
        //  Détection
        // ------------------------------------------------------------------

        private static string InstallDir()
        {
            string[] roots =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            };
            foreach (string root in roots)
            {
                if (string.IsNullOrEmpty(root)) continue;
                string dir = Path.Combine(root, "Samsung", "Samsung Display Manager");
                try { if (Directory.Exists(dir)) return dir; } catch { }
            }
            return null;
        }

        private static bool IsSdmName(string n)
        {
            string low = (n ?? "").ToLowerInvariant();
            foreach (string p in ProcNames) if (low == p) return true;
            return false;
        }

        private static List<Process> FindProcesses()
        {
            var list = new List<Process>();
            int self = 0;
            try { self = Process.GetCurrentProcess().Id; } catch { }
            foreach (Process p in Process.GetProcesses())
            {
                bool keep = false;
                try { keep = p.Id != self && IsSdmName(p.ProcessName); } catch { }
                if (keep) list.Add(p);
                else { try { p.Dispose(); } catch { } }
            }
            return list;
        }

        private static void ProbeMaptService(Status st)
        {
            try
            {
                using (var s = new ManagementObjectSearcher(
                           "SELECT Name,State,PathName FROM Win32_Service"))
                {
                    foreach (ManagementObject mo in s.Get())
                    {
                        string path = (Convert.ToString(mo["PathName"]) ?? "").ToLowerInvariant();
                        string name = Convert.ToString(mo["Name"]) ?? "";
                        if (!path.Contains("maptservice") && !name.ToLowerInvariant().Contains("mapt")) continue;
                        st.MaptInstalled = true;
                        st.MaptServiceName = name;
                        if (string.Equals(Convert.ToString(mo["State"]), "Running", StringComparison.OrdinalIgnoreCase))
                            st.MaptRunning = true;
                        break;
                    }
                }
            }
            catch { }
        }

        private static bool IsSdmRunValue(RegistryKey k, string name)
        {
            string all = (name + " " + (Convert.ToString(k.GetValue(name)) ?? "")).ToLowerInvariant();
            return all.Contains("samsungdisplaymanager") || all.Contains("samsung display manager")
                || all.Contains("sdmrun");
        }

        private static bool HasRunEntry(RegistryKey hive, string sub)
        {
            try
            {
                using (RegistryKey k = hive.OpenSubKey(sub))
                {
                    if (k == null) return false;
                    foreach (string name in k.GetValueNames())
                        if (IsSdmRunValue(k, name)) return true;
                }
            }
            catch { }
            return false;
        }

        private static int RemoveRunEntries(RegistryKey hive, string sub, Action<string, int> log)
        {
            if (!HasRunEntry(hive, sub)) return 0;
            int n = 0;
            try
            {
                using (RegistryKey k = hive.OpenSubKey(sub, true))
                {
                    if (k == null) return 0;
                    foreach (string name in k.GetValueNames())
                        if (IsSdmRunValue(k, name))
                        {
                            k.DeleteValue(name, false);
                            n++;
                            if (log != null) log("SDM : entrée de démarrage « " + name + " » retirée.", 1);
                        }
                }
            }
            catch (Exception ex) { if (log != null) log("SDM : nettoyage de " + sub + " impossible (" + ex.Message + ").", 3); }
            return n;
        }

        private static List<string> FindStartupLinks()
        {
            var found = new List<string>();
            Environment.SpecialFolder[] folders = { Environment.SpecialFolder.Startup, Environment.SpecialFolder.CommonStartup };
            foreach (Environment.SpecialFolder f in folders)
            {
                try
                {
                    string dir = Environment.GetFolderPath(f);
                    if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                    foreach (string lnk in Directory.GetFiles(dir, "*.lnk"))
                    {
                        string low = Path.GetFileName(lnk).ToLowerInvariant();
                        if (low.Contains("samsung display") || low.Contains("samsungdisplaymanager") || low.Contains("sdm"))
                            found.Add(lnk);
                    }
                }
                catch { }
            }
            return found;
        }

        private static List<string> FindScheduledTasks()
        {
            var found = new List<string>();
            try
            {
                NativeResult r = Sys.Run(Sys.Sys32("schtasks.exe"), "/query /fo csv /nh");
                if (r.ExitCode != 0) return found;
                foreach (string line in r.Output.Split('\n'))
                {
                    if (line.IndexOf("samsung", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    string low = line.ToLowerInvariant();
                    if (!low.Contains("display") && !low.Contains("sdm") && !low.Contains("mapt")) continue;
                    // 1re colonne CSV = "\Nom de la tâche"
                    string col = line.TrimStart().Split(',')[0].Trim().Trim('"');
                    if (col.Length > 1 && !found.Contains(col)) found.Add(col);
                }
            }
            catch { }
            return found;
        }
    }
}
