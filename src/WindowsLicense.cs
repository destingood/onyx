using System;
using System.Management;
using Microsoft.Win32;

namespace BTOptimizer
{
    /// <summary>
    /// Lecture SEULE et LÉGALE de l'état de licence Windows : activé ou non, type de licence
    /// (OEM / Retail / Volume), édition, clé partielle, et la clé OEM gravée dans le firmware
    /// (MSDM) — qui appartient à l'utilisateur et lui sert à réactiver Windows après une
    /// réinstallation. ONYX ne modifie ni ne contourne RIEN : il informe, c'est tout.
    /// </summary>
    internal static class WindowsLicense
    {
        public sealed class Info
        {
            public string Edition = "Windows";
            public int StatusCode = -1;
            public string StatusText = "Inconnu";
            public bool Activated;
            public string Channel;      // OEM / Retail / Volume…
            public string PartialKey;   // 5 derniers caractères
            public string OemKey;       // clé complète du firmware (si présente)
            public string Note = "";
        }

        // ApplicationID du système d'exploitation Windows (constant Microsoft).
        private const string WindowsAppId = "55c92734-d682-4d71-983e-d6ec3f16059f";

        public static Info Read()
        {
            var i = new Info();

            // Édition : registre, sans droits admin.
            try
            {
                using (RegistryKey b = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey k = b.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                    if (k != null)
                    {
                        string name = k.GetValue("ProductName") as string;
                        string disp = k.GetValue("DisplayVersion") as string;
                        if (!string.IsNullOrEmpty(name)) i.Edition = name + (string.IsNullOrEmpty(disp) ? "" : " (" + disp + ")");
                    }
            }
            catch { }

            // Statut d'activation + canal + clé partielle (WMI, lecture seule).
            try
            {
                using (var s = new ManagementObjectSearcher(
                    "SELECT Description, LicenseStatus, PartialProductKey FROM SoftwareLicensingProduct WHERE ApplicationID='" + WindowsAppId + "'"))
                foreach (ManagementObject o in s.Get())
                {
                    string pk = o["PartialProductKey"] as string;
                    if (string.IsNullOrEmpty(pk)) continue;   // seule la licence Windows ACTIVE porte une clé partielle
                    try { i.StatusCode = Convert.ToInt32(o["LicenseStatus"]); } catch { }
                    i.PartialKey = pk;
                    i.Channel = Channel(o["Description"] as string);
                    break;
                }
            }
            catch { }

            i.Activated = i.StatusCode == 1;
            i.StatusText = StatusText(i.StatusCode);

            // Clé OEM du firmware (MSDM) — c'est TA clé, légale à afficher et à réutiliser.
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT OA3xOriginalProductKey FROM SoftwareLicensingService"))
                foreach (ManagementObject o in s.Get())
                {
                    string k = o["OA3xOriginalProductKey"] as string;
                    if (!string.IsNullOrEmpty(k)) i.OemKey = k;
                    break;
                }
            }
            catch { }

            i.Note = i.Activated
                ? (string.IsNullOrEmpty(i.OemKey)
                    ? "Windows est activé. Si ta licence est « numérique », elle est liée à ton compte Microsoft : après une réinstallation, connecte-toi avec ce compte et Windows se réactive tout seul."
                    : "Windows est activé. Ta clé OEM est gravée dans le firmware du PC (ci-dessus) — garde-la : elle réactive Windows après une réinstallation, même sur ce PC.")
                : "Windows n'est pas (ou plus) activé. Utilise une clé légitime : ta clé OEM ci-dessus si elle existe, ta clé d'achat, ou une licence numérique liée à ton compte Microsoft.";
            return i;
        }

        private static string Channel(string desc)
        {
            string d = (desc ?? "").ToUpperInvariant();
            if (d.Contains("OEM")) return "OEM — préinstallée par le constructeur";
            if (d.Contains("RETAIL")) return "Retail — achat (boîte ou numérique)";
            if (d.Contains("KMS")) return "Volume / KMS — entreprise";
            if (d.Contains("MAK")) return "Volume / MAK — entreprise";
            if (d.Contains("VOLUME")) return "Volume — entreprise";
            if (d.Contains("TIMEBASED") || d.Contains("EVAL")) return "Évaluation — limitée dans le temps";
            return string.IsNullOrEmpty(d) ? null : "Canal Windows";
        }

        private static string StatusText(int code)
        {
            switch (code)
            {
                case 0: return "Non activé";
                case 1: return "Activé";
                case 2: return "Période de grâce (à activer)";
                case 3: return "Hors tolérance (à activer)";
                case 4: return "Non authentique (grâce)";
                case 5: return "Notification (à réactiver)";
                case 6: return "Grâce étendue";
                default: return "Indéterminé";
            }
        }
    }
}
