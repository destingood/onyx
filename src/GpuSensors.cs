using System;
using LibreHardwareMonitor.Hardware;

namespace BTOptimizer
{
    /// <summary>Relevé GPU complet (tous constructeurs), lu nativement.</summary>
    internal class GpuReading
    {
        public bool Ok;
        public string Name;
        public double TempC = double.NaN, LoadPct = double.NaN, CoreMhz = double.NaN, PowerW = double.NaN;
        public long VramUsedMB = -1, VramTotalMB = -1;
    }

    /// <summary>
    /// Capteurs GPU NATIFS, tous constructeurs, via LibreHardwareMonitorLib en mode « GPU SEUL ».
    /// Important : on n'active QUE le GPU (CPU / carte mère / etc. désactivés) — donc le pilote
    /// noyau de la lib (qui ne sert QU'AU CPU/carte mère via MSR/SuperIO) n'est JAMAIS chargé.
    /// Températures / charge / fréquence / puissance / VRAM passent par les API user-mode des
    /// constructeurs (NVAPI / ADL / IGCL) : natif, sans injection, compatible anticheat.
    /// </summary>
    internal static class GpuSensors
    {
        private static readonly object _lock = new object();
        private static Computer _computer;
        private static bool _failed;

        private static void EnsureOpen()
        {
            if (_computer != null) return;
            _computer = new Computer
            {
                IsGpuEnabled        = true,   // SEUL le GPU : aucun pilote noyau chargé.
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

        private static bool IsGpu(HardwareType t)
        {
            return t == HardwareType.GpuNvidia || t == HardwareType.GpuAmd || t == HardwareType.GpuIntel;
        }

        /// <summary>Relevé complet du GPU principal (température, charge, fréquence, puissance, VRAM).
        /// Instance LHM ouverte une seule fois puis réutilisée. À appeler en arrière-plan.</summary>
        public static GpuReading Read()
        {
            var r = new GpuReading();
            lock (_lock)
            {
                if (_failed) return r;
                try
                {
                    EnsureOpen();
                    foreach (IHardware hw in _computer.Hardware)
                    {
                        if (!IsGpu(hw.HardwareType)) continue;
                        hw.Update();
                        r.Name = hw.Name; r.Ok = true;
                        foreach (ISensor s in hw.Sensors)
                        {
                            if (!s.Value.HasValue) continue;
                            double v = s.Value.Value;
                            string n = s.Name ?? "";
                            switch (s.SensorType)
                            {
                                case SensorType.Temperature:
                                    if (v > 0 && v < 150 && (Has(n, "Core") || double.IsNaN(r.TempC))) r.TempC = v;
                                    break;
                                case SensorType.Load:
                                    if (Has(n, "Core") || double.IsNaN(r.LoadPct)) r.LoadPct = v;
                                    break;
                                case SensorType.Clock:
                                    if (Has(n, "Core")) r.CoreMhz = v;
                                    break;
                                case SensorType.Power:
                                    if (Has(n, "Package") || Has(n, "GPU Power") || double.IsNaN(r.PowerW)) r.PowerW = v;
                                    break;
                                case SensorType.SmallData:
                                    if (Has(n, "Memory Used") && !Has(n, "D3D")) r.VramUsedMB = (long)v;
                                    else if (Has(n, "Memory Total")) r.VramTotalMB = (long)v;
                                    break;
                            }
                        }
                        break;   // premier GPU dédié suffit
                    }
                    return r;
                }
                catch
                {
                    _failed = true;
                    try { if (_computer != null) _computer.Close(); } catch { }
                    _computer = null;
                    return new GpuReading();
                }
            }
        }

        private static bool Has(string s, string sub) { return s != null && s.IndexOf(sub, StringComparison.OrdinalIgnoreCase) >= 0; }

        /// <summary>Température (°C) du GPU principal, tous constructeurs. NaN si indisponible.</summary>
        public static double ReadGpuTempC(out string gpuName)
        {
            GpuReading r = Read();
            gpuName = r.Name;
            return r.TempC;
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
