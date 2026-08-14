using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    // ----------------------------------------------------------------------
    //  Overlay de stats in-game : petite fenêtre topmost "click-through" qui
    //  affiche CPU / GPU / températures / RAM en direct par-dessus le jeu
    //  (fenêtré / sans bordure), façon Afterburner. Réutilise le pattern
    //  overlay du viseur (Crosshair) + le monitoring matériel (HwMonitor).
    // ----------------------------------------------------------------------

    internal class StatsOverlaySettings
    {
        public bool Enabled = false;
        public int Corner = 1;   // 0 haut-gauche · 1 haut-droit · 2 bas-gauche · 3 bas-droit

        public string ConfigPath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-overlay.txt"); } }

        public void Save() { try { File.WriteAllText(ConfigPath, (Enabled ? "1" : "0") + ";" + Corner); } catch { } }

        public static StatsOverlaySettings Load()
        {
            var s = new StatsOverlaySettings();
            try
            {
                if (!File.Exists(s.ConfigPath)) return s;
                string[] p = File.ReadAllText(s.ConfigPath).Trim().Split(';');
                if (p.Length > 0) s.Enabled = p[0] == "1";
                int v; if (p.Length > 1 && int.TryParse(p[1], out v)) s.Corner = v;
            }
            catch { }
            return s;
        }
    }

    /// <summary>Gestion globale de l'overlay de stats (singleton).</summary>
    internal static class StatsOverlayManager
    {
        private static StatsOverlayWindow _win;

        public static bool IsVisible { get { return _win != null && !_win.IsDisposed && _win.Visible; } }

        public static void Show(StatsOverlaySettings s)
        {
            if (_win == null || _win.IsDisposed) _win = new StatsOverlayWindow();
            _win.SetCorner(s.Corner);
            if (!_win.Visible) _win.Show();
            _win.BeginSampling();
        }

        public static void Hide() { if (_win != null && !_win.IsDisposed) _win.Hide(); }

        public static void Toggle(StatsOverlaySettings s) { if (IsVisible) Hide(); else Show(s); }

        public static void ShowOnStartupIfEnabled(Action<string, int> log)
        {
            var s = StatsOverlaySettings.Load();
            if (!s.Enabled) return;
            try { Show(s); if (log != null) log("Overlay de stats affiché (activé dans les réglages).", 0); } catch { }
        }
    }

    /// <summary>Fenêtre overlay compacte, translucide, topmost et click-through.</summary>
    internal class StatsOverlayWindow : Form
    {
        private readonly HwMonitor _mon = new HwMonitor();
        private readonly Timer _timer = new Timer();
        private HwSample _last;
        private bool _sampling;
        private int _corner = 1;
        private FpsEtw _fps;
        private double _lastFps = double.NaN;
        private double _lastLow = double.NaN;
        private string _fpsName;
        private readonly System.Collections.Generic.Queue<double> _fpsHist = new System.Collections.Generic.Queue<double>();
        private const int HistMax = 46;

        private const int WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80,
                          WS_EX_NOACTIVATE = 0x8000000, WS_EX_TOPMOST = 0x8;

        private static readonly Color Bg = Color.FromArgb(10, 12, 11);
        private static readonly Color Neon = FpsUi.Gold;
        private static readonly Color Ink = Color.FromArgb(235, 238, 236);
        private static readonly Color Dim = Color.FromArgb(150, 154, 150);

        public StatsOverlayWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Bg;
            Opacity = 0.82;
            DoubleBuffered = true;
            ClientSize = new Size(222, 178);
            try { Region = RoundedRegion(ClientRectangle, 12); } catch { }

            _timer.Interval = 1000;
            _timer.Tick += (s, e) => Sample();
            var keeper = new Timer { Interval = 3000 };
            keeper.Tick += (s, e) => { try { if (Visible) TopMost = true; } catch { } };
            keeper.Start();
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

        public void BeginSampling()
        {
            // Session ETW pour les FPS du jeu au premier plan (nécessite l'élévation ; échoue en
            // silence sinon → l'overlay affiche « FPS — » et garde les autres stats).
            if (_fps == null) { try { _fps = new FpsEtw(); _fps.Start(); } catch { } }
            Sample();
            if (!_timer.Enabled) _timer.Start();
        }

        /// <summary>Alimente l'overlay avec des données de démonstration (pour l'inspection
        /// visuelle hors jeu). Fige l'échantillonnage pour que le rendu reste stable.</summary>
        internal void SeedDemo()
        {
            try { _timer.Stop(); } catch { }
            _lastFps = 144; _lastLow = 118; _fpsName = "Counter-Strike 2";
            _last = new HwSample { CpuLoad = 38, RamLoad = 46, CpuTempC = 58 };
            _last.Gpu = new GpuInfo { Ok = true, Util = 72, TempC = 64 };
            _fpsHist.Clear();
            int[] demo = { 120, 132, 140, 138, 144, 150, 146, 142, 139, 148, 152, 144, 141, 137, 145, 150, 148, 144, 142, 146, 151, 149, 143, 140, 147, 152, 146, 141 };
            foreach (int v in demo) _fpsHist.Enqueue(v);
            Invalidate();
        }

        public void SetCorner(int corner)
        {
            _corner = corner;
            try
            {
                var wa = Screen.PrimaryScreen.WorkingArea;
                int m = 16;
                int x = (corner == 1 || corner == 3) ? wa.Right - Width - m : wa.Left + m;
                int y = (corner == 2 || corner == 3) ? wa.Bottom - Height - m : wa.Top + m;
                Location = new Point(x, y);
            }
            catch { }
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible) { try { _timer.Stop(); } catch { } }
        }

        protected override void OnHandleDestroyed(EventArgs e) { try { _timer.Stop(); _mon.Dispose(); if (_fps != null) _fps.Dispose(); } catch { } base.OnHandleDestroyed(e); }

        private void Sample()
        {
            if (_sampling) return;
            _sampling = true;
            Task.Run(() =>
            {
                HwSample s = null;
                try { s = _mon.Sample(); } catch { }
                double fps = double.NaN, low = double.NaN; string fname = null;
                try
                {
                    if (_fps != null && _fps.Running)
                    {
                        var ps = _fps.Snapshot(1000);
                        // Le jeu est la fenêtre au premier plan — PAS le processus le plus rapide.
                        // Un navigateur accéléré dépasse un jeu bridé à 60 Hz, et l'overlay
                        // affichait alors le débit d'images de Chrome pendant la partie.
                        var jeu = FpsEtw.ChoisirJeu(ps, FpsEtw.PidPremierPlan());
                        if (jeu != null) { fps = jeu.Fps; low = jeu.OnePctLowFps; fname = jeu.Name; }
                    }
                }
                catch { }
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        _last = s; _lastFps = fps; _lastLow = low; _fpsName = fname;
                        if (!double.IsNaN(fps)) { _fpsHist.Enqueue(fps); while (_fpsHist.Count > HistMax) _fpsHist.Dequeue(); }
                        _sampling = false; Invalidate();
                    }));
                }
                catch { _sampling = false; }
            });
        }

        private static Region RoundedRegion(Rectangle r, int radius)
        {
            using (var p = new GraphicsPath())
            {
                int d = radius * 2;
                p.AddArc(r.X, r.Y, d, d, 180, 90);
                p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                p.CloseFigure();
                return new Region(p);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            var r = ClientRectangle;

            // Liseré néon fin.
            using (var pen = new Pen(FpsUi.Accent(70))) g.DrawRectangle(pen, 0, 0, r.Width - 1, r.Height - 1);

            // En-tête : marque + nom du jeu détecté (à droite).
            using (var hf = new Font("Segoe UI Semibold", 8f))
            {
                TextRenderer.DrawText(g, "ONYX", hf, new Point(12, 7), Neon, TextFormatFlags.NoPadding);
                if (!string.IsNullOrEmpty(_fpsName))
                {
                    string nm = _fpsName; if (nm.Length > 18) nm = nm.Substring(0, 17) + "…";
                    TextRenderer.DrawText(g, nm, hf, new Rectangle(90, 7, r.Width - 98, 14), Dim,
                        TextFormatFlags.Right | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                }
            }

            // FPS (métrique héros).
            string fv = double.IsNaN(_lastFps) ? "—" : _lastFps.ToString("0");
            Color fc = double.IsNaN(_lastFps) ? Dim : _lastFps >= 100 ? Neon : _lastFps >= 55 ? Color.FromArgb(230, 175, 45) : Color.FromArgb(232, 84, 74);
            using (var big = new Font("Segoe UI", 26f, FontStyle.Bold))
            using (var unit = new Font("Segoe UI Semibold", 11f))
            using (var lowLab = new Font("Segoe UI", 7.5f))
            using (var lowVal = new Font("Segoe UI Semibold", 13f))
            {
                var sz = TextRenderer.MeasureText(g, fv, big, Size.Empty, TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, fv, big, new Point(10, 20), fc, TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, "FPS", unit, new Point(16 + sz.Width, 40), fc, TextFormatFlags.NoPadding);
                // 1% low : indicateur de micro-saccades (le plus parlant en compétitif).
                if (!double.IsNaN(_lastLow) && _lastLow > 0)
                {
                    Color lc = _lastLow >= 80 ? Neon : _lastLow >= 45 ? Color.FromArgb(230, 175, 45) : Color.FromArgb(232, 84, 74);
                    TextRenderer.DrawText(g, "1% LOW", lowLab, new Rectangle(r.Width - 80, 24, 68, 12), Dim, TextFormatFlags.Right | TextFormatFlags.NoPadding);
                    TextRenderer.DrawText(g, _lastLow.ToString("0"), lowVal, new Rectangle(r.Width - 80, 36, 68, 22), lc, TextFormatFlags.Right | TextFormatFlags.NoPadding);
                }
            }
            using (var pen = new Pen(Color.FromArgb(40, 43, 41))) g.DrawLine(pen, 12, 64, r.Width - 12, 64);

            HwSample s = _last;
            int y = 72;
            using (var lab = new Font("Segoe UI", 8.5f))
            using (var val = new Font("Segoe UI Semibold", 10.5f))
            {
                Row(g, lab, val, ref y, "CPU", s == null || s.CpuLoad < 0 ? "—" : s.CpuLoad.ToString("0") + " %",
                    s != null && !double.IsNaN(s.CpuTempC) ? s.CpuTempC.ToString("0") + "°" : null,
                    s == null ? Dim : LoadColor(s.CpuLoad));
                double gu = s != null && s.Gpu != null && s.Gpu.Ok ? s.Gpu.Util : double.NaN;
                double gt = s != null && s.Gpu != null && s.Gpu.Ok ? s.Gpu.TempC : double.NaN;
                Row(g, lab, val, ref y, "GPU", double.IsNaN(gu) ? "—" : gu.ToString("0") + " %",
                    double.IsNaN(gt) ? null : gt.ToString("0") + "°", double.IsNaN(gu) ? Dim : LoadColor(gu));
                Row(g, lab, val, ref y, "RAM", s == null ? "—" : s.RamLoad.ToString("0") + " %", null, s == null ? Dim : LoadColor(s.RamLoad));
            }

            // Mini-historique des FPS (sparkline).
            if (_fpsHist.Count >= 2)
            {
                var plot = new Rectangle(12, r.Height - 22, r.Width - 24, 14);
                double[] arr = _fpsHist.ToArray();
                double max = 1; foreach (double v in arr) if (v > max) max = v;
                var pts = new PointF[arr.Length];
                for (int i = 0; i < arr.Length; i++)
                {
                    float px = plot.Left + (float)i / (HistMax - 1) * plot.Width;
                    float py = plot.Bottom - (float)(arr[i] / max) * plot.Height;
                    pts[i] = new PointF(px, py);
                }
                using (var pen = new Pen(Color.FromArgb(190, FpsUi.Gold), 1.4f)) g.DrawLines(pen, pts);
            }
        }

        private void Row(Graphics g, Font lab, Font val, ref int y, string name, string value, string temp, Color vc)
        {
            TextRenderer.DrawText(g, name, lab, new Point(12, y + 2), Dim, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, value, val, new Rectangle(48, y, 70, 22), vc, TextFormatFlags.Right | TextFormatFlags.NoPadding);
            if (temp != null)
                TextRenderer.DrawText(g, temp, val, new Rectangle(130, y, 66, 22), TempColor(temp), TextFormatFlags.Right | TextFormatFlags.NoPadding);
            y += 28;
        }

        private static Color LoadColor(double v)
        {
            if (double.IsNaN(v)) return Dim;
            return v < 60 ? Neon : v < 85 ? Color.FromArgb(230, 175, 45) : Color.FromArgb(232, 84, 74);
        }

        private static Color TempColor(string t)
        {
            int v; if (!int.TryParse(t.Replace("°", ""), out v)) return Ink;
            return v < 70 ? Neon : v < 84 ? Color.FromArgb(230, 175, 45) : Color.FromArgb(232, 84, 74);
        }
    }
}
