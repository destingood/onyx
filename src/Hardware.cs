using System;
using System.Collections.Generic;
using System.Management;

namespace BTOptimizer
{
    public class HwProfile
    {
        public int DiskCount;
        public bool AllSsd;
        public bool AnyHdd;
        public string GpuVendor = "?";   // NVIDIA / AMD / Intel / ?
        public string GpuName = "-";
        public int RamGB;
        public string CpuName = "-";
        public bool CpuUnlocked;
        public int MaxHz;                // meilleure fréquence supportée parmi les écrans (500 Hz...)
        public int ScreensBelowMax;      // écrans qui tournent SOUS leur fréquence max (à corriger)
        // Signaux additionnels pour l'auto-tune intelligent
        public bool IsLaptop;
        public bool IsWin11;
        public bool HasBluetooth;
        public bool HasPrinter;
        public bool HasTouch;
        public bool HypervisorActive;    // VBS/Hyper-V en cours (coûte quelques % de CPU)
        public int RamRatedMTs;          // vitesse max des barrettes (XMP)
        public int RamRunningMTs;        // vitesse réellement configurée

        public string Summary()
        {
            string disk = DiskCount == 0 ? "disque : ?" :
                (AllSsd ? "disque : SSD" : (AnyHdd ? "disque : SSD+HDD mixte" : "disque : ?"));
            string screen = MaxHz > 0 ? "  ·  écran " + MaxHz + " Hz" : "";
            return CpuName + "  ·  " + RamGB + " Go RAM  ·  " + GpuName + "  ·  " + disk
                + "  ·  " + (IsLaptop ? "portable" : "fixe") + "  ·  " + (IsWin11 ? "Windows 11" : "Windows 10") + screen;
        }
    }

