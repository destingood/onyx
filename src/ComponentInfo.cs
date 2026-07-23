using System;
using System.Collections.Generic;
using System.Management;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>Détection détaillée des composants matériels (façon inventaire système).</summary>
    internal static class ComponentInfo
    {
        public class Section
        {
            public string Title;
            public List<string[]> Rows = new List<string[]>(); // [propriété, valeur]
            public void Add(string k, string v) { if (!string.IsNullOrWhiteSpace(v)) Rows.Add(new[] { k, v.Trim() }); }
        }

        private static string S(ManagementBaseObject mo, string p)
        {
            try { object o = mo[p]; return o == null ? "" : Convert.ToString(o); } catch { return ""; }
        }
        private static long L(ManagementBaseObject mo, string p)
        {
            try { object o = mo[p]; return o == null ? 0 : Convert.ToInt64(o); } catch { return 0; }
        }

        private static IEnumerable<ManagementObject> Query(string wql, string ns)
        {
            var scope = string.IsNullOrEmpty(ns) ? "root\\cimv2" : ns;
            using (var s = new ManagementObjectSearcher(scope, wql))
                foreach (ManagementObject mo in s.Get())
                    yield return mo;
        }

        public static List<Section> Gather()
        {
            var list = new List<Section>();

            // ---- Système ----
            var os = new Section { Title = "Système" };
            os.Add("Système d'exploitation", Sys.OsDescription());
            try
            {
                foreach (ManagementObject mo in Query("SELECT BuildNumber,OSArchitecture,InstallDate FROM Win32_OperatingSystem", null))
                {
                    os.Add("Build", S(mo, "BuildNumber"));
                    os.Add("Architecture", S(mo, "OSArchitecture"));
                    break;
                }
            }
            catch { }
            os.Add("Nom de la machine", Environment.MachineName);
            list.Add(os);

            // ---- Processeur ----
            var cpu = new Section { Title = "Processeur" };
            try
            {
                foreach (ManagementObject mo in Query("SELECT Name,NumberOfCores,NumberOfLogicalProcessors,MaxClockSpeed,L2CacheSize,L3CacheSize,SocketDesignation FROM Win32_Processor", null))
                {
                    cpu.Add("Modèle", S(mo, "Name"));
                    cpu.Add("Cœurs / threads", S(mo, "NumberOfCores") + " / " + S(mo, "NumberOfLogicalProcessors"));
                    long mhz = L(mo, "MaxClockSpeed");
                    if (mhz > 0) cpu.Add("Fréquence de base", (mhz / 1000.0).ToString("0.0") + " GHz");
                    long l2 = L(mo, "L2CacheSize"), l3 = L(mo, "L3CacheSize");
                    if (l2 > 0) cpu.Add("Cache L2", (l2 / 1024.0).ToString("0.#") + " Mo");
                    if (l3 > 0) cpu.Add("Cache L3", (l3 / 1024.0).ToString("0.#") + " Mo");
                    cpu.Add("Socket", S(mo, "SocketDesignation"));
                    break;
                }
            }
            catch { }
            list.Add(cpu);

            // ---- Carte mère / BIOS ----
            var mb = new Section { Title = "Carte mère / BIOS" };
            try
            {
                foreach (ManagementObject mo in Query("SELECT Manufacturer,Product FROM Win32_BaseBoard", null))
                {
                    mb.Add("Fabricant", S(mo, "Manufacturer"));
                    mb.Add("Modèle", S(mo, "Product"));
                    break;
                }
                foreach (ManagementObject mo in Query("SELECT SMBIOSBIOSVersion,Manufacturer,ReleaseDate FROM Win32_BIOS", null))
                {
                    mb.Add("BIOS", S(mo, "Manufacturer") + " " + S(mo, "SMBIOSBIOSVersion"));
                    string d = S(mo, "ReleaseDate");
                    if (d.Length >= 8) mb.Add("Date du BIOS", d.Substring(6, 2) + "/" + d.Substring(4, 2) + "/" + d.Substring(0, 4));
                    break;
                }
            }
            catch { }
            list.Add(mb);

            // ---- Mémoire ----
            var ram = new Section { Title = "Mémoire (RAM)" };
            try
            {
                long total = 0; int n = 0;
                var modules = new List<string[]>();
                foreach (ManagementObject mo in Query("SELECT Capacity,Speed,ConfiguredClockSpeed,Manufacturer,PartNumber,DeviceLocator FROM Win32_PhysicalMemory", null))
                {
                    n++;
                    long cap = L(mo, "Capacity");
                    total += cap;
                    long spd = L(mo, "ConfiguredClockSpeed"); if (spd == 0) spd = L(mo, "Speed");
                    string label = S(mo, "DeviceLocator");
                    string desc = (cap / (1024 * 1024 * 1024)) + " Go · " + spd + " MT/s · "
                        + S(mo, "Manufacturer").Trim() + " " + S(mo, "PartNumber").Trim();
                    modules.Add(new[] { label, desc });
                }
                ram.Add("Total", (total / (1024.0 * 1024 * 1024)).ToString("0") + " Go sur " + n + " barrette(s)");
                foreach (string[] m in modules) ram.Add(m[0], m[1]);
            }
            catch { }
            list.Add(ram);

            // ---- Carte graphique ----
            var gpu = new Section { Title = "Carte graphique" };
            try
            {
                long nvVramMB = NvidiaVramMB(); // vraie VRAM (Win32 plafonne à ~4 Go)
                foreach (ManagementObject mo in Query("SELECT Name,AdapterRAM,DriverVersion,DriverDate FROM Win32_VideoController", null))
                {
                    string name = S(mo, "Name");
                    if (string.IsNullOrEmpty(name)) continue;
                    string low = name.ToLowerInvariant();
                    if (low.Contains("microsoft") || low.Contains("basic") || low.Contains("parsec") || low.Contains("virtual")) continue;
                    gpu.Add("Modèle", name);
                    bool isNv = low.Contains("nvidia") || low.Contains("geforce") || low.Contains("rtx") || low.Contains("gtx");
                    if (isNv && nvVramMB > 0)
                        gpu.Add("Mémoire vidéo", (nvVramMB / 1024.0).ToString("0") + " Go");
                    else
                    {
                        long vram = L(mo, "AdapterRAM");
                        if (vram > 0) gpu.Add("Mémoire vidéo", (vram / (1024.0 * 1024 * 1024)).ToString("0.#") + " Go");
                    }
                    gpu.Add("Version du pilote", S(mo, "DriverVersion"));
                    string dd = S(mo, "DriverDate");
                    if (dd.Length >= 8) gpu.Add("Date du pilote", dd.Substring(6, 2) + "/" + dd.Substring(4, 2) + "/" + dd.Substring(0, 4));
                }
            }
            catch { }
            list.Add(gpu);

            // ---- Stockage ----
            var disk = new Section { Title = "Stockage" };
            try
            {
                foreach (ManagementObject mo in Query("SELECT FriendlyName,MediaType,Size,BusType FROM MSFT_PhysicalDisk", @"root\Microsoft\Windows\Storage"))
                {
                    string name = S(mo, "FriendlyName");
                    long size = L(mo, "Size");
                    int mt = (int)L(mo, "MediaType");
                    string type = mt == 4 ? "SSD" : (mt == 3 ? "HDD" : (mt == 5 ? "SCM" : "?"));
                    disk.Add(name, (size / (1000.0 * 1000 * 1000)).ToString("0") + " Go · " + type);
                }
            }
            catch { }
            if (disk.Rows.Count == 0)
                foreach (ManagementObject mo in Query("SELECT Model,Size FROM Win32_DiskDrive", null))
                    disk.Add(S(mo, "Model"), (L(mo, "Size") / (1000.0 * 1000 * 1000)).ToString("0") + " Go");
            list.Add(disk);

            // ---- Réseau ----
            var net = new Section { Title = "Réseau" };
            try
            {
                foreach (ManagementObject mo in Query("SELECT Name,MACAddress,Speed,PhysicalAdapter FROM Win32_NetworkAdapter WHERE PhysicalAdapter=true AND MACAddress IS NOT NULL", null))
                {
                    long spd = L(mo, "Speed");
                    string s = spd > 0 ? (" · " + (spd / 1000000) + " Mb/s") : "";
                    net.Add(S(mo, "Name"), S(mo, "MACAddress") + s);
                }
            }
            catch { }
            list.Add(net);

            // ---- Écrans ----
            var mon = new Section { Title = "Écrans" };
            try
            {
                var modes = DisplayInfo.Query();
                if (modes.Count > 0)
                {
                    int i = 1;
                    foreach (DisplayInfo.DisplayMode d in modes)
                    {
                        mon.Add("Écran " + i + (d.Primary ? " (principal)" : ""),
                            d.Name + " — " + d.Width + " × " + d.Height + " @ " + d.CurrentHz + " Hz"
                            + (d.BelowMax ? "  (max " + d.MaxHz + " Hz !)" : "  (max " + d.MaxHz + " Hz)"));
                        i++;
                    }
                }
                else
                {
                    int i = 1;
                    foreach (Screen sc in Screen.AllScreens)
                    {
                        mon.Add("Écran " + i + (sc.Primary ? " (principal)" : ""),
                            sc.Bounds.Width + " × " + sc.Bounds.Height);
                        i++;
                    }
                }
            }
            catch { }
            list.Add(mon);

            return list;
        }

        private static long NvidiaVramMB()
        {
            try
            {
                string smi = Sys.NvSmiPath();
                if (smi == null) return 0;
                NativeResult r = Sys.Run(smi, "--query-gpu=memory.total --format=csv,noheader,nounits");
                if (r.ExitCode != 0) return 0;
                long v;
                return long.TryParse(r.Output.Split('\n')[0].Trim(), out v) ? v : 0;
            }
            catch { return 0; }
        }

        public static string ToText(List<Section> sections)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== COMPOSANTS DU SYSTÈME — Fluide ===");
            sb.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            foreach (Section s in sections)
            {
                sb.AppendLine();
                sb.AppendLine("[" + s.Title + "]");
                foreach (string[] r in s.Rows) sb.AppendLine("  " + r[0] + " : " + r[1]);
            }
            return sb.ToString();
        }
    }
}
