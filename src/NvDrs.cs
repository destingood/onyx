using System;
using System.Runtime.InteropServices;

namespace BTOptimizer
{
    /// <summary>
    /// LECTURE SEULE des réglages du panneau NVIDIA (base de profils « DRS ») : V-Sync, limiteur
    /// d'images et G-SYNC, pour le profil d'un jeu ou à défaut le profil global. Rien n'est
    /// jamais écrit ici (pas d'appel à SetSetting ni SaveSettings).
    ///
    /// Identifiants et structures : dépôt NVIDIA/nvapi (nvapi_interface.h, NvApiDriverSettings.h,
    /// nvapi.h). NVDRS_SETTING_V1 = 12 320 octets ; la valeur DWORD courante est à l'octet 8 220.
    /// NVDRS_APPLICATION change de taille selon la version : on essaie V4 → V1, le pilote refuse
    /// une version qu'il ne connaît pas (code −9) sans rien lire de travers.
    /// </summary>
    internal static class NvDrs
    {
        [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr QueryInterface(uint id);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FnInit();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FnSession(out IntPtr h);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FnHandle(IntPtr h);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FnBase(IntPtr h, out IntPtr profil);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FnFindApp(IntPtr h, IntPtr nom, out IntPtr profil, IntPtr app);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FnGet(IntPtr h, IntPtr profil, uint id, IntPtr setting);

        private const uint IdInitialize = 0x0150E828, IdCreate = 0x0694D52E, IdDestroy = 0xDAD9CFF8,
                           IdLoad = 0x375DBD6B, IdBase = 0xDA8466A0, IdGlobalCourant = 0x617BFF9F, IdFindApp = 0xEEE566B2, IdGet = 0x73BF8338;

        public const uint VSyncId = 0x00A879CF, LimiteurId = 0x10835002, GSyncGlobalId = 0x1194F158;

        private const int TailleSetting = 12320, OffsetValeur = 8220, Unicode = 4096;

        /// <summary>null partout = illisible (pas de carte NVIDIA, pilote absent, refus).</summary>
        public sealed class Lecture
        {
            public uint? VSync, Limiteur, GSync;
            public bool ProfilJeu;     // au moins une valeur vient du profil du jeu
            public string Motif;
            public string Trace = "";  // « id:code=valeur » de chaque lecture (CLI -coherence)
            public int CodeJeu;       // code NVAPI de la recherche du profil du jeu (0 = trouvé)
            public int DernierCode;   // dernier code d'erreur NVAPI de GetSetting, hors « non stocké » (diagnostic)
        }

        private static T Fn<T>(uint id) where T : class
        {
            try
            {
                IntPtr p = QueryInterface(id);
                return p == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
            }
            catch { return null; }
        }

        /// <summary>exe : « Overwatch.exe » ; null = profil global seulement.</summary>
        public static Lecture Lit(string exe)
        {
            var l = new Lecture();
            IntPtr session = IntPtr.Zero, nom = IntPtr.Zero, app = IntPtr.Zero, setting = IntPtr.Zero;
            FnHandle destroy = null;
            try
            {
                var init = Fn<FnInit>(IdInitialize);
                var create = Fn<FnSession>(IdCreate);
                destroy = Fn<FnHandle>(IdDestroy);
                var load = Fn<FnHandle>(IdLoad);
                var baseP = Fn<FnBase>(IdBase);
                var get = Fn<FnGet>(IdGet);
                if (init == null || create == null || destroy == null || load == null || baseP == null || get == null)
                { l.Motif = "NVAPI indisponible (pas de carte NVIDIA ou pilote absent)."; return l; }
                if (init() != 0) { l.Motif = "NVAPI refuse l'initialisation."; return l; }
                if (create(out session) != 0) { l.Motif = "Session DRS refusée."; session = IntPtr.Zero; return l; }
                if (load(session) != 0) { l.Motif = "Profils du pilote illisibles."; return l; }
                IntPtr global;
                // Le panneau NVIDIA règle le profil global COURANT ; le profil de base n'est qu'un repli.
                var courant = Fn<FnBase>(IdGlobalCourant);
                if (courant == null || courant(session, out global) != 0 || global == IntPtr.Zero)
                    if (baseP(session, out global) != 0) { l.Motif = "Profil global illisible."; return l; }

                IntPtr profilJeu = IntPtr.Zero;
                var find = Fn<FnFindApp>(IdFindApp);
                if (!string.IsNullOrEmpty(exe) && find != null)
                {
                    nom = Marshal.AllocHGlobal(Unicode);
                    app = Marshal.AllocHGlobal(Unicode * 5 + 16);
                    Zero(nom, Unicode);
                    char[] cs = exe.ToCharArray();
                    Marshal.Copy(cs, 0, nom, Math.Min(cs.Length, Unicode / 2 - 1));
                    // V4, V3, V2, V1 : version = taille | (n << 16).
                    int[] tailles = { 20492, 16396, 16392, 12296 };
                    for (int v = 4; v >= 1 && profilJeu == IntPtr.Zero; v--)
                    {
                        Zero(app, Unicode * 5 + 16);
                        Marshal.WriteInt32(app, tailles[4 - v] | (v << 16));
                        IntPtr p;
                        int code = find(session, nom, out p, app);
                        l.CodeJeu = code;
                        if (code == 0) profilJeu = p;
                        else if (code != -9) break;   // −9 : version inconnue → essayer la suivante
                    }
                }

                _dernier = 0;
                _trace = new System.Text.StringBuilder();
                setting = Marshal.AllocHGlobal(TailleSetting);
                bool deJeu;
                l.VSync = Valeur(get, session, profilJeu, global, VSyncId, 0x60925292u, setting, out deJeu); l.ProfilJeu |= deJeu;
                l.Limiteur = Valeur(get, session, profilJeu, global, LimiteurId, 0u, setting, out deJeu); l.ProfilJeu |= deJeu;
                l.GSync = Valeur(get, session, profilJeu, global, GSyncGlobalId, 1u, setting, out deJeu); l.ProfilJeu |= deJeu;
                l.DernierCode = _dernier;
                l.Trace = _trace.ToString().Trim();
                _trace = null;
                return l;
            }
            catch (Exception ex) { l.Motif = "Lecture NVAPI impossible (" + ex.Message + ")."; return l; }
            finally
            {
                _trace = null;
                if (session != IntPtr.Zero && destroy != null) try { destroy(session); } catch { }
                if (nom != IntPtr.Zero) Marshal.FreeHGlobal(nom);
                if (app != IntPtr.Zero) Marshal.FreeHGlobal(app);
                if (setting != IntPtr.Zero) Marshal.FreeHGlobal(setting);
            }
        }

        /// <summary>Profil du jeu, puis global ; un réglage jamais touché n'est pas stocké
        /// (code −160, NVAPI_SETTING_NOT_FOUND) : il vaut alors la valeur d'usine du pilote.
        /// Recoupé le 24/11/2026 avec G-Assist sur RTX 4080 SUPER : −160 partout = « V-Sync contrôlé
        /// par l'application, G-SYNC plein écran seulement, aucun limiteur ».</summary>
        private static uint? Valeur(FnGet get, IntPtr session, IntPtr jeu, IntPtr global, uint id, uint defaut, IntPtr buf, out bool deJeu)
        {
            deJeu = false;
            int code;
            if (jeu != IntPtr.Zero)
            {
                uint? v = Un(get, session, jeu, id, buf, out code);
                if (v != null) { deJeu = true; return v; }
            }
            uint? g = Un(get, session, global, id, buf, out code);
            if (g != null) return g;
            return code == -160 ? defaut : (uint?)null;
        }

        private static uint? Un(FnGet get, IntPtr session, IntPtr profil, uint id, IntPtr buf, out int code)
        {
            Zero(buf, TailleSetting);
            Marshal.WriteInt32(buf, TailleSetting | (1 << 16));
            code = get(session, profil, id, buf);
            if (_trace != null) _trace.Append(id.ToString("X")).Append(':').Append(code).Append(code == 0 ? "=" + Marshal.ReadInt32(buf, OffsetValeur) : "").Append(' ');
            if (code != 0) { if (code != -160) _dernier = code; return null; }
            return unchecked((uint)Marshal.ReadInt32(buf, OffsetValeur));
        }

        [ThreadStatic] private static int _dernier;
        [ThreadStatic] private static System.Text.StringBuilder _trace;

        private static void Zero(IntPtr p, int n)
        {
            for (int i = 0; i + 8 <= n; i += 8) Marshal.WriteInt64(p, i, 0);
            for (int i = n - n % 8; i < n; i++) Marshal.WriteByte(p, i, 0);
        }
    }
}
