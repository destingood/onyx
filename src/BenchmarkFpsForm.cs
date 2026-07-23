using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Benchmark FPS : capture une session chronométrée et sort un RÉSUMÉ unique
    /// (moyenne · 1% low · 0.1% low · pire frame) pour comparer avant/après optimisation.
    /// S'appuie sur FpsEtw (session ETW type PresentMon). Lecture seule.
    /// </summary>
    internal class BenchmarkFpsForm : Form
    {
        private readonly Action<string, int> _log;
        private static readonly Color Accent = Color.FromArgb(79, 70, 229);

        private FpsEtw _etw;
        private readonly Timer _timer = new Timer();
        private Stopwatch _sw;
        private int _durSec = 60;
        private bool _running, _done;
        private double _avg = double.NaN, _low1 = double.NaN, _low01 = double.NaN, _worstMs = double.NaN, _liveFps = double.NaN;
        private long _frames;
        private string _game;

        private Panel _canvas;
        private ComboBox _dur;
        private Button _btnStart, _btnStop, _btnCopy, _btnClose;

        public BenchmarkFpsForm(Action<string, int> log)
        {
            _log = log;
            Build();
            Theme.Apply(this);
            _timer.Interval = 500;
            _timer.Tick += (s, e) => Tick();
        }

        private void Build()
        {
            Text = "Fluide — Benchmark FPS";
            ClientSize = new Size(560, 440);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(12, 14, 13) };
            banner.Controls.Add(new Label
            {
                Text = "  Benchmark FPS — mesure ta session, compare avant/après",
                Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 12.5f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            var ld = new Label { Text = "Durée :", Location = new Point(18, 64), Size = new Size(52, 24) };
            Controls.Add(ld);
            _dur = new ComboBox { Location = new Point(72, 60), Size = new Size(180, 26), DropDownStyle = ComboBoxStyle.DropDownList };
            _dur.Items.AddRange(new object[] { "30 secondes", "1 minute", "2 minutes", "Manuel (j'arrête)" });
            _dur.SelectedIndex = 1;
            _dur.SelectedIndexChanged += (s, e) => { _durSec = new[] { 30, 60, 120, 0 }[_dur.SelectedIndex]; };
            Controls.Add(_dur);

            _canvas = new Panel { Location = new Point(0, 96), Size = new Size(560, 286), BackColor = Color.Transparent };
            _canvas.Paint += PaintBody;
            Controls.Add(_canvas);

            _btnStart = MakeBtn("Lancer la capture", 18, 392, 180, 36, true);
            _btnStart.Click += (s, e) => Start();
            _btnStop = MakeBtn("Arrêter", 206, 392, 110, 36, false);
            _btnStop.Visible = false;
            _btnStop.Click += (s, e) => Finish();
            _btnCopy = MakeBtn("Copier le résumé", 324, 392, 130, 36, false);
            _btnCopy.Enabled = false;
            _btnCopy.Click += (s, e) => CopySummary();
            _btnClose = MakeBtn("Fermer", 462, 392, 82, 36, false);
            _btnClose.Click += (s, e) => Close();
            Controls.Add(_btnStart); Controls.Add(_btnStop); Controls.Add(_btnCopy); Controls.Add(_btnClose);
        }

        private static Button MakeBtn(string text, int x, int y, int w, int h, bool primary)
        {
            var b = new Button
            {
                Text = text, Location = new Point(x, y), Size = new Size(w, h), FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Color.FromArgb(40, 44, 52),
                Font = primary ? new Font("Segoe UI Semibold", 9.5f) : new Font("Segoe UI", 9f)
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            return b;
        }

        private void Start()
        {
            if (_running) return;
            if (_etw == null) _etw = new FpsEtw();
            if (!_etw.Running && !_etw.Start())
            {
                MessageBox.Show(this, "Session ETW impossible (droits administrateur requis).\n\n" + (_etw.LastError ?? ""),
                    "Benchmark FPS", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _etw.Reset();
            _avg = _low1 = _low01 = _worstMs = _liveFps = double.NaN; _frames = 0; _game = null;
            _running = true; _done = false;
            _sw = Stopwatch.StartNew();
            _btnStart.Enabled = false; _dur.Enabled = false; _btnCopy.Enabled = false;
            _btnStop.Visible = _durSec == 0;
            _timer.Start();
            _canvas.Invalidate();
        }

        private void Tick()
        {
            if (!_running) return;
            try { var ps = _etw.Snapshot(2000); _liveFps = ps.Count > 0 ? ps[0].Fps : double.NaN; } catch { }
            if (_durSec > 0 && _sw.Elapsed.TotalSeconds >= _durSec) Finish();
            else _canvas.Invalidate();
        }

        private void Finish()
        {
            if (!_running) return;
            _timer.Stop(); _running = false; _done = true;
            double windowMs = _sw.Elapsed.TotalMilliseconds; _sw.Stop();
            try
            {
                var ps = _etw.Snapshot(Math.Max(1000, windowMs));
                if (ps.Count > 0)
                {
                    var t = ps[0];
                    _game = t.Name; _avg = t.Fps; _low1 = t.OnePctLowFps; _low01 = t.TenthPctLowFps; _worstMs = t.WorstMs; _frames = t.Total;
                }
            }
            catch { }
            _btnStart.Enabled = true; _dur.Enabled = true; _btnStop.Visible = false;
            _btnCopy.Enabled = !double.IsNaN(_avg);
            if (_log != null && !double.IsNaN(_avg)) _log("Benchmark FPS : moy " + _avg.ToString("0") + " · 1% low " + _low1.ToString("0"), 0);
            _canvas.Invalidate();
        }

        /// <summary>Résumé de démonstration (inspection visuelle hors jeu).</summary>
        internal void SeedDemo()
        {
            _done = true; _running = false;
            _game = "Counter-Strike 2"; _avg = 287; _low1 = 198; _low01 = 142; _worstMs = 11.4; _frames = 17220;
            _btnCopy.Enabled = true;
            _canvas.Invalidate();
        }

        private void CopySummary()
        {
            try { Clipboard.SetText(SummaryText()); }
            catch { }
        }

        private string SummaryText()
        {
            return "Fluide — Benchmark FPS" + (string.IsNullOrEmpty(_game) ? "" : " (" + _game + ")") + "\n"
                 + "Moyenne : " + _avg.ToString("0") + " FPS\n"
                 + "1% low  : " + (_low1 > 0 ? _low1.ToString("0") : "n/d") + " FPS\n"
                 + "0.1% low: " + (_low01 > 0 ? _low01.ToString("0") : "n/d") + " FPS\n"
                 + "Pire frame : " + (_worstMs > 0 ? _worstMs.ToString("0.0") + " ms" : "n/d") + "\n"
                 + "Images : " + _frames;
        }

        private void PaintBody(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            int w = _canvas.Width;
            Color ink = Theme.InkColor, dim = Theme.InkDimColor, neon = Color.FromArgb(0, 200, 120);

            if (!_running && !_done)
            {
                using (var f = new Font("Segoe UI", 10f))
                    TextRenderer.DrawText(g, "Choisis une durée puis lance la capture. Joue normalement pendant la mesure : "
                        + "le jeu au premier plan est mesuré automatiquement.\n\nAstuce : fais un run AVANT optimisation, "
                        + "puis un APRÈS, et compare les résumés (« Copier le résumé »).", f,
                        new Rectangle(30, 30, w - 60, 120), dim, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                return;
            }

            if (_running)
            {
                double el = _sw != null ? _sw.Elapsed.TotalSeconds : 0;
                string prog = _durSec > 0 ? el.ToString("0") + " / " + _durSec + " s" : el.ToString("0") + " s (manuel)";
                using (var f = new Font("Segoe UI Semibold", 11f))
                    TextRenderer.DrawText(g, "Capture en cours…  " + prog, f, new Point(30, 28), ink, TextFormatFlags.NoPadding);
                // Barre de progression.
                if (_durSec > 0)
                {
                    var bar = new Rectangle(30, 58, w - 60, 10);
                    using (var b1 = new SolidBrush(Color.FromArgb(30, 33, 31))) g.FillRectangle(b1, bar);
                    int fw = (int)(bar.Width * Math.Min(1.0, el / _durSec));
                    using (var b2 = new SolidBrush(neon)) g.FillRectangle(b2, bar.X, bar.Y, fw, bar.Height);
                }
                using (var vf = new Font("Segoe UI", 40f, FontStyle.Bold))
                    TextRenderer.DrawText(g, double.IsNaN(_liveFps) ? "—" : _liveFps.ToString("0"), vf, new Point(26, 92), neon, TextFormatFlags.NoPadding);
                using (var uf = new Font("Segoe UI Semibold", 12f))
                    TextRenderer.DrawText(g, "FPS en direct", uf, new Point(30, 178), dim, TextFormatFlags.NoPadding);
                return;
            }

            // Résumé final.
            if (double.IsNaN(_avg))
            {
                using (var f = new Font("Segoe UI", 10f))
                    TextRenderer.DrawText(g, "Aucune image capturée. Assure-toi qu'un jeu tournait au premier plan et relance "
                        + "(les droits administrateur sont nécessaires pour la session ETW).", f,
                        new Rectangle(30, 30, w - 60, 80), dim, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                return;
            }

            using (var hf = new Font("Segoe UI Semibold", 10f))
                TextRenderer.DrawText(g, "RÉSUMÉ" + (string.IsNullOrEmpty(_game) ? "" : "  —  " + _game), hf, new Point(30, 16), dim, TextFormatFlags.NoPadding);

            Metric(g, 30, 44, "MOYENNE", _avg, "FPS", _avg >= 100 ? neon : _avg >= 55 ? Color.FromArgb(220, 170, 40) : Color.FromArgb(210, 90, 70));
            Metric(g, w / 2 + 6, 44, "1% LOW", _low1, "FPS", _low1 >= 80 ? neon : _low1 >= 45 ? Color.FromArgb(220, 170, 40) : Color.FromArgb(210, 90, 70));
            Metric(g, 30, 150, "0.1% LOW", _low01, "FPS", _low01 >= 60 ? neon : _low01 >= 30 ? Color.FromArgb(220, 170, 40) : Color.FromArgb(210, 90, 70));
            Metric(g, w / 2 + 6, 150, "PIRE FRAME", _worstMs, "ms", _worstMs <= 20 ? neon : _worstMs <= 40 ? Color.FromArgb(220, 170, 40) : Color.FromArgb(210, 90, 70));

            using (var f = new Font("Segoe UI", 9f))
                TextRenderer.DrawText(g, _frames + " images mesurées. « Copier le résumé » pour comparer avant/après.", f,
                    new Point(30, 256), dim, TextFormatFlags.NoPadding);
        }

        private void Metric(Graphics g, int x, int y, string label, double value, string unit, Color col)
        {
            TextRenderer.DrawText(g, label, new Font("Segoe UI Semibold", 8.5f), new Point(x, y), Theme.InkDimColor, TextFormatFlags.NoPadding);
            string v = value > 0 ? (unit == "ms" ? value.ToString("0.0") : value.ToString("0")) : "n/d";
            using (var vf = new Font("Segoe UI", 34f, FontStyle.Bold))
                TextRenderer.DrawText(g, v, vf, new Point(x - 3, y + 16), col, TextFormatFlags.NoPadding);
            if (value > 0)
                using (var uf = new Font("Segoe UI Semibold", 10f))
                    TextRenderer.DrawText(g, unit, uf, new Point(x + 2, y + 74), Theme.InkColor, TextFormatFlags.NoPadding);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try { _timer.Stop(); } catch { }
            try { if (_etw != null) _etw.Dispose(); } catch { }
            base.OnFormClosing(e);
        }
    }
}
