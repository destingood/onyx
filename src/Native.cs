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

        // ---- Détection d'un jeu / appli plein écran au premier plan ----
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        /// <summary>Vrai si la fenêtre au premier plan couvre tout son écran (jeu plein écran / borderless).</summary>
        public static bool IsGameFullscreen()
        {
            try
            {
                IntPtr h = GetForegroundWindow();
                if (h == IntPtr.Zero) return false;
                RECT r;
                if (!GetWindowRect(h, out r)) return false;
                System.Drawing.Rectangle scr = System.Windows.Forms.Screen.FromHandle(h).Bounds;
                if (r.Left > scr.Left || r.Top > scr.Top || r.Right < scr.Right || r.Bottom < scr.Bottom)
                    return false;
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                if (pid == 0) return false;
                if (pid == (uint)System.Diagnostics.Process.GetCurrentProcess().Id) return false;
                string name = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName.ToLowerInvariant();
                // Surfaces systèmes qui occupent l'écran sans être des jeux.
                if (name == "explorer" || name == "searchhost" || name == "lockapp" ||
                    name == "shellexperiencehost" || name == "startmenuexperiencehost" ||
                    name == "dwm" || name == "idle") return false;
                return true;
            }
            catch { return false; }
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
