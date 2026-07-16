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
                if (Sys.GetServiceStart(svc) < 0) continue;      // absent
                if (Sys.GetServiceStart(svc) == 4) continue;     // déjà désactivé (on n'y touche pas)
                if (!Sys.IsServiceRunning(svc)) continue;        // déjà arrêté
                Sys.StopService(svc);
                _stopped.Add(svc);
            }

            IsActive = true;
            log("Mode Jeu ACTIVÉ : timer 1 ms, ~" + Math.Max(0, freed) + " Mo RAM libérés, "
                + _stopped.Count + " service(s) de fond suspendu(s).", 1);
        }

        public static void Deactivate(Action<string, int> log)
        {
            if (!IsActive) return;

            foreach (string svc in _stopped)
                Sys.StartService(svc);
            int restored = _stopped.Count;
            _stopped.Clear();

            if (!_timerWasActive) Native.SetTimer1ms(false);

            IsActive = false;
            log("Mode Jeu désactivé : " + restored + " service(s) relancé(s), timer rendu au système.", 0);
        }
    }
}
