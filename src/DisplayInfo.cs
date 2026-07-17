using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Fréquence de rafraîchissement par écran : mode actuel et maximum supporté
    /// à la résolution actuelle (EnumDisplaySettings). Lecture seule.
    /// </summary>
    internal static class DisplayInfo
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public ushort dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public uint dmFields;
            public int dmPositionX, dmPositionY;
            public uint dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public ushort dmLogPixels;
            public uint dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public uint dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplayDevices(string device, uint devNum, ref DISPLAY_DEVICE displayDevice, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int ChangeDisplaySettingsEx(string deviceName, ref DEVMODE devMode, IntPtr hwnd, uint flags, IntPtr lParam);

        private const int ENUM_CURRENT_SETTINGS = -1;
        private const uint DM_DISPLAYFREQUENCY = 0x400000;
        private const uint CDS_TEST = 0x2;
        private const uint CDS_UPDATEREGISTRY = 0x1;
        private const int DISP_CHANGE_SUCCESSFUL = 0;

        public class DisplayMode
        {
            public string Device;    // nom d'adaptateur Windows (\\.\DISPLAY1), pour ChangeDisplaySettingsEx
            public string Name;
            public int Width, Height;
            public int CurrentHz;
            public int MaxHz;        // max supporté à la résolution ACTUELLE
            public bool Primary;
            public bool BelowMax { get { return MaxHz > CurrentHz + 4; } } // tolère 59/60 etc.
        }

        private static DEVMODE NewDevMode()
        {
            var dm = new DEVMODE();
            dm.dmSize = (ushort)Marshal.SizeOf(typeof(DEVMODE));
            return dm;
        }

        private static string MonitorName(string adapterDevice, int index)
        {
            try
            {
                var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE)) };
                if (EnumDisplayDevices(adapterDevice, 0, ref dd, 0) && !string.IsNullOrWhiteSpace(dd.DeviceString))
                    return dd.DeviceString.Trim();
            }
            catch { }
            return "Écran " + index;
        }

        public static List<DisplayMode> Query()
        {
            var list = new List<DisplayMode>();
            try
            {
                int idx = 1;
                foreach (Screen sc in Screen.AllScreens)
                {
                    try
                    {
                        DEVMODE cur = NewDevMode();
                        if (!EnumDisplaySettings(sc.DeviceName, ENUM_CURRENT_SETTINGS, ref cur)) { idx++; continue; }
                        int w = (int)cur.dmPelsWidth, h = (int)cur.dmPelsHeight, hz = (int)cur.dmDisplayFrequency;
                        if (w <= 0 || h <= 0 || hz <= 0) { idx++; continue; }

                        int max = hz;
                        DEVMODE m = NewDevMode();
                        for (int i = 0; EnumDisplaySettings(sc.DeviceName, i, ref m); i++)
                        {
                            if ((int)m.dmPelsWidth == w && (int)m.dmPelsHeight == h && (int)m.dmDisplayFrequency > max)
                                max = (int)m.dmDisplayFrequency;
                            m = NewDevMode();
                        }

                        list.Add(new DisplayMode
                        {
                            Device = sc.DeviceName,
                            Name = MonitorName(sc.DeviceName, idx),
                            Width = w, Height = h, CurrentHz = hz, MaxHz = max, Primary = sc.Primary
                        });
                    }
                    catch { }
                    idx++;
                }
            }
            catch { }
            return list;
        }

        /// <summary>
        /// Change la fréquence de rafraîchissement d'un écran (résolution inchangée).
        /// Teste d'abord le mode (CDS_TEST) puis l'applique et le mémorise dans le registre.
        /// Renvoie true si le mode a bien été appliqué.
        /// </summary>
        public static bool SetHz(string device, int hz)
        {
            if (string.IsNullOrEmpty(device) || hz <= 0) return false;
            try
            {
                DEVMODE dm = NewDevMode();
                if (!EnumDisplaySettings(device, ENUM_CURRENT_SETTINGS, ref dm)) return false;
                dm.dmDisplayFrequency = (uint)hz;
                dm.dmFields = DM_DISPLAYFREQUENCY;
                if (ChangeDisplaySettingsEx(device, ref dm, IntPtr.Zero, CDS_TEST, IntPtr.Zero) != DISP_CHANGE_SUCCESSFUL)
                    return false;
                return ChangeDisplaySettingsEx(device, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero) == DISP_CHANGE_SUCCESSFUL;
            }
            catch { return false; }
        }
    }
}
