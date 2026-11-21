using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    internal class PageSystem : FpsPage
    {
        private readonly HwMonitor _monitor = new HwMonitor();
        private readonly object _sampleGate = new object();
        private readonly Timer _timer = new Timer { Interval = 1000 };
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly List<Reading> _history = new List<Reading>();
        private readonly DeviceCard[] _devices = new DeviceCard[3];
        private readonly Button[] _ranges = new Button[3];
        private readonly int[] _seconds = { 30, 60, 120 };
        private readonly BufferedPanel _chart = new BufferedPanel(), _details = new BufferedPanel(), _ramSettings = new BufferedPanel();
        private readonly Button _components, _report, _pause;
        private readonly ToggleSwitch _ramAuto = new ToggleSwitch();
        private readonly NumericUpDown _ramThreshold = new NumericUpDown();
        private bool _sampling, _disposed, _paused;
        private int _device, _window = 60;
        private double _pausedAt;
        private HwSample _last;
        private DateTime _updated;

        private sealed class Reading
        {
            public double Time;
            public HwSample Sample;
        }
        private static readonly string[] Titles = { "Processeur", "Carte graphique", "Mémoire RAM" };
        private static readonly string[] Tags = { "CPU", "GPU", "RAM" };
        private static readonly Color[] Colors = { FpsUi.Gold, FpsUi.GraphSecondary, FpsUi.Ok };

        public PageSystem(DashboardForm host) : base(host)
        {
            _components = OnyxDesign.Button("Composants détaillés");
            _components.Click += (s, e) => Host.OpenDialog(new SystemInfoForm(Host.Log)); Controls.Add(_components);
            _report = OnyxDesign.Button("Rapport de santé");
            _report.Click += (s, e) => Host.GenerateHealthReport(); Controls.Add(_report);
            for (int i = 0; i < _devices.Length; i++)
            {
                int device = i;
                var card = new DeviceCard(this, i) { Name = "hardware-device-" + i, AccessibleName = Titles[i], Text = Titles[i] };
                card.Click += (s, e) => { _device = device; RefreshDisplay(); };
                _devices[i] = card; Controls.Add(card);
            }
            _chart.Name = "hardware-chart"; _chart.BackColor = Color.Transparent; _chart.Paint += PaintChart; Controls.Add(_chart);
            _details.BackColor = Color.Transparent; _details.Paint += PaintDetails; Controls.Add(_details);
            for (int i = 0; i < _ranges.Length; i++)
            {
                int seconds = _seconds[i];
                _ranges[i] = OnyxDesign.Button(seconds + " s"); _ranges[i].Name = "hardware-range-" + seconds;
                _ranges[i].Click += (s, e) => { _window = seconds; RefreshDisplay(); };
                _chart.Controls.Add(_ranges[i]);
            }
            _pause = OnyxDesign.Button("Pause"); _pause.Name = "hardware-pause";
            _pause.Click += (s, e) => TogglePause(); _chart.Controls.Add(_pause);
            BuildRamSettings();
            _timer.Tick += (s, e) => Sample();
            Resize += (s, e) => DoLayout();
            RefreshDisplay();
        }

        private void BuildRamSettings()
        {
            _ramSettings.BackColor = Color.Transparent; _ramSettings.Paint += PaintRamSettings; Controls.Add(_ramSettings);
            int threshold;
            _ramAuto.On = Sys.LoadRamCleaner(out threshold);
            _ramAuto.Name = "hardware-ram-auto"; _ramAuto.AccessibleName = "Nettoyeur RAM automatique";
            _ramThreshold.Minimum = 100; _ramThreshold.Maximum = 64000; _ramThreshold.Increment = 500;
            _ramThreshold.Value = Math.Max(100, Math.Min(64000, threshold));
            _ramThreshold.Font = FpsUi.H3; _ramThreshold.BackColor = FpsUi.CardHi; _ramThreshold.ForeColor = FpsUi.Ink;
            _ramThreshold.BorderStyle = BorderStyle.FixedSingle;
            _ramThreshold.AccessibleName = "Seuil de mémoire libre en mégaoctets";
            _ramThreshold.Enabled = _ramAuto.On;
            _ramAuto.Toggled += (s, e) => SaveRamSettings();
            _ramThreshold.ValueChanged += (s, e) => SaveRamSettings();
            _ramSettings.Controls.Add(_ramAuto); _ramSettings.Controls.Add(_ramThreshold);
        }
        private void SaveRamSettings()
        {
            Sys.SaveRamCleaner(_ramAuto.On, (int)_ramThreshold.Value);
            _ramThreshold.Enabled = _ramAuto.On; _ramSettings.Invalidate();
        }
        private void DoLayout()
        {
            int width = ClientSize.Width - 68;
            _components.SetBounds(ClientSize.Width - 224, 20, 190, 36);
            _report.SetBounds(_components.Left - 162, 20, 150, 36);
            int cardWidth = (width - 32) / 3;
            for (int i = 0; i < 3; i++) _devices[i].SetBounds(34 + i * (cardWidth + 16), 96, cardWidth, 136);
            int graphWidth = width - 272, chartHeight = Math.Max(270, ClientSize.Height - 366);
            _chart.SetBounds(34, 250, graphWidth, chartHeight);
            _details.SetBounds(_chart.Right + 16, 250, 256, chartHeight);
            int controlsX = graphWidth - 274;
            for (int i = 0; i < 3; i++) _ranges[i].SetBounds(controlsX + i * 54, 17, 48, 30);
            _pause.SetBounds(graphWidth - 106, 17, 88, 30);
            _ramSettings.SetBounds(34, ClientSize.Height - 100, width, 82);
            _ramAuto.Location = new Point(width - 284, 30);
            _ramThreshold.SetBounds(width - 170, 32, 102, 28);
            Invalidate();
        }
        private void TogglePause()
        {
            _paused = !_paused;
            if (_paused) { _pausedAt = _clock.Elapsed.TotalSeconds; _timer.Stop(); }
            else if (Visible) { Sample(); _timer.Start(); }
            _pause.Text = _paused ? "Reprendre" : "Pause";
            RefreshDisplay();
        }
        public override void OnShown() { DoLayout(); if (!_paused) { Sample(); _timer.Start(); } }
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e); if (!Visible) _timer.Stop();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Stop(); _timer.Dispose();
                lock (_sampleGate) { _disposed = true; if (!_sampling) _monitor.Dispose(); }
            }
            base.Dispose(disposing);
        }
        private void Sample()
        {
            lock (_sampleGate)
            {
                if (_sampling || _disposed || _paused || !Visible) return;
                _sampling = true;
            }
            Task.Run(() =>
            {
                HwSample sample = null;
                try { sample = _monitor.Sample(); } catch { }
                lock (_sampleGate)
                {
                    _sampling = false;
                    if (_disposed) { _monitor.Dispose(); return; }
                }
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        if (_disposed || _paused || !Visible) return;
                        double now = _clock.Elapsed.TotalSeconds;
                        _last = sample; _updated = DateTime.Now;
                        _history.Add(new Reading { Time = now, Sample = sample });
                        while (_history.Count > 1 && _history[1].Time < now - 120) _history.RemoveAt(0);
                        RefreshDisplay();
                    }));
                }
                catch { }
            });
        }
        private void RefreshDisplay()
        {
            foreach (var card in _devices) if (card != null) card.Invalidate();
            for (int i = 0; i < _ranges.Length; i++)
            {
                if (_ranges[i] == null) continue;
                bool active = _seconds[i] == _window;
                _ranges[i].BackColor = active ? FpsUi.BlendGold(FpsUi.Card, .12f) : FpsUi.Card;
                _ranges[i].ForeColor = active ? FpsUi.Gold : FpsUi.Dim;
                _ranges[i].FlatAppearance.BorderSize = active ? 1 : 0;
                _ranges[i].FlatAppearance.BorderColor = active ? FpsUi.GoldDim : FpsUi.Border;
                _ranges[i].Invalidate();
            }
            _chart.Invalidate(); _details.Invalidate(); Invalidate();
        }
        private static double ReadLoad(HwSample sample, int device)
        {
            if (sample == null) return double.NaN;
            double value = device == 0 ? sample.CpuLoad : device == 1
                ? sample.Gpu != null && sample.Gpu.Ok ? sample.Gpu.Util : double.NaN
                : sample.RamTotalMB > 0 ? sample.RamLoad : double.NaN;
            return double.IsNaN(value) || double.IsInfinity(value) || value < 0 || value > 100 ? double.NaN : value;
        }
        private static string Percent(double value) { return double.IsNaN(value) ? "—" : value.ToString("0") + " %"; }
        private static string Positive(double value, string unit) { return double.IsNaN(value) || value <= 0 ? "—" : value.ToString("0") + unit; }
        private static string Memory(long mb) { return (mb / 1024.0).ToString("0.0") + " Go"; }
        private double EndTime { get { return _paused ? _pausedAt : _clock.Elapsed.TotalSeconds; } }

