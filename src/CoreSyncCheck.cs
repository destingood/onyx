using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
using Microsoft.Win32;

namespace BTOptimizer
{
    /// <summary>
    /// Détection et neutralisation du logiciel Samsung CoreSync — l'appli compagnon qui
    /// synchronise l'éclairage arrière « Core Lighting » des moniteurs Odyssey (G6, G7,
    /// G8, G9, Neo, Ark) avec l'image. Pour teinter les LED, elle capture l'écran en
    /// continu : cause connue de micro-saccades, pertes de FPS et d'input lag en jeu.
    /// Attention : Adobe Creative Cloud installe aussi un « CoreSync.exe » (synchro
    /// cloud, sans rapport) — il est exclu via son chemin d'installation.
    /// </summary>
    internal static class CoreSyncCheck
    {
        public class Status
        {
            public int Running;             // nb de processus CoreSync (hors Adobe) en cours
            public bool StartupEnabled;     // lancé au démarrage de Windows (Run ou appli Store)
            public bool Installed;          // paquet Store / entrée de démarrage / processus présent
            public string SamsungMonitor;   // nom du moniteur Samsung détecté (null sinon)
        }

        // Clés HKCU des applis du Microsoft Store (paquets installés + état de démarrage).
        private const string PkgRepo =
            @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";
        private const string SysAppData =
            @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData";
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunKey32 = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";

        public static Status Probe()
        {
            var st = new Status();
            List<Process> procs = FindProcesses();
            st.Running = procs.Count;
            foreach (Process p in procs) { try { p.Dispose(); } catch { } }
            st.SamsungMonitor = DetectSamsungMonitor();
            ProbeInstallAndStartup(st);
            if (st.Running > 0) st.Installed = true;
            return st;
        }

        /// <summary>Ferme CoreSync et coupe son démarrage automatique. Retourne le nombre d'actions.</summary>
        public static int Disable(Action<string, int> log)
        {
            int actions = 0;

            // 1) Processus en cours
            foreach (Process p in FindProcesses())
            {
                try
                {
                    string n = p.ProcessName;
                    p.Kill();
                    p.WaitForExit(3000);
                    actions++;
                    if (log != null) log("CoreSync : processus " + n + " (PID " + p.Id + ") arrêté.", 1);
                }
                catch (Exception ex) { if (log != null) log("CoreSync : arrêt du processus impossible (" + ex.Message + ").", 3); }
                finally { try { p.Dispose(); } catch { } }
            }

            // 2) Entrées de démarrage classiques (Run) — HKCU et HKLM (+ vue 32 bits)
            actions += RemoveRunEntries(Registry.CurrentUser, RunKey, log);
            actions += RemoveRunEntries(Registry.LocalMachine, RunKey, log);
            actions += RemoveRunEntries(Registry.LocalMachine, RunKey32, log);

            // 3) Tâche de démarrage de l'appli Store (State : 2/4 = activée, 1 = désactivée
            //    par l'utilisateur — même mécanisme que Gestionnaire des tâches > Démarrage)
            try
            {
                using (RegistryKey root = Registry.CurrentUser.OpenSubKey(SysAppData, true))
                {
                    if (root != null)
                        foreach (string pkg in root.GetSubKeyNames())
                        {
                            if (pkg.IndexOf("coresync", StringComparison.OrdinalIgnoreCase) < 0) continue;
                            using (RegistryKey pk = root.OpenSubKey(pkg, true))
                            {
                                if (pk == null) continue;
                                foreach (string task in pk.GetSubKeyNames())
                                    using (RegistryKey tk = pk.OpenSubKey(task, true))
                                    {
                                        if (tk == null || !(tk.GetValue("State") is int)) continue;
                                        int state = (int)tk.GetValue("State");
                                        if (state == 2 || state == 4)
                                        {
                                            tk.SetValue("State", 1, RegistryValueKind.DWord);
                                            actions++;
                                            if (log != null) log("CoreSync : démarrage automatique de l'appli Store désactivé (" + pkg + ").", 1);
                                        }
                                    }
                            }
                        }
                }
            }
            catch (Exception ex) { if (log != null) log("CoreSync : lecture des applis de démarrage impossible (" + ex.Message + ").", 3); }

            return actions;
        }

        // ------------------------------------------------------------------
        //  Détection
        // ------------------------------------------------------------------

        private static List<Process> FindProcesses()
        {
            var list = new List<Process>();
            int self = 0;
            try { self = Process.GetCurrentProcess().Id; } catch { }
            foreach (Process p in Process.GetProcesses())
            {
                bool keep = false;
                try
                {
                    string n = p.ProcessName ?? "";
                    keep = p.Id != self
                        && n.IndexOf("coresync", StringComparison.OrdinalIgnoreCase) >= 0
                        && !IsAdobe(p);
                }
                catch { }
                if (keep) list.Add(p);
                else { try { p.Dispose(); } catch { } }
            }
            return list;
        }

