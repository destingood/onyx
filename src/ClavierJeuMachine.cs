using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace BTOptimizer
{
    /// <summary>
    /// CLAVIER PENDANT LA PARTIE — branchement. Appelé par <see cref="GameBoost"/> à l'activation
    /// et à la sortie du Mode Jeu.
    ///
    /// Touche Windows : un crochet clavier bas niveau (WH_KEYBOARD_LL) sur un fil à lui, avec sa
    /// propre boucle de messages. Pas sur le fil d'interface : si celui-ci est occupé plus de
    /// quelques centaines de millisecondes, Windows retire le crochet sans prévenir, et chaque
    /// frappe attend d'ici là. Le crochet vit dans ONYX : rien n'entre dans le jeu.
    ///
    /// Raccourcis Maj : SystemParametersInfo SANS écrire le profil (fWinIni = 0). Le réglage
    /// enregistré de l'utilisateur n'est jamais modifié : au pire (ONYX tué), une fermeture de
    /// session le rend. Le marqueur bt-gamemode-clavier-etat.txt, écrit AVANT le changement,
    /// permet à <see cref="Soigne"/> de le rendre dès le lancement suivant.
    /// </summary>
    internal static partial class ClavierJeu
    {
        private static string CheminOptions { get { return AppPaths.File("bt-gamemode-clavier.txt"); } }
        private static string CheminEtat { get { return AppPaths.File("bt-gamemode-clavier-etat.txt"); } }

        private static readonly object _verrou = new object();
        private static Options _options;

        public static Options Reglages
        {
            get
            {
                lock (_verrou)
                {
                    if (_options == null)
                    {
                        try { _options = LitOptions(File.Exists(CheminOptions) ? File.ReadAllText(CheminOptions) : null); }
                        catch { _options = new Options(); }
                    }
                    return new Options { ToucheWindows = _options.ToucheWindows, Raccourcis = _options.Raccourcis };
                }
            }
            set
            {
                if (value == null) return;
                lock (_verrou)
                {
                    _options = new Options { ToucheWindows = value.ToucheWindows, Raccourcis = value.Raccourcis };
                    try { File.WriteAllText(CheminOptions, EcritOptions(_options)); }
                    catch (Exception ex) { JournalTechnique.Echec("ClavierJeu.Reglages", ex); }
                }
            }
        }

        /// <summary>Début du Mode Jeu. jeu = processus du jeu (mode automatique), null s'il est inconnu.</summary>
        public static void Active(string jeu, Action<string, int> log)
        {
            Options o = Reglages;
            var fait = new List<string>();
            if (o.Raccourcis && CoupeRaccourcis()) fait.Add("raccourcis Maj (touches rémanentes, touches filtres) coupés");
            if (o.ToucheWindows)
            {
                if (DemarreCrochet(jeu)) fait.Add("touche Windows bloquée quand le jeu est devant");
                else if (log != null) log("Touche Windows : Windows a refusé le crochet clavier, elle reste active.", 2);
            }
            if (fait.Count > 0 && log != null) log("Clavier : " + string.Join(", ", fait) + ".", 1);
        }

        /// <summary>Sortie du Mode Jeu : tout est rendu, même si les options ont changé entre-temps.</summary>
        public static void Desactive(Action<string, int> log)
        {
            bool crochet = ArreteCrochet();
            bool rendus = RendRaccourcis();
            if ((crochet || rendus) && log != null)
                log("Clavier rendu : " + (crochet ? "touche Windows active" : "") + (crochet && rendus ? ", " : "")
                    + (rendus ? "raccourcis Maj rétablis" : "") + ".", 0);
        }

        /// <summary>Au lancement d'ONYX : un marqueur laissé par un plantage rend les raccourcis.</summary>
        public static bool Soigne(Action<string, int> log)
        {
            bool ok = RendRaccourcis();
            if (ok && log != null) log("Mode Jeu interrompu la dernière fois : raccourcis Maj rétablis.", 1);
            return ok;
        }

        // ------------------------------------------------------------------ Raccourcis Maj

        private const uint SPI_GETFILTERKEYS = 0x0032, SPI_SETFILTERKEYS = 0x0033;
        private const uint SPI_GETSTICKYKEYS = 0x003A, SPI_SETSTICKYKEYS = 0x003B;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, [In, Out] uint[] pvParam, uint fWinIni);

        // STICKYKEYS : cbSize, dwFlags. FILTERKEYS : cbSize, dwFlags, iWaitMSec, iDelayMSec, iRepeatMSec, iBounceMSec.
        private static uint[] Lit(uint get, int taille)
        {
            var s = new uint[taille];
            s[0] = (uint)(taille * 4);
            return SystemParametersInfo(get, s[0], s, 0) ? s : null;
        }

        private static bool Ecrit(uint set, uint[] s)
        {
            return SystemParametersInfo(set, s[0], s, 0);   // 0 : session seulement, profil intact
        }

        /// <summary>Flags actuels (pour le test réel) : rémanentes, filtres ; null si illisible.</summary>
        public static uint? DrapeauxRemanentes() { uint[] s = Lit(SPI_GETSTICKYKEYS, 2); return s != null ? s[1] : (uint?)null; }
        public static uint? DrapeauxFiltres() { uint[] s = Lit(SPI_GETFILTERKEYS, 6); return s != null ? s[1] : (uint?)null; }

#if BTTEST
        /// <summary>Test réel : pose des drapeaux pour la session seulement (profil intact).</summary>
        public static void PoseDrapeauxPourTest(uint? remanentes, uint? filtres)
        {
            uint[] st = Lit(SPI_GETSTICKYKEYS, 2), fk = Lit(SPI_GETFILTERKEYS, 6);
            if (remanentes != null && st != null) { st[1] = remanentes.Value; Ecrit(SPI_SETSTICKYKEYS, st); }
            if (filtres != null && fk != null) { fk[1] = filtres.Value; Ecrit(SPI_SETFILTERKEYS, fk); }
        }
#endif

        private static bool CoupeRaccourcis()
        {
            try
            {
                if (File.Exists(CheminEtat)) return false;   // déjà coupés (marqueur d'un plantage non soigné) : l'origine est déjà notée
                uint[] st = Lit(SPI_GETSTICKYKEYS, 2), fk = Lit(SPI_GETFILTERKEYS, 6);
                uint? r = st != null && (st[1] & Raccourci) != 0 ? st[1] : (uint?)null;
                uint? f = fk != null && (fk[1] & Raccourci) != 0 ? fk[1] : (uint?)null;
                if (r == null && f == null) return false;    // rien à couper : déjà réglé par l'utilisateur
                File.WriteAllText(CheminEtat, EcritEtat(r, f));   // l'origine d'abord, le changement ensuite
                bool ok = false;
                if (r != null) { st[1] = SansRaccourci(st[1]); ok |= Ecrit(SPI_SETSTICKYKEYS, st); }
                if (f != null) { fk[1] = SansRaccourci(fk[1]); ok |= Ecrit(SPI_SETFILTERKEYS, fk); }
                if (!ok) File.Delete(CheminEtat);
                return ok;
            }
            catch (Exception ex) { JournalTechnique.Echec("ClavierJeu.CoupeRaccourcis", ex); return false; }
        }

        private static bool RendRaccourcis()
        {
            try
            {
                if (!File.Exists(CheminEtat)) return false;
                uint? r, f;
                LitEtat(File.ReadAllText(CheminEtat), out r, out f);
                bool ok = false;
                if (r != null) { uint[] st = Lit(SPI_GETSTICKYKEYS, 2); if (st != null) { st[1] = Rendu(st[1], r.Value); ok |= Ecrit(SPI_SETSTICKYKEYS, st); } }
                if (f != null) { uint[] fk = Lit(SPI_GETFILTERKEYS, 6); if (fk != null) { fk[1] = Rendu(fk[1], f.Value); ok |= Ecrit(SPI_SETFILTERKEYS, fk); } }
                File.Delete(CheminEtat);
                return ok;
            }
            catch (Exception ex) { JournalTechnique.Echec("ClavierJeu.RendRaccourcis", ex); return false; }
        }

        // ------------------------------------------------------------------ Touche Windows

        private delegate IntPtr ProcClavier(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, ProcClavier fn, IntPtr hMod, uint threadId);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
        [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandle(string nom);

        private const int WH_KEYBOARD_LL = 13;
        private const uint WM_QUIT = 0x0012, LLKHF_INJECTED = 0x10, LLKHF_UP = 0x80;

        private static Thread _fil;
        private static uint _filId;
        private static IntPtr _crochet;
        private static ProcClavier _proc;      // gardé en champ : le ramasse-miettes ne doit pas le libérer
        private static Filtre _filtre;
        private static string _jeu;
        private static uint _dernierPid;
        private static string _dernierNom;

        /// <summary>Crochet en place (test réel).</summary>
        public static bool CrochetActif { get { return _crochet != IntPtr.Zero; } }

        private static bool DemarreCrochet(string jeu)
        {
            lock (_verrou)
            {
                if (_fil != null) return CrochetActif;
                _jeu = jeu; _filtre = new Filtre(); _dernierPid = 0; _dernierNom = null;
                _proc = Recoit;
                var pret = new ManualResetEventSlim(false);
                _fil = new Thread(delegate()
                {
                    _filId = GetCurrentThreadId();
                    _crochet = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
                    pret.Set();
                    if (_crochet == IntPtr.Zero) return;
                    MSG m;
                    while (GetMessage(out m, IntPtr.Zero, 0, 0) > 0) { }
                    UnhookWindowsHookEx(_crochet);
                    _crochet = IntPtr.Zero;
                }) { IsBackground = true, Name = "ONYX clavier en jeu" };
                _fil.Start();
                pret.Wait(2000);
                if (CrochetActif) return true;
                _fil = null;
                return false;
            }
        }

        private static bool ArreteCrochet()
        {
            lock (_verrou)
            {
                if (_fil == null) return false;
                try { PostThreadMessage(_filId, WM_QUIT, IntPtr.Zero, IntPtr.Zero); _fil.Join(2000); } catch { }
                _fil = null;
                return true;
            }
        }

        private static IntPtr Recoit(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0)
                {
                    var k = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                    int vk = (int)k.vkCode;
                    if (vk == VkWinGauche || vk == VkWinDroite)
                    {
                        bool relache = (k.flags & LLKHF_UP) != 0;
                        bool devant = !relache && JeuDevantMaintenant();   // au relâchement, seul l'appui compte
                        if (_filtre.Bloque(vk, relache, (k.flags & LLKHF_INJECTED) != 0, devant)) return new IntPtr(1);
                    }
                }
            }
            catch { }   // jamais d'exception vers Windows : la frappe passerait mal
            return CallNextHookEx(_crochet, nCode, wParam, lParam);
        }

        /// <summary>Appelé seulement sur une touche Windows : quelques appels système, pas de liste de processus.</summary>
        private static bool JeuDevantMaintenant()
        {
            IntPtr h = GetForegroundWindow();
            if (h == IntPtr.Zero) return false;
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            if (pid == 0) return false;
            if (pid != _dernierPid)
            {
                string nom = null;
                try { using (Process p = Process.GetProcessById((int)pid)) nom = p.ProcessName; } catch { }
                _dernierPid = pid; _dernierNom = nom;
            }
            bool plein = false;
            if (string.IsNullOrEmpty(_jeu))
            {
                RECT r;
                var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
                IntPtr mon = MonitorFromWindow(h, 2 /* MONITOR_DEFAULTTONEAREST */);
                if (GetWindowRect(h, out r) && mon != IntPtr.Zero && GetMonitorInfo(mon, ref mi))
                    plein = r.Left <= mi.rcMonitor.Left && r.Top <= mi.rcMonitor.Top && r.Right >= mi.rcMonitor.Right && r.Bottom >= mi.rcMonitor.Bottom;
            }
            return JeuDevant(_dernierNom, _jeu, plein);
        }
    }
}