#if BTTEST
        internal int ReadingCount { get { return _history.Count; } }
        internal bool SamplingEnabled { get { return _timer.Enabled; } }
        internal void PreviewDevice(int device) { _devices[device].PerformClick(); }
        internal void PreviewPause() { _pause.PerformClick(); }
        internal void ValidateHardwareUi()
        {
            if (!double.IsNaN(ReadLoad(null, 0)) || !double.IsNaN(ReadLoad(new HwSample(), 0)) ||
                !double.IsNaN(ReadLoad(new HwSample(), 1)) || !double.IsNaN(ReadLoad(new HwSample(), 2)))
                throw new InvalidOperationException("Un capteur absent ne doit pas afficher zéro.");
            for (int i = 0; i < 3; i++)
            {
                _devices[i].PerformClick();
                if (_device != i) throw new InvalidOperationException("Sélection du composant incorrecte.");
                _ranges[i].PerformClick();
                if (_window != _seconds[i]) throw new InvalidOperationException("Durée d'historique incorrecte.");
            }
            _ranges[1].PerformClick();
            foreach (Control control in Controls)
                if (!ClientRectangle.Contains(control.Bounds)) throw new InvalidOperationException("Contrôle hors de la page : " + control.Name);
            if (_chart.Bottom > _ramSettings.Top || _devices[0].Bottom > _chart.Top)
                throw new InvalidOperationException("Les cartes se chevauchent.");
        }
