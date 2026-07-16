using System;
using System.Collections.Generic;
using System.IO;
using System.Management;

namespace BTOptimizer
{
    /// <summary>Analyse l'état du système et produit des constats actionnables (niveau : 0 OK, 1 attention, 2 problème).</summary>
    internal static class Diagnostics
    {
        public class Finding
        {
            public int Level;
            public string Text;
            public Finding(int lvl, string t) { Level = lvl; Text = t; }
        }

        public static List<Finding> Run()
        {
            var f = new List<Finding>();

            // Espace disque système
            try
            {
                string root = Path.GetPathRoot(Environment.SystemDirectory);
                var d = new DriveInfo(root);
                long freeGB = d.AvailableFreeSpace / 1000000000;
                double pct = d.TotalSize > 0 ? 100.0 * d.AvailableFreeSpace / d.TotalSize : 100;
                if (pct < 10 || freeGB < 20)
                    f.Add(new Finding(2, "Disque système presque plein (" + freeGB + " Go libres) — utilise le Nettoyage disque."));
                else
                    f.Add(new Finding(0, "Espace disque système correct (" + freeGB + " Go libres)."));
            }
            catch { }

            // Type de disque
            HwProfile hw = Hardware.Detect();
            if (hw.AnyHdd)
                f.Add(new Finding(1, "Disque mécanique (HDD) présent — évite SysMain/Prefetch off dessus (l'auto-tune le gère)."));
            else if (hw.AllSsd)
                f.Add(new Finding(0, "Stockage 100 % SSD : tweaks disque sûrs."));

            // Vitesse RAM (XMP/EXPO)
            Sys.RamInfo ram = Sys.QueryRam();
            if (ram.SpeedRated > 0 && ram.SpeedRunning > 0 && ram.SpeedRunning < ram.SpeedRated - 50)
                f.Add(new Finding(1, "RAM à " + ram.SpeedRunning + " MT/s au lieu de " + ram.SpeedRated + " — active le profil XMP/EXPO dans le BIOS."));
            else if (ram.SpeedRunning > 0)
                f.Add(new Finding(0, "RAM à sa vitesse nominale (" + ram.SpeedRunning + " MT/s)."));

            // Âge du pilote GPU
            int days = GpuDriverAgeDays();
            if (days > 270)
                f.Add(new Finding(1, "Pilote GPU ancien (~" + Math.Max(1, days / 30) + " mois) — une mise à jour peut améliorer perfs et stabilité."));
            else if (days >= 0)
                f.Add(new Finding(0, "Pilote GPU récent (~" + days + " jour(s))."));

            // Intégrité mémoire (VBS/HVCI)
            object hvci = Sys.GetMachine(@"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled");
            if (Sys.IntEquals(hvci, 1))
                f.Add(new Finding(1, "Intégrité de la mémoire (VBS/HVCI) activée — coûte des performances en jeu (désactivable dans la section Système)."));

            // Résolution du timer
            double t = Native.CurrentTimerMs();
            if (t > 1.2)
                f.Add(new Finding(1, "Timer système à " + t.ToString("0.0") + " ms — active « Timer 1 ms » ou le Mode Jeu pour plus de réactivité."));
            else if (t > 0)
                f.Add(new Finding(0, "Timer système à " + t.ToString("0.0") + " ms."));

            // Restauration système
            try
            {
                object srDisabled = Sys.GetMachine(@"SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore", "DisableSR");
                if (Sys.IntEquals(srDisabled, 1))
                    f.Add(new Finding(1, "La restauration système semble désactivée — active-la pour pouvoir revenir en arrière."));
            }
            catch { }

            return f;
        }

        private static int GpuDriverAgeDays()
        {
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Name,DriverDate FROM Win32_VideoController"))
                {
                    foreach (ManagementObject mo in s.Get())
                    {
                        string name = Convert.ToString(mo["Name"]) ?? "";
                        string low = name.ToLowerInvariant();
                        if (low.Contains("microsoft") || low.Contains("basic") || low.Contains("parsec") || low.Contains("virtual")) continue;
                        string dd = Convert.ToString(mo["DriverDate"]);
                        if (dd == null || dd.Length < 8) continue;
                        int y = int.Parse(dd.Substring(0, 4)), m = int.Parse(dd.Substring(4, 2)), day = int.Parse(dd.Substring(6, 2));
                        var date = new DateTime(y, m, day);
                        return (int)(DateTime.Now - date).TotalDays;
                    }
                }
            }
            catch { }
            return -1;
        }
    }
}