    /// <summary>Détection matérielle (disque SSD/HDD, GPU, RAM, CPU) pour l'auto-tune intelligent.</summary>
    internal static class Hardware
    {
        public static HwProfile Detect()
        {
            var hw = new HwProfile();

            // Disques : SSD (MediaType=4) vs HDD (3).
            try
            {
                using (var s = new ManagementObjectSearcher(
                    @"root\Microsoft\Windows\Storage", "SELECT MediaType FROM MSFT_PhysicalDisk"))
                {
                    foreach (ManagementObject mo in s.Get())
                    {
                        object mt = mo["MediaType"];
                        if (mt == null) continue;
                        int t = Convert.ToInt32(mt);
                        hw.DiskCount++;
                        if (t == 3) hw.AnyHdd = true;
                    }
                }
                hw.AllSsd = hw.DiskCount > 0 && !hw.AnyHdd;
            }
            catch { }

            // GPU : nvidia-smi donne le vrai nom NVIDIA (évite les adaptateurs virtuels type Parsec).
            if (Sys.NvSmiPath() != null)
            {
                hw.GpuVendor = "NVIDIA";
                Sys.GpuOcInfo g = Sys.QueryGpuOc();
                if (g.Ok && !string.IsNullOrEmpty(g.Name)) hw.GpuName = g.Name;
            }
            try
            {
                string firstReal = null, firstAny = null;
                using (var s = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController"))
                {
                    foreach (ManagementObject mo in s.Get())
                    {
                        string name = Convert.ToString(mo["Name"]);
                        if (string.IsNullOrEmpty(name)) continue;
                        string low = name.ToLowerInvariant();
                        if (low.Contains("microsoft") || low.Contains("basic")) continue;
                        if (firstAny == null) firstAny = name;
                        bool real = low.Contains("nvidia") || low.Contains("geforce") || low.Contains("rtx") || low.Contains("gtx")
                                 || low.Contains("amd") || low.Contains("radeon") || low.Contains("arc") || low.Contains("iris");
                        // On ignore les écrans virtuels (Parsec, Spacedesk, Remote, Meta, DisplayLink...).
                        bool virt = low.Contains("virtual") || low.Contains("parsec") || low.Contains("spacedesk")
                                 || low.Contains("remote") || low.Contains("displaylink") || low.Contains("idd") || low.Contains("meta");
                        if (real && !virt) { firstReal = name; break; }
                    }
                }
                string pick = firstReal ?? firstAny;
                if (pick != null)
                {
                    if (hw.GpuVendor != "NVIDIA" || hw.GpuName == "-") hw.GpuName = firstReal ?? hw.GpuName;
                    if (hw.GpuName == "-") hw.GpuName = pick;
                    string low = pick.ToLowerInvariant();
                    if (hw.GpuVendor == "?")
                    {
                        if (low.Contains("nvidia") || low.Contains("geforce") || low.Contains("rtx") || low.Contains("gtx")) hw.GpuVendor = "NVIDIA";
                        else if (low.Contains("amd") || low.Contains("radeon")) hw.GpuVendor = "AMD";
                        else if (low.Contains("intel")) hw.GpuVendor = "Intel";
                    }
                }
            }
            catch { }

            // Écrans : la fréquence max supportée décide du profil « très hauts FPS ».
            try
            {
                foreach (DisplayInfo.DisplayMode d in DisplayInfo.Query())
                {
                    if (d.MaxHz > hw.MaxHz) hw.MaxHz = d.MaxHz;
                    if (d.BelowMax) hw.ScreensBelowMax++;
                }
            }
            catch { }

            // Hyperviseur actif (VBS / Hyper-V) : coûte quelques % de CPU en jeu.
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT HypervisorPresent FROM Win32_ComputerSystem"))
                    foreach (ManagementObject mo in s.Get())
                    {
                        object hv = mo["HypervisorPresent"];
                        if (hv != null && Convert.ToBoolean(hv)) hw.HypervisorActive = true;
                    }
            }
            catch { }

            Sys.RamInfo ram = Sys.QueryRam();
            hw.RamGB = (int)Math.Round(ram.TotalMB / 1024.0);
            hw.RamRatedMTs = ram.SpeedRated;
            hw.RamRunningMTs = ram.SpeedRunning;
            Sys.CpuInfo cpu = Sys.QueryCpu();
            hw.CpuName = cpu.Name;
            string cl = (cpu.Name ?? "").ToLowerInvariant();
            hw.CpuUnlocked = cl.Contains("ryzen") || cl.EndsWith("k") || cl.Contains("k ") ||
                             cl.Contains("kf") || cl.Contains("ks") || cl.Contains("x ") || cl.Contains("threadripper");

            // Windows 11 = build >= 22000 (instantané).
            try { hw.IsWin11 = Environment.OSVersion.Version.Build >= 22000; } catch { }

            // Écran tactile (instantané) : SM_MAXIMUMTOUCHES.
            try { hw.HasTouch = GetSystemMetrics(SM_MAXIMUMTOUCHES) > 0; } catch { }

            // Portable vs fixe : type de châssis (Win32_SystemEnclosure).
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT ChassisTypes FROM Win32_SystemEnclosure"))
                    foreach (ManagementObject mo in s.Get())
                    {
                        var types = mo["ChassisTypes"] as ushort[];
                        if (types == null) continue;
                        foreach (ushort t in types)
                            if (t == 8 || t == 9 || t == 10 || t == 11 || t == 12 || t == 14 || t == 30 || t == 31 || t == 32)
                                hw.IsLaptop = true;
                    }
            }
            catch { }

            // Bluetooth présent (radio/périphérique).
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Name FROM Win32_PnPEntity WHERE PNPClass='Bluetooth'"))
                    foreach (ManagementObject mo in s.Get()) { hw.HasBluetooth = true; break; }
            }
            catch { }

