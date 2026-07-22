using System;
using System.Collections.Generic;
using System.IO;

namespace BTOptimizer
{
    /// <summary>
    /// Mode Jeu : un interrupteur temporaire qui nettoie la RAM, suspend (arrête, sans
    /// désactiver) des services de fond non essentiels et force le timer 1 ms.
    /// « Désactiver » restaure exactement l'état précédent (services relancés, timer rendu).
    /// </summary>
    internal static class GameBoost
    {
        public static bool IsActive { get; private set; }

        private static readonly List<string> _stopped = new List<string>();
        private static bool _timerWasActive;

        // Services sûrs à suspendre pendant une partie (tous relançables à la demande).
        private static readonly string[] Suspendable =
        {
            "SysMain", "WSearch", "Spooler", "DiagTrack", "WMPNetworkSvc", "MapsBroker", "dmwappushservice"
        };

        /// <summary>Ce que le mode jeu PEUT suspendre (pour l'écran « services coupés & exclusions »).</summary>
        public static string[] AffectedServices
        {
            get { return (string[])Suspendable.Clone(); }
        }

        // --- Exclusions : services que l'utilisateur interdit de suspendre ------
        private static string ExclusionsPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-gamemode-excl.txt"); }
        }

        /// <summary>Services exclus de la suspension (choix utilisateur, persisté).</summary>
        public static HashSet<string> LoadExclusions()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(ExclusionsPath))
                    foreach (string line in File.ReadAllLines(ExclusionsPath))
                        if (line.Trim().Length > 0) set.Add(line.Trim());
            }
            catch { }
            return set;
        }

        public static void SaveExclusions(IEnumerable<string> excluded)
        {
            try { File.WriteAllLines(ExclusionsPath, excluded); }
            catch { }
        }

        public static void Activate(Action<string, int> log)
        {
            if (IsActive) return;

            _timerWasActive = Native.TimerActive;
            Native.SetTimer1ms(true);

            long freed = Sys.CleanMemory(log);

            HashSet<string> excluded = LoadExclusions();
            _stopped.Clear();
            foreach (string svc in Suspendable)
            {
                if (excluded.Contains(svc)) continue;   // interdit par l'utilisateur
                // Chaque service isolé : un service récalcitrant ne doit NI faire échouer le mode
                // jeu, NI laisser les autres à moitié suspendus. Un service n'est enregistré dans
                // _stopped que si son arrêt a réussi (sinon on n'essaiera pas de le relancer).
                try
                {
                    int start = Sys.GetServiceStart(svc);
                    if (start < 0) continue;                   // absent
                    if (start == 4) continue;                  // déjà désactivé (on n'y touche pas)
                    if (!Sys.IsServiceRunning(svc)) continue;  // déjà arrêté
                    Sys.StopService(svc);
                    _stopped.Add(svc);
                }
                catch { }
            }

            IsActive = true;
            log("Mode Jeu ACTIVÉ : timer 1 ms, ~" + Math.Max(0, freed) + " Mo RAM libérés, "
                + _stopped.Count + " service(s) de fond suspendu(s).", 1);
        }

        public static void Deactivate(Action<string, int> log)
        {
            if (!IsActive) return;

            // Restauration ROBUSTE : un service qui refuse de redémarrer ne doit pas empêcher de
            // relancer les autres ni de rendre le timer. On sort TOUJOURS de l'état « mode jeu ».
            int restored = 0;
            foreach (string svc in _stopped)
            {
                try { Sys.StartService(svc); restored++; }
                catch { }
            }
            _stopped.Clear();

            try { if (!_timerWasActive) Native.SetTimer1ms(false); } catch { }

            IsActive = false;
            log("Mode Jeu désactivé : " + restored + " service(s) relancé(s), timer rendu au système.", 0);
        }
    }
}
