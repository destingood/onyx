using System;
using System.IO;
using System.Runtime.InteropServices;

namespace BTOptimizer
{
    /// <summary>
    /// VRAIES NOTIFICATIONS WINDOWS — celles qui restent dans le centre de notifications.
    ///
    /// Jusqu'ici ONYX utilisait NotifyIcon.ShowBalloonTip. Sur Windows 10/11 la bulle RESSEMBLE à
    /// une notification, mais elle n'est pas enregistrée : elle s'affiche quelques secondes puis
    /// disparaît SANS LAISSER DE TRACE. Un utilisateur absent de son écran, ou en pleine partie
    /// plein écran, ne la voit jamais et ne peut pas la retrouver ensuite.
    ///
    /// Une vraie notification (toast) exige trois choses, et les trois sont indispensables :
    ///   1. un identifiant d'application (AUMID) posé sur le processus ;
    ///   2. un raccourci du menu Démarrer PORTANT le même AUMID — sans lui, Windows refuse
    ///      silencieusement d'afficher le toast d'une application de bureau ;
    ///   3. l'appel WinRT ToastNotificationManager avec ce même identifiant.
    ///
    /// Si l'un des trois échoue, Show renvoie false et l'appelant retombe sur la bulle : on ne perd
    /// jamais l'information, on tente seulement de mieux la délivrer.
    /// </summary>
    internal static class WinToast
    {
        /// <summary>Identifiant stable de l'application. NE JAMAIS le changer : Windows lie les
        /// notifications déjà enregistrées et les réglages de l'utilisateur à cette chaîne.</summary>
        public const string Aumid = "BT.ONYX.Optimiseur";

        private const string NomRaccourci = "ONYX.lnk";

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string appID);

        /// <summary>À appeler UNE fois au démarrage, avant toute notification.</summary>
        public static void DeclareIdentite()
        {
            try { SetCurrentProcessExplicitAppUserModelID(Aumid); }
            catch { }   // l'app doit démarrer même si l'identité échoue
        }

        public static string CheminRaccourci
        {
            get
            {
                try
                {
                    return Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        @"Microsoft\Windows\Start Menu\Programs", NomRaccourci);
                }
                catch { return null; }
            }
        }

        // ---- COM : raccourci porteur de l'AUMID -------------------------------

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLinkCom { }

        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder file, int maxPath, IntPtr fd, int flags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder name, int maxName);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder dir, int maxPath);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder args, int maxArgs);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
            void GetHotkey(out short hotkey);
            void SetHotkey(short hotkey);
            void GetShowCmd(out int showCmd);
            void SetShowCmd(int showCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder icon, int maxPath, out int index);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string icon, int index);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pathRel, int reserved);
            void Resolve(IntPtr hwnd, int flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
        }

        [ComImport, Guid("0000010b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPersistFile
        {
            void GetClassID(out Guid classID);
            [PreserveSig] int IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, int mode);
            void Save([MarshalAs(UnmanagedType.LPWStr)] string fileName, [MarshalAs(UnmanagedType.Bool)] bool remember);
            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string fileName);
            void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string fileName);
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct PropertyKey { public Guid fmtid; public int pid; }

        [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            void GetCount(out uint count);
            void GetAt(uint index, out PropertyKey key);
            void GetValue(ref PropertyKey key, IntPtr pv);
            void SetValue(ref PropertyKey key, IntPtr pv);
            void Commit();
        }

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(IntPtr pvar);

        private const ushort VT_LPWSTR = 31;
        private const int TaillePropVariant = 24;   // suffisant en 32 comme en 64 bits
        private const int OffsetUnion = 8;          // début de l'union dans la structure

        /// <summary>
        /// Construit un PROPVARIANT de type chaîne. On l'assemble à la main parce que
        /// InitPropVariantFromString n'est PAS exporté par propsys.dll : c'est une fonction inline
        /// de l'en-tête C++. L'appeler par P/Invoke échoue avec EntryPointNotFoundException.
        /// La chaîne est allouée avec l'allocateur COM, donc PropVariantClear la libère.
        /// </summary>
        private static IntPtr NouveauPropVariantChaine(string valeur)
        {
            IntPtr pv = Marshal.AllocCoTaskMem(TaillePropVariant);
            for (int i = 0; i < TaillePropVariant; i++) Marshal.WriteByte(pv, i, 0);
            IntPtr chaine = Marshal.StringToCoTaskMemUni(valeur ?? "");
            Marshal.WriteInt16(pv, 0, unchecked((short)VT_LPWSTR));
            Marshal.WriteIntPtr(pv, OffsetUnion, chaine);
            return pv;
        }

        // System.AppUserModel.ID
        private static readonly Guid FmtidAppUserModel = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");

        /// <summary>
        /// Garantit l'existence du raccourci porteur de l'AUMID. Sans lui, Windows refuse
        /// SILENCIEUSEMENT les toasts d'une application de bureau — c'est l'étape que tout le
        /// monde oublie. Renvoie false si le raccourci n'a pas pu être créé.
        /// </summary>
        public static bool AssureRaccourci()
        {
            string lnk = CheminRaccourci;
            if (string.IsNullOrEmpty(lnk)) return false;
            try { if (File.Exists(lnk)) return true; }
            catch { return false; }

            IntPtr pv = IntPtr.Zero;
            try
            {
                try { Directory.CreateDirectory(Path.GetDirectoryName(lnk)); } catch { }
                string exe = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;

                var link = (IShellLinkW)new ShellLinkCom();
                link.SetPath(exe);
                try { link.SetWorkingDirectory(Path.GetDirectoryName(exe)); } catch { }
                link.SetDescription("ONYX — optimiseur");

                var store = (IPropertyStore)link;
                var key = new PropertyKey { fmtid = FmtidAppUserModel, pid = 5 };
                pv = NouveauPropVariantChaine(Aumid);
                store.SetValue(ref key, pv);
                store.Commit();

                ((IPersistFile)link).Save(lnk, true);
                return true;
            }
            catch { return false; }
            finally
            {
                if (pv != IntPtr.Zero)
                {
                    try { PropVariantClear(pv); } catch { }
                    try { Marshal.FreeCoTaskMem(pv); } catch { }
                }
            }
        }

        /// <summary>Échappe le texte pour le XML du toast. PUR → testable.</summary>
        public static string Echappe(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                    .Replace("\"", "&quot;").Replace("'", "&apos;");
        }

        /// <summary>Document XML du toast. PUR → testable sans Windows.</summary>
        public static string Xml(string titre, string texte)
        {
            return "<toast><visual><binding template=\"ToastGeneric\">"
                 + "<text>" + Echappe(titre) + "</text>"
                 + "<text>" + Echappe(texte) + "</text>"
                 + "</binding></visual></toast>";
        }

        /// <summary>
        /// Affiche une VRAIE notification Windows, enregistrée dans le centre de notifications.
        /// false si l'environnement ne le permet pas — l'appelant retombe alors sur la bulle.
        /// </summary>
        public static bool Show(string titre, string texte)
        {
            try
            {
                if (!AssureRaccourci()) return false;
                var doc = new Windows.Data.Xml.Dom.XmlDocument();
                doc.LoadXml(Xml(titre, texte));
                var toast = new Windows.UI.Notifications.ToastNotification(doc);
                Windows.UI.Notifications.ToastNotificationManager.CreateToastNotifier(Aumid).Show(toast);
                return true;
            }
            catch { return false; }
        }
    }
}
