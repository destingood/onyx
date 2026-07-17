using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace BTOptimizer
{
    /// <summary>
    /// Outils audio via l'API COM MMDevice (celle du panneau Son de Windows).
    /// Lecture seule des périphériques (l'énumération registre est bloquée par ACL) +
    /// redémarrage du service audio. Les réglages d'améliorations se font dans le
    /// panneau Windows natif — on ne touche pas aux clés protégées FxProperties.
    /// </summary>
    internal static class AudioTools
    {
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorCom { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        }

        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceCollection
        {
            int GetCount(out int count);
            int Item(int index, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, out IntPtr iface);
            int OpenPropertyStore(int stgmAccess, out IPropertyStore properties);
            int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
            int GetState(out int state);
        }

        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            int GetCount(out int count);
            int GetAt(int index, out PropertyKey key);
            int GetValue(ref PropertyKey key, out PropVariant value);
            int SetValue(ref PropertyKey key, ref PropVariant value);
            int Commit();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PropertyKey { public Guid FmtId; public int Pid; }

        [StructLayout(LayoutKind.Explicit)]
        private struct PropVariant
        {
            [FieldOffset(0)] public ushort vt;
            [FieldOffset(8)] public IntPtr p;   // pwszVal / autres pointeurs (x64)
        }

        [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant pv);

        private const int EndpointFriendly = 14;  // PKEY_Device_FriendlyName, fmtid a45c254e-...
        private static readonly Guid PkeyDeviceFriendly = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0");
        private const int VT_LPWSTR = 31;
        private const int STGM_READ = 0;
        private const int STATE_ACTIVE = 0x1;

        public class AudioDevice
        {
            public string Id;
            public string Name;
            public bool IsDefault;
        }

        private static string ReadFriendlyName(IMMDevice dev)
        {
            IPropertyStore store = null;
            try
            {
                if (dev.OpenPropertyStore(STGM_READ, out store) != 0 || store == null) return null;
                var key = new PropertyKey { FmtId = PkeyDeviceFriendly, Pid = EndpointFriendly };
                PropVariant val;
                if (store.GetValue(ref key, out val) != 0) return null;
                string name = null;
                if (val.vt == VT_LPWSTR && val.p != IntPtr.Zero) name = Marshal.PtrToStringUni(val.p);
                PropVariantClear(ref val);
                return name;
            }
            catch { return null; }
            finally { if (store != null) Marshal.ReleaseComObject(store); }
        }

        public static string DefaultRenderId()
        {
            IMMDeviceEnumerator enu = null;
            try
            {
                enu = (IMMDeviceEnumerator)(object)new MMDeviceEnumeratorCom();
                IMMDevice dev;
                if (enu.GetDefaultAudioEndpoint(0, 1, out dev) != 0 || dev == null) return null;
                string id;
                int hr = dev.GetId(out id);
                Marshal.ReleaseComObject(dev);
                return hr == 0 ? id : null;
            }
            catch { return null; }
            finally { if (enu != null) Marshal.ReleaseComObject(enu); }
        }

        /// <summary>Périphériques de lecture actifs (nom via COM), défaut en tête.</summary>
        public static List<AudioDevice> ListRender()
        {
            var list = new List<AudioDevice>();
            IMMDeviceEnumerator enu = null;
            IMMDeviceCollection col = null;
            try
            {
                enu = (IMMDeviceEnumerator)(object)new MMDeviceEnumeratorCom();
                string defId = null;
                IMMDevice ddev;
                if (enu.GetDefaultAudioEndpoint(0, 1, out ddev) == 0 && ddev != null)
                {
                    ddev.GetId(out defId);
                    Marshal.ReleaseComObject(ddev);
                }
                if (enu.EnumAudioEndpoints(0, STATE_ACTIVE, out col) != 0 || col == null) return list;
                int n;
                if (col.GetCount(out n) != 0) return list;
                for (int i = 0; i < n; i++)
                {
                    IMMDevice dev;
                    if (col.Item(i, out dev) != 0 || dev == null) continue;
                    try
                    {
                        string id;
                        if (dev.GetId(out id) != 0) continue;
                        string name = ReadFriendlyName(dev);
                        if (string.IsNullOrEmpty(name)) name = "Périphérique audio";
                        list.Add(new AudioDevice
                        {
                            Id = id,
                            Name = name.Trim(),
                            IsDefault = defId != null && string.Equals(id, defId, StringComparison.OrdinalIgnoreCase)
                        });
                    }
                    finally { Marshal.ReleaseComObject(dev); }
                }
            }
            catch { }
            finally
            {
                if (col != null) Marshal.ReleaseComObject(col);
                if (enu != null) Marshal.ReleaseComObject(enu);
            }
            list.Sort((a, b) =>
            {
                if (a.IsDefault != b.IsDefault) return a.IsDefault ? -1 : 1;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        /// <summary>Redémarre le service audio (coupe le son ~2 s ; résout grésillements/coupures).</summary>
        public static void RestartAudioService(Action<string, int> log)
        {
            log("Redémarrage du service audio (le son coupe ~2 secondes)...", 0);
            Sys.StopService("Audiosrv");
            System.Threading.Thread.Sleep(800);
            Sys.StartService("Audiosrv");
            System.Threading.Thread.Sleep(800);
            bool ok = Sys.IsServiceRunning("Audiosrv");
            log(ok ? "Service audio redémarré." : "Le service audio ne s'est pas relancé — redémarre le PC si le son est coupé.", ok ? 1 : 3);
        }
    }
}
