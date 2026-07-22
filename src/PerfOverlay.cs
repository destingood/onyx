using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    // ----------------------------------------------------------------------
    //  Overlay de performances EN JEU : FPS (ETW, façon PresentMon) + CPU /
    //  GPU / RAM (capteurs en-process) affichés dans un coin de l'écran,
    //  par-dessus les jeux fenêtrés / sans bordure. Comme MSI Afterburner /
    //  RTSS, mais 100 % NATIF : aucune injection dans le jeu (compatible
    //  anticheat), aucun pilote. Réglages persistés dans bt-overlay.txt.
    // ----------------------------------------------------------------------

    /// <summary>Réglages de l'overlay de performances.</summary>
    internal class PerfOverlaySettings
    {
        public int Corner = 1;          // 0 haut-gauche · 1 haut-droite · 2 bas-gauche · 3 bas-droite
        public int TextPt = 11;         // taille du texte (8..20 pt)
        public int Opacity = 85;        // 30..100 %
        public bool ShowFps = true;     // FPS + frametime (ETW, admin requis)
        public bool ShowCpu = true;     // charge + température CPU
        public bool ShowGpu = true;     // charge + température GPU
        public bool ShowRam = false;    // RAM utilisée / totale
        public bool GameOnly = false;   // n'afficher qu'en jeu plein écran / sans bordure
        public bool Enabled = false;    // affiché au démarrage

        public string ConfigPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-overlay.txt"); }
        }

        public void Save()
        {
            try
            {
                string s = string.Join(";", new string[]
                {
                    Corner.ToString(CultureInfo.InvariantCulture),
                    TextPt.ToString(CultureInfo.InvariantCulture),
                    Opacity.ToString(CultureInfo.InvariantCulture),
                    ShowFps ? "1" : "0",
                    ShowCpu ? "1" : "0",
                    ShowGpu ? "1" : "0",
                    ShowRam ? "1" : "0",
                    GameOnly ? "1" : "0",
                    Enabled ? "1" : "0"
                });
                File.WriteAllText(ConfigPath, s);
            }
            catch { }
        }

        public static PerfOverlaySettings Load()
        {
            var c = new PerfOverlaySettings();
            try
            {
                string path = c.ConfigPath;
                if (!File.Exists(path)) return c;
                string[] p = File.ReadAllText(path).Trim().Split(';');
                int v;
                if (p.Length > 0 && int.TryParse(p[0], out v)) c.Corner = Math.Max(0, Math.Min(3, v));
                if (p.Length > 1 && int.TryParse(p[1], out v)) c.TextPt = Math.Max(8, Math.Min(20, v));
                if (p.Length > 2 && int.TryParse(p[2], out v)) c.Opacity = Math.Max(30, Math.Min(100, v));
                if (p.Length > 3) c.ShowFps = p[3] == "1";
                if (p.Length > 4) c.ShowCpu = p[4] == "1";
                if (p.Length > 5) c.ShowGpu = p[5] == "1";
                if (p.Length > 6) c.ShowRam = p[6] == "1";
                if (p.Length > 7) c.GameOnly = p[7] == "1";
                if (p.Length > 8) c.Enabled = p[8] == "1";
            }
            catch { }
            return c;
        }
    }

    /// <summary>Gestion globale de l'overlay de performances (singleton).</summary>
    internal static class PerfOverlay
    {
        private static PerfOverlayWindow _win;

        public static bool IsVisible
        {
            get { return _win != null && !_win.IsDisposed && _win.Visible; }
        }

        public static void Show(PerfOverlaySettings s)
        {
            if (_win == null || _win.IsDisposed)
                _win = new PerfOverlayWindow();
            _win.ApplySettings(s);
            if (!_win.Visible) _win.Show();
        }

        public static void Update(PerfOverlaySettings s)
        {
            if (_win != null && !_win.IsDisposed) _win.ApplySettings(s);
        }

        /// <summary>Masque ET libère tout (session ETW, compteurs) — l'overlay ne coûte plus rien.</summary>
        public static void Hide()
        {
            if (_win == null) return;
            try { if (!_win.IsDisposed) { _win.Hide(); _win.Close(); _win.Dispose(); } }
            catch { }
            _win = null;
        }

        /// <summary>Bascule on/off (raccourci global Ctrl+Alt+O) ; persiste l'état.</summary>
        public static bool Toggle(Action<string, int> log)
        {
            var s = PerfOverlaySettings.Load();
            if (IsVisible)
            {
                Hide();
                s.Enabled = false; s.Save();
                if (log != null) log("Overlay perfs masqué (Ctrl+Alt+O).", 0);
                return false;
            }
            Show(s);
            s.Enabled = true; s.Save();
            if (log != null) log("Overlay perfs affiché (Ctrl+Alt+O pour masquer).", 1);
            return true;
        }

        /// <summary>Affiche l'overlay au démarrage si l'utilisateur l'avait activé.</summary>
        public static void ShowOnStartupIfEnabled(Action<string, int> log)
        {
            var s = PerfOverlaySettings.Load();
            if (!s.Enabled) return;
            try { Show(s); if (log != null) log("Overlay perfs en jeu affiché (activé dans les réglages).", 0); }
            catch { }
        }
    }

    /// <summary>
    /// Fenêtre overlay : petite boîte sombre, topmost, transparente et « click-through »,
    /// posée dans un coin de l'écran principal. Les mesures (FPS via ETW, CPU/GPU/RAM via
    /// capteurs en-process) sont prises sur un THREAD DE FOND avec garde anti-réentrance,
    /// puis marshalées vers l'UI — zéro freeze, zéro coût pour le jeu.
    /// </summary>
    internal class PerfOverlayWindow : Form
    {
        private PerfOverlaySettings _s = new PerfOverlaySettings();
        private Timer _tick;                 // relevé toutes les secondes
        private Timer _topmostKeeper;        // certains jeux repassent devant : on se remet topmost
        private HwMonitor _mon;              // CPU/RAM/GPU (PDH + LibreHardwareMonitor en-process)
        private FpsEtw _fps;                 // session ETW dédiée (n'entre pas en conflit avec le panneau FPS)
        private bool _fpsTried, _fpsOk;
        private volatile bool _busy;         // anti-réentrance de l'échantillonnage
        private bool _inGame = true;         // mode « en jeu seulement » : dernier verdict plein écran

        private Font _fontMain, _fontSmall;  // recréées quand la taille change, libérées à la fermeture

        // Dernier relevé (rempli côté thread de fond, lu côté UI après marshaling).
        private class Data
        {
            public double Fps = -1, FrameMs = -1;
            public string Game = null;
            public double CpuLoad = -1, CpuTemp = double.NaN;
            public double GpuLoad = -1, GpuTemp = double.NaN;
            public long RamUsedMB = -1, RamTotalMB = -1;
        }
        private Data _d = new Data();

        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_TOOLWINDOW = 0x80;
        private const int WS_EX_NOACTIVATE = 0x8000000;
        private const int WS_EX_TOPMOST = 0x8;

        private const int Pad = 10;          // marges internes de la boîte
        private const int Margin = 14;       // distance au bord de l'écran

        public PerfOverlayWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Magenta;        // couleur « magique » rendue transparente
            TransparencyKey = Color.Magenta;
            DoubleBuffered = true;
            Bounds = new Rectangle(-2000, -2000, 10, 10);   // hors écran tant que rien n'est mesuré

            _topmostKeeper = new Timer();
            _topmostKeeper.Interval = 3000;
            _topmostKeeper.Tick += (s, e) => { try { if (Visible) TopMost = true; } catch { } };
            _topmostKeeper.Start();

            _tick = new Timer();
            _tick.Interval = 1000;
            _tick.Tick += OnTick;
            _tick.Start();
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
                return cp;
            }
        }

        public void ApplySettings(PerfOverlaySettings s)
        {
            _s = s;
            double o = s.Opacity; if (o < 30) o = 30; if (o > 100) o = 100;
            Opacity = o / 100.0;
            RebuildFonts();
            Relayout();
            Invalidate();
        }

        private void RebuildFonts()
        {
            float pt = Math.Max(8, Math.Min(20, _s.TextPt));
            Font old1 = _fontMain, old2 = _fontSmall;
            _fontMain = new Font("Segoe UI Semibold", pt);
            _fontSmall = new Font("Segoe UI", Math.Max(7f, pt - 2.5f));
            if (old1 != null) old1.Dispose();
            if (old2 != null) old2.Dispose();
        }

        // ------------------------------------------------------------------
        //  Échantillonnage (thread de fond, garde anti-réentrance)
        // ------------------------------------------------------------------
        private void OnTick(object sender, EventArgs e)
        {
            if (_busy) return;
            _busy = true;
            Task.Run(delegate ()
            {
                Data d = null;
                bool inGame = true;
                try
                {
                    if (_s.GameOnly)
                        try { inGame = Native.IsGameFullscreen(); } catch { inGame = true; }
                    d = SampleOnce(inGame);
                }
                catch { d = null; }
                try
                {
                    BeginInvoke((Action)delegate ()
                    {
                        _busy = false;
                        if (IsDisposed) return;
                        _inGame = inGame;
                        if (d != null) _d = d;
                        Relayout();
                        Invalidate();
                    });
                }
                catch { _busy = false; }   // fenêtre détruite pendant la mesure
            });
        }

        /// <summary>Prend un relevé complet. inGame=false et mode « en jeu seulement » : relevé allégé.</summary>
        private Data SampleOnce(bool inGame)
        {
            var d = new Data();
            if (_s.GameOnly && !inGame) return d;   // rien d'affiché : inutile de mesurer

            // FPS (ETW) — session dédiée, démarrée au premier besoin.
            if (_s.ShowFps)
            {
                if (!_fpsTried)
                {
                    _fpsTried = true;
                    try { _fps = new FpsEtw("BTOptimizer-FPS-Overlay"); _fpsOk = _fps.Start(); }
                    catch { _fpsOk = false; }
                }
                if (_fpsOk && _fps != null)
                {
                    List<FpsEtw.ProcStat> stats = _fps.Snapshot(1000);
                    if (stats.Count > 0)
                    {
                        // Priorité au jeu au premier plan ; sinon le plus gros présenteur.
                        FpsEtw.ProcStat pick = stats[0];
                        int fgPid = ForegroundPid();
                        if (fgPid > 0)
                            foreach (FpsEtw.ProcStat st in stats)
                                if (st.Pid == fgPid) { pick = st; break; }
                        d.Fps = pick.Fps;
                        d.FrameMs = pick.AvgMs;
                        d.Game = pick.Name;
                    }
                }
            }

            // CPU / RAM / GPU — HwMonitor persistant (les compteurs PDH ont besoin de continuité).
            if (_s.ShowCpu || _s.ShowGpu || _s.ShowRam)
            {
                if (_mon == null) _mon = new HwMonitor();
                HwSample hs = _mon.Sample();
                d.CpuLoad = hs.CpuLoad;
                d.CpuTemp = hs.CpuTempC;
                d.RamUsedMB = hs.RamUsedMB;
                d.RamTotalMB = hs.RamTotalMB;
                if (hs.Gpu != null && hs.Gpu.Ok)
                {
                    d.GpuLoad = hs.Gpu.Util;
                    d.GpuTemp = hs.Gpu.TempC;
                }
            }
            return d;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        private static int ForegroundPid()
        {
            try
            {
                IntPtr h = GetForegroundWindow();
                if (h == IntPtr.Zero) return 0;
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                return (int)pid;
            }
            catch { return 0; }
        }

        // ------------------------------------------------------------------
        //  Lignes affichées + mise en page
        // ------------------------------------------------------------------
        private class Line
        {
            public string Text;
            public Color Color;
            public bool Small;
            public Line(string t, Color c, bool small) { Text = t; Color = c; Small = small; }
        }

        private static readonly Color ColText = Color.FromArgb(230, 232, 236);
        private static readonly Color ColDim  = Color.FromArgb(150, 156, 166);
        private static readonly Color ColGood = Color.FromArgb(0, 230, 118);
        private static readonly Color ColWarn = Color.FromArgb(235, 180, 60);
        private static readonly Color ColBad  = Color.FromArgb(255, 90, 80);

        private List<Line> BuildLines()
        {
            var lines = new List<Line>();
            Data d = _d;

            if (_s.ShowFps)
            {
                if (d.Fps >= 0)
                {
                    Color c = d.Fps >= 120 ? ColGood : (d.Fps >= 60 ? ColWarn : ColBad);
                    lines.Add(new Line(d.Fps.ToString("0") + " FPS  ·  " + d.FrameMs.ToString("0.0") + " ms", c, false));
                    if (!string.IsNullOrEmpty(d.Game))
                        lines.Add(new Line(d.Game, ColDim, true));
                }
                else
                {
                    string why = _fpsTried && !_fpsOk ? "FPS : n/d (admin requis)" : "FPS : en attente d'images…";
                    lines.Add(new Line(why, ColDim, true));
                }
            }
            if (_s.ShowCpu && d.CpuLoad >= 0)
            {
                string t = "CPU  " + d.CpuLoad.ToString("0") + " %";
                Color c = ColText;
                if (!double.IsNaN(d.CpuTemp))
                {
                    t += "   " + d.CpuTemp.ToString("0") + " °C";
                    c = d.CpuTemp >= 90 ? ColBad : (d.CpuTemp >= 80 ? ColWarn : ColText);
                }
                lines.Add(new Line(t, c, false));
            }
            if (_s.ShowGpu && d.GpuLoad >= 0)
            {
                string t = "GPU  " + d.GpuLoad.ToString("0") + " %";
                Color c = ColText;
                if (!double.IsNaN(d.GpuTemp) && d.GpuTemp > 0)
                {
                    t += "   " + d.GpuTemp.ToString("0") + " °C";
                    c = d.GpuTemp >= 85 ? ColBad : (d.GpuTemp >= 75 ? ColWarn : ColText);
                }
                lines.Add(new Line(t, c, false));
            }
            if (_s.ShowRam && d.RamTotalMB > 0)
            {
                lines.Add(new Line(
                    "RAM  " + (d.RamUsedMB / 1024.0).ToString("0.0") + " / " + (d.RamTotalMB / 1024.0).ToString("0") + " Go",
                    ColText, false));
            }
            return lines;
        }

        /// <summary>Recalcule la taille de la boîte et sa position (coin choisi de l'écran principal).</summary>
        private void Relayout()
        {
            if (_fontMain == null) RebuildFonts();

            if (_s.GameOnly && !_inGame)
            {
                // Rien à afficher : on gare la fenêtre hors écran (elle reste invisible et gratuite).
                Bounds = new Rectangle(-2000, -2000, 10, 10);
                return;
            }

            List<Line> lines = BuildLines();
            int w = 0, h = Pad * 2;
            foreach (Line l in lines)
            {
                Size sz = TextRenderer.MeasureText(l.Text, l.Small ? _fontSmall : _fontMain);
                if (sz.Width > w) w = sz.Width;
                h += sz.Height + 2;
            }
            if (lines.Count == 0) { Bounds = new Rectangle(-2000, -2000, 10, 10); return; }
            w += Pad * 2;

            Rectangle scr;
            try { scr = Screen.PrimaryScreen.Bounds; }
            catch { scr = new Rectangle(0, 0, 1920, 1080); }

            int x = (_s.Corner == 0 || _s.Corner == 2) ? scr.Left + Margin : scr.Right - Margin - w;
            int y = (_s.Corner == 0 || _s.Corner == 1) ? scr.Top + Margin : scr.Bottom - Margin - h;
            Bounds = new Rectangle(x, y, w, h);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_s.GameOnly && !_inGame) return;   // invisible (tout est magenta → transparent)
            List<Line> lines = BuildLines();
            if (lines.Count == 0) return;

            Graphics g = e.Graphics;
            var rf = new RectangleF(0.5f, 0.5f, ClientSize.Width - 1f, ClientSize.Height - 1f);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Theme.RoundPath(rf, 8f))
            {
                using (var br = new SolidBrush(Color.FromArgb(24, 26, 32))) g.FillPath(br, path);
                using (var pen = new Pen(Color.FromArgb(60, 64, 74))) g.DrawPath(pen, path);
            }
            g.SmoothingMode = SmoothingMode.None;

            int y = Pad;
            foreach (Line l in lines)
            {
                Font f = l.Small ? _fontSmall : _fontMain;
                TextRenderer.DrawText(g, l.Text, f, new Point(Pad, y), l.Color);
                y += TextRenderer.MeasureText(l.Text, f).Height + 2;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Timers d'abord : plus aucun tick pendant la teardown.
            try { if (_tick != null) { _tick.Stop(); _tick.Dispose(); _tick = null; } } catch { }
            try { if (_topmostKeeper != null) { _topmostKeeper.Stop(); _topmostKeeper.Dispose(); _topmostKeeper = null; } } catch { }
            try { if (_fps != null) { _fps.Dispose(); _fps = null; } } catch { }
            try { if (_mon != null) { _mon.Dispose(); _mon = null; } } catch { }
            try { if (_fontMain != null) { _fontMain.Dispose(); _fontMain = null; } } catch { }
            try { if (_fontSmall != null) { _fontSmall.Dispose(); _fontSmall = null; } } catch { }
            base.OnFormClosing(e);
        }
    }
}
