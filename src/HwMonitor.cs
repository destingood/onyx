using System;
using System.Globalization;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;

namespace BTOptimizer
{
    public class GpuInfo
    {
        public bool Ok;
        public string Name = "-";
        public double TempC, Util, CoreMhz, MemMhz, PowerW;
        public long VramUsedMB, VramTotalMB;
    }

    public class HwSample
    {
        public double CpuLoad = -1;       // %
        public long RamUsedMB, RamTotalMB;
        public double RamLoad;            // %
        public double CpuTempC = double.NaN;
        public double TimerMs;
        public GpuInfo Gpu = new GpuInfo();
    }

    /// <summary>
    /// Monitoring matériel léger, sans dépendance externe incompatible :
    /// CPU (PDH), RAM (GlobalMemoryStatusEx), température CPU (WMI ACPI, best-effort)
    /// et GPU tous constructeurs EN-PROCESS via LibreHardwareMonitor (mode GPU seul, API
    /// user-mode NVAPI/ADL/IGCL — AUCUN pilote noyau). nvidia-smi ne sert plus que de repli
    /// et pour les raisons détaillées de bridage (réservées à NVIDIA).
    /// </summary>
    internal class HwMonitor : IDisposable
    {
        // ---- PDH (charge CPU totale) ----
        [DllImport("pdh.dll")] private static extern uint PdhOpenQuery(IntPtr src, IntPtr user, out IntPtr q);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounter(IntPtr q, string path, IntPtr user, out IntPtr c);
        [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr q);
        [DllImport("pdh.dll")] private static extern uint PdhGetFormattedCounterValue(IntPtr c, uint fmt, IntPtr res, out PdhValue v);
        [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr q);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint fmt, ref uint bufSize, out uint itemCount, IntPtr buffer);
        [StructLayout(LayoutKind.Sequential)] private struct PdhValue { public uint CStatus; public double Value; }
        [StructLayout(LayoutKind.Sequential)] private struct PdhItemDouble { public IntPtr szName; public uint CStatus; public double Value; }
        private const uint PDH_FMT_DOUBLE = 0x00000200;
        private const uint PDH_MORE_DATA = 0x800007D2;

        /// <summary>
        /// Charge 3D GPU (%) et VRAM dédiée utilisée (Mo) via les compteurs PDH « GPU Engine » /
        /// « GPU Adapter Memory » — TOUS constructeurs (NVIDIA/AMD/Intel), comme le Gestionnaire
        /// des tâches. Retourne false si indisponible (Windows &lt; 1709). Sans température (réservée
        /// aux SDK constructeur).
        /// </summary>
        public static bool TryReadGpuPerf(out double load3dPct, out long vramUsedMB)
        {
            load3dPct = 0; vramUsedMB = 0;
            IntPtr q = IntPtr.Zero;
            try
            {
                if (PdhOpenQuery(IntPtr.Zero, IntPtr.Zero, out q) != 0) return false;
                IntPtr cUtil, cMem;
                bool haveUtil = PdhAddEnglishCounter(q, @"\GPU Engine(*)\Utilization Percentage", IntPtr.Zero, out cUtil) == 0;
                bool haveMem  = PdhAddEnglishCounter(q, @"\GPU Adapter Memory(*)\Dedicated Usage", IntPtr.Zero, out cMem) == 0;
                if (!haveUtil && !haveMem) return false;
                PdhCollectQueryData(q);
                System.Threading.Thread.Sleep(200);
                PdhCollectQueryData(q);
                if (haveUtil) load3dPct = SumCounterArray(cUtil, "engtype_3d", true);
                if (haveMem) vramUsedMB = (long)(SumCounterArray(cMem, null, false) / (1024.0 * 1024.0));
                return true;
            }
            catch { return false; }
            finally { if (q != IntPtr.Zero) PdhCloseQuery(q); }
        }

