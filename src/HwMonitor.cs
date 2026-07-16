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
    /// et GPU NVIDIA via nvidia-smi (l'outil officiel NVIDIA présent sur la machine).
    /// </summary>
    internal class HwMonitor : IDisposable
    {
        // ---- PDH (charge CPU totale) ----
        [DllImport("pdh.dll")] private static extern uint PdhOpenQuery(IntPtr src, IntPtr user, out IntPtr q);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounter(IntPtr q, string path, IntPtr user, out IntPtr c);
        [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr q);
        [DllImport("pdh.dll")] private static extern uint PdhGetFormattedCounterValue(IntPtr c, uint fmt, IntPtr res, out PdhValue v);
        [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr q);
        [StructLayout(LayoutKind.Sequential)] private struct PdhValue { public uint CStatus; public double Value; }
        private const uint PDH_FMT_DOUBLE = 0x00000200;

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
            if (_nvsmi != null) s.Gpu = ReadGpu();
            return s;
        }

        // ---- Température CPU via zone thermique ACPI (nécessite l'élévation) ----
        private static double ReadCpuTemp()
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

        private GpuInfo ReadGpu()
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

        public void Dispose()
        {
            if (_pdhReady) { PdhCloseQuery(_query); _pdhReady = false; }
        }
    }
}
