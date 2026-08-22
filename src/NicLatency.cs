using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace BTOptimizer
{
    /// <summary>
    /// RÉGLAGES DE LATENCE DE LA CARTE RÉSEAU — le moteur, sans interface.
    ///
    /// Cette logique vivait dans la fenêtre « Carte réseau » : elle n'était donc atteignable qu'en
    /// ouvrant cette fenêtre et en cliquant. Elle ne pouvait pas entrer dans un préréglage, alors
    /// qu'elle en a tout à fait le profil : réglages standardisés, valeurs d'origine sauvegardées,
    /// entièrement réversible.
    ///
    /// Elle est extraite ici pour que le préréglage ET la fenêtre passent par LE MÊME code et LA
    /// MÊME sauvegarde. Deux chemins d'écriture avec deux fichiers de sauvegarde auraient donné le
    /// pire cas possible : appliqué par le préréglage, « Rétablir » de la fenêtre ne trouve rien à
    /// remettre et ne fait rien — en annonçant que tout est rentré dans l'ordre.
    ///
    /// UNIQUEMENT DES MOTS-CLÉS NDIS NORMALISÉS (préfixe « * »), sauf une exception Realtek
    /// explicitement nommée. Les normalisés sont définis par Microsoft : 0 veut dire « désactivé »
    /// chez tous les fabricants. Les réglages propriétaires des cartes Wi-Fi (itinérance, MIMO,
    /// bande, largeur de canal) portent des noms ET des encodages différents selon le fabricant —
    /// écrire une valeur devinée dessus, c'est risquer de faire l'inverse de ce qu'on annonce.
    /// </summary>
    internal static class NicLatency
    {
        public const string ClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";

        /// <summary>Clé pilote -> (libellé, valeur « latence minimale »).</summary>
        public static readonly string[][] Managed =
        {
            new[] { "*InterruptModeration", "Modération d'interruptions", "0" },
            new[] { "*FlowControl",         "Contrôle de flux",           "0" },
            new[] { "*EEE",                 "Ethernet écoénergétique (EEE)", "0" },
            new[] { "EnableGreenEthernet",  "Green Ethernet (Realtek)",   "0" },
            // Regroupement de segments à la RÉCEPTION : la carte empile plusieurs segments avant de
            // les remettre à Windows. Elle économise du processeur en échange d'un délai — c'est
            // l'équivalent, côté réception, de la modération d'interruptions.
            new[] { "*RscIPv4",             "Regroupement de segments reçus (RSC IPv4)", "0" },
            new[] { "*RscIPv6",             "Regroupement de segments reçus (RSC IPv6)", "0" },
            new[] { "*PacketCoalescing",    "Regroupement de paquets",    "0" },
            new[] { "*SelectiveSuspend",    "Veille sélective de la carte", "0" },
        };

        public sealed class Reglage { public string Cle, Libelle, Optimal, Actuel; }

        public sealed class Carte
        {
            public string SousCle = "", Nom = "";
            public List<Reglage> Reglages = new List<Reglage>();
            public bool ToutOptimal
            {
                get
                {
                    foreach (Reglage r in Reglages) if (r.Actuel != r.Optimal) return false;
                    return Reglages.Count > 0;
                }
            }
        }

        // ------------------------------------------------------------------ pur

        /// <summary>Décision PURE : cette valeur doit-elle être écrite ? Non si elle y est déjà —
        /// sans quoi on écraserait la sauvegarde d'origine par la valeur optimisée, et le retour
        /// arrière deviendrait impossible.</summary>
        public static bool AEcrire(string actuel, string optimal)
        {
            return !string.IsNullOrEmpty(optimal) && actuel != optimal;
        }

        /// <summary>Clé PURE d'une entrée de sauvegarde.</summary>
        public static string Id(string sousCle, string motCle) { return sousCle + "|" + motCle; }

        /// <summary>Lecture PURE du fichier de sauvegarde (format : sous-clé, mot-clé, valeur).</summary>
        public static Dictionary<string, string> Analyse(string[] lignes)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (lignes == null) return d;
            foreach (string l in lignes)
            {
                if (string.IsNullOrEmpty(l)) continue;
                string[] p = l.Split('\t');
                if (p.Length >= 3) d[Id(p[0], p[1])] = p[2];
            }
            return d;
        }

        /// <summary>Écriture PURE du fichier de sauvegarde.</summary>
        public static string[] Serialise(Dictionary<string, string> d)
        {
            var l = new List<string>();
            if (d != null)
                foreach (KeyValuePair<string, string> kv in d)
                    l.Add(kv.Key.Replace("|", "\t") + "\t" + kv.Value);
            return l.ToArray();
        }

        // ------------------------------------------------------------------ matériel

        public static string CheminSauvegarde()
        {
            return AppPaths.File("bt-nic-backup.txt");
        }

        public static Dictionary<string, string> LitSauvegarde()
        {
            try { return File.Exists(CheminSauvegarde()) ? Analyse(File.ReadAllLines(CheminSauvegarde())) : Analyse(null); }
            catch { return Analyse(null); }
        }

        public static void EcritSauvegarde(Dictionary<string, string> d)
        {
            try { File.WriteAllLines(CheminSauvegarde(), Serialise(d)); }
            catch { }
        }

        /// <summary>Cartes présentes et réglages RÉELLEMENT exposés par chacune.</summary>
        public static List<Carte> Scan()
        {
            var res = new List<Carte>();
            try
            {
                using (RegistryKey cls = Registry.LocalMachine.OpenSubKey(ClassKey))
                {
                    if (cls == null) return res;
                    foreach (string sub in cls.GetSubKeyNames())
                    {
                        if (sub.Length != 4) continue;   // sous-clés NNNN uniquement
                        using (RegistryKey k = cls.OpenSubKey(sub))
                        {
                            if (k == null) continue;
                            string desc = Convert.ToString(k.GetValue("DriverDesc"));
                            if (string.IsNullOrEmpty(desc)) continue;
                            var c = new Carte { SousCle = sub, Nom = desc };
                            foreach (string[] m in Managed)
                            {
                                object v = k.GetValue(m[0]);
                                if (v == null) continue;   // non exposé par cette carte : on n'invente pas
                                c.Reglages.Add(new Reglage { Cle = m[0], Libelle = m[1], Optimal = m[2], Actuel = Convert.ToString(v) });
                            }
                            if (c.Reglages.Count > 0) res.Add(c);
                        }
                    }
                }
            }
            catch { }
            return res;
        }

        /// <summary>
        /// Applique (optimise = true) ou rétablit. Rend le nombre de valeurs réellement écrites.
        /// L'effet est effectif au redémarrage de la carte — ou de la machine.
        /// </summary>
        public static int Applique(bool optimise, Action<string, int> log)
        {
            Dictionary<string, string> sauv = LitSauvegarde();
            int n = 0;
            foreach (Carte c in Scan())
            {
                try
                {
                    using (RegistryKey k = Registry.LocalMachine.OpenSubKey(ClassKey + "\\" + c.SousCle, true))
                    {
                        if (k == null) continue;
                        foreach (Reglage r in c.Reglages)
                        {
                            string id = Id(c.SousCle, r.Cle);
                            try
                            {
                                if (optimise)
                                {
                                    if (!AEcrire(r.Actuel, r.Optimal)) continue;
                                    if (!sauv.ContainsKey(id)) sauv[id] = r.Actuel;   // valeur d'origine
                                    k.SetValue(r.Cle, r.Optimal, RegistryValueKind.String);
                                    n++;
                                }
                                else
                                {
                                    string orig;
                                    if (!sauv.TryGetValue(id, out orig)) continue;
                                    k.SetValue(r.Cle, orig, RegistryValueKind.String);
                                    sauv.Remove(id);
                                    n++;
                                }
                            }
                            catch (Exception ex) { if (log != null) log(c.Nom + " (" + r.Cle + ") : " + ex.Message, 2); }
                        }
                    }
                }
                catch { }
            }
            EcritSauvegarde(sauv);
            if (log != null && n > 0)
                log("Carte réseau : " + n + " réglage(s) " + (optimise ? "optimisé(s)" : "rétabli(s)")
                  + ". Effet au redémarrage de la carte.", 1);
            return n;
        }

        /// <summary>
        /// true = tout ce que les cartes exposent est déjà au réglage de latence minimale,
        /// false = il reste quelque chose à faire, null = aucune carte lisible (on ne conclut PAS).
        /// </summary>
        public static bool? Etat()
        {
            List<Carte> l = Scan();
            if (l.Count == 0) return null;
            foreach (Carte c in l)
                foreach (Reglage r in c.Reglages)
                    if (r.Actuel != r.Optimal) return false;
            return true;
        }
    }
}
