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

        public string Summary()
        {
            string disk = DiskCount == 0 ? "disque : ?" :
                (AllSsd ? "disque : SSD" : (AnyHdd ? "disque : SSD+HDD mixte" : "disque : ?"));
            return CpuName + "  ·  " + RamGB + " Go RAM  ·  " + GpuName + "  ·  " + disk;
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

            Sys.RamInfo ram = Sys.QueryRam();
            hw.RamGB = (int)Math.Round(ram.TotalMB / 1024.0);
            Sys.CpuInfo cpu = Sys.QueryCpu();
            hw.CpuName = cpu.Name;
            string cl = (cpu.Name ?? "").ToLowerInvariant();
            hw.CpuUnlocked = cl.Contains("ryzen") || cl.EndsWith("k") || cl.Contains("k ") ||
                             cl.Contains("kf") || cl.Contains("ks") || cl.Contains("x ") || cl.Contains("threadripper");
            return hw;
        }

        /// <summary>Sélection intelligente : base « eSport + recommandé », adaptée au matériel, hors sécurité/expérimental.</summary>
        public static HashSet<string> AutoTuneIds(List<Tweak> all, HwProfile hw)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Tweak t in all)
                if (t.Esport || t.Recommended) ids.Add(t.Id);

            // Tweaks dangereux sur disque mécanique : à retirer si un HDD est présent.
            if (!hw.AllSsd)
            {
                ids.Remove("sysmain_off");
                ids.Remove("prefetch_off");
            }
            // Jamais en auto : sécurité et expérimental (choix explicite requis).
            ids.Remove("spectre_off");
            ids.Remove("vbs_off");
            ids.Remove("cpu_idle_disable");
            ids.Remove("dynamic_tick");
            ids.Remove("input_queues");
            ids.Remove("wsearch_off");   // pénalise la recherche de fichiers : opt-in
            return ids;
        }

        /// <summary>Preset Benchmark : tout SAUF les tweaks qui réduisent la sécurité.</summary>
        public static HashSet<string> BenchmarkIds(List<Tweak> all)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Tweak t in all) ids.Add(t.Id);
            ids.Remove("spectre_off");
            ids.Remove("vbs_off");
            return ids;
        }
    }
}
