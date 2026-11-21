using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    // Centre de contrôle : actions Windows, mesures réelles et profil gaming.
    internal class PageDashboard : FpsPage
    {
        internal const string SupportUrl = "https://fluide.gumroad.com";
        private static readonly Font HeroFont = FpsUi.F(25f, true);
        private readonly HwMonitor _monitor = new HwMonitor();
        private readonly Timer _timer = new Timer { Interval = 1500 };
        private readonly BufferedPanel _scroll = new BufferedPanel(), _content = new BufferedPanel();
        private readonly BufferedPanel _hero = new BufferedPanel(), _graph = new BufferedPanel(), _configuration = new BufferedPanel();
        private readonly BufferedPanel[] _metrics = new BufferedPanel[3];
        private readonly Queue<double>[] _history = { new Queue<double>(), new Queue<double>(), new Queue<double>() };
        private readonly Color[] _series = { FpsUi.Gold, FpsUi.GraphSecondary, FpsUi.Ok };
        private readonly string[] _names = { "PROCESSEUR", "CARTE GRAPHIQUE", "MÉMOIRE RAM" };
        private readonly double[] _values = { double.NaN, double.NaN, double.NaN };
        private readonly Button _optimize, _gaming, _settings;
        private readonly DashboardShortcut[] _shortcuts;
        private ScrollWheelFilter _wheel;
        private bool _sampling, _disposed;
        private AppStats.Snap _stats;
        private HwSample _sample;
        private const int Samples = 40;

        public PageDashboard(DashboardForm host) : base(host)
        {
            _scroll.Name = "dashboard-scroll";
            _scroll.AutoScroll = true;
            _scroll.BackColor = _content.BackColor = FpsUi.BgMain;
            Controls.Add(_scroll); _scroll.Controls.Add(_content);
            _wheel = new ScrollWheelFilter(_scroll); Application.AddMessageFilter(_wheel);
            _hero.BackColor = _graph.BackColor = _configuration.BackColor = FpsUi.BgMain;
            _hero.Paint += PaintHero; _graph.Paint += PaintGraph; _configuration.Paint += PaintConfiguration;
            _content.Controls.AddRange(new Control[] { _hero, _graph, _configuration });
            _optimize = FpsUi.PrimaryButton("Optimiser Windows  →");
            _optimize.Name = "dashboard-optimize";
            _optimize.Click += (s, e) => Host.Goto(1);
            _gaming = FpsUi.GoldButton("Configurer mon profil gaming");
            _gaming.Click += (s, e) => Host.OpenDialog(new CompetitionForm(Catalog.All()));
            _hero.Controls.AddRange(new Control[] { _optimize, _gaming });
            _settings = FpsUi.GhostButton("Parcourir les réglages  →");
            _settings.Click += (s, e) => Host.Goto(1); _configuration.Controls.Add(_settings);
            for (int i = 0; i < 3; i++)
            {
                int metric = i;
                var panel = new BufferedPanel { BackColor = FpsUi.BgMain };
                panel.Paint += (s, e) => PaintMetric(e.Graphics, panel.ClientRectangle, metric);
                _metrics[i] = panel; _content.Controls.Add(panel);
            }
            _shortcuts = new[]
            {
                new DashboardShortcut(NavIcons.Stockage, "Nettoyer Windows", "Disques, caches et fichiers", () => Host.Goto(8)),
                new DashboardShortcut(NavIcons.Optimisations, "Réduire la latence", "Souris, écran et chaîne d'entrée", () => Host.OpenDialog(new ChaineEntreeForm(Host.Log))),
                new DashboardShortcut(NavIcons.Laboratoire, "Mesurer les performances", "FPS, benchmarks et stabilité", () => Host.Goto(4))
            };
            foreach (var shortcut in _shortcuts) _content.Controls.Add(shortcut);
            _timer.Tick += (s, e) => Sample();
            Resize += (s, e) => DoLayout();
        }

        public override void OnShown()
        {
            DoLayout(); _hero.Invalidate(); _configuration.Invalidate();
            AppStats.Get(stats =>
            {
                if (_disposed) return;
                try { BeginInvoke((Action)(() => { _stats = stats; _configuration.Invalidate(); })); } catch { }
            });
            Sample(); _timer.Start();
        }
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible && !_disposed) _timer.Stop();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true; _timer.Dispose();
                if (_wheel != null) { Application.RemoveMessageFilter(_wheel); _wheel = null; }
                // Si une lecture est en cours, son worker libère le moniteur à sa sortie.
                lock (_monitor) { if (!_sampling) _monitor.Dispose(); }
            }
            base.Dispose(disposing);
        }
        private void DoLayout()
        {
            _scroll.SetBounds(30, 92, Math.Max(300, Width - 46), Math.Max(120, Height - 108));
            int width = Math.Max(300, _scroll.Width - SystemInformation.VerticalScrollBarWidth - 1);
            int height = Math.Max(678, _scroll.Height);
            _content.Size = new Size(width, height);
            _hero.SetBounds(0, 0, width, 202);
            _optimize.SetBounds(24, 144, 196, 38); _gaming.SetBounds(232, 144, 236, 38);
            int cell = (width - 32) / 3;
            for (int i = 0; i < 3; i++) _metrics[i].SetBounds(i * (cell + 16), 218, cell, 96);
            int side = 280, chartHeight = height - 430;
            _graph.SetBounds(0, 330, width - side - 16, chartHeight);
            _configuration.SetBounds(width - side, 330, side, chartHeight);
            _settings.SetBounds(20, chartHeight - 52, side - 40, 32);
            for (int i = 0; i < 3; i++) _shortcuts[i].SetBounds(i * (cell + 16), height - 84, cell, 84);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Draw(e.Graphics, "Centre de contrôle", FpsUi.H1, FpsUi.Ink, new Rectangle(30, 20, Width - 60, 36));
            Draw(e.Graphics, "Windows, matériel et gaming : tout au même endroit.", FpsUi.Body, FpsUi.Dim, new Rectangle(31, 59, Width - 62, 23));
        }
        private void PaintHero(object sender, PaintEventArgs e)
        {
            var g = e.Graphics; var r = _hero.ClientRectangle;
            FpsUi.PaintCard(g, r, Color.FromArgb(32, 28, 21), Color.FromArgb(83, 69, 43), 14f);
            int split = Math.Max(510, r.Width - 300);
            using (var pen = new Pen(Color.FromArgb(38, FpsUi.Gold)))
                for (int i = 0; i < 5; i++)
                {
                    int x = split + i * 35;
                    g.DrawLines(pen, new[] { new Point(x, 1), new Point(x, 30 + i * 8), new Point(x + 36, 66 + i * 8), new Point(r.Width - 12, 66 + i * 8) });
                }
            Draw(g, "OPTIMISATION WINDOWS / GAMING", FpsUi.Small, FpsUi.Gold, new Rectangle(24, 18, split - 40, 22));
            Draw(g, "Ton PC. Tes performances.", HeroFont, FpsUi.Ink, new Rectangle(22, 44, split - 18, 48));
            Draw(g, "Maîtrise les réglages, l'activité en fond et la latence.", FpsUi.Body, FpsUi.Dim, new Rectangle(24, 102, split - 32, 25));
            if (r.Width - split >= 245)
            {
                var session = new Rectangle(split + 12, 26, r.Width - split - 36, 150);
                FpsUi.PaintCard(g, session, FpsUi.RailBg, FpsUi.Border, 12);
                NavIcons.Draw(g, NavIcons.Jeux, new RectangleF(session.X + 18, session.Y + 18, 34, 34), FpsUi.Gold);
                Draw(g, "MODE JEU", FpsUi.Small, FpsUi.Dim, new Rectangle(session.X + 66, session.Y + 18, session.Width - 78, 20));
                Draw(g, GameBoost.IsActive ? "Activé" : "En veille", FpsUi.H2, GameBoost.IsActive ? FpsUi.Ok : FpsUi.Ink,
                    new Rectangle(session.X + 66, session.Y + 40, session.Width - 78, 26));
                Draw(g, "Un profil dédié à tes sessions.\nÀ configurer selon tes jeux.", FpsUi.Body, FpsUi.Dim,
                    new Rectangle(session.X + 18, session.Y + 86, session.Width - 36, 48), true);
            }
        }
        private void PaintMetric(Graphics g, Rectangle r, int index)
        {
            FpsUi.PaintCard(g, r, FpsUi.Card, FpsUi.Border, 10f);
            using (var brush = new SolidBrush(_series[index])) g.FillRectangle(brush, 1, 20, 3, 54);
            Draw(g, _names[index], FpsUi.Tiny, FpsUi.Dim, new Rectangle(18, 12, r.Width - 36, 20));
            Draw(g, Percent(_values[index]), FpsUi.Num, FpsUi.Ink, new Rectangle(16, 32, 128, 40));
            string detail = index == 0 ? Environment.ProcessorCount + " processeurs logiques"
                : index == 1 ? (_sample != null && _sample.Gpu != null && _sample.Gpu.Ok ? _sample.Gpu.Name : "Capteur GPU")
                : _sample != null && _sample.RamTotalMB > 0 ? (_sample.RamUsedMB / 1024.0).ToString("0.0") + " / " + (_sample.RamTotalMB / 1024.0).ToString("0.0") + " Go" : "Lecture en cours…";
            Draw(g, detail, FpsUi.Tiny, FpsUi.Dim2, new Rectangle(18, 74, r.Width - 36, 18));
            DrawSeries(g, new Rectangle(r.Width - 120, 39, 100, 28), _history[index], _series[index]);
        }
        private void PaintConfiguration(object sender, PaintEventArgs e)
        {
            var g = e.Graphics; var r = _configuration.ClientRectangle;
            FpsUi.PaintCard(g, r, FpsUi.Card, FpsUi.Border, 12);
            Draw(g, "Configuration Windows", FpsUi.H3, FpsUi.Ink, new Rectangle(20, 18, r.Width - 40, 26));
            Draw(g, _stats == null ? "—" : _stats.OptiActive.ToString(), FpsUi.Num, FpsUi.Gold, new Rectangle(18, 52, 100, 42));
            Draw(g, _stats == null ? "Analyse en cours…" : "réglages actifs sur " + _stats.OptiTotal, FpsUi.Body, FpsUi.Dim,
                new Rectangle(20, 98, r.Width - 40, 24));
            int filled = _stats == null || _stats.OptiTotal == 0 ? 0 : (r.Width - 40) * _stats.OptiActive / _stats.OptiTotal;
            using (var b = new SolidBrush(FpsUi.Border)) g.FillRectangle(b, 20, 134, r.Width - 40, 4);
            if (filled > 0) using (var b = new SolidBrush(FpsUi.Gold)) g.FillRectangle(b, 20, 134, filled, 4);
            Draw(g, "Choisis les réglages adaptés à tes usages.", FpsUi.Small, FpsUi.Dim, new Rectangle(20, 151, r.Width - 40, 36), true);
        }
        private void PaintGraph(object sender, PaintEventArgs e)
        {
            var g = e.Graphics; var r = _graph.ClientRectangle;
            FpsUi.PaintCard(g, r, FpsUi.Card, FpsUi.Border, 12);
            Draw(g, "Charge système", FpsUi.H3, FpsUi.Ink, new Rectangle(20, 17, r.Width - 40, 24));
            Draw(g, "EN DIRECT · 60 S", FpsUi.Tiny, FpsUi.Dim, new Rectangle(r.Width - 136, 22, 116, 18));
            string[] legends = { "CPU", "GPU", "RAM" };
            for (int i = 0; i < 3; i++)
            {
                using (var b = new SolidBrush(_series[i])) g.FillEllipse(b, 20 + i * 86, 55, 6, 6);
                Draw(g, legends[i], FpsUi.Small, FpsUi.Dim, new Rectangle(34 + i * 86, 49, 60, 22));
            }
            var plot = new Rectangle(48, 88, Math.Max(10, r.Width - 68), Math.Max(20, r.Height - 122));
            for (int value = 0; value <= 100; value += 50)
            {
                int y = plot.Bottom - value * plot.Height / 100;
                using (var pen = new Pen(FpsUi.Border)) g.DrawLine(pen, plot.Left, y, plot.Right, y);
                Draw(g, value.ToString(), FpsUi.Tiny, FpsUi.Dim2, new Rectangle(12, y - 9, 30, 18));
            }
            for (int i = 0; i < 3; i++) DrawSeries(g, plot, _history[i], _series[i]);
            Draw(g, "−60 s", FpsUi.Tiny, FpsUi.Dim2, new Rectangle(plot.Left, plot.Bottom + 10, 80, 18));
            Draw(g, "Maintenant", FpsUi.Tiny, FpsUi.Dim2, new Rectangle(plot.Right - 66, plot.Bottom + 10, 76, 18));
        }
        private static void DrawSeries(Graphics g, Rectangle plot, Queue<double> queue, Color color)
        {
            var values = queue.ToArray(); g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(color, 1.8f))
                for (int i = 1; i < values.Length; i++)
                {
                    if (double.IsNaN(values[i - 1]) || double.IsNaN(values[i])) continue;
                    float x = plot.Left + (float)(Samples - values.Length + i - 1) / (Samples - 1) * plot.Width;
                    float next = plot.Left + (float)(Samples - values.Length + i) / (Samples - 1) * plot.Width;
                    g.DrawLine(pen, x, plot.Bottom - (float)Math.Clamp(values[i - 1], 0, 100) * plot.Height / 100,
                        next, plot.Bottom - (float)Math.Clamp(values[i], 0, 100) * plot.Height / 100);
                }
        }
        private void Sample()
        {
            lock (_monitor) { if (_sampling || _disposed) return; _sampling = true; }
            Task.Run(() =>
            {
                HwSample sample = null;
                try { sample = _monitor.Sample(); } catch { }
                lock (_monitor) { _sampling = false; if (_disposed) { _monitor.Dispose(); return; } }
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        if (_disposed) return;
                        _sample = sample;
                        _values[0] = sample != null && sample.CpuLoad >= 0 ? sample.CpuLoad : double.NaN;
                        _values[1] = sample != null && sample.Gpu != null && sample.Gpu.Ok ? sample.Gpu.Util : double.NaN;
                        _values[2] = sample != null && sample.RamTotalMB > 0 ? sample.RamLoad : double.NaN;
                        for (int i = 0; i < 3; i++)
                        {
                            _history[i].Enqueue(_values[i]);
                            while (_history[i].Count > Samples) _history[i].Dequeue();
                            _metrics[i].Invalidate();
                        }
                        _graph.Invalidate(); _hero.Invalidate();
                    }));
                }
                catch { }
            });
        }
        private static string Percent(double value) { return double.IsNaN(value) ? "—" : value.ToString("0") + " %"; }
        private static void Draw(Graphics g, string text, Font font, Color color, Rectangle rect, bool wrap = false)
        {
            TextRenderer.DrawText(g, text, font, rect, color, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix
                | TextFormatFlags.EndEllipsis | (wrap ? TextFormatFlags.WordBreak : TextFormatFlags.SingleLine));
        }
    }

    internal sealed class DashboardShortcut : Button
    {
        private readonly int _icon;
        private readonly string _description;
        private bool _hover;
        public DashboardShortcut(int icon, string title, string description, Action action)
        {
            _icon = icon; Text = title; _description = description; AccessibleName = title;
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; BackColor = FpsUi.BgMain; Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            MouseEnter += (s, e) => { _hover = true; Invalidate(); }; MouseLeave += (s, e) => { _hover = false; Invalidate(); };
            GotFocus += (s, e) => Invalidate(); LostFocus += (s, e) => Invalidate(); Click += (s, e) => action();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(FpsUi.BgMain);
            FpsUi.PaintCard(g, ClientRectangle, _hover ? FpsUi.CardHi : FpsUi.Card,
                _hover || (Focused && ShowFocusCues) ? FpsUi.GoldDim : FpsUi.Border, 10);
            NavIcons.Draw(g, _icon, new RectangleF(16, 18, 22, 22), FpsUi.Gold);
            TextRenderer.DrawText(g, Text, FpsUi.H3, new Rectangle(48, 18, Width - 58, 24), FpsUi.Ink,
                TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, _description, FpsUi.Small, new Rectangle(16, 51, Width - 32, 24), FpsUi.Dim,
                TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }
}
