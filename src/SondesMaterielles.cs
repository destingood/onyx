using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace BTOptimizer
{
    /// <summary>
    /// LES LOGICIELS QUI LISENT TES CAPTEURS — la première cause de DPC qu'aucun réglage ne corrige.
    ///
    /// Lire une température de carte mère, une tension de VRM ou la vitesse d'un ventilateur ne se
    /// fait pas en mémoire : ça passe par les bus SMBus et I²C, qui sont LENTS et BLOQUANTS. Le
    /// pilote qui interroge immobilise son cœur pendant des dizaines à des centaines de
    /// microsecondes, à chaque relevé. C'est exactement la forme d'un pic de latence différée.
    ///
    /// Un seul de ces outils ne se voit pas. Empilés — et ils s'empilent, parce que chaque marque
    /// livre le sien — ils deviennent le premier poste de la mesure. Sur la machine de référence,
    /// SEPT tournaient en même temps, et le cadre d'exécution qui les héberge tous
    /// (Wdf01000.sys) était en tête du classement de latence.
    ///
    /// ONYX ne les supprime pas et n'en désinstalle aucun : ce sont des choix de l'utilisateur, et
    /// certains rendent un vrai service. Il les NOMME, explique ce qu'ils coûtent, et laisse
    /// décider. Le Mode Jeu peut suspendre les SERVICES d'arrière-plan — pas les applications
    /// visibles, dont l'arrêt brutal aurait des effets que l'utilisateur n'a pas demandés.
    /// </summary>
    internal static class SondesMaterielles
    {
        public sealed class Sonde
        {
            public string Processus;    // nom du processus, sans .exe
            public string Libelle;      // ce que l'utilisateur reconnaît
            public bool Service;        // true = service d'arrière-plan, suspendable
        }

        /// <summary>
        /// Outils connus pour interroger le matériel en boucle. La liste ne contient QUE des
        /// logiciels dont c'est la fonction principale ou une fonction permanente — pas des
        /// programmes qui liraient un capteur une fois au démarrage.
        /// </summary>
        public static readonly Sonde[] Connues =
        {
            new Sonde { Processus = "HWMonitor",       Libelle = "CPUID HWMonitor (capteurs)", Service = false },
            new Sonde { Processus = "HWiNFO64",        Libelle = "HWiNFO (capteurs)",          Service = false },
            new Sonde { Processus = "OpenHardwareMonitor", Libelle = "Open Hardware Monitor",  Service = false },
            new Sonde { Processus = "MSIAfterburner",  Libelle = "MSI Afterburner (capteurs + courbe GPU)", Service = false },
            new Sonde { Processus = "RTSS",            Libelle = "RivaTuner Statistics Server", Service = false },
            new Sonde { Processus = "iCUE",            Libelle = "Corsair iCUE (RGB, ventilateurs)", Service = false },
            new Sonde { Processus = "OpenRGB",         Libelle = "OpenRGB",                    Service = false },
            new Sonde { Processus = "SignalRgbLauncher", Libelle = "SignalRGB",                Service = false },
            new Sonde { Processus = "ArmouryCrate.UserSessionHelper", Libelle = "ASUS Armoury Crate", Service = false },
            new Sonde { Processus = "NZXT CAM",        Libelle = "NZXT CAM",                   Service = false },
            new Sonde { Processus = "lghub_agent",     Libelle = "Logitech G HUB",             Service = false },
            new Sonde { Processus = "ProcessLasso",    Libelle = "Process Lasso (affinités)",  Service = false },
            new Sonde { Processus = "ProcessGovernor", Libelle = "Process Governor (affinités)", Service = false },
        };

        /// <summary>Services d'arrière-plan des mêmes suites : ceux-là peuvent être suspendus le
        /// temps d'une partie et relancés ensuite, sans rien casser de visible.</summary>
        public static readonly string[] ServicesSondes =
        {
            "CorsairCpuIdService",   // relevés processeur pour iCUE
            "CorsairGamingAudioConfig",
            "LGHUBUpdaterService",
            "NvContainerLocalSystem",
            "CCleanerPerformanceOptimizerService",
            "MSIAfterburnerService",
        };

        // ------------------------------------------------------------------ pur

        /// <summary>PUR : ce nom de processus est-il une sonde connue ? Rend le libellé, ou null.</summary>
        public static string Reconnait(string nomProcessus)
        {
            if (string.IsNullOrEmpty(nomProcessus)) return null;
            string n = nomProcessus.Trim();
            if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 4);
            foreach (Sonde s in Connues)
                if (string.Equals(s.Processus, n, StringComparison.OrdinalIgnoreCase)) return s.Libelle;
            return null;
        }

        /// <summary>
        /// Constat PUR. null en dessous de deux sondes : une seule ne se mesure pas, et crier au
        /// loup pour un logiciel que l'utilisateur a installé exprès ferait perdre sa crédibilité au
        /// reste du diagnostic.
        /// </summary>
        public static string Texte(List<string> libelles)
        {
            if (libelles == null || libelles.Count < 2) return null;
            string liste = "";
            for (int i = 0; i < libelles.Count && i < 4; i++)
                liste += (liste.Length > 0 ? ", " : "") + libelles[i];
            if (libelles.Count > 4) liste += " et " + (libelles.Count - 4) + " autre(s)";

            return libelles.Count + " logiciels interrogent tes capteurs en permanence (" + liste
                 + "). Lire une température ou une tension passe par des bus lents et BLOQUANTS : "
                 + "chaque relevé immobilise un cœur le temps de la réponse, et c'est la forme même "
                 + "d'un pic de latence. Un seul ne se voit pas ; empilés, ils deviennent le premier "
                 + "poste de la mesure. Garde ceux qui te servent, ferme les autres avant de jouer.";
        }

        /// <summary>PUR : niveau du constat. Informatif jusqu'à trois, à corriger au-delà.</summary>
        public static int Niveau(int nombre)
        {
            if (nombre < 2) return 0;
            return nombre >= 4 ? 1 : 0;
        }

        // ------------------------------------------------------------------ machine

        /// <summary>Sondes réellement en cours d'exécution.</summary>
        public static List<string> EnCours()
        {
            var l = new List<string>();
            try
            {
                foreach (Process p in Process.GetProcesses())
                {
                    string lib;
                    try { lib = Reconnait(p.ProcessName); }
                    catch { continue; }
                    finally { try { p.Dispose(); } catch { } }
                    if (lib != null && !l.Contains(lib)) l.Add(lib);
                }
            }
            catch { }
            return l;
        }
    }
}
