using System;

namespace BTOptimizer
{
    /// <summary>Détection de la mémoire de la machine (RAM et VRAM), utilisée par la mémoire du Copilote.</summary>
    internal static class MachineInfo
    {
        /// <summary>VRAM dédiée du GPU en Mo, tous constructeurs, SANS pilote noyau : d'abord le
        /// registre (HardwareInformation.qwMemorySize, fiable et non plafonné), puis les capteurs
        /// LHM (déjà en-process), puis nvidia-smi. 0 si indéterminé (→ on retombe sur la RAM).</summary>
        public static int DetectVramMB()
        {
            long best = 0;
            try
            {
                using (var cls = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                {
                    if (cls != null)
                        foreach (string sub in cls.GetSubKeyNames())
                        {
                            if (sub.Length != 4) continue;   // 0000, 0001… (pas « Properties »)
                            try
                            {
                                using (var k = cls.OpenSubKey(sub))
                                {
                                    object v = k != null ? k.GetValue("HardwareInformation.qwMemorySize") : null;
                                    if (v is long) best = Math.Max(best, (long)v / 1048576);
                                }
                            }
                            catch { }
                        }
                }
            }
            catch { }
            if (best <= 0)
            {
                try { using (var mon = new HwMonitor()) { HwSample s = mon.Sample(); if (s.Gpu != null && s.Gpu.VramTotalMB > 0) best = s.Gpu.VramTotalMB; } }
                catch { }
            }
            return (int)best;
        }

        public static int DetectRamMB()
        {
            try { long t = NativeMem.TotalPhysMB(); if (t > 0) return (int)t; } catch { }
            try { var r = Sys.QueryRam(); if (r != null && r.TotalMB > 0) return (int)r.TotalMB; } catch { }
            return 8192;   // hypothèse prudente à défaut
        }
    }
}
