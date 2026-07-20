using System;
using LibreHardwareMonitor.Hardware;

namespace BTOptimizer
{
    /// <summary>
    /// Capteurs GPU NATIFS, tous constructeurs, via LibreHardwareMonitorLib en mode « GPU SEUL ».
    /// Important : on n'active QUE le GPU (CPU / carte mère / etc. désactivés) — donc le pilote
    /// noyau de la lib (qui ne sert QU'AU CPU/carte mère via MSR/SuperIO) n'est JAMAIS chargé.
    /// Les températures GPU passent par les API user-mode des constructeurs (NVAPI / ADL / IGCL),
    /// ce qui comble la température GPU AMD/Intel que Windows n'expose pas — sans injection.
    /// </summary>
    internal static class GpuSensors
    {
        private static readonly object _lock = new object();
        private static Computer _computer;
        private static bool _failed;

        /// <summary>Température (°C) du GPU principal, tous constructeurs. NaN si indisponible.
        /// Instance LHM ouverte une seule fois puis réutilisée. À appeler en arrière-plan.</summary>
        public static double ReadGpuTempC(out string gpuName)
        {
            gpuName = null;
            lock (_lock)
            {
                if (_failed) return double.NaN;
                try
                {
                    if (_computer == null)
                    {
                        _computer = new Computer
                        {
                            IsGpuEnabled        = true,   // SEUL le GPU : pas de pilote noyau.
                            IsCpuEnabled        = false,
                            IsMotherboardEnabled= false,
                            IsMemoryEnabled     = false,
                            IsStorageEnabled    = false,
                            IsNetworkEnabled    = false,
                            IsControllerEnabled = false,
                            IsBatteryEnabled    = false,
                            IsPsuEnabled        = false
                        };
                        _computer.Open();
                    }

                    double best = double.NaN;
                    foreach (IHardware hw in _computer.Hardware)
                    {
                        if (hw.HardwareType != HardwareType.GpuNvidia
                            && hw.HardwareType != HardwareType.GpuAmd
                            && hw.HardwareType != HardwareType.GpuIntel) continue;

                        hw.Update();
                        double core = double.NaN, any = double.NaN;
                        foreach (ISensor s in hw.Sensors)
                        {
                            if (s.SensorType != SensorType.Temperature || !s.Value.HasValue) continue;
                            double v = s.Value.Value;
                            if (v <= 0 || v >= 150) continue;
                            if (double.IsNaN(any)) any = v;
                            if (s.Name != null && s.Name.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0) core = v;
                        }
                        double pick = !double.IsNaN(core) ? core : any;
                        if (!double.IsNaN(pick)) { best = pick; gpuName = hw.Name; break; }
                    }
                    return best;
                }
                catch
                {
                    _failed = true;
                    try { if (_computer != null) _computer.Close(); } catch { }
                    _computer = null;
                    return double.NaN;
                }
            }
        }

        /// <summary>Ferme proprement l'instance LHM (à la fermeture de l'app).</summary>
        public static void Shutdown()
        {
            lock (_lock)
            {
                try { if (_computer != null) _computer.Close(); } catch { }
                _computer = null;
            }
        }
    }
}
