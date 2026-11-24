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
        private static readonly Font HeroFont = FpsUi.F(30f, true);
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
            _optimize = OnyxDesign.PrimaryButton("Optimiser Windows  →");
            _optimize.Name = "dashboard-optimize";
            _optimize.Click += (s, e) => Host.Goto(1);
            _gaming = OnyxDesign.AccentButton("Configurer mon profil gaming");
            _gaming.Click += (s, e) => Host.OpenDialog(new CompetitionForm(Catalog.All()));
            _hero.Controls.AddRange(new Control[] { _optimize, _gaming });
            _settings = OnyxDesign.Button("Parcourir les réglages  →");
            _settings.Click += (s, e) => Host.Goto(1); _configuration.Controls.Add(_settings);
            for (int i = 0; i < 3; i++)
            {
                int metric = i;
                var panel = new BufferedPanel { BackColor = FpsUi.BgMain };
                panel.Paint += (s, e) => PaintMetric(e.Graphics, panel.ClientRectangle, metric);
                panel.Cursor = Cursors.Hand;
                panel.Click += (s, e) => Host.Goto(7);
                OnyxDesign.HoverCard(panel);
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
        internal bool MinuteurActif { get { return _timer.Enabled; } }
        public override void OnFenetreVisible(bool visible)
        {
            if (_disposed) return;
            if (visible && Visible) { Sample(); _timer.Start(); } else _timer.Stop();
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
            int height = Math.Max(692, _scroll.Height);
            _content.Size = new Size(width, height);
            _hero.SetBounds(0, 0, width, 234);
            _optimize.SetBounds(28, 172, 202, 42); _gaming.SetBounds(242, 172, 242, 42);
            int cell = (width - 32) / 3;
            for (int i = 0; i < 3; i++) _metrics[i].SetBounds(i * (cell + 16), 250, cell, 96);
            int side = 280, chartHeight = height - 462;
            _graph.SetBounds(0, 362, width - side - 16, chartHeight);
            _configuration.SetBounds(width - side, 362, side, chartHeight);
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
            OnyxDesign.PaintCard(g, r, Color.FromArgb(25, 23, 19), OnyxDesign.Edge, 16f);
            int split = Math.Max(510, r.Width - 360);
            // Trame carbone très légère, confinée à la partie illustration.
            var state = g.Save();
            using (var clip = FpsUi.Round(new RectangleF(1, 1, r.Width - 2, r.Height - 2), 15)) g.SetClip(clip);
            using (var pen = new Pen(Color.FromArgb(9, FpsUi.Gold)))
                for (int x = split - 80; x < r.Width + r.Height; x += 14) g.DrawLine(pen, x, 0, x - r.Height, r.Height);
            using (var pen = new Pen(Color.FromArgb(48, FpsUi.Gold))) g.DrawLine(pen, 28, 1, r.Width - 28, 1);
            g.Restore(state);
            Draw(g, "ONYX  /  WINDOWS & GAMING", FpsUi.Small, FpsUi.Gold, new Rectangle(28, 20, split - 40, 22));
            Draw(g, "La performance,", HeroFont, FpsUi.Ink, new Rectangle(25, 47, split - 12, 54));
            Draw(g, "sous ton contrôle.", HeroFont, FpsUi.Gold, new Rectangle(25, 94, split - 12, 54));
            DrawChip(g, new Rectangle(split + (r.Width - split) / 2 - 55, 42, 110, 110));
            Draw(g, "MODE JEU", FpsUi.Small, FpsUi.Dim, new Rectangle(split, 174, r.Width - split - 12, 20));
            Draw(g, GameBoost.IsActive ? "Activé pour ta session" : "Prêt à configurer", FpsUi.H3,
                GameBoost.IsActive ? FpsUi.Ok : FpsUi.Ink, new Rectangle(split, 197, r.Width - split - 12, 24));
        }

        private static void DrawChip(Graphics g, Rectangle r)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Color.FromArgb(52, FpsUi.Gold)))
            {
                for (int i = 0; i < 6; i++)
                {
                    int offset = 18 + i * 15;
                    g.DrawLine(pen, r.Left - 42, r.Top + offset, r.Left, r.Top + offset);
                    g.DrawLine(pen, r.Right, r.Top + offset, r.Right + 42, r.Top + offset);
                    g.DrawLine(pen, r.Left + offset, r.Top - 22, r.Left + offset, r.Top);
                    g.DrawLine(pen, r.Left + offset, r.Bottom, r.Left + offset, r.Bottom + 12);
                }
            }
            OnyxDesign.PaintCard(g, Rectangle.Inflate(r, 9, 9), Color.FromArgb(32, 28, 21), OnyxDesign.Edge, 16);
            OnyxDesign.PaintCard(g, r, FpsUi.RailBg, FpsUi.GoldDim, 12);
            NavIcons.Draw(g, NavIcons.Jeux, new RectangleF(r.X + 28, r.Y + 22, 54, 54), FpsUi.Gold);
            TextRenderer.DrawText(g, "ONYX", FpsUi.Small, new Rectangle(r.X, r.Y + 78, r.Width, 20), FpsUi.Dim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
        }
        private void PaintMetric(Graphics g, Rectangle r, int index)
        {
            OnyxDesign.PaintCard(g, r, FpsUi.Card, FpsUi.Border, 10f);
            using (var brush = new SolidBrush(_series[index])) g.FillRectangle(brush, 1, 20, 3, 54);
            Draw(g, _names[index], FpsUi.Tiny, FpsUi.Dim, new Rectangle(18, 12, r.Width - 36, 20));
            Draw(g, Percent(_values[index]), OnyxDesign.Number, FpsUi.Ink, new Rectangle(16, 28, 128, 46));
            string detail = index == 0 ? Environment.ProcessorCount + " processeurs logiques"
                : index == 1 ? (_sample != null && _sample.Gpu != null && _sample.Gpu.Ok ? _sample.Gpu.Name : "Capteur GPU")
                : _sample != null && _sample.RamTotalMB > 0 ? (_sample.RamUsedMB / 1024.0).ToString("0.0") + " / " + (_sample.RamTotalMB / 1024.0).ToString("0.0") + " Go" : "Lecture en cours…";
            Draw(g, detail, FpsUi.Tiny, FpsUi.Dim2, new Rectangle(18, 74, r.Width - 36, 18));
            DrawSeries(g, new Rectangle(r.Width - 120, 39, 100, 28), _history[index], _series[index]);
        }
        private void PaintConfiguration(object sender, PaintEventArgs e)
        {
            var g = e.Graphics; var r = _configuration.ClientRectangle;
            OnyxDesign.PaintCard(g, r, FpsUi.Card, FpsUi.Border, 12);
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
            OnyxDesign.PaintCard(g, r, FpsUi.Card, FpsUi.Border, 12);
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
            using (var fill = new SolidBrush(Color.FromArgb(15, color)))
                for (int i = 1; i < values.Length; i++)
                {
                    if (double.IsNaN(values[i - 1]) || double.IsNaN(values[i])) continue;
                    float x = plot.Left + (float)(Samples - values.Length + i - 1) / (Samples - 1) * plot.Width;
                    float next = plot.Left + (float)(Samples - values.Length + i) / (Samples - 1) * plot.Width;
                    float y = plot.Bottom - (float)Math.Clamp(values[i - 1], 0, 100) * plot.Height / 100;
                    float nextY = plot.Bottom - (float)Math.Clamp(values[i], 0, 100) * plot.Height / 100;
                    if (plot.Height > 60) g.FillPolygon(fill, new[] { new PointF(x, plot.Bottom), new PointF(x, y), new PointF(next, nextY), new PointF(next, plot.Bottom) });
                    g.DrawLine(pen, x, y, next, nextY);
                    if (i == values.Length - 1)
                        using (var dot = new SolidBrush(color)) g.FillEllipse(dot, next - 2, nextY - 2, 4, 4);
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
            OnyxDesign.PaintCard(g, ClientRectangle, _hover ? FpsUi.CardHi : FpsUi.Card,
                _hover || (Focused && ShowFocusCues) ? FpsUi.GoldDim : FpsUi.Border, 10);
            NavIcons.Draw(g, _icon, new RectangleF(16, 18, 22, 22), FpsUi.Gold);
            TextRenderer.DrawText(g, Text, FpsUi.H3, new Rectangle(48, 18, Width - 58, 24), FpsUi.Ink,
                TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, _description, FpsUi.Small, new Rectangle(16, 51, Width - 32, 24), FpsUi.Dim,
                TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }
}