#endif

        private void PaintDevice(Graphics g, DeviceCard card, int device, bool hot)
        {
            bool selected = device == _device;
            Color color = Colors[device];
            OnyxDesign.PaintCard(g, card.ClientRectangle, selected ? FpsUi.BlendGold(FpsUi.Card, .04f) : FpsUi.Card, selected || hot ? FpsUi.GoldDim : FpsUi.Border, 12);
            OnyxDesign.Text(g, Tags[device] + "  /  " + Titles[device], FpsUi.H3, selected ? FpsUi.Gold : FpsUi.Dim, new Rectangle(18, 16, card.Width - 36, 22));
            OnyxDesign.Text(g, Percent(ReadLoad(_last, device)), OnyxDesign.Number, FpsUi.Ink, new Rectangle(16, 47, 145, 44));
            DrawHistory(g, new Rectangle(card.Width - 120, 51, 102, 38), device, 60, false);
            string detail = device == 0 ? Environment.ProcessorCount + " processeurs logiques"
                : device == 1 ? _last != null && _last.Gpu != null && _last.Gpu.Ok ? _last.Gpu.Name : "Capteur GPU indisponible"
                : _last != null && _last.RamTotalMB > 0 ? Memory(_last.RamUsedMB) + " / " + Memory(_last.RamTotalMB) : "Capteur mémoire indisponible";
            OnyxDesign.Text(g, detail, FpsUi.Small, FpsUi.Dim, new Rectangle(18, 107, card.Width - 36, 20));
            if (selected) using (var pen = new Pen(color, 2)) g.DrawLine(pen, 18, card.Height - 1, card.Width - 18, card.Height - 1);
        }
        private void PaintChart(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            OnyxDesign.PaintCard(g, _chart.ClientRectangle, FpsUi.Card, FpsUi.Border, 12);
            OnyxDesign.Text(g, "CHARGE " + Tags[_device], FpsUi.H3, FpsUi.Ink, new Rectangle(20, 22, 180, 24));
            string status = _paused ? "EN PAUSE" : "EN DIRECT";
            OnyxDesign.Text(g, status + "  ·  historique sur " + _window + " s", FpsUi.Small, _paused ? FpsUi.Dim : Colors[_device], new Rectangle(20, 58, _chart.Width - 40, 22));
            var plot = new Rectangle(20, 96, _chart.Width - 66, _chart.Height - 136);
            for (int i = 0; i <= 4; i++)
            {
                int y = plot.Top + i * plot.Height / 4;
                using (var pen = new Pen(FpsUi.Border)) g.DrawLine(pen, plot.Left, y, plot.Right, y);
                OnyxDesign.Text(g, (100 - i * 25) + "%", FpsUi.Tiny, FpsUi.Dim2, new Rectangle(plot.Right + 9, y - 7, 34, 18));
            }
            DrawHistory(g, plot, _device, _window, true);
            OnyxDesign.Text(g, "−" + _window + " s", FpsUi.Small, FpsUi.Dim2, new Rectangle(plot.Left, plot.Bottom + 15, 80, 20));
            TextRenderer.DrawText(g, _paused ? "Pause" : "Maintenant", FpsUi.Small, new Rectangle(plot.Right - 100, plot.Bottom + 15, 100, 20), FpsUi.Dim2,
                TextFormatFlags.Right | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }
        private void DrawHistory(Graphics g, Rectangle plot, int device, int seconds, bool caption)
        {
            if (plot.Width < 2 || plot.Height < 2) return;
            double end = EndTime, start = end - seconds;
            var segment = new List<PointF>(); bool any = false;
            double previous = double.NaN;
            var saved = g.Save(); g.SetClip(plot); g.SmoothingMode = SmoothingMode.AntiAlias;
            foreach (var reading in _history)
            {
                if (reading.Time < start || reading.Time > end) continue;
                double value = ReadLoad(reading.Sample, device);
                if (double.IsNaN(value) || (!double.IsNaN(previous) && reading.Time - previous > 3.5))
                { PaintSegment(g, segment, plot.Bottom, Colors[device]); segment.Clear(); }
                previous = reading.Time;
                if (double.IsNaN(value)) continue;
                any = true;
                segment.Add(new PointF(plot.Left + (float)((reading.Time - start) / seconds * plot.Width), plot.Bottom - (float)(value / 100 * plot.Height)));
            }
            PaintSegment(g, segment, plot.Bottom, Colors[device]);
            g.Restore(saved);
            if (!any && caption)
            {
                string message = _history.Count == 0 ? "En attente du premier relevé…" : "Mesure indisponible pour ce capteur";
                TextRenderer.DrawText(g, message, FpsUi.Body, plot, FpsUi.Dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }
        private static void PaintSegment(Graphics g, List<PointF> points, int bottom, Color color)
        {
            if (points.Count == 0) return;
            if (points.Count > 1)
            {
                var polygon = new List<PointF>(points) { new PointF(points[points.Count - 1].X, bottom), new PointF(points[0].X, bottom) };
                using (var brush = new SolidBrush(Color.FromArgb(20, color))) g.FillPolygon(brush, polygon.ToArray());
                using (var pen = new Pen(color, 1.8f)) g.DrawLines(pen, points.ToArray());
            }
            var last = points[points.Count - 1];
            using (var brush = new SolidBrush(color)) g.FillEllipse(brush, last.X - 2, last.Y - 2, 4, 4);
        }
        private double Statistic(bool maximum)
        {
            double total = 0, max = 0, start = EndTime - _window; int count = 0;
            foreach (var reading in _history)
            {
                if (reading.Time < start || reading.Time > EndTime) continue;
                double value = ReadLoad(reading.Sample, _device); if (double.IsNaN(value)) continue;
                total += value; max = Math.Max(max, value); count++;
            }
            return count == 0 ? double.NaN : maximum ? max : total / count;
        }
        private void PaintDetails(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            OnyxDesign.PaintCard(g, _details.ClientRectangle, FpsUi.Card, FpsUi.Border, 12);
            OnyxDesign.Text(g, "DÉTAILS " + Tags[_device], FpsUi.H3, FpsUi.Ink, new Rectangle(18, 20, _details.Width - 36, 24));
            string[] labels, values;
            GpuInfo gpu = _last != null && _last.Gpu != null && _last.Gpu.Ok ? _last.Gpu : null;
            if (_device == 0)
            {
                labels = new[] { "Température", "Processeurs logiques", "Moyenne · " + _window + " s", "Maximum · " + _window + " s" };
                values = new[] { _last == null ? "—" : Positive(_last.CpuTempC, " °C"), Environment.ProcessorCount.ToString(), Percent(Statistic(false)), Percent(Statistic(true)) };
            }
            else if (_device == 1)
            {
                labels = new[] { "Température", "Fréquence GPU", "Puissance", "Mémoire vidéo utilisée" };
                values = new[] { gpu == null ? "—" : Positive(gpu.TempC, " °C"), gpu == null ? "—" : Positive(gpu.CoreMhz, " MHz"), gpu == null ? "—" : Positive(gpu.PowerW, " W"), gpu == null || gpu.VramTotalMB <= 0 ? "—" : Memory(gpu.VramUsedMB) + " / " + Memory(gpu.VramTotalMB) };
            }
            else
            {
                bool available = _last != null && _last.RamTotalMB > 0;
                labels = new[] { "Mémoire utilisée", "Mémoire disponible", "Capacité totale", "Maximum · " + _window + " s" };
                values = new[] { available ? Memory(_last.RamUsedMB) : "—", available ? Memory(Math.Max(0, _last.RamTotalMB - _last.RamUsedMB)) : "—", available ? Memory(_last.RamTotalMB) : "—", Percent(Statistic(true)) };
            }
            int rowHeight = Math.Min(82, (_details.Height - 80) / 4);
            for (int i = 0; i < 4; i++)
            {
                int y = 59 + i * rowHeight;
                OnyxDesign.Text(g, labels[i], FpsUi.Small, FpsUi.Dim, new Rectangle(18, y, _details.Width - 36, 20));
                OnyxDesign.Text(g, values[i], FpsUi.H2, FpsUi.Ink, new Rectangle(18, y + 22, _details.Width - 36, 26));
            }
            OnyxDesign.Text(g, _updated == default(DateTime) ? "En attente des capteurs" : "Dernier relevé · " + _updated.ToString("HH:mm:ss"), FpsUi.Tiny, FpsUi.Dim2, new Rectangle(18, _details.Height - 26, _details.Width - 36, 18));
        }
        private void PaintRamSettings(object sender, PaintEventArgs e)
        {
            var g = e.Graphics; int width = _ramSettings.Width;
            OnyxDesign.PaintCard(g, _ramSettings.ClientRectangle, FpsUi.Card, FpsUi.Border, 12);
            OnyxDesign.IconWell(g, NavIcons.Stockage, new Rectangle(18, 22, 38, 38), _ramAuto.On);
            OnyxDesign.Text(g, "Gestion automatique de la RAM", FpsUi.H3, FpsUi.Ink, new Rectangle(70, 17, width - 480, 24));
            OnyxDesign.Text(g, "Déclenchement sous le seuil de mémoire libre.", FpsUi.Small, FpsUi.Dim, new Rectangle(70, 46, width - 480, 20));
            OnyxDesign.Text(g, _ramAuto.On ? "Activée" : "Désactivée", FpsUi.Small, FpsUi.Dim, new Rectangle(width - 372, 34, 88, 20));
            OnyxDesign.Text(g, "SEUIL LIBRE", FpsUi.Tiny, FpsUi.Dim2, new Rectangle(width - 170, 12, 116, 18));
            OnyxDesign.Text(g, "Mo", FpsUi.Small, FpsUi.Dim, new Rectangle(width - 52, 36, 30, 20));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            PaintPremiumTitle(e.Graphics, "MATÉRIEL EN DIRECT", "CPU, GPU et mémoire : observe la charge et les capteurs de ton PC.");
        }
        private sealed class DeviceCard : Button
        {
            private readonly PageSystem _page; private readonly int _device; private bool _hot;
            public DeviceCard(PageSystem page, int device)
            {
                _page = page; _device = device; Cursor = Cursors.Hand; BackColor = FpsUi.BgMain;
                FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                MouseEnter += (s, e) => { _hot = true; Invalidate(); }; MouseLeave += (s, e) => { _hot = false; Invalidate(); };
                GotFocus += (s, e) => Invalidate(); LostFocus += (s, e) => Invalidate();
            }
            protected override void OnPaint(PaintEventArgs e) { e.Graphics.Clear(BackColor); _page.PaintDevice(e.Graphics, this, _device, _hot || (Focused && ShowFocusCues)); }
        }
    }
}
