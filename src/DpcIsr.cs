using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace DTGOptimizer
{
    /// <summary>Statistiques DPC + ISR agrégées pour un pilote (module noyau).</summary>
    public class DriverStat
    {
        public string Module;
        public string Description;
        public long DpcCount, DpcTotalUs;
        public double DpcMaxUs;   // borne haute du pire intervalle observé
        public long IsrCount, IsrTotalUs;
        public double IsrMaxUs;
        public double WorstUs { get { return Math.Max(DpcMaxUs, IsrMaxUs); } }
    }

    public class DpcIsrReport
    {
        public string SourceFile;
        public List<DriverStat> Drivers = new List<DriverStat>();
        public long TotalDpc, TotalIsr;
        public double MaxDpcUs; public string MaxDpcModule = "-";
        public double MaxIsrUs; public string MaxIsrModule = "-";
        public double DurationSec;

        public double WorstUs { get { return Math.Max(MaxDpcUs, MaxIsrUs); } }

        public int VerdictLevel   // 1 vert, 2 orange, 3 rouge
        {
            get
            {
                double w = WorstUs;
                if (w <= 0) return 1;
                if (w <= 500) return 1;
                if (w <= 1000) return 2;
                return 3;
            }
        }

        public string VerdictTitle
        {
            get
            {
                double w = WorstUs;
                if (w <= 0) return "Aucune donnée exploitable";
                if (w <= 150) return "EXCELLENT — adapté au jeu compétitif et au temps réel";
                if (w <= 500) return "BON — aucune latence pilote préoccupante";
                if (w <= 1000) return "À SURVEILLER — un pilote génère des pics de latence";
                return "PROBLÈME — un pilote provoque des latences élevées";
            }
        }

        public string VerdictDetail
        {
            get
            {
                if (WorstUs <= 0) return "Le rapport ne contient pas d'histogramme exploitable.";
                string worst = (MaxDpcUs >= MaxIsrUs) ? (MaxDpcModule + " (DPC)") : (MaxIsrModule + " (ISR)");
                return string.Format(
                    "Pire latence noyau observée : ≤ {0:0} µs, causée par {1}. " +
                    "Repère : < 500 µs = très bien, > 1000 µs = à corriger (mise à jour du pilote concerné).",
                    WorstUs, worst);
            }
        }

        // ------------------------------------------------------------------
        //  Parsing du format "xperf -a dpcisr"
        // ------------------------------------------------------------------
        private static readonly Regex ModuleRow =
            new Regex(@",\s*([A-Za-z0-9_\-\.]+\.(?:sys|exe|dll))\s*$",
                      RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex LeadingInt =
            new Regex(@"^\s*(\d+)", RegexOptions.Compiled);
        private static readonly Regex BlockStart =
            new Regex(@"^Total = (\d+) for module (.+?)\s*$", RegexOptions.Compiled);
        private static readonly Regex OverallStart =
            new Regex(@"^Total = (\d+)\s*$", RegexOptions.Compiled);
        private static readonly Regex BucketCapped =
            new Regex(@"AND <=\s*(\d+)\s*usecs,\s*(\d+),", RegexOptions.Compiled);
        private static readonly Regex BucketOpen =
            new Regex(@">\s*(\d+)\s*usecs,\s*(\d+), or", RegexOptions.Compiled);
        private static readonly Regex Elapsed =
            new Regex(@"Elapsed Time,", RegexOptions.Compiled);
        private static readonly Regex DurationRx =
            new Regex(@"from\s+0\s+us\s+to\s+(\d+)\s+us", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static DpcIsrReport Parse(string path)
        {
            string text = File.ReadAllText(path);
            var rep = new DpcIsrReport();
            rep.SourceFile = path;

            Match dm = DurationRx.Match(text);
            if (dm.Success) rep.DurationSec = long.Parse(dm.Groups[1].Value) / 1e6;

            int split = text.IndexOf("Interrupt Info", StringComparison.OrdinalIgnoreCase);
            string dpcText = (split >= 0) ? text.Substring(0, split) : text;
            string isrText = (split >= 0) ? text.Substring(split) : "";

            var map = new Dictionary<string, DriverStat>(StringComparer.OrdinalIgnoreCase);
            long totDpc = ParseSection(dpcText, map, true);
            long totIsr = ParseSection(isrText, map, false);
            rep.TotalDpc = totDpc;
            rep.TotalIsr = totIsr;

            foreach (DriverStat d in map.Values)
            {
                d.Description = DescribeDriver(d.Module);
                if (d.DpcMaxUs > rep.MaxDpcUs) { rep.MaxDpcUs = d.DpcMaxUs; rep.MaxDpcModule = d.Module; }
                if (d.IsrMaxUs > rep.MaxIsrUs) { rep.MaxIsrUs = d.IsrMaxUs; rep.MaxIsrModule = d.Module; }
            }
            rep.Drivers = map.Values
                .OrderByDescending(d => d.WorstUs)
                .ThenByDescending(d => d.DpcTotalUs + d.IsrTotalUs)
                .ToList();
            return rep;
        }

        // Retourne le nombre total d'événements (DPC ou ISR) de la section.
        private static long ParseSection(string sectionText, Dictionary<string, DriverStat> map, bool isDpc)
        {
            string[] lines = sectionText.Split('\n');

            // 1) Table des totaux par module (usec) — lignes finissant par un nom de module.
            foreach (string raw in lines)
            {
                string line = raw.TrimEnd('\r');
                Match mm = ModuleRow.Match(line);
                if (!mm.Success) continue;
                string module = mm.Groups[1].Value;
                string head = line.Substring(0, mm.Index);
                long sum = 0;
                foreach (string chunk in head.Split(','))
                {
                    Match im = LeadingInt.Match(chunk);
                    if (im.Success) sum += long.Parse(im.Groups[1].Value);
                }
                DriverStat d = Get(map, module);
                if (isDpc) d.DpcTotalUs += sum; else d.IsrTotalUs += sum;
            }

            // 2) Histogrammes : "Total = N for module NAME" + buckets.
            long overall = 0;
            string cur = null;
            double curMax = 0;
            long curCount = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd('\r');

                Match bs = BlockStart.Match(line);
                if (bs.Success)
                {
                    FlushBlock(map, cur, curCount, curMax, isDpc);
                    cur = bs.Groups[2].Value.Trim();
                    curCount = long.Parse(bs.Groups[1].Value);
                    curMax = 0;
                    continue;
                }
                Match os = OverallStart.Match(line);
                if (os.Success && line.IndexOf("for module", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    FlushBlock(map, cur, curCount, curMax, isDpc);
                    cur = null; curCount = 0; curMax = 0;
                    overall = long.Parse(os.Groups[1].Value);
                    continue;
                }
                if (cur != null && Elapsed.IsMatch(line))
                {
                    Match bc = BucketCapped.Match(line);
                    if (bc.Success)
                    {
                        if (long.Parse(bc.Groups[2].Value) > 0)
                        {
                            double up = double.Parse(bc.Groups[1].Value, CultureInfo.InvariantCulture);
                            if (up > curMax) curMax = up;
                        }
                        continue;
                    }
                    Match bo = BucketOpen.Match(line);
                    if (bo.Success && long.Parse(bo.Groups[2].Value) > 0)
                    {
                        double low = double.Parse(bo.Groups[1].Value, CultureInfo.InvariantCulture);
                        if (low > curMax) curMax = low; // borne basse (bucket ouvert)
                    }
                }
            }
            FlushBlock(map, cur, curCount, curMax, isDpc);
            return overall;
        }

        private static void FlushBlock(Dictionary<string, DriverStat> map, string module,
                                       long count, double maxUs, bool isDpc)
        {
            if (module == null) return;
            DriverStat d = Get(map, module);
            if (isDpc) { d.DpcCount += count; if (maxUs > d.DpcMaxUs) d.DpcMaxUs = maxUs; }
            else { d.IsrCount += count; if (maxUs > d.IsrMaxUs) d.IsrMaxUs = maxUs; }
        }

        private static DriverStat Get(Dictionary<string, DriverStat> map, string module)
        {
            DriverStat d;
            if (!map.TryGetValue(module, out d))
            {
                d = new DriverStat { Module = module };
                map[module] = d;
            }
            return d;
        }

        // ------------------------------------------------------------------
        //  Comparaison de deux rapports (avant / après)
        // ------------------------------------------------------------------
        public class ModuleDelta
        {
            public string Module;
            public string Description;
            public double WorstBefore, WorstAfter;
            public long EventsBefore, EventsAfter;
            public double Delta { get { return WorstAfter - WorstBefore; } }
        }

        public static List<ModuleDelta> Compare(DpcIsrReport before, DpcIsrReport after)
        {
            var map = new Dictionary<string, ModuleDelta>(StringComparer.OrdinalIgnoreCase);
            foreach (DriverStat d in before.Drivers)
            {
                ModuleDelta m = new ModuleDelta { Module = d.Module, Description = d.Description };
                m.WorstBefore = d.WorstUs;
                m.EventsBefore = d.DpcCount + d.IsrCount;
                map[d.Module] = m;
            }
            foreach (DriverStat d in after.Drivers)
            {
                ModuleDelta m;
                if (!map.TryGetValue(d.Module, out m))
                {
                    m = new ModuleDelta { Module = d.Module, Description = d.Description };
                    map[d.Module] = m;
                }
                if (string.IsNullOrEmpty(m.Description)) m.Description = d.Description;
                m.WorstAfter = d.WorstUs;
                m.EventsAfter = d.DpcCount + d.IsrCount;
            }
            return map.Values
                .OrderByDescending(m => Math.Max(m.WorstBefore, m.WorstAfter))
                .ThenByDescending(m => Math.Abs(m.Delta))
                .ToList();
        }

        // ------------------------------------------------------------------
        //  Descriptions des pilotes courants (comme LatencyMon)
        // ------------------------------------------------------------------
        private static readonly Dictionary<string, string> Known =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "ntoskrnl.exe", "Noyau Windows (NT OS Kernel)" },
            { "nvlddmkm.sys", "Pilote graphique NVIDIA" },
            { "atikmdag.sys", "Pilote graphique AMD" },
            { "amdkmdag.sys", "Pilote graphique AMD" },
            { "igdkmd64.sys", "Pilote graphique Intel" },
            { "dxgkrnl.sys", "DirectX Graphics Kernel" },
            { "dxgmms2.sys", "DirectX Graphics MMS" },
            { "tcpip.sys", "Pile réseau TCP/IP" },
            { "ndis.sys", "Pilote réseau NDIS" },
            { "afd.sys", "Ancillary Function Driver (sockets)" },
            { "storport.sys", "Pilote de stockage (StorPort)" },
            { "stornvme.sys", "Pilote SSD NVMe" },
            { "storahci.sys", "Pilote SATA AHCI" },
            { "iastorac.sys", "Intel RST (stockage)" },
            { "classpnp.sys", "Classe de périphériques de stockage" },
            { "hdaudbus.sys", "Bus audio HD (High Definition Audio)" },
            { "wdf01000.sys", "Windows Driver Framework (KMDF)" },
            { "usbport.sys", "Pilote USB" },
            { "usbxhci.sys", "Contrôleur USB xHCI" },
            { "ntfs.sys", "Système de fichiers NTFS" },
            { "fltmgr.sys", "Filter Manager (mini-filtres)" },
            { "wdfilter.sys", "Microsoft Defender (filtre)" },
            { "ndu.sys", "Windows Network Data Usage" },
            { "rdyboost.sys", "ReadyBoost / SuperFetch" },
            { "winhvr.sys", "Hyperviseur Windows (VBS/virtualisation)" },
            { "winhv.sys", "Hyperviseur Windows" },
            { "hvservice.sys", "Service hyperviseur" },
            { "vmbusr.sys", "Bus virtuel Hyper-V (VMBus)" },
            { "vmswitch.sys", "Commutateur virtuel Hyper-V" },
            { "storvsp.sys", "Fournisseur stockage Hyper-V" },
            { "vhdmp.sys", "Disque virtuel (VHD)" },
            { "winnat.sys", "NAT réseau Windows" },
            { "symcryptk.dll", "Bibliothèque cryptographique Windows" },
            { "usbaudio2.sys", "Audio USB" },
            { "rtwlane.sys", "Wi-Fi Realtek" },
            { "rt640x64.sys", "Réseau Realtek Ethernet" },
            { "e1d68x64.sys", "Réseau Intel Ethernet" },
            { "mouclass.sys", "Classe souris" },
            { "kbdclass.sys", "Classe clavier" },
            { "acpi.sys", "Gestion ACPI (alimentation)" },
        };

        private static string DescribeDriver(string module)
        {
            string d;
            if (Known.TryGetValue(module, out d)) return d;
            return "";
        }
    }
}
