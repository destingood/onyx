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

        private const int WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80,
                          WS_EX_NOACTIVATE = 0x8000000, WS_EX_TOPMOST = 0x8;

        private static readonly Color Bg = Color.FromArgb(10, 12, 11);
        private static readonly Color Neon = Color.FromArgb(0, 255, 136);
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
            ClientSize = new Size(212, 120);
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
            Sample();
            if (!_timer.Enabled) _timer.Start();
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

        protected override void OnHandleDestroyed(EventArgs e) { try { _timer.Stop(); _mon.Dispose(); } catch { } base.OnHandleDestroyed(e); }

        private void Sample()
        {
            if (_sampling) return;
            _sampling = true;
            Task.Run(() =>
            {
                HwSample s = null;
                try { s = _mon.Sample(); } catch { }
                try { BeginInvoke((Action)(() => { _last = s; _sampling = false; Invalidate(); })); } catch { _sampling = false; }
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
            using (var pen = new Pen(Color.FromArgb(70, 0, 255, 136))) g.DrawRectangle(pen, 0, 0, r.Width - 1, r.Height - 1);
            using (var f = new Font("Segoe UI Semibold", 8f))
                TextRenderer.DrawText(g, "DesTinGOOD", f, new Point(12, 8), Neon, TextFormatFlags.NoPadding);

            HwSample s = _last;
            int y = 30;
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
