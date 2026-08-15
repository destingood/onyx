using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace BTOptimizer
{
    /// <summary>
    /// CE QUI INTERROGE LES CAPTEURS PENDANT QU'ON MESURE.
    ///
    /// Les trois autres modules cherchent un coût STATIONNAIRE : un pilote cher, un CPU lent, une
    /// politique d'interruption absurde. Aucun de ces défauts ne produit un motif qui revient à
    /// intervalle régulier. Quand la latence va bien pendant quarante secondes puis recommence,
    /// c'est qu'une HORLOGE déclenche quelque chose.
    ///
    /// LE MÉCANISME, ET POURQUOI IL EST INVISIBLE :
    ///
    ///   Lire une température, une tension, une vitesse de ventilateur ou l'état d'une barrette
    ///   RGB passe par le SMBus, le Super-I/O ou les registres du processeur. Sur la plupart des
    ///   cartes mères, ces accès déclenchent un SMI — une interruption de gestion système. Le
    ///   processeur bascule alors en mode SMM et GÈLE TOUS LES CŒURS, le temps que le firmware
    ///   fasse son travail.
    ///
    ///   Windows ne voit rien. Il ne compte pas ce temps, ne l'attribue à aucun pilote, ne le
    ///   fait apparaître dans aucun onglet « Drivers ». C'est exactement la situation où toutes
    ///   les colonnes semblent saines — pire DPC sous la demi-milliseconde — et où la machine
    ///   saccade quand même. La latence est là ; elle n'est simplement écrite nulle part.
    ///
    /// ET NOUS DANS TOUT ÇA :
    ///
    ///   ONYX lit les capteurs matériels, lui aussi. Quand il mesure la latence pendant qu'il
    ///   interroge le Super-I/O, il mesure une machine que lui-même dérange. LeviersLatence dit
    ///   déjà cette phrase à propos du pilote de LatencyMon — « tu ne mesures jamais ta machine
    ///   au repos, tu la mesures pendant qu'on la mesure ». Elle vaut pour nous, et se taire
    ///   dessus serait le seul biais vraiment impardonnable : celui qu'on s'accorde à soi-même.
    ///   C'est pourquoi ONYX se signale EN PREMIER dans la liste.
    /// </summary>
    internal static class PolleursDeCapteurs
    {
        public sealed class Polleur
        {
            public string Nom = "";
            /// <summary>Ce qui a été trouvé en marche — service ou processus.</summary>
            public string Trouve = "";
            /// <summary>Vrai pour ONYX : se dénoncer avant de dénoncer les autres.</summary>
            public bool EstNousMemes;
            public string Cout = "";
        }

        /// <summary>
        /// Une famille de logiciels qui interroge le matériel.
        /// Les motifs sont des FRAGMENTS : les éditeurs renomment leurs services à chaque version,
        /// et coller au nom exact ferait passer la détection à côté à la première mise à jour.
        /// </summary>
        private sealed class Famille
        {
            public string Nom;
            public string[] Services;
            public string[] Processus;
            public string Cout;
            public bool Nous;
        }

        private static readonly Famille[] Connues =
        {
            new Famille {
                Nom = "ONYX (nous-mêmes)", Nous = true,
                Services = new string[0],
                Processus = new[] { "BTOptimizer" },
                Cout = "Ferme ONYX le temps du relevé, ou mesure depuis LatencyMon seul. "
                     + "Rien n'est perdu : les réglages appliqués restent."
            },
            new Famille {
                Nom = "Corsair iCUE",
                Services = new[] { "CorsairCpuIdService", "iCUEUpdate", "CorsairGamingAudio", "CorsairService" },
                Processus = new[] { "iCUE", "CorsairService", "Corsair.Service" },
                Cout = "Plus de pilotage RGB ni de courbes de ventilation Corsair pendant l'arrêt. "
                     + "Les ventilateurs repassent sur la courbe du BIOS — ils ne s'arrêtent pas."
            },
            new Famille {
                Nom = "Logitech (G HUB / LampArray)",
                Services = new[] { "logi_lamparray", "LGHUBUpdater", "LogiRegistryService" },
                Processus = new[] { "lghub", "LogiOptions", "logioptionsplus" },
                Cout = "Plus d'éclairage ni de macros Logitech. Souris et clavier continuent de "
                     + "fonctionner normalement : leur cadence ne dépend pas de ce logiciel."
            },
            new Famille {
                Nom = "Razer Synapse",
                Services = new[] { "Razer", "RzActionSvc" },
                Processus = new[] { "Razer Synapse", "RzSDKService" },
                Cout = "Plus d'éclairage ni de macros Razer. Les périphériques restent utilisables."
            },
            new Famille {
                Nom = "ASUS (Armoury Crate / Aura)",
                Services = new[] { "AsusCertService", "LightingService", "ArmouryCrate", "AsusAppService" },
                Processus = new[] { "ArmouryCrate", "AuraService", "AsusOptimization" },
                Cout = "Plus d'éclairage Aura ni de profils Armoury Crate."
            },
            new Famille {
                Nom = "MSI (Center / Mystic Light)",
                Services = new[] { "MSI_Center", "Mystic_Light", "MSI_Foundation" },
                Processus = new[] { "MSI Center", "MysticLight" },
                Cout = "Plus d'éclairage Mystic Light ni de profils MSI Center."
            },
            new Famille {
                Nom = "Surveillance matérielle (HWiNFO, AIDA64, Afterburner…)",
                Services = new[] { "HWiNFO", "AIDA64", "RTSS" },
                Processus = new[] { "HWiNFO64", "HWiNFO32", "aida64", "MSIAfterburner", "RTSS",
                                    "OpenHardwareMonitor", "LibreHardwareMonitor", "GPU-Z", "HWMonitor" },
                Cout = "Tu perds l'affichage des températures et l'overlay pendant le relevé. "
                     + "C'est le logiciel le plus souvent responsable d'un motif qui se répète."
            },
            new Famille {
                Nom = "RGB tiers (OpenRGB, SignalRGB, NZXT CAM)",
                Services = new[] { "OpenRGB", "SignalRgb", "NZXT" },
                Processus = new[] { "OpenRGB", "SignalRgb", "NZXT CAM" },
                Cout = "Plus de pilotage RGB pendant l'arrêt."
            }
        };

        // ==================================================================
        //  Analyse — PURE
        // ==================================================================

        /// <summary>
        /// PUR : le rapport, ou vide s'il n'y a rien qui interroge le matériel.
        ///
        /// ONYX passe en tête quand il est présent. Dénoncer iCUE en taisant qu'on fait la même
        /// chose au même moment donnerait un rapport malhonnête, et surtout un rapport FAUX :
        /// l'utilisateur arrêterait iCUE, verrait le motif persister, et conclurait à tort.
        /// </summary>
        public static string Rapport(List<Polleur> l)
        {
            if (l == null || l.Count == 0) return "";
            l.Sort((a, b) => b.EstNousMemes.CompareTo(a.EstNousMemes));

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("CE QUI INTERROGE TES CAPTEURS PENDANT LA MESURE");
            sb.AppendLine("   Lire températures, tensions et RGB passe par le SMBus : sur la plupart");
            sb.AppendLine("   des cartes mères, ça déclenche un SMI qui GÈLE tous les cœurs. Windows");
            sb.AppendLine("   ne le voit pas et ne l'attribue à aucun pilote — c'est la latence qui");
            sb.AppendLine("   n'apparaît dans AUCUNE colonne, et la seule qui revienne par cycles.");
            sb.AppendLine();
            foreach (Polleur p in l)
            {
                sb.AppendLine("   " + (p.EstNousMemes ? "◆" : "•") + " " + p.Nom + "   (" + p.Trouve + ")");
                sb.AppendLine("        " + p.Cout);
            }
            sb.AppendLine();
            sb.AppendLine("   MÉTHODE : arrête-les TOUS, refais un relevé de même durée. Si le motif");
            sb.AppendLine("   disparaît, rallume-les UN PAR UN pour nommer le responsable. S'il reste,");
            sb.AppendLine("   aucun n'est en cause et c'est le firmware — ça se traite au BIOS.");
            return sb.ToString();
        }

        // ==================================================================
        //  Lecture machine — IMPURE, et qui n'a pas le droit de lever
        // ==================================================================

        /// <summary>Ce qui tourne en ce moment et interroge le matériel. Liste vide si rien —
        /// ou si la lecture a échoué : mieux vaut ne rien dire que dénoncer au hasard.</summary>
        public static List<Polleur> EnMarche()
        {
            var l = new List<Polleur>();
            try
            {
                string[] services = ServicesActifs();
                string[] procs = ProcessusActifs();
                foreach (Famille f in Connues)
                {
                    string trouve = Cherche(f, services, procs);
                    if (trouve.Length == 0) continue;
                    l.Add(new Polleur
                    {
                        Nom = f.Nom, Trouve = trouve, Cout = f.Cout, EstNousMemes = f.Nous
                    });
                }
            }
            catch { }
            return l;
        }

        /// <summary>PUR : quelle trace de cette famille est en marche, s'il y en a une.</summary>
        private static string Cherche(Famille f, string[] services, string[] procs)
        {
            foreach (string motif in f.Services)
                foreach (string s in services)
                    if (s.IndexOf(motif, StringComparison.OrdinalIgnoreCase) >= 0)
                        return "service " + s;

            foreach (string motif in f.Processus)
                foreach (string p in procs)
                    if (p.IndexOf(motif, StringComparison.OrdinalIgnoreCase) >= 0)
                        return p + ".exe";

            return "";
        }

        private static string[] ServicesActifs()
        {
            var noms = new List<string>();
            try
            {
                using (var s = new System.Management.ManagementObjectSearcher(
                    "SELECT Name FROM Win32_Service WHERE State = 'Running'"))
                    foreach (System.Management.ManagementObject mo in s.Get())
                    {
                        object n = mo["Name"];
                        if (n != null) noms.Add(n.ToString());
                    }
            }
            catch { }
            return noms.ToArray();
        }

        private static string[] ProcessusActifs()
        {
            var noms = new List<string>();
            try
            {
                foreach (Process p in Process.GetProcesses())
                {
                    try { noms.Add(p.ProcessName); }
                    catch { }
                    finally { p.Dispose(); }
                }
            }
            catch { }
            return noms.ToArray();
        }
    }
}
