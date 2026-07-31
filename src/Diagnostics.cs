using System;
using System.Collections.Generic;
using System.IO;
using System.Management;

namespace BTOptimizer
{
    /// <summary>Action corrective proposée à côté d'un constat.</summary>
    public enum FixKind { None, CleanDisk, Timer1ms, DisableVbs, OpenRestore, WindowsUpdate, DisableCoreSync, DisableSdm, DisplaySettings }

    /// <summary>Analyse l'état du système et produit des constats actionnables (niveau : 0 OK, 1 attention, 2 problème).</summary>
    internal static class Diagnostics
    {
        public class Finding
        {
            public int Level;
            public string Text;
            public FixKind Fix = FixKind.None;
            public string FixLabel = "";
            public Finding(int lvl, string t) { Level = lvl; Text = t; }
            public Finding(int lvl, string t, FixKind fix, string fixLabel) { Level = lvl; Text = t; Fix = fix; FixLabel = fixLabel; }
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
                    f.Add(new Finding(2, "Disque système presque plein (" + freeGB + " Go libres) — libère de l'espace.", FixKind.CleanDisk, "Nettoyer"));
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
                f.Add(new Finding(1, "Pilote GPU ancien (~" + Math.Max(1, days / 30) + " mois) — une mise à jour peut améliorer perfs et stabilité.", FixKind.WindowsUpdate, "Windows Update"));
            else if (days >= 0)
                f.Add(new Finding(0, "Pilote GPU récent (~" + days + " jour(s))."));

            // Intégrité mémoire (VBS/HVCI)
            object hvci = Sys.GetMachine(@"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled");
            if (Sys.IntEquals(hvci, 1))
                f.Add(new Finding(1, "Intégrité de la mémoire (VBS/HVCI) activée — coûte des performances en jeu.", FixKind.DisableVbs, "Désactiver"));

            // Samsung CoreSync (synchro d'éclairage des moniteurs Odyssey) — capture
            // l'écran en continu : cause connue de saccades / pertes de FPS en jeu.
            try
            {
                CoreSyncCheck.Status cs = CoreSyncCheck.Probe();
                string mon = cs.SamsungMonitor != null ? " (écran " + cs.SamsungMonitor + ")" : "";
                if (cs.Running > 0)
                    f.Add(new Finding(1, "Samsung CoreSync tourne en fond" + mon + " — la synchro d'éclairage capture l'écran en continu : saccades et pertes de FPS en jeu.", FixKind.DisableCoreSync, "Désactiver"));
                else if (cs.StartupEnabled)
                    f.Add(new Finding(1, "Samsung CoreSync démarre avec Windows" + mon + " — source connue de saccades en jeu ; il reviendra au prochain redémarrage.", FixKind.DisableCoreSync, "Désactiver"));
                else if (cs.Installed)
                    f.Add(new Finding(0, "Samsung CoreSync présent mais inactif" + mon + " : aucun impact."));
                else if (cs.SamsungMonitor != null)
                    f.Add(new Finding(0, "Écran Samsung détecté (" + cs.SamsungMonitor + "), logiciel CoreSync absent : OK. Saccades liées à l'éclairage ? Désactive CoreSync dans le menu du moniteur (Jeu → Éclairage Core)."));
            }
            catch { }

            // Samsung Display Manager + service MAPT — appli compagnon inutile pour
            // CoreSync (l'éclairage est calculé par le moniteur) ; MAPT = pont réseau
            // B2B via la prise LAN du moniteur, sans intérêt à la maison.
            try
            {
                SdmCheck.Status sd = SdmCheck.Probe();
                if (sd.MaptRunning || sd.MaptInstalled)
                    f.Add(new Finding(1, "Service Samsung MAPT " + (sd.MaptRunning ? "actif" : "installé") + " (pont réseau B2B via le moniteur) — inutile à la maison, à désactiver.", FixKind.DisableSdm, "Désactiver"));
                else if (sd.Running > 0)
                    f.Add(new Finding(1, "Samsung Display Manager tourne en fond — inutile pour CoreSync (géré par l'écran) ; un logiciel de fond en moins = moins de saccades.", FixKind.DisableSdm, "Désactiver"));
                else if (sd.StartupEnabled)
                    f.Add(new Finding(1, "Samsung Display Manager démarre avec Windows — inutile pour CoreSync (géré par l'écran).", FixKind.DisableSdm, "Désactiver"));
                else if (sd.Installed)
                    f.Add(new Finding(0, "Samsung Display Manager présent mais inactif : aucun impact."));
            }
            catch { }

            // Résolution du timer
            double t = Native.CurrentTimerMs();
            if (t > 1.2)
                f.Add(new Finding(1, "Timer système à " + t.ToString("0.0") + " ms — force 1 ms pour plus de réactivité.", FixKind.Timer1ms, "Forcer 1 ms"));
            else if (t > 0)
                f.Add(new Finding(0, "Timer système à " + t.ToString("0.0") + " ms."));

