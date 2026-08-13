using System;
using System.Collections.Generic;
using System.Management;
using Microsoft.Win32;

namespace BTOptimizer
{
    /// <summary>
    /// VEILLE USB, AU NIVEAU DU PÉRIPHÉRIQUE — l'étage que le plan d'alimentation ne couvre pas.
    ///
    /// Le réglage « suspension sélective USB » du plan d'alimentation est global et saute dès qu'on
    /// change de plan. À côté, chaque concentrateur USB porte SA propre case dans le Gestionnaire de
    /// périphériques : « Autoriser l'ordinateur à éteindre ce périphérique pour économiser
    /// l'énergie ». Tant qu'elle est cochée, Windows peut couper l'alimentation d'un hub resté
    /// inactif — et le réveil coûte quelques millisecondes au premier mouvement de souris, exactement
    /// au moment où l'on tenait une visée immobile.
    ///
    /// HONNÊTETÉ REQUISE : ces valeurs vivent sous HKLM\SYSTEM\CurrentControlSet\Enum, dont les
    /// permissions appartiennent souvent à SYSTEM. Même en administrateur, certaines clés REFUSENT
    /// l'écriture. On tente, on COMPTE, et on rapporte le vrai résultat — jamais un succès supposé.
    /// </summary>
    internal static class UsbPower
    {
        private const string EnumBase = @"SYSTEM\CurrentControlSet\Enum\";

        public sealed class Hub
        {
            public string Name;
            public string InstanceId;
        }

        /// <summary>Résultat d'une tentative : ce qui est passé, ce qui a résisté.</summary>
        public sealed class Result
        {
            public int Total;
            public int Ok;
            public int Refuse;
            public List<string> Refuses = new List<string>();
        }

        /// <summary>Un nom de périphérique désigne-t-il un concentrateur ? (FR et EN.)</summary>
        public static bool EstHub(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.ToLowerInvariant();
            return n.Contains("hub") || n.Contains("concentrateur");
        }

        /// <summary>Concentrateurs USB présents. Liste vide si WMI ne répond pas.</summary>
        public static List<Hub> Hubs()
        {
            var list = new List<Hub>();
            try
            {
                using (var s = new ManagementObjectSearcher(
                    "SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE PNPClass = 'USB'"))
                    foreach (ManagementObject mo in s.Get())
                    {
                        string name = Convert.ToString(mo["Name"]) ?? "";
                        string id = Convert.ToString(mo["PNPDeviceID"]) ?? "";
                        if (id.Length == 0 || !EstHub(name)) continue;
                        list.Add(new Hub { Name = name, InstanceId = id });
                    }
            }
            catch { }
            return list;
        }

        // 0 = Windows n'a plus le droit de couper l'alimentation de ce concentrateur.
        // Les trois valeurs correspondent aux mécanismes successifs de Windows ; on écrit celles
        // qui existent déjà ET on pose les deux principales, sans quoi la case reste cochée.
        private static readonly string[] Valeurs = { "EnhancedPowerManagementEnabled", "SelectiveSuspendEnabled", "AllowIdleIrpInD3" };

        /// <summary>Écrit sur un concentrateur. false si la clé refuse l'écriture.</summary>
        private static bool Ecrit(string instanceId, int value)
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(
                           EnumBase + instanceId + @"\Device Parameters", true))
                {
                    if (k == null) return false;
                    k.SetValue("EnhancedPowerManagementEnabled", value, RegistryValueKind.DWord);
                    k.SetValue("SelectiveSuspendEnabled", value, RegistryValueKind.DWord);
                    // AllowIdleIrpInD3 n'existe que sur certains pilotes : on ne le CRÉE pas,
                    // on le corrige seulement s'il est déjà là (sinon on invente un réglage).
                    if (k.GetValue("AllowIdleIrpInD3") != null)
                        k.SetValue("AllowIdleIrpInD3", value, RegistryValueKind.DWord);
                    return true;
                }
            }
            catch { return false; }   // accès refusé : compté comme un refus, pas comme un succès
        }

        /// <summary>Interdit (value 0) ou réautorise (value 1) la mise en veille de tous les hubs.</summary>
        public static Result Applique(int value, Action<string, int> log)
        {
            var r = new Result();
            foreach (Hub h in Hubs())
            {
                r.Total++;
                if (Ecrit(h.InstanceId, value)) r.Ok++;
                else { r.Refuse++; if (r.Refuses.Count < 8) r.Refuses.Add(h.Name); }
            }
            if (log != null)
            {
                if (r.Total == 0) log("Veille USB par périphérique : aucun concentrateur détecté.", 2);
                else if (r.Refuse == 0) log("Veille USB coupée sur " + r.Ok + " concentrateur(s).", 1);
                else log("Veille USB : " + r.Ok + "/" + r.Total + " concentrateur(s) traités — "
                       + r.Refuse + " ont refusé l'écriture (clés protégées par le système).", 2);
            }
            return r;
        }

        /// <summary>
        /// État réel. true = tous les hubs lisibles ont la veille coupée ; false = au moins un
        /// l'a encore ; null = rien de lisible (aucun hub, ou clés inaccessibles) — dans ce cas on
        /// ne conclut RIEN, donc jamais de fausse dérive.
        /// </summary>
        public static bool? Etat()
        {
            int lus = 0, coupes = 0;
            foreach (Hub h in Hubs())
            {
                try
                {
                    using (RegistryKey k = Registry.LocalMachine.OpenSubKey(EnumBase + h.InstanceId + @"\Device Parameters", false))
                    {
                        if (k == null) continue;
                        object v = k.GetValue("EnhancedPowerManagementEnabled");
                        if (v == null) continue;      // valeur absente : ce hub ne se prononce pas
                        lus++;
                        if (Convert.ToInt32(v) == 0) coupes++;
                    }
                }
                catch { }
            }
            if (lus == 0) return null;
            return coupes == lus;
        }
    }
}
