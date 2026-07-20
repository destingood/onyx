using System;
using System.Collections.Generic;

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

        public static void Activate(Action<string, int> log)
        {
            if (IsActive) return;

            _timerWasActive = Native.TimerActive;
            Native.SetTimer1ms(true);

            long freed = Sys.CleanMemory(log);

            _stopped.Clear();
            foreach (string svc in Suspendable)
            {
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
