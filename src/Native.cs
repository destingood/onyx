using System;
using System.Runtime.InteropServices;

namespace BTOptimizer
{
    /// <summary>Appels Win32 : paramètres souris en direct et timer haute résolution.</summary>
    internal static class Native
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, int[] pvParam, uint fWinIni);

        [DllImport("winmm.dll")]
        private static extern uint timeBeginPeriod(uint uMilliseconds);

        [DllImport("winmm.dll")]
        private static extern uint timeEndPeriod(uint uMilliseconds);

        private const uint SPI_SETMOUSE       = 0x0004;
        private const uint SPIF_UPDATEINIFILE = 0x01;
        private const uint SPIF_SENDCHANGE    = 0x02;

        /// <summary>Applique [seuil1, seuil2, accélération] immédiatement dans la session.</summary>
        public static void SetMouseParams(int threshold1, int threshold2, int acceleration)
        {
            SystemParametersInfo(SPI_SETMOUSE, 0,
                new int[] { threshold1, threshold2, acceleration },
                SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
        }

        [DllImport("ntdll.dll")]
        private static extern int NtQueryTimerResolution(out uint minimum, out uint maximum, out uint current);

        /// <summary>Résolution actuelle du timer système en millisecondes (0 si indisponible).</summary>
        public static double CurrentTimerMs()
        {
            uint min, max, cur;
            if (NtQueryTimerResolution(out min, out max, out cur) == 0)
                return cur / 10000.0; // unités de 100 ns -> ms
            return 0;
        }

        private static bool _timerActive;
        public static bool TimerActive { get { return _timerActive; } }

        /// <summary>Force la résolution du timer Windows à 1 ms tant que l'app tourne.</summary>
        public static void SetTimer1ms(bool enable)
        {
            if (enable && !_timerActive)
            {
                timeBeginPeriod(1);
                _timerActive = true;
            }
            else if (!enable && _timerActive)
            {
                timeEndPeriod(1);
                _timerActive = false;
            }
        }
    }
}
