using System;
using System.Runtime.InteropServices;

namespace DTGOptimizer
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

    /// <summary>Nettoyage mémoire : vide les working sets et purge la liste standby.</summary>
    internal static class NativeMem
    {
        [DllImport("psapi.dll")]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        [DllImport("ntdll.dll")]
        private static extern int NtSetSystemInformation(int infoClass, ref int info, int length);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr h, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LookupPrivilegeValue(string sys, string name, out long luid);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES newState, int len, IntPtr prev, IntPtr retlen);
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMSTAT lp);

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_PRIVILEGES { public int Count; public long Luid; public int Attributes; }

        [StructLayout(LayoutKind.Sequential)]
        private class MEMSTAT
        {
            public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMSTAT));
            public uint dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }

        public static long UsedPhysMB()
        {
            var m = new MEMSTAT();
            if (GlobalMemoryStatusEx(m)) return (long)((m.ullTotalPhys - m.ullAvailPhys) / (1024 * 1024));
            return 0;
        }

        public static int EmptyAllWorkingSets()
        {
            int done = 0;
            foreach (System.Diagnostics.Process p in System.Diagnostics.Process.GetProcesses())
            {
                try { if (EmptyWorkingSet(p.Handle)) done++; }
                catch { }
                finally { try { p.Dispose(); } catch { } }
            }
            return done;
        }

        public static bool PurgeStandby()
        {
            try
            {
                if (!EnablePrivilege("SeProfileSingleProcessPrivilege")) return false;
                int cmd = 4; // MemoryPurgeStandbyList
                return NtSetSystemInformation(0x50, ref cmd, sizeof(int)) == 0; // 0x50 = SystemMemoryListInformation
            }
            catch { return false; }
        }

        private static bool EnablePrivilege(string name)
        {
            IntPtr token;
            if (!OpenProcessToken(GetCurrentProcess(), 0x0020 | 0x0008, out token)) return false;
            long luid;
            if (!LookupPrivilegeValue(null, name, out luid)) return false;
            var tp = new TOKEN_PRIVILEGES { Count = 1, Luid = luid, Attributes = 0x00000002 };
            return AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
        }
    }

    /// <summary>Corbeille via shell32 (taille et vidage).</summary>
    internal static class NativeRecycle
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHQueryRecycleBin(string pszRootPath, ref SHQUERYRBINFO info);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHEmptyRecycleBin(IntPtr hwnd, string pszRootPath, uint dwFlags);

        public static bool QueryBytes(out long bytes)
        {
            var info = new SHQUERYRBINFO();
            info.cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO));
            int rc = SHQueryRecycleBin(null, ref info);
            bytes = (rc == 0) ? info.i64Size : 0;
            return rc == 0;
        }

        public static void Empty()
        {
            SHEmptyRecycleBin(IntPtr.Zero, null, 7); // NOCONFIRMATION|NOPROGRESSUI|NOSOUND
        }
    }
}