            // Imprimante réelle (hors PDF/XPS/OneNote/Fax et ports virtuels).
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Name,PortName FROM Win32_Printer"))
                    foreach (ManagementObject mo in s.Get())
                    {
                        string n = (Convert.ToString(mo["Name"]) ?? "").ToLowerInvariant();
                        string port = (Convert.ToString(mo["PortName"]) ?? "").ToLowerInvariant();
                        if (n.Contains("pdf") || n.Contains("xps") || n.Contains("onenote") || n.Contains("fax") || n.Contains("clipboard")) continue;
                        if (port.StartsWith("portprompt") || port.StartsWith("nul") || port.StartsWith("shrfax") || port.Length == 0) continue;
                        hw.HasPrinter = true; break;
                    }
            }
            catch { }

            return hw;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);
        private const int SM_MAXIMUMTOUCHES = 95;

        public const int LevelPrudent = 0, LevelBalanced = 1, LevelAggressive = 2;

        /// <summary>
        /// Réglages EXPÉRIMENTAUX / haute chaleur (état CPU 100 % permanent, C-States off) :
        /// JAMAIS inclus dans une sélection automatique — ni Auto (Prudent/Équilibré/Agressif),
        /// ni ⚡ TOUT OPTIMISER, ni preset eSport, ni packs Latence/500 FPS, ni Benchmark.
        /// L'utilisateur doit les cocher lui-même (demande explicite). Restent disponibles à la
        /// main, via « Tout cocher », et — pour l'état 100 % — via le bouton « Boost CPU maximal »
        /// de l'overclock (qui les affiche noir sur blanc avant application).
        /// </summary>
        public static readonly string[] NeverAuto = { "proc_min_100", "cpu_idle_disable" };

        // Extras sûrs et réversibles ajoutés au niveau Agressif (le matériel filtre ensuite).
        private static readonly string[] AggroExtras =
        {
            "input_queues", "dynamic_tick", "disk_timeout_off",
            "ssdp_off", "wcncsvc_off", "dot3svc_off", "wfds_off", "diag_collector_off", "ajrouter_off",
            "fax_off", "wallet_service_off", "wisvc_off", "wmp_network_off", "dmwappush_off", "retail_demo_off",
            "semgr_off", "ndu_off", "geo_service_off", "phone_service_off", "smartcard_off",
            "store_auto_update_off", "first_logon_anim_off", "low_disk_warning_off", "auto_end_tasks",
            "wait_kill_service", "recent_docs_off", "online_speech_off", "inking_personalization_off",
            "lang_list_access_off", "voice_activation_off", "clipboard_cloud_off", "clipboard_history_off",
            "settings_sync_off", "search_history_off", "find_my_device_off", "spooler_off", "printnotify_off",
            "tablet_service_off", "sensor_service_off", "displayenhancement_off", "crash_dump_minimal",
            "no_lock_screen", "llmnr_off", "ipv6_tunnels_off", "smb_throttle_off", "numlock_boot"
        };

        /// <summary>Overload compat (niveau Équilibré).</summary>
        public static HashSet<string> AutoTuneIds(List<Tweak> all, HwProfile hw)
        {
            return AutoTuneIds(all, hw, LevelBalanced);
        }

        /// <summary>
        /// Sélection auto adaptée au matériel ET au niveau souhaité :
        /// 0 Prudent (recommandé, sans redémarrage), 1 Équilibré (latence/perf sûr),
        /// 2 Agressif (max sûr, réversible). Sécurité et MSI stockage jamais inclus.
        /// </summary>
        public static HashSet<string> AutoTuneIds(List<Tweak> all, HwProfile hw, int level)
        {
            var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Tweak t in all) have.Add(t.Id);

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Base selon le niveau
            foreach (Tweak t in all)
            {
                if (level == LevelPrudent) { if (t.Recommended && !t.Reboot) ids.Add(t.Id); }
                else if (t.Esport || t.Recommended) ids.Add(t.Id);
            }

            // Niveau Agressif : ajoute les extras sûrs (le matériel filtre juste après).
            if (level >= LevelAggressive)
                foreach (string id in AggroExtras) ids.Add(id);

            // --- Disque ---
            if (hw.AllSsd)
            {
                ids.Add("sysmain_off");
                ids.Add("prefetch_off");
            }
            else
            {
                ids.Remove("sysmain_off");
                ids.Remove("prefetch_off");
                ids.Remove("disk_timeout_off");
            }

            // --- RAM (combinaison de pages = redémarrage : pas au niveau Prudent) ---
            if (hw.RamGB >= 16 && level >= LevelBalanced)
                ids.Add("disable_paging_combining");
            else
            {
                ids.Remove("disable_paging_combining");
                if (hw.RamGB < 16) ids.Remove("paging_executive");
            }

            // --- GPU ---
            bool nvidia = hw.GpuVendor != null && hw.GpuVendor.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0;
            if (nvidia && level >= LevelBalanced) ids.Add("nvidia_telemetry_off");
            else ids.Remove("nvidia_telemetry_off");

            // --- Réseau : MSI carte réseau (redémarrage : pas au niveau Prudent) ---
            if (level >= LevelBalanced) ids.Add("msi_network"); else ids.Remove("msi_network");

            // --- Portable : préserver batterie/chaleur (gagne sur les extras Agressif) ---
            if (hw.IsLaptop)
            {
                ids.Remove("proc_min_100");
                ids.Remove("power_throttling");
                ids.Remove("cpu_perf_boost_aggressive");
                ids.Remove("cpu_idle_disable");
                ids.Remove("usb_suspend");
                ids.Remove("pcie_aspm_off");
                ids.Remove("disk_timeout_off");
            }
            else if (!hw.HasTouch && level >= LevelBalanced)
            {
                ids.Add("sensor_service_off");
                ids.Add("displayenhancement_off");
            }

            // Écran tactile : ne pas couper les services tactiles/capteurs.
            if (hw.HasTouch)
            {
                ids.Remove("sensor_service_off");
                ids.Remove("displayenhancement_off");
                ids.Remove("tablet_service_off");
            }

            // --- Windows 11 vs 10 ---
            if (hw.IsWin11)
            {
                if (level >= LevelBalanced) { ids.Add("widgets_off"); ids.Add("chat_taskbar_off"); ids.Add("copilot_off"); }
            }
            else
            {
                ids.Remove("widgets_off"); ids.Remove("chat_taskbar_off"); ids.Remove("copilot_off");
                ids.Remove("dx_vrr"); ids.Remove("taskbar_end_task"); ids.Remove("snap_assist_off");
            }

            // --- Bluetooth / Imprimante ---
            if (!hw.HasBluetooth) ids.Remove("audio_bt_absolute_volume_off");
            if (hw.HasPrinter) { ids.Remove("spooler_off"); ids.Remove("printnotify_off"); }
            else if (level >= LevelBalanced) { ids.Add("spooler_off"); ids.Add("printnotify_off"); }

            // --- Jamais en auto (sécurité / avancé), quel que soit le niveau : appliqué EN DERNIER ---
            ids.Remove("spectre_off");
            ids.Remove("vbs_off");
            ids.Remove("wsearch_off");
            ids.Remove("msi_storage");

            // --- Écran très haute fréquence (240 Hz+) : on vise les très hauts FPS ---
            // Dès Équilibré (jamais en Prudent : redémarrage/consommation) ; le portable
            // garde ses protections batterie/chaleur.
            if (hw.MaxHz >= 240 && level >= LevelBalanced)
            {
                ids.Add("input_queues");             // files souris/clavier courtes (neutre batterie)
                if (!hw.IsLaptop)
                    ids.Add("dynamic_tick");         // tick noyau fixe : frame pacing plus régulier
            }

            // EXPÉRIMENTAL / haute chaleur : JAMAIS en auto, quel que soit le niveau ou l'écran.
            // Filet de sécurité final — même si une branche ci-dessus les avait ajoutés.
            foreach (string id in NeverAuto) ids.Remove(id);

            ids.RemoveWhere(id => !have.Contains(id));
            return ids;
        }

        /// <summary>
        /// Réglages écartés du bouton « ⚡ TOUT OPTIMISER » : causes prouvées de crashs
        /// (HAGS → « dispositif de rendu perdu ») ou de boutiques en jeu cassées
        /// (applis UWP de fond, tunnels IPv6). Ils restent cochables à la main.
        /// </summary>
        public static readonly string[] OneClickExcluded = { "hags", "bg_apps", "ipv6_tunnels_off" };

        /// <summary>Sélection du bouton « ⚡ TOUT OPTIMISER » : l'auto-tune du niveau donné,
        /// moins les réglages à risque de compatibilité jeux/boutiques.</summary>
        public static HashSet<string> OneClickIds(List<Tweak> all, HwProfile hw, int level)
        {
            HashSet<string> ids = AutoTuneIds(all, hw, level);
            foreach (string id in OneClickExcluded) ids.Remove(id);
            return ids;
        }

        /// <summary>Preset Benchmark : tout SAUF les tweaks qui réduisent la sécurité et
        /// les réglages expérimentaux/haute chaleur (jamais appliqués automatiquement).</summary>
        public static HashSet<string> BenchmarkIds(List<Tweak> all)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Tweak t in all) ids.Add(t.Id);
            ids.Remove("spectre_off");
            ids.Remove("vbs_off");
            foreach (string id in NeverAuto) ids.Remove(id);
            return ids;
        }
    }
}
