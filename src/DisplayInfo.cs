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
        /// <summary>Aucun drapeau : le mode s'applique tout de suite mais N'EST PAS mémorisé.
        /// Un redémarrage rétablit alors l'ancien mode — la sortie de secours.</summary>
        private const uint CDS_DYNAMIQUE = 0x0;
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

        internal static int EnumerationsPourTest;
        private static readonly Dictionary<string, int> _maxConnus = new Dictionary<string, int>();

        /// <summary>
        /// Fréquence maximale d'un écran à une résolution, mise en cache : énumérer tous les modes
        /// prend le verrou d'affichage de Windows, et toutes les peintures de l'appli ralentissent
        /// pendant ce temps (~50 ms à l'ouverture du Copilote). La liste des modes ne change pas
        /// sans changer d'écran ; une autre résolution donne une autre clé.
        /// </summary>
        private static int MaxSupporte(string device, int w, int h)
        {
            string cle = device + "|" + w + "x" + h;
            lock (_maxConnus)
            {
                int max;
                if (_maxConnus.TryGetValue(cle, out max)) return max;
                EnumerationsPourTest++;
                max = 0;
                DEVMODE m = NewDevMode();
                for (int i = 0; EnumDisplaySettings(device, i, ref m); i++)
                {
                    if ((int)m.dmPelsWidth == w && (int)m.dmPelsHeight == h && (int)m.dmDisplayFrequency > max)
                        max = (int)m.dmDisplayFrequency;
                    m = NewDevMode();
                }
                _maxConnus[cle] = max;
                return max;
            }
        }

        /// <summary>
        /// Résolutions que Windows accepte pour l'écran du jeu (« 2560x1440 », triées, au moins 800 × 600).
        /// Écran trouvé par son identifiant (« MONITOR\SAM793E\{…}\0002 ») ou son nom ; sinon, celles
        /// que tous les écrans allumés acceptent. Lecture seule ; prend le verrou d'affichage : hors du fil d'interface.
        /// </summary>
        internal static List<string> Resolutions(string idEcran, string nomEcran)
        {
            var parEcran = new List<HashSet<string>>();
            HashSet<string> trouve = null;
            try
            {
                for (uint i = 0; i < 32; i++)
                {
                    var a = new DISPLAY_DEVICE { cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE)) };
                    if (!EnumDisplayDevices(null, i, ref a, 0)) break;
                    if ((a.StateFlags & 0x1) == 0) continue;   // DISPLAY_DEVICE_ATTACHED_TO_DESKTOP
                    var modes = new HashSet<string>(StringComparer.Ordinal);
                    DEVMODE m = NewDevMode();
                    for (int k = 0; EnumDisplaySettings(a.DeviceName, k, ref m); k++) { modes.Add(m.dmPelsWidth + "x" + m.dmPelsHeight); m = NewDevMode(); }
                    if (modes.Count == 0) continue;
                    parEcran.Add(modes);
                    for (uint j = 0; j < 8; j++)
                    {
                        var e = new DISPLAY_DEVICE { cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE)) };
                        if (!EnumDisplayDevices(a.DeviceName, j, ref e, 0)) break;
                        if ((idEcran != null && string.Equals((e.DeviceID ?? "").Trim(), idEcran, StringComparison.OrdinalIgnoreCase))
                            || (nomEcran != null && string.Equals((e.DeviceString ?? "").Trim(), nomEcran.Trim(), StringComparison.OrdinalIgnoreCase)))
                            trouve = modes;
                    }
                }
            }
            catch { }
            return AffichageJeu.Trie(trouve ?? AffichageJeu.Communes(parEcran));
        }

        /// <summary>Oublie les modes connus (écran branché ou changé).</summary>
        internal static void OublieModes() { lock (_maxConnus) _maxConnus.Clear(); }

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

                        int max = Math.Max(hz, MaxSupporte(sc.DeviceName, w, h));

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
        ///
        /// <paramref name="persister"/> décide de la portée du changement, et c'est le réglage le
        /// plus important de cette fonction :
        ///
        ///   VRAI  — le mode est écrit dans le registre. Il survit au redémarrage. C'est ce qu'on
        ///           veut UNE FOIS QUE l'utilisateur a confirmé qu'il voit son écran.
        ///   FAUX  — le mode n'est appliqué que dynamiquement. Un redémarrage rétablit l'ancien.
        ///
        /// Pourquoi ça compte : CDS_TEST demande au PILOTE si le mode est valide, pas à l'écran
        /// s'il sait l'afficher. Un pilote peut annoncer une fréquence qu'un panneau ne
        /// synchronise pas — cas typique d'un très haut rafraîchissement sur un câble limite.
        /// Le test passe, le mode s'applique, et l'écran devient noir.
        ///
        /// Si ce mode a été écrit dans le registre, l'écran noir SURVIT AU REDÉMARRAGE. Appliquer
        /// d'abord sans persister laisse toujours une sortie de secours qui ne dépend pas d'ONYX :
        /// couper et rallumer la machine suffit.
        /// </summary>
        public static bool SetHz(string device, int hz, bool persister = true)
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
                uint drapeaux = persister ? CDS_UPDATEREGISTRY : CDS_DYNAMIQUE;
                return ChangeDisplaySettingsEx(device, ref dm, IntPtr.Zero, drapeaux, IntPtr.Zero) == DISP_CHANGE_SUCCESSFUL;
            }
            catch { return false; }
        }
    }
}