        // Somme les valeurs d'un compteur à instances multiples ; filtre par sous-chaîne de nom (facultatif).
        private static double SumCounterArray(IntPtr counter, string nameFilterLower, bool cap100)
        {
            uint size = 0, count = 0;
            if (PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE, ref size, out count, IntPtr.Zero) != PDH_MORE_DATA || size == 0)
                return 0;
            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                if (PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE, ref size, out count, buf) != 0) return 0;
                int itemSize = Marshal.SizeOf(typeof(PdhItemDouble));
                double sum = 0;
                for (int i = 0; i < count; i++)
                {
                    var it = (PdhItemDouble)Marshal.PtrToStructure((IntPtr)(buf.ToInt64() + (long)i * itemSize), typeof(PdhItemDouble));
                    if (it.CStatus != 0) continue;
                    if (nameFilterLower != null)
                    {
                        string nm = it.szName == IntPtr.Zero ? "" : (Marshal.PtrToStringUni(it.szName) ?? "");
                        if (nm.ToLowerInvariant().IndexOf(nameFilterLower, StringComparison.Ordinal) < 0) continue;
                    }
                    sum += it.Value;
                }
                return (cap100 && sum > 100) ? 100 : sum;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        private static string _gpuNameWmi;
        private static string GpuNameWmi()
        {
            if (_gpuNameWmi != null) return _gpuNameWmi;
            string best = "GPU";
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController"))
                    foreach (ManagementObject mo in s.Get())
                    {
                        string n = Convert.ToString(mo["Name"]);
                        if (!string.IsNullOrEmpty(n) && n.IndexOf("microsoft", StringComparison.OrdinalIgnoreCase) < 0)
                        { best = n; break; }
                    }
            }
            catch { }
            _gpuNameWmi = best;
            return best;
        }

        private IntPtr _query, _cpu;
        private bool _pdhReady;

        // ---- RAM ----
        [StructLayout(LayoutKind.Sequential)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            public uint dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile;
            public ulong ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lp);

        private readonly string _nvsmi;

        public HwMonitor()
        {
            if (PdhOpenQuery(IntPtr.Zero, IntPtr.Zero, out _query) == 0)
            {
                if (PdhAddEnglishCounter(_query, @"\Processor(_Total)\% Processor Time", IntPtr.Zero, out _cpu) == 0)
                {
                    PdhCollectQueryData(_query); // amorçage
                    _pdhReady = true;
                }
            }
            _nvsmi = FindNvidiaSmi();
        }

        public bool HasGpu { get { return _nvsmi != null; } }

        public HwSample Sample()
        {
            var s = new HwSample();
            s.TimerMs = Native.CurrentTimerMs();

            if (_pdhReady && PdhCollectQueryData(_query) == 0)
            {
                PdhValue v;
                if (PdhGetFormattedCounterValue(_cpu, PDH_FMT_DOUBLE, IntPtr.Zero, out v) == 0 && v.CStatus == 0)
                    s.CpuLoad = v.Value;
            }

            var mem = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(mem))
            {
                s.RamTotalMB = (long)(mem.ullTotalPhys / (1024 * 1024));
                s.RamUsedMB = (long)((mem.ullTotalPhys - mem.ullAvailPhys) / (1024 * 1024));
                s.RamLoad = mem.dwMemoryLoad;
            }

            s.CpuTempC = ReadCpuTemp();
            if (_nvsmi != null) s.Gpu = ReadGpuNvidia();   // NVIDIA : capteurs EN-PROCESS (LHM), repli nvidia-smi
            else s.Gpu = ReadGpuPdh();                      // AMD / Intel : charge + VRAM (PDH) + température (LHM)
            return s;
        }

        // ---- Température CPU via zone thermique ACPI (nécessite l'élévation) ----

        // Cache PARTAGÉ entre toutes les instances : plusieurs fenêtres tiennent chacune leur
        // HwMonitor et échantillonnent toutes à 1 Hz. Sans mise en commun, on multiplierait les
        // requêtes WMI par le nombre de fenêtres ouvertes. Voir CadenceSonde pour le raisonnement.
        private static readonly object _tempVerrou = new object();
        private static double _tempCache = double.NaN;
        private static System.Diagnostics.Stopwatch _tempAge;
        private static int _tempEchecs;

        private static double ReadCpuTemp()
        {
            lock (_tempVerrou)
            {
                double age = _tempAge == null ? -1 : _tempAge.Elapsed.TotalMilliseconds;
                if (!CadenceSonde.DoitRelire(age, _tempEchecs)) return _tempCache;

                double v = LitTempAcpi();
                if (double.IsNaN(v))
                {
                    _tempEchecs++;
                    // Une sonde qui tombe en panne ne doit pas laisser sa dernière valeur à
                    // l'écran : au bout de trois échecs, mieux vaut « n/d » qu'un chiffre figé
                    // que l'utilisateur croirait actuel.
                    if (_tempEchecs >= 3) _tempCache = double.NaN;
                }
                else { _tempEchecs = 0; _tempCache = v; }

                if (_tempAge == null) _tempAge = System.Diagnostics.Stopwatch.StartNew();
                else _tempAge.Restart();
                return _tempCache;
            }
        }

        /// <summary>Lecture brute, sans cache. NaN si la zone thermique ACPI est absente,
        /// refusée, ou hors bornes plausibles.</summary>
        private static double LitTempAcpi()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    @"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature"))
                {
                    double best = double.NaN;
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        object raw = mo["CurrentTemperature"];
                        if (raw == null) continue;
                        double c = (Convert.ToDouble(raw) / 10.0) - 273.15;
                        if (c > 15 && c < 120 && (double.IsNaN(best) || c > best)) best = c;
                    }
                    return best;
                }
            }
            catch { return double.NaN; }
        }

        // ---- GPU NVIDIA via nvidia-smi ----
        private static string FindNvidiaSmi()
        {
            string sys32 = Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe");
            if (File.Exists(sys32)) return sys32;
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string alt = Path.Combine(pf, @"NVIDIA Corporation\NVSMI\nvidia-smi.exe");
            if (File.Exists(alt)) return alt;
            return null;
        }

        // GPU NVIDIA : source PRINCIPALE = LibreHardwareMonitor EN-PROCESS (mode GPU seul, API
        // NVAPI user-mode — AUCUN pilote noyau, AUCUN processus externe lancé chaque seconde).
        // On garde CoreMhz renseigné (fréquence connue) → branche d'affichage NVIDIA « riche ».
        // Repli automatique sur nvidia-smi (outil officiel) si la lib ne rend aucune valeur.
        private GpuInfo ReadGpuNvidia()
        {
            GpuReading r = GpuSensors.Read();
            if (r.Ok && !double.IsNaN(r.TempC) && !double.IsNaN(r.CoreMhz))
            {
                var g = new GpuInfo();
                g.Name = string.IsNullOrEmpty(r.Name) ? "GPU NVIDIA" : r.Name;
                g.TempC = r.TempC;
                g.CoreMhz = r.CoreMhz;                          // > 0 → branche NVIDIA (temp + fréq + puissance)
                if (!double.IsNaN(r.MemMhz)) g.MemMhz = r.MemMhz;
                if (!double.IsNaN(r.LoadPct)) g.Util = r.LoadPct;
                if (!double.IsNaN(r.PowerW)) g.PowerW = r.PowerW;
                if (r.VramUsedMB >= 0) g.VramUsedMB = r.VramUsedMB;
                if (r.VramTotalMB >= 0) g.VramTotalMB = r.VramTotalMB;
                g.Ok = true;
                return g;
            }
            return ReadGpu();   // repli : outil officiel nvidia-smi
        }

        // ---- Repli GPU NVIDIA via nvidia-smi (outil officiel) ----

        // Ce repli LANCE UN PROCESSUS (mesuré : ~39 ms). Il n'est pris que si la lecture
        // en-processus échoue — mais rien ne garantissait qu'elle réussisse : il suffit qu'un
        // pilote ne publie pas la fréquence cœur pour que l'app se mette à créer un processus
        // PAR SECONDE, en pleine partie, sans que personne ne s'en aperçoive. La cadence de
        // CadenceSonde borne ce coût au lieu de compter sur la chance.
        private static readonly object _smiVerrou = new object();
        private static GpuInfo _smiCache;
        private static System.Diagnostics.Stopwatch _smiAge;
        private static int _smiEchecs;

        private GpuInfo ReadGpu()
        {
            lock (_smiVerrou)
            {
                double age = _smiAge == null ? -1 : _smiAge.Elapsed.TotalMilliseconds;
                if (!CadenceSonde.DoitRelire(age, _smiEchecs) && _smiCache != null) return _smiCache;

                GpuInfo g = LitGpuSmi();
                if (g.Ok) { _smiEchecs = 0; _smiCache = g; }
                else
                {
                    _smiEchecs++;
                    // Comme pour la température : au bout de trois échecs, on cesse d'afficher
                    // la dernière valeur connue plutôt que de la faire passer pour actuelle.
                    if (_smiEchecs >= 3 || _smiCache == null) _smiCache = g;
                }

                if (_smiAge == null) _smiAge = System.Diagnostics.Stopwatch.StartNew();
                else _smiAge.Restart();
                return _smiCache;
            }
        }

        /// <summary>Lecture brute par nvidia-smi, sans cadence. Crée un processus : ne pas
        /// appeler directement depuis une boucle d'échantillonnage.</summary>
        private GpuInfo LitGpuSmi()
        {
            var g = new GpuInfo();
            try
            {
                NativeResult r = Sys.Run(_nvsmi,
                    "--query-gpu=name,temperature.gpu,utilization.gpu,clocks.gr,clocks.mem,power.draw,memory.used,memory.total --format=csv,noheader,nounits");
                if (r.ExitCode != 0 || string.IsNullOrEmpty(r.Output)) return g;
                string line = r.Output.Split('\n')[0].Trim();
                string[] p = line.Split(',');
                if (p.Length < 8) return g;
                g.Name = p[0].Trim();
                g.TempC = D(p[1]); g.Util = D(p[2]);
                g.CoreMhz = D(p[3]); g.MemMhz = D(p[4]); g.PowerW = D(p[5]);
                g.VramUsedMB = (long)D(p[6]); g.VramTotalMB = (long)D(p[7]);
                g.Ok = true;
            }
            catch { }
            return g;
        }

        private static double D(string s)
        {
            double v;
            s = s.Trim();
            if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out v)) return v;
            return 0;
        }

        // GPU non-NVIDIA : charge 3D + VRAM via PDH ; température inconnue (NaN → « n/d »).
        private GpuInfo ReadGpuPdh()
        {
            var g = new GpuInfo();
            double load; long vram;
            if (!TryReadGpuPerf(out load, out vram)) return g;   // g.Ok reste false
            g.Name = GpuNameWmi();
            g.Util = load;
            g.VramUsedMB = vram;
            // Température NATIVE tous constructeurs via LibreHardwareMonitor en mode GPU seul
            // (API user-mode NVAPI/ADL/IGCL, AUCUN pilote noyau) — comble le manque AMD/Intel.
            string lhmName;
            g.TempC = GpuSensors.ReadGpuTempC(out lhmName);
            if (!string.IsNullOrEmpty(lhmName)) g.Name = lhmName;
            g.Ok = true;
            return g;
        }

        public void Dispose()
        {
            // Fermer dès que le handle est ouvert — même si l'ajout du compteur CPU a échoué
            // (sinon _pdhReady reste faux et le handle PDH fuit à chaque construction, sur les
            // Windows « débloatés » où les compteurs de perf sont parfois corrompus).
            if (_query != IntPtr.Zero) { PdhCloseQuery(_query); _query = IntPtr.Zero; }
            _pdhReady = false;
        }
    }
}
