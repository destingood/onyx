using System;
using System.Collections.Generic;
using System.Management;

namespace BTOptimizer
{
    /// <summary>
    /// Inventaire des périphériques (façon Gestionnaire de périphériques) via WMI Win32_PnPEntity.
    /// Lecture seule : on détecte les périphériques en erreur mais on ne modifie rien
    /// (désactiver/désinstaller un périphérique critique casserait la machine).
    /// </summary>
    internal static class DeviceInfo
    {
        public class Device
        {
            public string Name;
            public string Class;       // PNPClass : Display, Net, Media, USB...
            public string Manufacturer;
            public int ErrorCode;      // ConfigManagerErrorCode (0 = OK)
            public bool IsProblem;     // vrai problème (hors « désactivé » / « débranché »)
        }

        // Codes ConfigManagerErrorCode qui traduisent un VRAI problème (pilote KO, ressources...).
        // On exclut 22 (désactivé volontairement) et 45 (non connecté / débranché).
        private static readonly HashSet<int> ProblemCodes = new HashSet<int>(
            new[] { 1, 2, 3, 9, 10, 12, 14, 16, 18, 19, 28, 29, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 46, 47, 48, 49, 50, 51, 52 });

        public static string ErrorText(int code)
        {
            switch (code)
            {
                case 0: return "OK";
                case 1: return "Mal configuré";
                case 3: return "Pilote corrompu ou mémoire insuffisante";
                case 10: return "Impossible de démarrer le périphérique";
                case 12: return "Ressources insuffisantes";
                case 14: return "Redémarrage nécessaire";
                case 18: return "Réinstaller les pilotes";
                case 19: return "Problème de registre";
                case 21: return "En cours de suppression";
                case 22: return "Désactivé";
                case 28: return "Aucun pilote installé";
                case 31: return "Pilote non chargé";
                case 37: return "Échec du pilote au démarrage";
                case 39: return "Pilote manquant ou corrompu";
                case 43: return "Windows a arrêté ce périphérique (problème signalé)";
                case 45: return "Non connecté";
                case 48: return "Logiciel bloqué";
                case 52: return "Signature du pilote non vérifiée";
                default: return "Code d'erreur " + code;
            }
        }

        private static string S(ManagementBaseObject mo, string p)
        {
            try { object o = mo[p]; return o == null ? "" : Convert.ToString(o); } catch { return ""; }
        }

        public static List<Device> ListAll()
        {
            var list = new List<Device>();
            try
            {
                using (var s = new ManagementObjectSearcher("root\\cimv2",
                    "SELECT Name,PNPClass,Manufacturer,ConfigManagerErrorCode FROM Win32_PnPEntity"))
                {
                    foreach (ManagementObject mo in s.Get())
                    {
                        string name = S(mo, "Name");
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        int err = 0;
                        try { object o = mo["ConfigManagerErrorCode"]; if (o != null) err = Convert.ToInt32(o); } catch { }
                        string cls = S(mo, "PNPClass");
                        list.Add(new Device
                        {
                            Name = name.Trim(),
                            Class = string.IsNullOrEmpty(cls) ? "Autres" : cls,
                            Manufacturer = S(mo, "Manufacturer").Trim(),
                            ErrorCode = err,
                            IsProblem = ProblemCodes.Contains(err)
                        });
                    }
                }
            }
            catch { }
            list.Sort((a, b) =>
            {
                if (a.IsProblem != b.IsProblem) return a.IsProblem ? -1 : 1;
                int c = string.Compare(a.Class, b.Class, StringComparison.OrdinalIgnoreCase);
                return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        public static List<Device> Problems(List<Device> all)
        {
            var p = new List<Device>();
            foreach (Device d in all) if (d.IsProblem) p.Add(d);
            return p;
        }
    }
}