            // Écrans sous leur fréquence maximale (gros levier d'input lag, souvent oublié)
            try
            {
                foreach (DisplayInfo.DisplayMode d in DisplayInfo.Query())
                {
                    if (d.BelowMax)
                        f.Add(new Finding(1,
                            "Écran " + d.Name + (d.Primary ? " (principal)" : "") + " à " + d.CurrentHz
                            + " Hz alors qu'il supporte " + d.MaxHz + " Hz en " + d.Width + "×" + d.Height
                            + " — règle-le au maximum.", FixKind.DisplaySettings, "Régler l'écran"));
                    else
                        f.Add(new Finding(0, "Écran " + d.Name + (d.Primary ? " (principal)" : "")
                            + " à " + d.CurrentHz + " Hz (max " + d.MaxHz + " Hz : OK)."));
                }
            }
            catch { }

            // Restauration système
            try
            {
                object srDisabled = Sys.GetMachine(@"SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore", "DisableSR");
                if (Sys.IntEquals(srDisabled, 1))
                    f.Add(new Finding(1, "La restauration système semble désactivée — active-la pour pouvoir revenir en arrière.", FixKind.OpenRestore, "Ouvrir"));
            }
            catch { }

            return f;
        }

        public sealed class DiskHp { public string Name; public int Status; public int SizeGb; public bool IsSsd; }

        /// <summary>Santé SMART des disques physiques (WMI Storage : HealthStatus 0=sain, 1=avertissement,
        /// 2=défaillant). Windows la connaît mais ne la montre jamais — ONYX prévient AVANT la panne.</summary>
        public static System.Collections.Generic.List<DiskHp> DiskHealth()
        {
            var list = new System.Collections.Generic.List<DiskHp>();
            try
            {
                var scope = new ManagementScope(@"\\.\root\microsoft\windows\storage");
                scope.Connect();
                using (var s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT FriendlyName, MediaType, HealthStatus, Size FROM MSFT_PhysicalDisk")))
                    foreach (ManagementObject mo in s.Get())
                    {
                        string name = Convert.ToString(mo["FriendlyName"]) ?? "";
                        if (name.Length == 0) continue;
                        int st = 0, mt = 0; long size = 0;
                        try { st = Convert.ToInt32(mo["HealthStatus"]); } catch { }
                        try { mt = Convert.ToInt32(mo["MediaType"]); } catch { }
                        try { size = Convert.ToInt64(mo["Size"]); } catch { }
                        int gb = (int)(size / 1073741824);
                        if (gb < 32 && st == 0) continue;   // clés USB & lecteurs de cartes : hors-sujet si sains
                        list.Add(new DiskHp { Name = name, Status = st, SizeGb = gb, IsSsd = mt == 4 });
                    }
            }
            catch { }
            return list;
        }

        /// <summary>0 → « sain », 1 → « avertissement », 2 → « DÉFAILLANT ». Français, sans jargon.</summary>
        public static string DiskHealthLabel(int status)
        {
            switch (status)
            {
                case 0: return "sain";
                case 1: return "avertissement (pré-panne possible)";
                case 2: return "DÉFAILLANT — panne imminente";
                default: return "état inconnu (" + status + ")";
            }
        }

        public sealed class GpuDrv { public string Name; public string Version; public int AgeDays; }

        /// <summary>Pilote GPU principal : nom, version, âge en jours (WMI). null si illisible.
        /// Sert au « bilan mises à jour » du Copilote : il MESURE avant de proposer quoi que ce soit.</summary>
        public static GpuDrv GpuDriver()
        {
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Name,DriverVersion,DriverDate FROM Win32_VideoController"))
                {
                    foreach (ManagementObject mo in s.Get())
                    {
                        string name = Convert.ToString(mo["Name"]) ?? "";
                        string low = name.ToLowerInvariant();
                        if (low.Contains("microsoft") || low.Contains("basic") || low.Contains("parsec") || low.Contains("virtual")) continue;
                        int age = -1;
                        string dd = Convert.ToString(mo["DriverDate"]);
                        if (dd != null && dd.Length >= 8)
                        {
                            try
                            {
                                var date = new DateTime(int.Parse(dd.Substring(0, 4)), int.Parse(dd.Substring(4, 2)), int.Parse(dd.Substring(6, 2)));
                                age = (int)(DateTime.Now - date).TotalDays;
                            }
                            catch { }
                        }
                        return new GpuDrv { Name = name, Version = Convert.ToString(mo["DriverVersion"]) ?? "", AgeDays = age };
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>Âge du pilote GPU en jours (WMI DriverDate), −1 si illisible. Public :
        /// le Copilote s'en sert aussi (pilote très vieux = FPS et correctifs manqués).</summary>
        public static int GpuDriverAgeDays()
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
