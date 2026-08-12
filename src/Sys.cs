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

        /// <summary>Variable d'environnement UTILISATEUR persistante (survit au redémarrage) :
        /// écrite sous HKCU\Environment ET dans le process courant, sans écraser si déjà identique.</summary>
        public static void SetUserEnv(string name, string value)
        {
            try
            {
                using (RegistryKey k = UserBase().CreateSubKey(UserPrefix() + "Environment"))
                {
                    object cur = k.GetValue(name);
                    if (cur is string && string.Equals((string)cur, value, StringComparison.Ordinal)) return;
                    k.SetValue(name, value, RegistryValueKind.String);
                }
                try { Environment.SetEnvironmentVariable(name, value); } catch { }
            }
            catch { }
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

        /// <summary>Supprime une sous-clé utilisateur et toute sa descendance (rétablissement « clé entière »).</summary>
        public static void DelUserSubKeyTree(string sub)
        {
            try { UserBase().DeleteSubKeyTree(UserPrefix() + sub, false); } catch { }
        }

        /// <summary>Vrai si la sous-clé utilisateur existe (Check « présence de clé »).</summary>
        public static bool UserKeyExists(string sub)
        {
            using (RegistryKey k = UserBase().OpenSubKey(UserPrefix() + sub))
                return k != null;
        }

        /// <summary>Vrai si la sous-clé machine existe (ex. tester la présence d'un service pilote).</summary>
        public static bool MachineKeyExists(string sub)
        {
            using (RegistryKey k = Registry.LocalMachine.OpenSubKey(sub))
                return k != null;
        }

        /// <summary>true = CTCP actif (template Internet) ; false = autre fournisseur ; null = illisible.</summary>
        /// <summary>Algorithme de congestion TCP courant du profil Internet (« ctcp », « cubic »,
        /// « bbr2 »…). Les VALEURS restent en anglais même sur un Windows français : le test par
        /// jeton reste donc indépendant de la langue.</summary>
        public static bool? CongestionProviderIs(string wanted)
        {
            NativeResult r = Run(Sys32("netsh.exe"), "interface tcp show supplemental template=internet");
            if (r.ExitCode != 0) return null;
            foreach (string raw in r.Output.Split(new[] { ' ', '\t', '\r', '\n', ':' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string tok = raw.ToLowerInvariant();
                if (tok == "ctcp" || tok == "cubic" || tok == "newreno" || tok == "dctcp" || tok == "bbr2" || tok == "none")
                    return tok == wanted;
            }
            return null;
        }

        /// <summary>État de Teredo (tunnel IPv6 utilisé par le NAT Xbox derrière une IPv4 partagée).</summary>
        public static void SetTeredo(string state)
        {
            RunThrow(Sys32("netsh.exe"), "interface teredo set state type=" + state, "Réglage de Teredo (" + state + ")");
        }

        public static bool? TeredoTypeIs(string wanted)
        {
            NativeResult r = Run(Sys32("netsh.exe"), "interface teredo show state");
            if (r.ExitCode != 0) return null;
            foreach (string raw in r.Output.Split(new[] { ' ', '\t', '\r', '\n', ':' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string tok = raw.ToLowerInvariant();
                if (tok == "disabled" || tok == "client" || tok == "enterpriseclient" || tok == "default" || tok == "server")
                    return tok == wanted;
            }
            return null;
        }

        public static bool? CongestionCtcp()
        {
            NativeResult r = Run(Sys32("netsh.exe"), "interface tcp show supplemental template=internet");
            if (r.ExitCode != 0) return null;
            foreach (string raw in r.Output.Split(new[] { ' ', '\t', '\r', '\n', ':' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string tok = raw.ToLowerInvariant();
                if (tok == "ctcp") return true;
                if (tok == "cubic" || tok == "newreno" || tok == "dctcp" || tok == "bbr2" || tok == "none") return false;
            }
            return null;
        }

        private const string DisplayClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

        private static bool IsAmdDisplaySubKey(RegistryKey ik)
        {
            string desc = ik.GetValue("DriverDesc") as string;
            if (desc == null) return false;
            string low = desc.ToLowerInvariant();
            return low.Contains("amd") || low.Contains("radeon");
        }

        /// <summary>Écrit EnableUlps=0 (disable=true) ou 1 sur chaque GPU AMD/Radeon. Sans effet sans GPU AMD.</summary>
        public static void SetAmdUlps(bool disable)
        {
            using (RegistryKey rk = Registry.LocalMachine.OpenSubKey(DisplayClassKey))
            {
                if (rk == null) return;
                foreach (string c in rk.GetSubKeyNames())
                {
                    int n;
                    if (!int.TryParse(c, out n)) continue;
                    using (RegistryKey ik = rk.OpenSubKey(c, true))
                    {
                        if (ik == null || !IsAmdDisplaySubKey(ik)) continue;
                        ik.SetValue("EnableUlps", disable ? 0 : 1, RegistryValueKind.DWord);
                    }
                }
            }
        }

        /// <summary>true = ULPS coupé sur un GPU AMD ; false = encore actif ; null = pas de GPU AMD.</summary>
        public static bool? AmdUlpsDisabled()
        {
            using (RegistryKey rk = Registry.LocalMachine.OpenSubKey(DisplayClassKey))
            {
                if (rk == null) return null;
                bool found = false;
                foreach (string c in rk.GetSubKeyNames())
                {
                    int n;
                    if (!int.TryParse(c, out n)) continue;
                    using (RegistryKey ik = rk.OpenSubKey(c))
                    {
                        if (ik == null || !IsAmdDisplaySubKey(ik)) continue;
                        found = true;
                        if (IntEquals(ik.GetValue("EnableUlps"), 0)) return true;
                    }
                }
                return found ? (bool?)false : null;
            }
        }

        // ------------------------------------------------------------------
        //  Retrait d'applis Windows préinstallées (dé-bloatware curaté).
        //  Retire un paquet Appx par motif de nom. Best-effort : échec toléré
        //  (appli absente = pas grave). Réversible seulement via le Store.
        // ------------------------------------------------------------------
        private static string PowerShellExe
        {
            get { return Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"); }
        }

        public static bool RemoveAppxByName(string namePattern, Action<string, int> log)
        {
            string cmd = "-NoProfile -ExecutionPolicy Bypass -Command \"Get-AppxPackage -AllUsers -Name '"
                       + namePattern + "*' | Remove-AppxPackage -ErrorAction SilentlyContinue\"";
            NativeResult r = Run(PowerShellExe, cmd, 300000);
            bool ok = r.ExitCode == 0;
            if (log != null) log((ok ? "Retiré : " : "Absent/échec : ") + namePattern, ok ? 1 : 2);
            return ok;
        }

        // ------------------------------------------------------------------
        //  NetBIOS sur TCP/IP (par interface) et compression mémoire (MMAgent)
        // ------------------------------------------------------------------
        private const string NetBtIf = @"SYSTEM\CurrentControlSet\services\NetBT\Parameters\Interfaces";

        /// <summary>NetbiosOptions sur TOUTES les interfaces : 2 = désactivé, 0 = par défaut (DHCP).</summary>
        public static void SetNetbios(bool disable)
        {
            using (RegistryKey root = Registry.LocalMachine.OpenSubKey(NetBtIf, true))
            {
                if (root == null) return;
                foreach (string sub in root.GetSubKeyNames())
                {
                    if (!sub.StartsWith("Tcpip_", StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        using (RegistryKey k = root.OpenSubKey(sub, true))
                            if (k != null) k.SetValue("NetbiosOptions", disable ? 2 : 0, RegistryValueKind.DWord);
                    }
                    catch { }
                }
            }
        }

        /// <summary>Vrai si NetBIOS est désactivé sur TOUTES les interfaces (null si aucune trouvée).</summary>
        public static bool? NetbiosDisabled()
        {
            try
            {
                using (RegistryKey root = Registry.LocalMachine.OpenSubKey(NetBtIf))
                {
                    if (root == null) return null;
                    int seen = 0;
                    foreach (string sub in root.GetSubKeyNames())
                    {
                        if (!sub.StartsWith("Tcpip_", StringComparison.OrdinalIgnoreCase)) continue;
                        using (RegistryKey k = root.OpenSubKey(sub))
                        {
                            if (k == null) continue;
                            seen++;
                            if (!IntEquals(k.GetValue("NetbiosOptions"), 2)) return false;
                        }
                    }
                    return seen == 0 ? (bool?)null : true;
                }
            }
            catch { return null; }
        }

        /// <summary>Compression mémoire de Windows (MMAgent). L'API n'existe qu'en PowerShell.</summary>
        public static void SetMemoryCompression(bool enable)
        {
            RunThrow(PowerShellExe,
                "-NoProfile -ExecutionPolicy Bypass -Command \"" + (enable ? "Enable-MMAgent" : "Disable-MMAgent") + " -MemoryCompression\"",
                enable ? "Activation de la compression mémoire" : "Désactivation de la compression mémoire");
        }

        /// <summary>Vrai si la compression mémoire est DÉSACTIVÉE (le nom de propriété reste anglais).</summary>
        public static bool? MemoryCompressionDisabled()
        {
            try
            {
                NativeResult r = Run(PowerShellExe,
                    "-NoProfile -ExecutionPolicy Bypass -Command \"(Get-MMAgent).MemoryCompression\"", 20000);
                if (r == null || r.Output == null) return null;
                string o = r.Output.Trim();
                if (o.IndexOf("False", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (o.IndexOf("True", StringComparison.OrdinalIgnoreCase) >= 0) return false;
                return null;
            }
            catch { return null; }
        }

        /// <summary>Best-effort : réenregistre les paquets Windows encore présents et ouvre le Store.</summary>
        public static void ReprovisionDefaultApps(Action<string, int> log)
        {
            string cmd = "-NoProfile -ExecutionPolicy Bypass -Command \"Get-AppxPackage -AllUsers | "
                       + "ForEach-Object { Add-AppxPackage -DisableDevelopmentMode -Register "
                       + "($_.InstallLocation + '\\AppXManifest.xml') -ErrorAction SilentlyContinue }\"";
            Run(PowerShellExe, cmd, 300000);
            if (log != null) log("Réenregistrement des applis Windows lancé.", 0);
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-windows-store://home") { UseShellExecute = true }); }
            catch { }
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

        // Délai par défaut : borne les processus BLOQUÉS (winget en attente d'une invite,
        // nvidia-smi figé, tracert vers un hôte injoignable, wevtutil sur un journal géant…)
        // sans jamais couper une installation légitime. Les opérations réellement longues
        // (DISM/SFC/defrag) passent explicitement LongRunTimeoutMs.
        private const int DefaultRunTimeoutMs = 600000;    // 10 min
        public  const int LongRunTimeoutMs    = 3600000;   // 60 min

        public static NativeResult Run(string exe, string args)
        {
            return Run(exe, args, DefaultRunTimeoutMs);
        }

        public static NativeResult Run(string exe, string args, int timeoutMs)
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
                // Les DEUX flux en asynchrone : un processus qui garde stdout ouvert bloquerait
                // ReadToEnd() AVANT même WaitForExit — le délai ne servirait alors à rien.
                var outTask = p.StandardOutput.ReadToEndAsync();
                var errTask = p.StandardError.ReadToEndAsync();
                NativeResult r = new NativeResult();

                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(true); } catch { }        // tue aussi les processus enfants (winget…)
                    try { p.WaitForExit(3000); } catch { }
                    r.ExitCode = -1;
                    r.Output = SafeResult(outTask) + SafeResult(errTask)
                             + "\n[délai dépassé : processus arrêté après " + (timeoutMs / 1000) + " s]";
                    return r;
                }

                r.ExitCode = p.ExitCode;
                r.Output = SafeResult(outTask) + SafeResult(errTask);
                return r;
            }
        }

        private static string SafeResult(System.Threading.Tasks.Task<string> t)
        {
            try { return t.Result ?? ""; } catch { return ""; }
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

        // Plan actif AVANT l'activation d'« Ultimate » — pour rétablir EXACTEMENT ce plan-là
        // (Économie d'énergie sur portable, plan OEM/perso…) et non un « Utilisation normale » imposé.
        private static string PrevPlanPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-prev-powerplan.txt"); }
        }

        private static string GetActiveSchemeGuid()
        {
            NativeResult r = Run(Sys32("powercfg.exe"), "/getactivescheme");
            if (r.ExitCode != 0) return null;
            Match m = GuidRx.Match(r.Output ?? "");
            return m.Success ? m.Value : null;
        }

        public static void EnableUltimatePlan()
        {
            // Mémorise le plan actif AVANT de basculer (sauf s'il est déjà Ultimate) : « Rétablir »
            // reviendra à CE plan précis au lieu d'imposer « Utilisation normale ».
            try
            {
                if (!UltimateActive())
                {
                    string prev = GetActiveSchemeGuid();
                    if (prev != null) File.WriteAllText(PrevPlanPath, prev);
                }
            }
            catch { }

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
            // Rétablit le plan mémorisé avant l'activation d'Ultimate ; à défaut (ou si la photo
            // pointe vers un plan Ultimate), retombe sur « Utilisation normale ».
            string target = BalancedGuid;
            try
            {
                if (File.Exists(PrevPlanPath))
                {
                    string saved = File.ReadAllText(PrevPlanPath).Trim();
                    if (GuidRx.IsMatch(saved)
                        && saved.IndexOf("e9a42b02-d5df-448d-aa00-03f14749eb6", StringComparison.OrdinalIgnoreCase) < 0)
                        target = saved;
                }
            }
            catch { }
            RunThrow(Sys32("powercfg.exe"), "/setactive " + target, "Retour au plan d'alimentation précédent");
            try { if (File.Exists(PrevPlanPath)) File.Delete(PrevPlanPath); } catch { }
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
                if (pci == null) { if (log != null) log("Enum PCI introuvable.", 2); return; }
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

        // ------------------------------------------------------------------
        //  Mode MSI générique par classe de périphérique PCI (USB, stockage, réseau)
        //  Même mécanisme que le GPU : MSISupported=1. Réversible (retrait de la valeur).
        // ------------------------------------------------------------------
        public const string MsiUsbClass     = "{36fc9e60-c465-11cf-8056-444553540000}";
        public const string MsiStorageClass = "{4d36e97b-e325-11ce-bfc1-08002be10318}";
        public const string MsiNetClass     = "{4d36e972-e325-11ce-bfc1-08002be10318}";

        public static int SetMsiForClass(string classGuid, bool enable, Action<string, int> log)
        {
            int count = 0;
            using (RegistryKey pci = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\PCI"))
            {
                if (pci == null) { if (log != null) log("Enum PCI introuvable.", 2); return 0; }
                foreach (string devId in pci.GetSubKeyNames())
                {
                    using (RegistryKey dev = pci.OpenSubKey(devId))
                    {
                        if (dev == null) continue;
                        foreach (string inst in dev.GetSubKeyNames())
                        {
                            using (RegistryKey ik = dev.OpenSubKey(inst))
                            {
                                if (ik == null) continue;
                                string cls = ik.GetValue("ClassGUID") as string;
                                if (cls == null || !cls.Equals(classGuid, StringComparison.OrdinalIgnoreCase)) continue;
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
                                catch (Exception ex) { if (log != null) log("MSI (" + devId + ") : " + ex.Message, 2); }
                            }
                        }
                    }
                }
            }
            if (log != null)
                log("MSI mode " + (enable ? "activé" : "retiré") + " sur " + count + " périphérique(s). Redémarrage requis.", count > 0 ? 1 : 2);
            return count;
        }

        /// <summary>true = tous les périphériques de la classe ont MSI, false = au moins un sans, null = aucun trouvé.</summary>
        public static bool? MsiActiveForClass(string classGuid)
        {
            bool found = false;
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
                                if (ik == null) continue;
                                string cls = ik.GetValue("ClassGUID") as string;
                                if (cls == null || !cls.Equals(classGuid, StringComparison.OrdinalIgnoreCase)) continue;
                                found = true;
                                object v = GetMachine(@"SYSTEM\CurrentControlSet\Enum\PCI\" + devId + "\\" + inst +
                                    @"\Device Parameters\Interrupt Management\MessageSignaledInterruptProperties", "MSISupported");
                                if (!IntEquals(v, 1)) return false;
                            }
                        }
                    }
                }
            }
            return found ? (bool?)true : null;
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

        /// <summary>Arrête puis redémarre un service (courte attente entre les deux). Utilisé pour
        /// les réparations « à chaud » (audio, etc.). Un service qui refuse de s'arrêter n'empêche
        /// pas la tentative de démarrage.</summary>
        public static void RestartService(string name)
        {
            try { StopService(name); } catch { }
            try { System.Threading.Thread.Sleep(1200); } catch { }
            try { StartService(name); } catch { }
        }

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
            public string Kind = "temp";   // temp | gpu | history | bin (entretien par routine)
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
                // Caches de shaders : à vider après une MAJ de pilote ou en cas de stutters —
                // les jeux les recompilent au prochain lancement (saccades passagères normales).
                new CleanTarget { Name = "Shaders NVIDIA DirectX (recompilés au prochain lancement)", Kind = "gpu", Path = Path.Combine(local, @"NVIDIA\DXCache") },
                new CleanTarget { Name = "Shaders NVIDIA OpenGL/Vulkan", Kind = "gpu", Path = Path.Combine(local, @"NVIDIA\GLCache") },
                new CleanTarget { Name = "Shaders DirectX Windows (D3DSCache)", Kind = "gpu", Path = Path.Combine(local, "D3DSCache") },
                new CleanTarget { Name = "Shaders AMD (si GPU AMD)", Kind = "gpu", Path = Path.Combine(local, @"AMD\DxCache") },
                // Rapports de plantage : minidumps et vidages, aucun intérêt à les garder.
                new CleanTarget { Name = "Rapports de plantage (CrashDumps)", Path = Path.Combine(local, "CrashDumps") },
                new CleanTarget { Name = "Minidumps Windows (écrans bleus passés)", Path = Path.Combine(win, "Minidump") },
                // Cache de livraison des mises à jour (P2P) : se reconstitue tout seul.
                new CleanTarget { Name = "Cache de livraison des MAJ (Delivery Optimization)", Path = Path.Combine(win, @"SoftwareDistribution\DeliveryOptimization") },
                // Journaux d'installation de composants (souvent volumineux).
                new CleanTarget { Name = "Journaux Windows (CBS)", Path = Path.Combine(win, @"Logs\CBS") },
                new CleanTarget { Name = "Historique Explorateur : fichiers récents & Jump Lists", Kind = "history", Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Recent") },
                new CleanTarget { Name = "Cache des miniatures et icônes (Explorateur)", Kind = "history", Path = Path.Combine(local, @"Microsoft\Windows\Explorer") },
            };
            AddBrowserCaches(list, local);
            list.Add(new CleanTarget { Name = "Corbeille", Path = null, IsRecycleBin = true, Kind = "bin" });
            foreach (CleanTarget t in list) t.SizeMB = MeasureTarget(t);
            return list;
        }

        // Caches des navigateurs (tous profils) : Chrome, Edge, Brave (dossier Cache) et Firefox (cache2).
        private static void AddBrowserCaches(System.Collections.Generic.List<CleanTarget> list, string local)
        {
            try
            {
                var chromium = new[]
                {
                    new[] { "Chrome", Path.Combine(local, @"Google\Chrome\User Data") },
                    new[] { "Edge",   Path.Combine(local, @"Microsoft\Edge\User Data") },
                    new[] { "Brave",  Path.Combine(local, @"BraveSoftware\Brave-Browser\User Data") },
                };
                foreach (string[] b in chromium)
                {
                    string userData = b[1];
                    if (!Directory.Exists(userData)) continue;
                    foreach (string profile in ProfileDirs(userData))
                    {
                        string cache = Path.Combine(profile, "Cache");
                        if (Directory.Exists(cache))
                            list.Add(new CleanTarget { Name = "Cache " + b[0] + " (" + Path.GetFileName(profile) + ")", Path = cache });
                    }
                }

                string ff = Path.Combine(local, @"Mozilla\Firefox\Profiles");
                if (Directory.Exists(ff))
                    foreach (string profile in Directory.GetDirectories(ff))
                    {
                        string cache = Path.Combine(profile, "cache2");
                        if (Directory.Exists(cache))
                            list.Add(new CleanTarget { Name = "Cache Firefox (" + Path.GetFileName(profile) + ")", Path = cache });
                    }
            }
            catch { }
        }

        // Profils d'un navigateur Chromium : "Default" + "Profile N".
        private static System.Collections.Generic.IEnumerable<string> ProfileDirs(string userData)
        {
            var dirs = new System.Collections.Generic.List<string>();
            try
            {
                string def = Path.Combine(userData, "Default");
                if (Directory.Exists(def)) dirs.Add(def);
                foreach (string d in Directory.GetDirectories(userData, "Profile *")) dirs.Add(d);
            }
            catch { }
            return dirs;
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
        //  Dernier niveau Auto choisi (Prudent/Équilibré/Agressif)
        // ------------------------------------------------------------------
        private static string AutoLevelPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-autolevel.txt"); }
        }

        public static int LoadAutoLevel()
        {
            try
            {
                if (!File.Exists(AutoLevelPath)) return -1;
                int v;
                if (int.TryParse(File.ReadAllText(AutoLevelPath).Trim(), out v) && v >= 0 && v <= 2) return v;
            }
            catch { }
            return -1;
        }

        public static void SaveAutoLevel(int level)
        {
            try { File.WriteAllText(AutoLevelPath, level.ToString()); } catch { }
        }

        // ------------------------------------------------------------------
        //  Nettoyeur RAM auto
        // ------------------------------------------------------------------
        private static string RamCleanerPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-ramcleaner.txt"); }
        }

        public static bool LoadRamCleaner(out int thresholdMB)
        {
            thresholdMB = 1024;
            try
            {
                if (!File.Exists(RamCleanerPath)) return false;
                string[] lines = File.ReadAllLines(RamCleanerPath);
                if (lines.Length > 0 && lines[0] == "1")
                {
                    if (lines.Length > 1) int.TryParse(lines[1], out thresholdMB);
                    return true;
                }
            }
            catch { }
            return false;
        }

        public static void SaveRamCleaner(bool enabled, int thresholdMB)
        {
            try
            {
                File.WriteAllLines(RamCleanerPath, new[] { enabled ? "1" : "0", thresholdMB.ToString() });
            }
            catch { }
        }

        // ------------------------------------------------------------------
        //  Affinité CPU et Profils d'Alimentation
        // ------------------------------------------------------------------
        public static void SetProcessAffinity(int pid, long mask)
        {
            try { Process.GetProcessById(pid).ProcessorAffinity = (IntPtr)mask; } catch { }
        }

        public static void SetProcessPriority(int pid, ProcessPriorityClass prio)
        {
            try { Process.GetProcessById(pid).PriorityClass = prio; } catch { }
        }

        public static string GetActivePowerProfile()
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo { FileName = "powercfg", Arguments = "/getactivescheme", UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true });
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                var m = Regex.Match(output, @"GUID de.*:\s*([0-9a-f\-]{36})", RegexOptions.IgnoreCase);
                if (m.Success) return m.Groups[1].Value;
            }
            catch { }
            return null;
        }

        public static void SetActivePowerProfile(string guid)
        {
            try { Process.Start(new ProcessStartInfo { FileName = "powercfg", Arguments = "/setactive " + guid, UseShellExecute = false, CreateNoWindow = true }).WaitForExit(); } catch { }
        }

        public static string GameAffinityPath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-game-affinity.txt"); } }

        public static Dictionary<string, Tuple<string, long>> LoadGameAffinity()
        {
            var d = new Dictionary<string, Tuple<string, long>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(GameAffinityPath))
                {
                    foreach (string line in File.ReadAllLines(GameAffinityPath))
                    {
                        var p = line.Split('|');
                        if (p.Length >= 3)
                        {
                            long mask;
                            if (long.TryParse(p[2], out mask)) d[p[0]] = Tuple.Create(p[1], mask);
                        }
                    }
                }
            }
            catch { }
            return d;
        }

        public static void SaveGameAffinity(Dictionary<string, Tuple<string, long>> dict)
        {
            try
            {
                var lines = dict.Select(kvp => kvp.Key + "|" + kvp.Value.Item1 + "|" + kvp.Value.Item2.ToString());
                File.WriteAllLines(GameAffinityPath, lines.ToArray());
            }
            catch { }
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
            int failed = 0;
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
                {
                    // Distinguer « clé réellement absente » (bénin) de « présente mais export
                    // refusé » (ACL, chemin trop long…) : ce dernier est un VRAI trou de sauvegarde.
                    NativeResult q = Run(Sys32("reg.exe"), "query \"" + exportKey + "\"");
                    if (q.ExitCode == 0)
                    {
                        failed++;
                        log("ÉCHEC sauvegarde : " + exportKey + " (clé présente mais export refusé) — non restaurable.", 3);
                    }
                    else
                        log("Clé absente (rien à sauvegarder) : " + exportKey, 0);
                }
            }
            if (failed > 0)
                log("Attention : " + failed + " clé(s) n'ont PAS pu être sauvegardées — restauration incomplète pour celles-ci.", 2);

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
            CreateRestorePoint("ONYX", log);
        }

        public static void CreateRestorePoint(string description, Action<string, int> log)
        {
            log("Création d'un point de restauration système (peut prendre une minute)...", 0);
            try
            {
                ManagementScope scope = new ManagementScope(@"\\.\root\default");
                ManagementPath path = new ManagementPath("SystemRestore");
                using (ManagementClass mc = new ManagementClass(scope, path, new ObjectGetOptions()))
                using (ManagementBaseObject inParams = mc.GetMethodParameters("CreateRestorePoint"))
                {
                    inParams["Description"] = string.IsNullOrEmpty(description) ? "ONYX" : description;
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
        //  Exclusions Windows Defender (API WMI officielle, nécessite l'élévation)
        // ------------------------------------------------------------------
        private static ManagementScope DefenderScope()
        {
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Defender");
            scope.Connect();
            return scope;
        }

        /// <summary>Dossiers actuellement exclus de l'analyse Defender (vide si illisible).</summary>
        public static List<string> DefenderExclusions()
        {
            var list = new List<string>();
            try
            {
                using (var s = new ManagementObjectSearcher(DefenderScope(),
                    new ObjectQuery("SELECT ExclusionPath FROM MSFT_MpPreference")))
                    foreach (ManagementObject mo in s.Get())
                    {
                        string[] paths = mo["ExclusionPath"] as string[];
                        if (paths != null) foreach (string p in paths) if (!string.IsNullOrEmpty(p)) list.Add(p);
                    }
            }
            catch { }
            return list;
        }

        private static bool DefenderInvoke(string method, string path, Action<string, int> log)
        {
            try
            {
                using (var mc = new ManagementClass(DefenderScope(), new ManagementPath("MSFT_MpPreference"), null))
                using (ManagementBaseObject inParams = mc.GetMethodParameters(method))
                {
                    inParams["ExclusionPath"] = new[] { path };
                    using (mc.InvokeMethod(method, inParams, null)) { }
                }
                return true;
            }
            catch (Exception ex) { if (log != null) log("Defender (" + method + ") : " + ex.Message, 2); return false; }
        }

        public static bool DefenderAddExclusion(string path, Action<string, int> log) { return DefenderInvoke("Add", path, log); }
        public static bool DefenderRemoveExclusion(string path, Action<string, int> log) { return DefenderInvoke("Remove", path, log); }

        public class RestorePoint { public int Seq; public string Description; public DateTime When; public int Type; }

        /// <summary>Liste les points de restauration système existants (plus récents d'abord).</summary>
        public static List<RestorePoint> ListRestorePoints()
        {
            var list = new List<RestorePoint>();
            try
            {
                using (var s = new ManagementObjectSearcher(@"\\.\root\default", "SELECT * FROM SystemRestore"))
                    foreach (ManagementObject mo in s.Get())
                    {
                        var rp = new RestorePoint();
                        try { rp.Seq = Convert.ToInt32(mo["SequenceNumber"]); } catch { }
                        rp.Description = Convert.ToString(mo["Description"]);
                        try { rp.Type = Convert.ToInt32(mo["RestorePointType"]); } catch { }
                        string ct = Convert.ToString(mo["CreationTime"]);   // yyyyMMddHHmmss.xxxxxx±zzz
                        rp.When = ParseWmiDate(ct);
                        list.Add(rp);
                    }
            }
            catch { }
            list.Sort((a, b) => b.When.CompareTo(a.When));
            return list;
        }

        private static DateTime ParseWmiDate(string s)
        {
            try
            {
                if (!string.IsNullOrEmpty(s) && s.Length >= 14)
                    return new DateTime(
                        int.Parse(s.Substring(0, 4)), int.Parse(s.Substring(4, 2)), int.Parse(s.Substring(6, 2)),
                        int.Parse(s.Substring(8, 2)), int.Parse(s.Substring(10, 2)), int.Parse(s.Substring(12, 2)));
            }
            catch { }
            return DateTime.MinValue;
        }

        /// <summary>Active la restauration système sur le lecteur Windows (si un « optimiseur » l'a coupée).</summary>
        public static bool EnableSystemRestore(Action<string, int> log)
        {
            bool ok = false;
            try
            {
                // Retire d'abord la politique de blocage éventuelle.
                DelMachine(@"SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore", "DisableSR");
                ManagementScope scope = new ManagementScope(@"\\.\root\default");
                ManagementPath path = new ManagementPath("SystemRestore");
                using (ManagementClass mc = new ManagementClass(scope, path, new ObjectGetOptions()))
                using (ManagementBaseObject inParams = mc.GetMethodParameters("Enable"))
                {
                    inParams["Drive"] = Path.GetPathRoot(Environment.SystemDirectory);   // "C:\"
                    inParams["WaitTillEnabled"] = true;
                    using (ManagementBaseObject outParams = mc.InvokeMethod("Enable", inParams, null))
                    {
                        uint rv = Convert.ToUInt32(outParams["ReturnValue"]);
                        ok = rv == 0;
                        log(ok ? "Restauration système activée sur le lecteur Windows."
                               : "Activation de la restauration système : code " + rv + ".", ok ? 1 : 2);
                    }
                }
            }
            catch (Exception ex) { log("Restauration système : " + ex.Message, 2); }
            return ok;
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

        /// <summary>
        /// Applique le profil NVIDIA faible latence ADAPTÉ à la machine.
        ///
        /// Avant, cette méthode imposait « Ultra Low Latency + 1 image pré-rendue » à TOUT LE MONDE.
        /// Ces deux réglages suppriment la file d'attente de rendu, or c'est elle qui absorbe les
        /// à-coups du processeur : sur une machine limitée par le CPU, l'app faisait donc PERDRE des
        /// images en croyant en gagner (GPU à 40 % pendant que le CPU sature, chutes brutales à
        /// chaque pic). On applique désormais le profil sûr par défaut, et Ultra uniquement quand la
        /// mesure en jeu montre une carte graphique réellement à fond.
        /// </summary>
        public static void ApplyNvidiaLowLatency(Action<string, int> log)
        {
            double cpuAvg, gpuAvg; DateTime quand;
            if (!Bottleneck.LastMeasure(out cpuAvg, out gpuAvg, out quand)) { cpuAvg = -1; gpuAvg = -1; }
            NvProfile.Applique(NvProfile.Recommande(cpuAvg, gpuAvg), log);
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

        /// <summary>Photo des DNS IPv4 par carte (Description → serveurs ; null = automatique/DHCP).</summary>
        public static Dictionary<string, string[]> SnapshotDns()
        {
            var snap = new Dictionary<string, string[]>();
            try
            {
                using (var mos = new ManagementObjectSearcher(
                    "SELECT SettingID, DNSServerSearchOrder FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled=true"))
                {
                    foreach (ManagementObject mo in mos.Get())
                    {
                        // SettingID = GUID unique par carte (Description n'est PAS unique : cartes double-port).
                        string id = Convert.ToString(mo["SettingID"]);
                        if (string.IsNullOrEmpty(id) || snap.ContainsKey(id)) continue;
                        string[] dns = mo["DNSServerSearchOrder"] as string[];
                        snap[id] = (dns != null && dns.Length > 0) ? dns : null;
                    }
                }
            }
            catch { }
            return snap;
        }

        /// <summary>Restaure une photo de DNS IPv4 carte par carte (filet de sécurité du panneau DNS).</summary>
        public static void RestoreDnsSnapshot(Dictionary<string, string[]> snap, Action<string, int> log)
        {
            int done = 0;
            try
            {
                using (var mos = new ManagementObjectSearcher(
                    "SELECT * FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled=true"))
                {
                    foreach (ManagementObject mo in mos.Get())
                    {
                        string id = Convert.ToString(mo["SettingID"]);
                        string[] servers;
                        if (string.IsNullOrEmpty(id) || !snap.TryGetValue(id, out servers)) continue;
                        try
                        {
                            using (ManagementBaseObject inp = mo.GetMethodParameters("SetDNSServerSearchOrder"))
                            {
                                inp["DNSServerSearchOrder"] = servers; // null => automatique
                                using (mo.InvokeMethod("SetDNSServerSearchOrder", inp, null)) { done++; }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
            FlushDns();
            if (log != null) log("DNS précédents restaurés sur " + done + " carte(s).", done > 0 ? 1 : 2);
        }

        /// <summary>
        /// Applique des DNS IPv6 (null = retour DHCP/RA) sur les interfaces actives via netsh.
        /// Échecs tolérés : une interface sans IPv6 est simplement ignorée.
        /// </summary>
        public static void SetDnsV6(string[] servers, Action<string, int> log)
        {
            int done = 0;
            try
            {
                foreach (System.Net.NetworkInformation.NetworkInterface ni in
                         System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback ||
                        ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Tunnel) continue;

                    string name = ni.Name.Replace("\"", "");
                    if (servers == null)
                    {
                        if (Run(Sys32("netsh.exe"),
                                "interface ipv6 set dnsservers name=\"" + name + "\" source=dhcp").ExitCode == 0) done++;
                        continue;
                    }
                    bool ok = Run(Sys32("netsh.exe"),
                        "interface ipv6 set dnsservers name=\"" + name + "\" source=static address=" + servers[0] + " validate=no").ExitCode == 0;
                    for (int i = 1; ok && i < servers.Length; i++)
                        Run(Sys32("netsh.exe"),
                            "interface ipv6 add dnsservers name=\"" + name + "\" address=" + servers[i] + " index=" + (i + 1) + " validate=no");
                    if (ok) done++;
                }
            }
            catch { }
            if (log != null && done > 0)
                log((servers == null ? "DNS IPv6 remis en automatique" : "DNS IPv6 appliqué") + " sur " + done + " interface(s).", 1);
        }

        /// <summary>Rafraîchissement réseau LÉGER (sans coupure ni redémarrage) : vide le cache DNS et le cache ARP.</summary>
        public static void NetworkRefresh(Action<string, int> log)
        {
            NativeResult d = Run(Sys32("ipconfig.exe"), "/flushdns");
            log("Cache DNS vidé" + (d.ExitCode == 0 ? "." : " (code " + d.ExitCode + ")."), d.ExitCode == 0 ? 1 : 2);
            NativeResult a = Run(Sys32("netsh.exe"), "interface ip delete arpcache");
            log("Cache ARP vidé" + (a.ExitCode == 0 ? "." : " (code " + a.ExitCode + ")."), a.ExitCode == 0 ? 1 : 2);
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

        /// <summary>
        /// Répare l'intégrité de Windows : DISM /RestoreHealth (répare l'image) puis SFC /scannow
        /// (répare les fichiers système). Long (10-20 min). Utile quand des crashs persistent.
        /// </summary>
        public static void RepairWindows(Action<string, int> log)
        {
            log("Réparation de l'image Windows (DISM /RestoreHealth) — patiente, cela peut prendre 10-20 min...", 0);
            NativeResult dism = Run(Sys32("dism.exe"), "/Online /Cleanup-Image /RestoreHealth", LongRunTimeoutMs);
            string do_ = (dism.Output ?? "").ToLowerInvariant();
            if (dism.ExitCode == 0 || do_.Contains("terminée") || do_.Contains("completed successfully"))
                log("DISM : image Windows vérifiée/réparée.", 1);
            else
                log("DISM : code " + dism.ExitCode + " (voir plus haut). On lance quand même SFC.", 2);

            log("Vérification des fichiers système (SFC /scannow) — encore quelques minutes...", 0);
            NativeResult sfc = Run(Sys32("sfc.exe"), "/scannow", LongRunTimeoutMs);
            string so = (sfc.Output ?? "").ToLowerInvariant();
            if (so.Contains("did not find any integrity violations") || so.Contains("n'a trouvé aucune violation"))
                log("SFC : aucun fichier système corrompu. ✔", 1);
            else if (so.Contains("successfully repaired") || so.Contains("réparé"))
                log("SFC : fichiers corrompus trouvés et RÉPARÉS. Redémarre le PC.", 1);
            else if (so.Contains("unable to fix") || so.Contains("n'a pas pu réparer") || so.Contains("impossible de réparer"))
                log("SFC : des fichiers n'ont pas pu être réparés — relance après un redémarrage, ou envisage une réparation de Windows.", 2);
            else
                log("SFC terminé (code " + sfc.ExitCode + "). Redémarre le PC si des réparations ont eu lieu.", 0);

            log("Réparation d'intégrité Windows terminée.", 1);
        }

        private const string DefragTask = @"\Microsoft\Windows\Defrag\ScheduledDefrag";

        /// <summary>
        /// Optimise tous les lecteurs fixes : defrag /O choisit tout seul le RE-TRIM (SSD) ou
        /// la défragmentation (HDD). Réactive aussi la tâche planifiée si un « optimiseur » l'a
        /// coupée. Peut durer plusieurs minutes sur un disque dur mécanique.
        /// </summary>
        public static void OptimizeDrives(Action<string, int> log)
        {
            // Réactive la maintenance planifiée si elle a été désactivée.
            if (ScheduledTaskDisabled(DefragTask) == true)
            {
                SetScheduledTask(DefragTask, true);
                log("Optimisation planifiée des lecteurs réactivée (elle avait été désactivée).", 1);
            }

            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                try
                {
                    if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                    string letter = d.Name.TrimEnd('\\');   // "C:"
                    log("Optimisation de " + letter + " (RE-TRIM si SSD, défrag si HDD)...", 0);
                    NativeResult r = Run(Sys32("defrag.exe"), letter + " /O", LongRunTimeoutMs);
                    log(letter + " : " + (r.ExitCode == 0 ? "optimisé. ✔" : "code " + r.ExitCode + " (peut nécessiter un autre passage)."),
                        r.ExitCode == 0 ? 1 : 2);
                }
                catch (Exception ex) { log("Optimisation lecteur : " + ex.Message, 2); }
            }
            log("Optimisation des lecteurs terminée.", 1);
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
