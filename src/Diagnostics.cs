using System;
using System.Collections.Generic;
using System.IO;
using System.Management;

namespace BTOptimizer
{
    /// <summary>Action corrective proposée à côté d'un constat.</summary>
    public enum FixKind { None, CleanDisk, Timer1ms, DisableVbs, OpenRestore, WindowsUpdate, DisableCoreSync, DisableSdm, DisplaySettings, BiosGuide, CleanJunk, ReapplyTweaks, DeviceManager, NvLatencySafe }

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

            // Espace disque — TOUS les disques fixes, pas seulement le disque système. Les jeux vivent
            // sur D:/E:/F: et c'est justement là que le manque de place fait le plus mal : sous ~10 %
            // de libre, un SSD voit son cache d'écriture fondre, le ramasse-miettes tourne en boucle
            // et le débit s'effondre à quelques Mo/s — chargements interminables et disque « à 100 % »
            // dans le Gestionnaire des tâches alors que rien de lourd ne tourne. Ne regarder que C:
            // laissait passer exactement ce cas-là.
            try
            {
                string sysRoot = (Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\").ToUpperInvariant();
                foreach (DriveInfo d in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                        string root = d.RootDirectory.FullName;
                        bool isSystem = string.Equals(root.ToUpperInvariant(), sysRoot, StringComparison.Ordinal);
                        long freeGB = d.AvailableFreeSpace / 1000000000;
                        double pct = d.TotalSize > 0 ? 100.0 * d.AvailableFreeSpace / d.TotalSize : 100;
                        string who = isSystem ? "Disque système (" + root.TrimEnd('\\') + ")" : "Disque " + root.TrimEnd('\\');
                        string etat = freeGB + " Go libres, " + Math.Round(pct) + " %";

                        // Le disque système a besoin d'un matelas en valeur absolue (mises à jour,
                        // fichier d'échange, points de restauration) ; les disques de données, eux,
                        // ne souffrent que du pourcentage.
                        if (pct < 10 || (isSystem && freeGB < 20))
                            f.Add(new Finding(2, who + " presque plein (" + etat + ") — sous 10 % de libre, un SSD s'effondre : chargements lents et disque bloqué à 100 %.",
                                isSystem ? FixKind.CleanDisk : FixKind.CleanJunk, isSystem ? "Nettoyer" : "Trouver le poids mort"));
                        else if (pct < 15)
                            f.Add(new Finding(1, who + " se remplit (" + etat + ") — vise 15 % de libre pour garder ses performances.",
                                isSystem ? FixKind.CleanDisk : FixKind.CleanJunk, isSystem ? "Nettoyer" : "Trouver le poids mort"));
                        else
                            f.Add(new Finding(0, who + " : espace correct (" + etat + ")."));
                    }
                    catch { }
                }
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
            {
                // Constat de niveau 2 quand la perte dépasse 20 % : sur un PC bridé par le processeur,
                // c'est LE gain gratuit le plus important (jusqu'à 10-20 % de FPS déjà payés). Le
                // profil se règle dans le firmware, donc aucune correction automatique n'est possible :
                // le bouton ouvre le guide BIOS pas-à-pas au lieu de laisser l'utilisateur sans issue.
                int perte = (int)Math.Round(100.0 * (ram.SpeedRated - ram.SpeedRunning) / ram.SpeedRated);
                f.Add(new Finding(perte >= 20 ? 2 : 1,
                    "RAM à " + ram.SpeedRunning + " MT/s au lieu des " + ram.SpeedRated + " MT/s de tes barrettes (−" + perte
                    + " %) — le profil XMP/EXPO n'est pas activé dans le BIOS : ce sont des FPS gratuits que tu as déjà payés.",
                    FixKind.BiosGuide, "Guide BIOS"));
            }
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

            // Réglages ONYX annulés par Windows. Une mise à jour de fonctionnalité, une
            // réinstallation de pilote graphique ou un autre « optimiseur » remettent
            // silencieusement des valeurs par défaut : l'utilisateur croit son PC réglé alors que
            // la moitié des optimisations est retombée, et il cherche la perte d'images ailleurs.
            try
            {
                List<TweakDrift.Drifted> drift = TweakDrift.Detect();
                if (drift.Count > 0)
                {
                    string quand = "";
                    DateTime plus = DateTime.MinValue;
                    foreach (var d in drift) if (d.When > plus) plus = d.When;
                    if (plus != DateTime.MinValue) quand = ", appliqué(s) le " + plus.ToString("dd/MM/yyyy");
                    f.Add(new Finding(drift.Count >= 5 ? 2 : 1,
                        drift.Count + " réglage(s) ONYX ont été ANNULÉS par Windows" + quand
                        + " — souvent après une mise à jour ou une réinstallation de pilote.",
                        FixKind.ReapplyTweaks, "Ré-appliquer"));
                }
            }
            catch { }

            // Profil pilote NVIDIA « Ultra faible latence » posé par ONYX alors que la machine est
            // limitée par le PROCESSEUR. Ultra + 1 image pré-rendue suppriment la file d'attente de
            // rendu — or c'est ce tampon qui absorbe les à-coups du CPU. Sur une machine limitée par
            // le processeur, l'app faisait donc PERDRE des images en croyant gagner de la latence :
            // GPU qui traîne à 40 % pendant que le CPU sature, et des chutes brutales à chaque pic.
            try
            {
                NvProfile.Kind pose; DateTime posele;
                if (NvProfile.Applique(out pose, out posele) && pose == NvProfile.Kind.Ultra)
                {
                    double cpuAvg, gpuAvg; DateTime quand;
                    bool mesure = Bottleneck.LastMeasure(out cpuAvg, out gpuAvg, out quand);
                    string le = posele == DateTime.MinValue ? "" : " (appliqué le " + posele.ToString("dd/MM/yyyy") + ")";
                    if (mesure && NvProfile.UltraNocif(cpuAvg, gpuAvg))
                        f.Add(new Finding(2,
                            "Profil NVIDIA « Ultra faible latence »" + le + " alors que ta machine est limitée par le PROCESSEUR "
                            + "(CPU " + Math.Round(cpuAvg) + " %, GPU " + Math.Round(gpuAvg) + " % en jeu) — il supprime la file de rendu "
                            + "qui amortit les à-coups du CPU : tu perds des images et tu prends des chutes brutales.",
                            FixKind.NvLatencySafe, "Profil sûr"));
                    else if (!mesure)
                        f.Add(new Finding(1,
                            "Profil NVIDIA « Ultra faible latence »" + le + " : bénéfique seulement si ta carte graphique travaille à fond. "
                            + "Si c'est ton processeur qui limite, il te COÛTE des images. Lance la mesure « qui me limite ? » en jeu pour trancher.",
                            FixKind.NvLatencySafe, "Profil sûr"));
                }
            }
            catch { }

            // Écran virtuel actif (Parsec, spacedesk, Sunshine…). Ces cartes graphiques factices
            // restent branchées longtemps après qu'on a cessé de s'en servir : le jeu peut se
            // retrouver rendu dessus (donc recomposé au lieu d'aller droit à l'écran), et elles
            // sont une cause connue d'erreurs de pilote à répétition et de saccades.
            try
            {
                foreach (string v in VirtualDisplays())
                    f.Add(new Finding(1, "Écran virtuel actif : « " + v + " » — un jeu lancé dessus perd des images, "
                        + "et ces cartes factices provoquent des erreurs de pilote à répétition. Désactive-la si tu ne joues pas à distance.",
                        FixKind.DeviceManager, "Gestionnaire"));
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

        // Cartes graphiques factices créées par les logiciels de jeu à distance / d'écran
        // déporté. « Basic Display Adapter » n'est PAS dans la liste : c'est un pilote manquant,
        // un autre sujet, traité ailleurs.
        private static readonly string[] VirtualGpuMarks =
        {
            "parsec", "spacedesk", "sunshine", "virtual display", "idd driver",
            "usb display", "duet display", "amyuni", "mirage driver"
        };

        /// <summary>Noms des adaptateurs d'affichage virtuels ACTIFS (code d'erreur 0 = en service).</summary>
        public static List<string> VirtualDisplays()
        {
            var list = new List<string>();
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Name,ConfigManagerErrorCode FROM Win32_VideoController"))
                    foreach (ManagementObject mo in s.Get())
                    {
                        string name = Convert.ToString(mo["Name"]) ?? "";
                        if (name.Length == 0) continue;
                        string low = name.ToLowerInvariant();
                        bool virtuel = false;
                        foreach (var m in VirtualGpuMarks) if (low.Contains(m)) { virtuel = true; break; }
                        if (!virtuel) continue;
                        int err = -1;
                        try { err = Convert.ToInt32(mo["ConfigManagerErrorCode"]); } catch { }
                        if (err != 0) continue;      // déjà désactivé : rien à signaler
                        list.Add(name);
                    }
            }
            catch { }
            return list;
        }

        public sealed class DriveKind
        {
            public string Name;          // modèle du disque physique
            public int MediaType;        // 3 = HDD, 4 = SSD
            public int BusType;          // 17 = NVMe, 11 = SATA, 7 = USB…
            public bool IsSsd { get { return MediaType == 4; } }
            public bool IsNvme { get { return BusType == 17; } }
            /// <summary>Étiquette lisible : ce qui change VRAIMENT les temps de chargement.</summary>
            public string Label
            {
                get
                {
                    if (MediaType == 3) return "disque MÉCANIQUE (HDD)";
                    if (MediaType == 4) return BusType == 17 ? "SSD NVMe (le plus rapide)" : "SSD SATA";
                    return BusType == 7 ? "disque externe/USB" : "type inconnu";
                }
            }
        }

        /// <summary>Type de disque PAR LETTRE de lecteur (C:, D:…) — via partition → disque physique.
        /// Sert à dire si un jeu est installé sur un support lent.</summary>
        public static System.Collections.Generic.Dictionary<char, DriveKind> DriveTypes()
        {
            var map = new System.Collections.Generic.Dictionary<char, DriveKind>();
            try
            {
                var scope = new ManagementScope(@"\\.\root\microsoft\windows\storage");
                scope.Connect();
                var byDisk = new System.Collections.Generic.Dictionary<int, DriveKind>();
                using (var s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT DeviceId, MediaType, BusType, FriendlyName FROM MSFT_PhysicalDisk")))
                    foreach (ManagementObject mo in s.Get())
                    {
                        int id; if (!int.TryParse(Convert.ToString(mo["DeviceId"]), out id)) continue;
                        int mt = 0, bt = 0;
                        try { mt = Convert.ToInt32(mo["MediaType"]); } catch { }
                        try { bt = Convert.ToInt32(mo["BusType"]); } catch { }
                        byDisk[id] = new DriveKind { Name = Convert.ToString(mo["FriendlyName"]) ?? "", MediaType = mt, BusType = bt };
                    }
                using (var s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT DriveLetter, DiskNumber FROM MSFT_Partition")))
                    foreach (ManagementObject mo in s.Get())
                    {
                        string dl = Convert.ToString(mo["DriveLetter"]);
                        if (string.IsNullOrEmpty(dl) || dl == "\0") continue;
                        int dn; if (!int.TryParse(Convert.ToString(mo["DiskNumber"]), out dn)) continue;
                        DriveKind k;
                        if (byDisk.TryGetValue(dn, out k)) map[char.ToUpperInvariant(dl[0])] = k;
                    }
            }
            catch { }
            return map;
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