        private static bool IsAdobe(Process p)
        {
            try
            {
                string path = p.MainModule != null ? (p.MainModule.FileName ?? "") : "";
                string low = path.ToLowerInvariant();
                return low.Contains("adobe") || low.Contains("creative cloud");
            }
            catch { return false; } // chemin illisible : on suppose Samsung (l'app tourne en admin)
        }

        private static void ProbeInstallAndStartup(Status st)
        {
            // Paquet Store installé ?
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(PkgRepo))
                {
                    if (k != null)
                        foreach (string name in k.GetSubKeyNames())
                            if (name.IndexOf("coresync", StringComparison.OrdinalIgnoreCase) >= 0)
                            { st.Installed = true; break; }
                }
            }
            catch { }

            // Tâche de démarrage de l'appli Store activée ?
            try
            {
                using (RegistryKey root = Registry.CurrentUser.OpenSubKey(SysAppData))
                {
                    if (root != null)
                        foreach (string pkg in root.GetSubKeyNames())
                        {
                            if (pkg.IndexOf("coresync", StringComparison.OrdinalIgnoreCase) < 0) continue;
                            st.Installed = true;
                            using (RegistryKey pk = root.OpenSubKey(pkg))
                            {
                                if (pk == null) continue;
                                foreach (string task in pk.GetSubKeyNames())
                                    using (RegistryKey tk = pk.OpenSubKey(task))
                                    {
                                        object v = tk == null ? null : tk.GetValue("State");
                                        if (v is int && ((int)v == 2 || (int)v == 4)) st.StartupEnabled = true;
                                    }
                            }
                        }
                }
            }
            catch { }

            // Entrées Run classiques ?
            if (HasRunEntry(Registry.CurrentUser, RunKey) || HasRunEntry(Registry.LocalMachine, RunKey)
                || HasRunEntry(Registry.LocalMachine, RunKey32))
            { st.Installed = true; st.StartupEnabled = true; }
        }

        private static bool IsCoreSyncRunValue(RegistryKey k, string name)
        {
            string data = Convert.ToString(k.GetValue(name)) ?? "";
            string all = (name + " " + data).ToLowerInvariant();
            return all.Contains("coresync") && !all.Contains("adobe");
        }

        private static bool HasRunEntry(RegistryKey hive, string sub)
        {
            try
            {
                using (RegistryKey k = hive.OpenSubKey(sub))
                {
                    if (k == null) return false;
                    foreach (string name in k.GetValueNames())
                        if (IsCoreSyncRunValue(k, name)) return true;
                }
            }
            catch { }
            return false;
        }

        private static int RemoveRunEntries(RegistryKey hive, string sub, Action<string, int> log)
        {
            if (!HasRunEntry(hive, sub)) return 0;   // n'ouvre en écriture que si nécessaire
            int n = 0;
            try
            {
                using (RegistryKey k = hive.OpenSubKey(sub, true))
                {
                    if (k == null) return 0;
                    foreach (string name in k.GetValueNames())
                        if (IsCoreSyncRunValue(k, name))
                        {
                            k.DeleteValue(name, false);
                            n++;
                            if (log != null) log("CoreSync : entrée de démarrage « " + name + " » retirée (" + hive.Name + "\\" + sub + ").", 1);
                        }
                }
            }
            catch (Exception ex) { if (log != null) log("CoreSync : nettoyage de " + sub + " impossible (" + ex.Message + ").", 3); }
            return n;
        }

        /// <summary>Nom du moniteur Samsung branché (EDID via WMI), ou null.
        /// S'il y a plusieurs écrans Samsung, préfère celui nommé « Odyssey ».</summary>
        public static string DetectSamsungMonitor()
        {
            string first = null;
            try
            {
                using (var s = new ManagementObjectSearcher(@"root\wmi",
                           "SELECT ManufacturerName,UserFriendlyName FROM WmiMonitorID"))
                {
                    foreach (ManagementObject mo in s.Get())
                    {
                        string manu = DecodeU16(mo["ManufacturerName"]);
                        string name = DecodeU16(mo["UserFriendlyName"]);
                        bool samsung = manu.Equals("SAM", StringComparison.OrdinalIgnoreCase)
                            || name.IndexOf("odyssey", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (!samsung) continue;
                        if (string.IsNullOrEmpty(name)) name = "Samsung";
                        if (name.IndexOf("odyssey", StringComparison.OrdinalIgnoreCase) >= 0) return name;
                        if (first == null) first = name;
                    }
                }
            }
            catch { }
            return first;
        }

        private static string DecodeU16(object o)
        {
            var arr = o as ushort[];
            if (arr == null) return "";
            var sb = new System.Text.StringBuilder();
            foreach (ushort u in arr) { if (u == 0) break; sb.Append((char)u); }
            return sb.ToString().Trim();
        }
    }
}
