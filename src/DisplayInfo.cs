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

        private const int ENUM_CURRENT_SETTINGS = -1;

        public class DisplayMode
        {
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
    }
}
