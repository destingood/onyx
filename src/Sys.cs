using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Principal;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace BTOptimizer
{
    public class NativeResult
    {
        public int ExitCode;
        public string Output;
    }

    /// <summary>
    /// Contexte système + primitives : registre (ruche du VRAI utilisateur connecté,
    /// même si l'élévation UAC est passée par un autre compte admin), exécutables
    /// natifs (powercfg, bcdedit, sc, reg), sauvegarde/restauration, point de
    /// restauration système.
    /// </summary>
    internal static class Sys
    {
        public static string CurrentSid;
        public static string TargetSid;
        public static bool SameUser = true;
        public static string BackupDesktop;

        public static void Init()
        {
            CurrentSid = WindowsIdentity.GetCurrent().User.Value;
            TargetSid = CurrentSid;

            // Utilisateur réellement connecté = propriétaire d'explorer.exe.
            // S'il diffère du compte élevé (élévation "over-the-shoulder"),
            // les réglages par-utilisateur visent SA ruche, pas celle de l'admin.
            string consoleSid = GetExplorerOwnerSid();
            if (!string.IsNullOrEmpty(consoleSid) && consoleSid != CurrentSid)
            {
                RegistryKey hive = Registry.Users.OpenSubKey(consoleSid);
                if (hive != null)
                {
                    hive.Close();
                    TargetSid = consoleSid;
                    SameUser = false;
                }
            }
            BackupDesktop = ResolveDesktop();
        }

        private static string GetExplorerOwnerSid()
        {
            try
            {
                using (ManagementObjectSearcher searcher =
                    new ManagementObjectSearcher("SELECT ProcessId FROM Win32_Process WHERE Name='explorer.exe'"))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        object[] args = new object[1];
                        int rc = Convert.ToInt32(mo.InvokeMethod("GetOwnerSid", args));
                        if (rc == 0 && args[0] is string && ((string)args[0]).Length > 0)
                            return (string)args[0];
                    }
                }
            }
            catch { }
            return null;
        }

        private static string ResolveDesktop()
        {
            if (!SameUser)
            {
                try
                {
                    object p = GetMachine(
                        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\" + TargetSid,
                        "ProfileImagePath");
                    if (p is string && ((string)p).Length > 0)
                        return Path.Combine((string)p, "Desktop");
                }
                catch { }
            }
            return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        }

        // ------------------------------------------------------------------
        //  Registre — ruche UTILISATEUR cible et HKLM
        // ------------------------------------------------------------------
        private static string UserPrefix() { return SameUser ? "" : TargetSid + "\\"; }
        private static RegistryKey UserBase() { return SameUser ? Registry.CurrentUser : Registry.Users; }

        public static void SetUser(string sub, string name, object val, RegistryValueKind kind)
        {
            using (RegistryKey k = UserBase().CreateSubKey(UserPrefix() + sub))
                k.SetValue(name, val, kind);
        }

        public static object GetUser(string sub, string name)
        {
            using (RegistryKey k = UserBase().OpenSubKey(UserPrefix() + sub))
                return k == null ? null : k.GetValue(name);
        }

        public static void DelUser(string sub, string name)
        {
            using (RegistryKey k = UserBase().OpenSubKey(UserPrefix() + sub, true))
                if (k != null) k.DeleteValue(name, false);
        }

        public static void SetMachine(string sub, string name, object val, RegistryValueKind kind)
        {
            using (RegistryKey k = Registry.LocalMachine.CreateSubKey(sub))
                k.SetValue(name, val, kind);
        }

        public static object GetMachine(string sub, string name)
        {
            using (RegistryKey k = Registry.LocalMachine.OpenSubKey(sub))
                return k == null ? null : k.GetValue(name);
        }

        public static void DelMachine(string sub, string name)
        {
            using (RegistryKey k = Registry.LocalMachine.OpenSubKey(sub, true))
                if (k != null) k.DeleteValue(name, false);
        }

        public static bool IntEquals(object v, int expected)
        {
            return (v is int) && (int)v == expected;
        }

        public static bool StrEquals(object v, string expected)
        {
            return (v is string) && string.Equals((string)v, expected, StringComparison.OrdinalIgnoreCase);
        }

        // ------------------------------------------------------------------
        //  Exécutables natifs (chemins complets System32 : immunisé du PATH)
        // ------------------------------------------------------------------
        public static string Sys32(string exeName)
        {
            return Path.Combine(Environment.SystemDirectory, exeName);
        }

        public static NativeResult Run(string exe, string args)
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = exe;
            psi.Arguments = args;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            using (Process p = Process.Start(psi))
            {
                var errTask = p.StandardError.ReadToEndAsync();
                string outText = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                NativeResult r = new NativeResult();
                r.ExitCode = p.ExitCode;
                r.Output = outText + errTask.Result;
                return r;
            }
        }

        public static string RunThrow(string exe, string args, string label)
        {
            NativeResult r = Run(exe, args);
            if (r.ExitCode != 0)
                throw new Exception(label + " a échoué (code " + r.ExitCode + ").");
            return r.Output;
        }

        // ------------------------------------------------------------------
        //  Plan d'alimentation "Performances ultimes"
        // ------------------------------------------------------------------
        private const string UltimateSrc   = "e9a42b02-d5df-448d-aa00-03f14749eb61"; // plan intégré (masqué)
        private const string UltimateClone = "e9a42b02-d5df-448d-aa00-03f14749eb62"; // copie à GUID fixe
        private const string BalancedGuid  = "381b4222-f694-41f0-9685-ff5bb260df2e";
        private static readonly Regex GuidRx = new Regex(
            "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");

        public static void EnableUltimatePlan()
        {
            // Détection par GUID (indépendant de la langue) ; duplication vers un GUID
            // fixe pour rester idempotent (pas d'accumulation de plans en double).
            string list = RunThrow(Sys32("powercfg.exe"), "/list", "Lecture des plans d'alimentation");
            string guid = null;
            if (list.IndexOf(UltimateClone, StringComparison.OrdinalIgnoreCase) >= 0) guid = UltimateClone;
            else if (list.IndexOf(UltimateSrc, StringComparison.OrdinalIgnoreCase) >= 0) guid = UltimateSrc;
            else
            {
                foreach (string line in list.Split('\n'))
                {
                    if (line.IndexOf("Performances ultimes", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        line.IndexOf("Ultimate Performance", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Match m = GuidRx.Match(line);
                        if (m.Success) { guid = m.Value; break; }
                    }
                }
            }
            if (guid == null)
            {
                RunThrow(Sys32("powercfg.exe"),
                    "/duplicatescheme " + UltimateSrc + " " + UltimateClone,
                    "Création du plan Performances ultimes");
                guid = UltimateClone;
            }
            RunThrow(Sys32("powercfg.exe"), "/setactive " + guid, "Activation du plan");
        }

        public static void RestoreBalancedPlan()
        {
            RunThrow(Sys32("powercfg.exe"), "/setactive " + BalancedGuid, "Retour au plan Utilisation normale");
        }

        public static bool UltimateActive()
        {
            NativeResult r = Run(Sys32("powercfg.exe"), "/getactivescheme");
            if (r.ExitCode != 0) return false;
            return r.Output.IndexOf("e9a42b02-d5df-448d-aa00-03f14749eb6", StringComparison.OrdinalIgnoreCase) >= 0
                || r.Output.IndexOf("Performances ultimes", StringComparison.OrdinalIgnoreCase) >= 0
                || r.Output.IndexOf("Ultimate Performance", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static void SetUsbSuspend(bool disable)
        {
            string v = disable ? "0" : "1";
            string ids = "2a737441-1930-4402-8d77-b2bebba308a3 48e6b7a6-50f5-4782-a5d4-53bb8f07e226 ";
            RunThrow(Sys32("powercfg.exe"), "/setacvalueindex SCHEME_CURRENT " + ids + v, "Réglage USB (secteur)");
            RunThrow(Sys32("powercfg.exe"), "/setdcvalueindex SCHEME_CURRENT " + ids + v, "Réglage USB (batterie)");
            RunThrow(Sys32("powercfg.exe"), "/setactive SCHEME_CURRENT", "Application du plan");
        }

        /// <summary>Index AC courant d'un paramètre d'alimentation du plan actif (lecture registre, indépendante de la langue).</summary>
        public static int? GetPowerAcIndex(string subGuid, string settingGuid)
        {
            object act = GetMachine(@"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes", "ActivePowerScheme");
            string scheme = act as string;
            if (string.IsNullOrEmpty(scheme)) return null;
            object v = GetMachine(@"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes\" + scheme + "\\" + subGuid + "\\" + settingGuid, "ACSettingIndex");
            if (v is int) return (int)v;
            return null;
        }

        public static bool? PowerAcEquals(string subGuid, string settingGuid, int expected)
        {
            int? v = GetPowerAcIndex(subGuid, settingGuid);
            return v.HasValue ? (bool?)(v.Value == expected) : null;
        }

        /// <summary>Écrit une valeur AC/DC d'un paramètre caché ou visible du plan actif.</summary>
        public static void SetPowerValue(string subGuid, string settingGuid, int ac, int dc)
        {
            RunThrow(Sys32("powercfg.exe"),
                "/setacvalueindex SCHEME_CURRENT " + subGuid + " " + settingGuid + " " + ac,
                "Réglage alimentation (secteur)");
            RunThrow(Sys32("powercfg.exe"),
                "/setdcvalueindex SCHEME_CURRENT " + subGuid + " " + settingGuid + " " + dc,
                "Réglage alimentation (batterie)");
            RunThrow(Sys32("powercfg.exe"), "/setactive SCHEME_CURRENT", "Application du plan");
        }

        // ------------------------------------------------------------------
        //  Préférences GPU DirectX globales (Win11) — valeur à jetons
        //  "TokenA=1;TokenB=0;" : on modifie UN jeton sans toucher aux autres.
        // ------------------------------------------------------------------
        private const string DxSub  = @"Software\Microsoft\DirectX\UserGpuPreferences";
        private const string DxName = "DirectXUserGlobalSettings";

        public static void SetDxToken(string token, string value)
        {
            object cur = GetUser(DxSub, DxName);
            string s = (cur is string) ? (string)cur : "";
            var parts = new List<string>();
            foreach (string p in s.Split(';'))
            {
                string t = p.Trim();
                if (t.Length == 0) continue;
                if (t.StartsWith(token + "=", StringComparison.OrdinalIgnoreCase)) continue;
                parts.Add(t);
            }
            if (value != null) parts.Add(token + "=" + value);
            if (parts.Count == 0) { DelUser(DxSub, DxName); return; }
            SetUser(DxSub, DxName, string.Join(";", parts.ToArray()) + ";", RegistryValueKind.String);
        }

        public static bool DxTokenEquals(string token, string value)
        {
            object cur = GetUser(DxSub, DxName);
            string s = cur as string;
            if (s == null) return false;
            return s.IndexOf(token + "=" + value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ------------------------------------------------------------------
        //  Nagle (par interface réseau)
        // ------------------------------------------------------------------
        public static void SetNagle(bool disable)
        {
            const string root = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";
            using (RegistryKey rk = Registry.LocalMachine.OpenSubKey(root, true))
            {
                if (rk == null) return;
                foreach (string child in rk.GetSubKeyNames())
                {
                    using (RegistryKey ik = rk.OpenSubKey(child, true))
                    {
                        if (ik == null) continue;
                        if (disable)
                        {
                            ik.SetValue("TcpAckFrequency", 1, RegistryValueKind.DWord);
                            ik.SetValue("TCPNoDelay", 1, RegistryValueKind.DWord);
                        }
                        else
                        {
                            ik.DeleteValue("TcpAckFrequency", false);
                            ik.DeleteValue("TCPNoDelay", false);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Empêche (ou réautorise) Windows d'éteindre les cartes réseau pour
        /// économiser l'énergie (PnPCapabilities=0x18 sur chaque adaptateur).
        /// </summary>
        public static void SetNicPowerSaving(bool disable)
        {
            const string netClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";
            using (RegistryKey rk = Registry.LocalMachine.OpenSubKey(netClass))
            {
                if (rk == null) return;
                foreach (string child in rk.GetSubKeyNames())
                {
                    // Sous-clés numériques uniquement ("0000", "0001", ...) :
                    // la sous-clé "Properties" est verrouillée même pour un admin.
                    int ignored;
                    if (!int.TryParse(child, out ignored)) continue;
                    try
                    {
                        using (RegistryKey ik = Registry.LocalMachine.OpenSubKey(netClass + "\\" + child, true))
                        {
                            if (ik == null) continue;
                            if (ik.GetValue("NetCfgInstanceId") == null) continue; // pas un adaptateur
                            if (disable)
                                ik.SetValue("PnPCapabilities", 0x18, RegistryValueKind.DWord);
                            else
                                ik.DeleteValue("PnPCapabilities", false);
                        }
                    }
                    catch { } // adaptateur protégé : on passe au suivant
                }
            }
        }

        // ------------------------------------------------------------------
        //  MSI mode (Message Signaled Interrupts) sur le(s) GPU
        // ------------------------------------------------------------------
        private const string DisplayClass = "{4d36e968-e325-11ce-bfc1-08002be10318}";

        private static bool IsGpuDevice(RegistryKey instKey)
        {
            string cls = instKey.GetValue("ClassGUID") as string;
            string svc = instKey.GetValue("Service") as string;
            if (cls != null && cls.Equals(DisplayClass, StringComparison.OrdinalIgnoreCase)) return true;
            if (svc != null)
            {
                svc = svc.ToLowerInvariant();
                if (svc == "nvlddmkm" || svc == "amdkmdag" || svc == "atikmdag" || svc.StartsWith("igfx") || svc == "igdkmd64")
                    return true;
            }
            return false;
        }

        public static void SetGpuMsi(bool enable, Action<string, int> log)
        {
            int count = 0;
            using (RegistryKey pci = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\PCI"))
            {
                if (pci == null) { log("Enum PCI introuvable.", 2); return; }
                foreach (string devId in pci.GetSubKeyNames())
                {
                    using (RegistryKey dev = pci.OpenSubKey(devId))
                    {
                        if (dev == null) continue;
                        foreach (string inst in dev.GetSubKeyNames())
                        {
                            using (RegistryKey ik = dev.OpenSubKey(inst))
                            {
                                if (ik == null || !IsGpuDevice(ik)) continue;
                                string msiPath = @"SYSTEM\CurrentControlSet\Enum\PCI\" + devId + "\\" + inst +
                                    @"\Device Parameters\Interrupt Management\MessageSignaledInterruptProperties";
                                try
                                {
                                    if (enable)
                                    {
                                        using (RegistryKey m = Registry.LocalMachine.CreateSubKey(msiPath))
                                            m.SetValue("MSISupported", 1, RegistryValueKind.DWord);
                                    }
                                    else
                                    {
                                        using (RegistryKey m = Registry.LocalMachine.OpenSubKey(msiPath, true))
                                            if (m != null) m.DeleteValue("MSISupported", false);
                                    }
                                    count++;
                                }
                                catch (Exception ex) { if (log != null) log("MSI GPU (" + devId + ") : " + ex.Message, 2); }
                            }
                        }
                    }
                }
            }
            if (log != null)
                log("MSI mode GPU " + (enable ? "activé" : "retiré (défaut pilote)") + " sur " + count + " périphérique(s). Redémarrage requis.", count > 0 ? 1 : 2);
            if (count == 0) throw new Exception("Aucun GPU trouvé pour le MSI mode.");
        }

        public static bool? GpuMsiActive()
        {
            using (RegistryKey pci = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\PCI"))
            {
                if (pci == null) return null;
                foreach (string devId in pci.GetSubKeyNames())
                {
                    using (RegistryKey dev = pci.OpenSubKey(devId))
                    {
                        if (dev == null) continue;
                        foreach (string inst in dev.GetSubKeyNames())
                        {
                            using (RegistryKey ik = dev.OpenSubKey(inst))
                            {
                                if (ik == null || !IsGpuDevice(ik)) continue;
                                object v = GetMachine(@"SYSTEM\CurrentControlSet\Enum\PCI\" + devId + "\\" + inst +
                                    @"\Device Parameters\Interrupt Management\MessageSignaledInterruptProperties", "MSISupported");
                                return IntEquals(v, 1);
                            }
                        }
                    }
                }
            }
            return null;
        }

        public static bool? NagleActive()
        {
            const string root = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";
            using (RegistryKey rk = Registry.LocalMachine.OpenSubKey(root))
            {
                if (rk == null) return null;
                foreach (string c in rk.GetSubKeyNames())
                    using (RegistryKey ik = rk.OpenSubKey(c))
                        if (ik != null && IntEquals(ik.GetValue("TcpAckFrequency"), 1)) return true;
            }
            return false;
        }

        public static bool? NicPowerDisabled()
        {
            const string netClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";
            using (RegistryKey rk = Registry.LocalMachine.OpenSubKey(netClass))
            {
                if (rk == null) return null;
                foreach (string c in rk.GetSubKeyNames())
                {
                    int n;
                    if (!int.TryParse(c, out n)) continue;
                    using (RegistryKey ik = rk.OpenSubKey(c))
                    {
                        if (ik == null || ik.GetValue("NetCfgInstanceId") == null) continue;
                        if (IntEquals(ik.GetValue("PnPCapabilities"), 24)) return true;
                    }
                }
            }
            return false;
        }

        public static bool? RscDisabled()
        {
            NativeResult r = Run(Sys32("netsh.exe"), "int tcp show global");
            if (r.ExitCode != 0) return null;
            foreach (string line in r.Output.Split('\n'))
            {
                if (line.IndexOf("RSC", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (line.IndexOf("disabled", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    if (line.IndexOf("enabled", StringComparison.OrdinalIgnoreCase) >= 0) return false;
                }
            }
            return null;
        }

        // ------------------------------------------------------------------
        //  Services Windows
        // ------------------------------------------------------------------
        public static void ConfigureService(string name, string startType, bool stopNow, bool startNow)
        {
            // startType : disabled | demand | auto | delayed-auto
            RunThrow(Sys32("sc.exe"), "config " + name + " start= " + startType,
                "Configuration du service " + name);
            if (stopNow) Run(Sys32("sc.exe"), "stop " + name);    // échec toléré (déjà arrêté)
            if (startNow) Run(Sys32("sc.exe"), "start " + name);  // échec toléré (déjà démarré)
        }

        public static bool ServiceDisabled(string name)
        {
            object v = GetMachine(@"SYSTEM\CurrentControlSet\Services\" + name, "Start");
            return IntEquals(v, 4);
        }

        /// <summary>Valeur Start du service : 2=auto, 3=manuel, 4=désactivé, -1=absent.</summary>
        public static int GetServiceStart(string name)
        {
            object v = GetMachine(@"SYSTEM\CurrentControlSet\Services\" + name, "Start");
            return (v is int) ? (int)v : -1;
        }

        public static bool IsServiceRunning(string name)
        {
            NativeResult r = Run(Sys32("sc.exe"), "query " + name);
            return r.ExitCode == 0 && r.Output.IndexOf("RUNNING", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static void StopService(string name) { Run(Sys32("sc.exe"), "stop " + name); }
        public static void StartService(string name) { Run(Sys32("sc.exe"), "start " + name); }

        public static void SetScheduledTask(string taskPath, bool enable)
        {
            Run(Sys32("schtasks.exe"), "/change /tn \"" + taskPath + "\" /" + (enable ? "enable" : "disable"));
        }

        public static bool? ScheduledTaskDisabled(string taskPath)
        {
            NativeResult r = Run(Sys32("schtasks.exe"), "/query /tn \"" + taskPath + "\" /fo LIST");
            if (r.ExitCode != 0) return null;
            // "Status"/"État" ... "Disabled"/"Désactivé" — on cherche le token désactivé.
            string o = r.Output.ToLowerInvariant();
            if (o.Contains("disabled") || o.Contains("désactiv") || o.Contains("desactiv")) return true;
            if (o.Contains("ready") || o.Contains("prêt") || o.Contains("running")) return false;
            return null;
        }

        // ------------------------------------------------------------------
        //  Programmes au démarrage (comme l'onglet Démarrage du Gestionnaire)
        // ------------------------------------------------------------------
        public class StartupEntry
        {
            public string Name;
            public string Command;
            public string Scope;
            public bool Machine;
            public bool Enabled;
        }

        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

        public static System.Collections.Generic.List<StartupEntry> ListStartup()
        {
            var list = new System.Collections.Generic.List<StartupEntry>();
            AddRun(list, Registry.CurrentUser, "Utilisateur", false);
            AddRun(list, Registry.LocalMachine, "Tous les utilisateurs", true);
            return list;
        }

        private static void AddRun(System.Collections.Generic.List<StartupEntry> list, RegistryKey root, string scope, bool machine)
        {
            try
            {
                using (RegistryKey run = root.OpenSubKey(RunKey))
                {
                    if (run == null) return;
                    using (RegistryKey appr = root.OpenSubKey(ApprovedKey))
                    {
                        foreach (string name in run.GetValueNames())
                        {
                            if (string.IsNullOrEmpty(name)) continue;
                            var e = new StartupEntry
                            {
                                Name = name,
                                Command = Convert.ToString(run.GetValue(name)),
                                Scope = scope,
                                Machine = machine,
                                Enabled = true
                            };
                            byte[] b = (appr == null) ? null : appr.GetValue(name) as byte[];
                            if (b != null && b.Length > 0 && b[0] == 0x03) e.Enabled = false;
                            list.Add(e);
                        }
                    }
                }
            }
            catch { }
        }

        /// <summary>Active/désactive une entrée de démarrage sans la supprimer (même mécanisme que le Gestionnaire des tâches).</summary>
        public static void SetStartupEnabled(StartupEntry e, bool enable)
        {
            RegistryKey root = e.Machine ? Registry.LocalMachine : Registry.CurrentUser;
            using (RegistryKey appr = root.CreateSubKey(ApprovedKey))
            {
                var v = new byte[12];
                v[0] = (byte)(enable ? 0x02 : 0x03);
                appr.SetValue(e.Name, v, RegistryValueKind.Binary);
            }
        }

        // ------------------------------------------------------------------
        //  Nettoyage disque (dossiers temporaires sûrs)
        // ------------------------------------------------------------------
        public class CleanTarget
        {
            public string Name;
            public string Path;
            public bool IsRecycleBin;
            public long SizeMB;
        }

        public static System.Collections.Generic.List<CleanTarget> CleanTargets()
        {
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var list = new System.Collections.Generic.List<CleanTarget>
            {
                new CleanTarget { Name = "Fichiers temporaires (utilisateur)", Path = Environment.GetEnvironmentVariable("TEMP") },
                new CleanTarget { Name = "Fichiers temporaires (Windows)", Path = Path.Combine(win, "Temp") },
                new CleanTarget { Name = "Cache Windows Update", Path = Path.Combine(win, @"SoftwareDistribution\Download") },
                new CleanTarget { Name = "Prefetch", Path = Path.Combine(win, "Prefetch") },
                new CleanTarget { Name = "Rapports d'erreurs (WER)", Path = Path.Combine(local, @"Microsoft\Windows\WER") },
                new CleanTarget { Name = "Corbeille", Path = null, IsRecycleBin = true },
            };
            foreach (CleanTarget t in list) t.SizeMB = MeasureTarget(t);
            return list;
        }

        private static long MeasureTarget(CleanTarget t)
        {
            try
            {
                if (t.IsRecycleBin) { long b; return NativeRecycle.QueryBytes(out b) ? b / (1024 * 1024) : 0; }
                if (string.IsNullOrEmpty(t.Path) || !Directory.Exists(t.Path)) return 0;
                long sum = 0;
                foreach (string f in Directory.EnumerateFiles(t.Path, "*", SearchOption.AllDirectories))
                {
                    try { sum += new FileInfo(f).Length; } catch { }
                }
                return sum / (1024 * 1024);
            }
            catch { return 0; }
        }

        /// <summary>Vide une cible ; retourne le nombre d'éléments supprimés. Ignore les fichiers verrouillés.</summary>
        public static int CleanTargetNow(CleanTarget t, Action<string, int> log)
        {
            int removed = 0;
            try
            {
                if (t.IsRecycleBin)
                {
                    NativeRecycle.Empty();
                    log("Corbeille vidée.", 1);
                    return 1;
                }
                if (string.IsNullOrEmpty(t.Path) || !Directory.Exists(t.Path)) return 0;
                foreach (string f in Directory.EnumerateFiles(t.Path, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); removed++; } catch { }
                }
                foreach (string d in Directory.EnumerateDirectories(t.Path))
                {
                    try { Directory.Delete(d, true); } catch { }
                }
                log(t.Name + " : " + removed + " fichier(s) supprimé(s).", 1);
            }
            catch (Exception ex) { log(t.Name + " : " + ex.Message, 2); }
            return removed;
        }

        // ------------------------------------------------------------------
        //  Acceptation des conditions d'utilisation (EULA)
        // ------------------------------------------------------------------
        private static string EulaPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-eula.txt"); }
        }

        public static int EulaAcceptedVersion()
        {
            try
            {
                if (!File.Exists(EulaPath)) return 0;
                int v;
                return int.TryParse(File.ReadAllText(EulaPath).Trim(), out v) ? v : 0;
            }
            catch { return 0; }
        }

        public static void SetEulaAccepted(int version)
        {
            try { File.WriteAllText(EulaPath, version.ToString()); } catch { }
        }

        // ------------------------------------------------------------------
        //  Profil (sélection sauvegardée) et gardien de démarrage
        // ------------------------------------------------------------------
        public static string ProfilePath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-profile.txt"); }
        }

        public static void SaveProfile(List<string> tweakIds)
        {
            File.WriteAllLines(ProfilePath, tweakIds.ToArray());
        }

        public static List<string> LoadProfile()
        {
            var ids = new List<string>();
            if (!File.Exists(ProfilePath)) return ids;
            foreach (string line in File.ReadAllLines(ProfilePath))
            {
                string t = line.Trim();
                if (t.Length > 0 && !t.StartsWith("#")) ids.Add(t);
            }
            return ids;
        }

        private const string GuardTask = "BTOptimizerGuard";

        public static bool GuardExists()
        {
            return Run(Sys32("schtasks.exe"), "/query /tn " + GuardTask).ExitCode == 0;
        }

        /// <summary>Crée/supprime la tâche planifiée qui ré-applique le profil à l'ouverture de session.</summary>
        public static bool SetGuard(bool enable, string exePath, Action<string, int> log)
        {
            if (enable)
            {
                string tr = "\"\\\"" + exePath + "\\\" -apply profile\"";
                NativeResult r = Run(Sys32("schtasks.exe"),
                    "/create /f /rl HIGHEST /sc ONLOGON /tn " + GuardTask + " /tr " + tr);
                if (r.ExitCode == 0)
                {
                    log("Gardien activé : le profil sera ré-appliqué à chaque ouverture de session.", 1);
                    return true;
                }
                log("Création de la tâche planifiée impossible (code " + r.ExitCode + ").", 3);
                return false;
            }
            NativeResult d = Run(Sys32("schtasks.exe"), "/delete /f /tn " + GuardTask);
            if (d.ExitCode == 0) log("Gardien désactivé (tâche planifiée supprimée).", 0);
            return d.ExitCode == 0;
        }

        // ------------------------------------------------------------------
        //  Sauvegarde / restauration du registre
        // ------------------------------------------------------------------
        public static string ExportBackup(List<Tweak> tweaks, Action<string, int> log)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string dir = Path.Combine(BackupDesktop, "bt-optimizer-backup-" + stamp);
            Directory.CreateDirectory(dir);

            List<string> keys = tweaks.SelectMany(t => t.BackupKeys)
                                      .Distinct().OrderBy(k => k).ToList();
            foreach (string key in keys)
            {
                // reg.exe ignore les vues .NET : on vise la vraie ruche utilisateur.
                string exportKey = key;
                if (!SameUser && key.StartsWith("HKCU", StringComparison.OrdinalIgnoreCase))
                    exportKey = "HKU\\" + TargetSid + key.Substring(4);

                string file = Path.Combine(dir, SanitizeFileName(exportKey) + ".reg");
                NativeResult r = Run(Sys32("reg.exe"),
                    "export \"" + exportKey + "\" \"" + file + "\" /y");
                if (r.ExitCode != 0)
                    log("Clé absente (rien à sauvegarder) : " + exportKey, 2);
            }

            NativeResult plan = Run(Sys32("powercfg.exe"), "/getactivescheme");
            File.WriteAllText(Path.Combine(dir, "plan-alimentation.txt"), plan.Output);
            NativeResult bcd = Run(Sys32("bcdedit.exe"), "/enum {current}");
            File.WriteAllText(Path.Combine(dir, "bcdedit.txt"), bcd.Output);
            return dir;
        }

        public static void ImportBackup(string folder, Action<string, int> log)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                log("Dossier introuvable : " + folder, 3);
                return;
            }
            string[] files = Directory.GetFiles(folder, "*.reg");
            if (files.Length == 0)
            {
                log("Aucun fichier .reg dans : " + folder, 2);
                return;
            }
            log("Restauration depuis : " + folder, 0);
            foreach (string f in files)
            {
                NativeResult r = Run(Sys32("reg.exe"), "import \"" + f + "\"");
                if (r.ExitCode == 0) log("Importé : " + Path.GetFileName(f), 1);
                else log("Échec import : " + Path.GetFileName(f), 3);
            }
            log("Restauration terminée. Un redémarrage peut être nécessaire.", 2);
        }

        private static string SanitizeFileName(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            return s.Replace(' ', '_').Replace('\\', '_');
        }

        // ------------------------------------------------------------------
        //  Point de restauration système (WMI, sans PowerShell)
        // ------------------------------------------------------------------
        public static void CreateRestorePoint(Action<string, int> log)
        {
            log("Création d'un point de restauration système (peut prendre une minute)...", 0);
            try
            {
                ManagementScope scope = new ManagementScope(@"\\.\root\default");
                ManagementPath path = new ManagementPath("SystemRestore");
                using (ManagementClass mc = new ManagementClass(scope, path, new ObjectGetOptions()))
                using (ManagementBaseObject inParams = mc.GetMethodParameters("CreateRestorePoint"))
                {
                    inParams["Description"] = "BT Optimizer";
                    inParams["RestorePointType"] = (uint)12; // MODIFY_SETTINGS
                    inParams["EventType"] = (uint)100;       // BEGIN_SYSTEM_CHANGE
                    using (ManagementBaseObject outParams = mc.InvokeMethod("CreateRestorePoint", inParams, null))
                    {
                        uint rv = Convert.ToUInt32(outParams["ReturnValue"]);
                        if (rv == 0)
                            log("Point de restauration demandé. NB : Windows l'ignore silencieusement si un point date de moins de 24 h.", 1);
                        else if (rv == 1058)
                            log("Point de restauration impossible : la restauration système est désactivée sur ce PC.", 2);
                        else
                            log("Point de restauration : code retour " + rv + ".", 2);
                    }
                }
            }
            catch (Exception ex)
            {
                log("Point de restauration impossible : " + ex.Message, 2);
            }
        }

        // ------------------------------------------------------------------
        //  Overclock GPU (outillage officiel nvidia-smi) + infos RAM/CPU
        // ------------------------------------------------------------------
        public class GpuOcInfo
        {
            public bool Ok;
            public string Name = "-";
            public double PowerCur, PowerDefault, PowerMax;
            public double MaxCoreMhz;
        }

        public static string NvSmiPath()
        {
            string p = Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe");
            return File.Exists(p) ? p : null;
        }

        public static GpuOcInfo QueryGpuOc()
        {
            var info = new GpuOcInfo();
            string smi = NvSmiPath();
            if (smi == null) return info;
            NativeResult r = Run(smi,
                "--query-gpu=name,power.limit,power.default_limit,power.max_limit,clocks.max.gr --format=csv,noheader,nounits");
            if (r.ExitCode != 0 || string.IsNullOrEmpty(r.Output)) return info;
            string[] p = r.Output.Split('\n')[0].Trim().Split(',');
            if (p.Length < 5) return info;
            info.Name = p[0].Trim();
            double v;
            if (double.TryParse(p[1].Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out v)) info.PowerCur = v;
            if (double.TryParse(p[2].Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out v)) info.PowerDefault = v;
            if (double.TryParse(p[3].Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out v)) info.PowerMax = v;
            if (double.TryParse(p[4].Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out v)) info.MaxCoreMhz = v;
            info.Ok = true;
            return info;
        }

        public static string GpuOcConfigPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-gpuoc.txt"); }
        }

        public static void SaveGpuOcConfig(int powerLimit)
        {
            File.WriteAllLines(GpuOcConfigPath, new[] { "pl=" + powerLimit });
        }

        public static bool LoadGpuOcConfig(out int powerLimit)
        {
            powerLimit = 0;
            if (!File.Exists(GpuOcConfigPath)) return false;
            foreach (string line in File.ReadAllLines(GpuOcConfigPath))
            {
                string[] kv = line.Split('=');
                if (kv.Length != 2) continue;
                int v;
                if (!int.TryParse(kv[1].Trim(), out v)) continue;
                // On ne lit QUE le power limit. Les anciennes clés de verrou (lgcmin/lgcmax)
                // sont volontairement ignorées : le verrou de fréquence a été retiré (dangereux).
                if (kv[0].Trim().ToLowerInvariant() == "pl") powerLimit = v;
            }
            return powerLimit > 0;
        }

        /// <summary>
        /// Applique un power limit GPU via nvidia-smi. VOLONTAIREMENT sans verrou de fréquence :
        /// figer le plancher de fréquence trop haut peut geler la machine (écran noir + reboot).
        /// Le GPU gère lui-même son boost dans sa courbe stable ; seul le budget de puissance change.
        /// </summary>
        public static void ApplyGpuOc(int powerLimit, Action<string, int> log)
        {
            string smi = NvSmiPath();
            if (smi == null) { log("nvidia-smi introuvable : OC GPU indisponible.", 3); return; }
            GpuOcInfo cur = QueryGpuOc();
            if (powerLimit <= 0 || !cur.Ok) return;
            int pl = powerLimit;
            if (cur.PowerMax > 0 && pl > (int)cur.PowerMax) pl = (int)cur.PowerMax;                 // borne haute (pilote)
            if (cur.PowerDefault > 0 && pl < (int)(cur.PowerDefault * 0.5)) pl = (int)(cur.PowerDefault * 0.5); // garde-fou bas
            NativeResult r = Run(smi, "-pl " + pl);
            if (r.ExitCode == 0) log("Power limit GPU -> " + pl + " W (fréquences gérées par le pilote).", 1);
            else log("Echec power limit (code " + r.ExitCode + ") : " + r.Output.Trim(), 3);
        }

        public static void ResetGpuLocks(Action<string, int> log)
        {
            string smi = NvSmiPath();
            if (smi == null) return;
            GpuOcInfo cur = QueryGpuOc();
            NativeResult r = Run(smi, "-rgc");
            if (r.ExitCode == 0) log("Verrou de fréquences GPU retiré (gestion pilote).", 1);
            else log("Echec -rgc (code " + r.ExitCode + ").", 2);
            if (cur.Ok && cur.PowerDefault > 0)
            {
                Run(smi, "-pl " + (int)cur.PowerDefault);
                log("Power limit GPU remis au défaut constructeur (" + (int)cur.PowerDefault + " W).", 0);
            }
        }

        private const string OcTask = "BTOptimizerOC";

        public static bool OcGuardExists()
        {
            return Run(Sys32("schtasks.exe"), "/query /tn " + OcTask).ExitCode == 0;
        }

        public static bool SetOcGuard(bool enable, string exePath, Action<string, int> log)
        {
            if (enable)
            {
                string tr = "\"\\\"" + exePath + "\\\" -gpuoc\"";
                NativeResult r = Run(Sys32("schtasks.exe"),
                    "/create /f /rl HIGHEST /sc ONLOGON /tn " + OcTask + " /tr " + tr);
                if (r.ExitCode == 0) { log("OC GPU persistant : ré-appliqué à chaque ouverture de session.", 1); return true; }
                log("Création de la tâche OC impossible (code " + r.ExitCode + ").", 3);
                return false;
            }
            NativeResult d = Run(Sys32("schtasks.exe"), "/delete /f /tn " + OcTask);
            if (d.ExitCode == 0) log("Persistance OC GPU désactivée.", 0);
            return d.ExitCode == 0;
        }

        // ---- RAM / CPU (diagnostic overclock) ----
        public class RamInfo
        {
            public int Modules;
            public long TotalMB;
            public int SpeedRated;      // MT/s annoncés (SPD/XMP max)
            public int SpeedRunning;    // MT/s configurés
        }

        public static RamInfo QueryRam()
        {
            var ram = new RamInfo();
            try
            {
                using (var s = new ManagementObjectSearcher(
                    "SELECT Capacity, Speed, ConfiguredClockSpeed FROM Win32_PhysicalMemory"))
                {
                    foreach (ManagementObject mo in s.Get())
                    {
                        ram.Modules++;
                        object cap = mo["Capacity"];
                        if (cap != null) ram.TotalMB += (long)(Convert.ToUInt64(cap) / (1024 * 1024));
                        object sp = mo["Speed"];
                        if (sp != null) ram.SpeedRated = Math.Max(ram.SpeedRated, Convert.ToInt32(sp));
                        object cc = mo["ConfiguredClockSpeed"];
                        if (cc != null) ram.SpeedRunning = Math.Max(ram.SpeedRunning, Convert.ToInt32(cc));
                    }
                }
            }
            catch { }
            return ram;
        }

        public class CpuInfo
        {
            public string Name = "-";
            public int Cores, Threads, MaxMhz;
        }

        public static CpuInfo QueryCpu()
        {
            var cpu = new CpuInfo();
            try
            {
                using (var s = new ManagementObjectSearcher(
                    "SELECT Name, MaxClockSpeed, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor"))
                {
                    foreach (ManagementObject mo in s.Get())
                    {
                        cpu.Name = Convert.ToString(mo["Name"]).Trim();
                        cpu.MaxMhz = Convert.ToInt32(mo["MaxClockSpeed"]);
                        cpu.Cores = Convert.ToInt32(mo["NumberOfCores"]);
                        cpu.Threads = Convert.ToInt32(mo["NumberOfLogicalProcessors"]);
                        break;
                    }
                }
            }
            catch { }
            return cpu;
        }

        // ------------------------------------------------------------------
        //  Profil NVIDIA faible latence (via nvidiaProfileInspector -silentImport)
        // ------------------------------------------------------------------
        private static string AppBase { get { return AppDomain.CurrentDomain.BaseDirectory; } }

        public static string FindNvpi()
        {
            string[] cands =
            {
                Path.Combine(AppBase, @"tools\npi\nvidiaProfileInspector.exe"),
                Path.Combine(AppBase, @"..\tools\npi\nvidiaProfileInspector.exe"),
                Path.Combine(AppBase, @"npi\nvidiaProfileInspector.exe"),
                Path.Combine(AppBase, "nvidiaProfileInspector.exe"),
            };
            foreach (string c in cands)
                if (File.Exists(c)) return Path.GetFullPath(c);
            return null;
        }

        private const string LowLatencyNip =
            "<?xml version=\"1.0\" encoding=\"utf-16\"?>\r\n" +
            "<ArrayOfProfile>\r\n  <Profile>\r\n    <ProfileName>Base Profile</ProfileName>\r\n" +
            "    <Executeables />\r\n    <Settings>\r\n" +
            "      <ProfileSetting><SettingNameInfo>Ultra Low Latency - CPL State (Ultra)</SettingNameInfo><SettingID>390467</SettingID><SettingValue>2</SettingValue><ValueType>Dword</ValueType></ProfileSetting>\r\n" +
            "      <ProfileSetting><SettingNameInfo>Maximum pre-rendered frames</SettingNameInfo><SettingID>8102046</SettingID><SettingValue>1</SettingValue><ValueType>Dword</ValueType></ProfileSetting>\r\n" +
            "      <ProfileSetting><SettingNameInfo>Power management mode (Prefer max perf)</SettingNameInfo><SettingID>274197361</SettingID><SettingValue>1</SettingValue><ValueType>Dword</ValueType></ProfileSetting>\r\n" +
            "      <ProfileSetting><SettingNameInfo>Ultra Low Latency - Enabled</SettingNameInfo><SettingID>277041152</SettingID><SettingValue>1</SettingValue><ValueType>Dword</ValueType></ProfileSetting>\r\n" +
            "    </Settings>\r\n    <ExecutableFindFiles />\r\n  </Profile>\r\n</ArrayOfProfile>";

        public static string EnsureLowLatencyNip()
        {
            string[] cands =
            {
                Path.Combine(AppBase, @"..\tools\input-lag-reapply.nip"),
                Path.Combine(AppBase, @"tools\input-lag-reapply.nip"),
                Path.Combine(AppBase, "input-lag-reapply.nip"),
            };
            foreach (string c in cands)
                if (File.Exists(c)) return Path.GetFullPath(c);
            string mine = Path.Combine(AppBase, "bt-nvidia-lowlatency.nip");
            File.WriteAllText(mine, LowLatencyNip, new System.Text.UnicodeEncoding(false, true));
            return mine;
        }

        public static bool NvpiAvailable() { return FindNvpi() != null; }

        /// <summary>Applique le profil NVIDIA faible latence (Ultra Low Latency, 1 frame pré-rendue, perf max).</summary>
        public static void ApplyNvidiaLowLatency(Action<string, int> log)
        {
            string exe = FindNvpi();
            if (exe == null)
            {
                log("nvidiaProfileInspector.exe introuvable (attendu dans tools\\npi\\). Impossible d'appliquer le profil NVIDIA.", 3);
                return;
            }
            string nip = EnsureLowLatencyNip();
            NativeResult r = Run(exe, "-silentImport \"" + nip + "\"");
            if (r.ExitCode == 0)
                log("Profil NVIDIA faible latence appliqué : Ultra Low Latency (Ultra), 1 frame pré-rendue, mode perf. max.", 1);
            else
                log("nvidiaProfileInspector a retourné le code " + r.ExitCode + ".", 2);
        }

        // ------------------------------------------------------------------
        //  DNS (par carte réseau active, via WMI) + vidage du cache
        // ------------------------------------------------------------------
        public static string CurrentDnsSummary()
        {
            var lines = new List<string>();
            try
            {
                using (var mos = new ManagementObjectSearcher(
                    "SELECT Description, DNSServerSearchOrder FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled=true"))
                {
                    foreach (ManagementObject mo in mos.Get())
                    {
                        string desc = Convert.ToString(mo["Description"]);
                        string[] dns = mo["DNSServerSearchOrder"] as string[];
                        string val = (dns != null && dns.Length > 0) ? string.Join(", ", dns) : "automatique (DHCP)";
                        lines.Add("• " + desc + " : " + val);
                    }
                }
            }
            catch (Exception ex) { lines.Add("Lecture DNS impossible : " + ex.Message); }
            if (lines.Count == 0) lines.Add("Aucune carte réseau active détectée.");
            return string.Join("\r\n", lines.ToArray());
        }

        /// <summary>Applique une liste de serveurs DNS (null = retour DHCP automatique) sur toutes les cartes actives.</summary>
        public static void SetDns(string[] servers, Action<string, int> log)
        {
            int done = 0, fail = 0;
            using (var mos = new ManagementObjectSearcher(
                "SELECT * FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled=true"))
            {
                foreach (ManagementObject mo in mos.Get())
                {
                    try
                    {
                        using (ManagementBaseObject inp = mo.GetMethodParameters("SetDNSServerSearchOrder"))
                        {
                            inp["DNSServerSearchOrder"] = servers; // null => automatique
                            using (ManagementBaseObject outp = mo.InvokeMethod("SetDNSServerSearchOrder", inp, null))
                            {
                                uint rv = Convert.ToUInt32(outp["ReturnValue"]);
                                if (rv == 0 || rv == 1) done++;
                                else { fail++; if (log != null) log("DNS échec (" + rv + ") : " + mo["Description"], 2); }
                            }
                        }
                    }
                    catch (Exception ex) { fail++; if (log != null) log("DNS : " + ex.Message, 2); }
                }
            }
            FlushDns();
            if (log != null)
                log((servers == null ? "DNS remis en automatique" : "DNS appliqué") + " sur " + done + " carte(s)"
                    + (fail > 0 ? " (" + fail + " échec)" : "") + ". Cache DNS vidé.", done > 0 ? 1 : 3);
        }

        public static void FlushDns()
        {
            Run(Sys32("ipconfig.exe"), "/flushdns");
        }

        /// <summary>Réparation réseau standard (vide le cache DNS, réinitialise Winsock et la pile TCP/IP). Redémarrage requis.</summary>
        public static void NetworkRepair(Action<string, int> log)
        {
            var steps = new[]
            {
                new[] { Sys32("ipconfig.exe"), "/flushdns", "Cache DNS vidé" },
                new[] { Sys32("netsh.exe"), "winsock reset", "Winsock réinitialisé" },
                new[] { Sys32("netsh.exe"), "int ip reset", "Pile TCP/IP réinitialisée" },
                new[] { Sys32("netsh.exe"), "int tcp reset", "Paramètres TCP réinitialisés" },
            };
            foreach (string[] s in steps)
            {
                NativeResult r = Run(s[0], s[1]);
                log(s[2] + (r.ExitCode == 0 ? "." : " (code " + r.ExitCode + ")."), r.ExitCode == 0 ? 1 : 2);
            }
            log("Réparation réseau terminée. Un REDÉMARRAGE est nécessaire.", 2);
        }

        // ------------------------------------------------------------------
        //  Nettoyage mémoire (RAM)
        // ------------------------------------------------------------------
        public static long CleanMemory(Action<string, int> log)
        {
            long before = NativeMem.UsedPhysMB();
            int n = NativeMem.EmptyAllWorkingSets();
            bool standby = NativeMem.PurgeStandby();
            System.Threading.Thread.Sleep(250);
            long after = NativeMem.UsedPhysMB();
            long freed = before - after;
            if (log != null)
                log("RAM : " + n + " processus vidés" + (standby ? " + liste standby purgée" : "")
                    + ", ~" + Math.Max(0, freed) + " Mo libérés.", 1);
            return freed;
        }

        // ------------------------------------------------------------------
        //  Infos système pour l'en-tête du journal
        // ------------------------------------------------------------------
        public static string OsDescription()
        {
            object name = GetMachine(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName");
            object ver = GetMachine(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion");
            string s = (name is string) ? (string)name : "Windows";
            if (ver is string) s += " " + (string)ver;
            return s;
        }
    }
}
