using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>Moniteur matériel en direct : tuiles CPU/RAM/GPU + sparklines.</summary>
    internal class MonitorForm : Form
    {
        private readonly HwMonitor _mon = new HwMonitor();
        private readonly Timer _timer = new Timer();
        private bool _busy;

        private readonly Dictionary<string, Label> _vals = new Dictionary<string, Label>();
        private readonly Queue<double> _cpuHist = new Queue<double>();
        private readonly Queue<double> _gpuHist = new Queue<double>();
        private Panel _spark;
        private Button _btnCsv, _btnRam;
        private string _csvPath;   // non nul = enregistrement en cours

        private static readonly Color Bg     = Color.FromArgb(20, 22, 28);
        private static readonly Color TileBg = Color.FromArgb(32, 35, 44);
        private static readonly Color Cyan   = Color.FromArgb(90, 200, 250);
        private static readonly Color GpuCol = Color.FromArgb(120, 230, 120);
        private const int Hist = 120;

        public MonitorForm()
        {
            Build();
            _timer.Interval = 1000;
            _timer.Tick += (s, e) => Tick();
            _timer.Start();
            Tick();
        }

        private void Build()
        {
            Text = "DesTinGOOD — Moniteur matériel";
            ClientSize = new Size(720, 470);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(600, 420);
            BackColor = Bg;
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Top;
            flow.Height = 250;
            flow.Padding = new Padding(12, 12, 12, 0);
            flow.BackColor = Bg;

            flow.Controls.Add(Tile("cpu", "Charge CPU", "—"));
            flow.Controls.Add(Tile("cputemp", "Température CPU (zone ACPI)", "—"));
            flow.Controls.Add(Tile("ram", "Mémoire RAM", "—"));
            flow.Controls.Add(Tile("timer", "Timer système", "—"));
            flow.Controls.Add(Tile("gpu", "GPU", "—"));
            flow.Controls.Add(Tile("gputemp", "Température GPU", "—"));
            flow.Controls.Add(Tile("gpuutil", "Charge GPU", "—"));
            flow.Controls.Add(Tile("gpuclk", "Fréquences GPU", "—"));
            flow.Controls.Add(Tile("gpupwr", "Consommation GPU", "—"));
            flow.Controls.Add(Tile("vram", "Mémoire vidéo", "—"));

            _spark = new Panel();
            _spark.Dock = DockStyle.Fill;
            _spark.BackColor = Color.FromArgb(16, 18, 23);
            _spark.Padding = new Padding(12);
            _spark.Paint += DrawSparklines;

            var bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 42;
            bottom.BackColor = Bg;
            bottom.Padding = new Padding(12, 6, 12, 6);
            var legend = new Label();
            legend.Dock = DockStyle.Fill;
            legend.ForeColor = Color.FromArgb(150, 155, 165);
            legend.TextAlign = ContentAlignment.MiddleLeft;
            legend.Text = "  ▬ CPU (cyan)    ▬ GPU (vert)    · échelle 0–100 % · mise à jour chaque seconde";
            var close = new Button();
            close.Text = "Fermer"; close.Width = 100; close.Dock = DockStyle.Right;
            close.FlatStyle = FlatStyle.Flat; close.BackColor = TileBg; close.ForeColor = Color.White;
            close.FlatAppearance.BorderColor = Color.FromArgb(70, 74, 84);
            close.Click += (s, e) => Close();

            _btnCsv = new Button();
            _btnCsv.Text = "Enregistrer CSV : OFF"; _btnCsv.Width = 170; _btnCsv.Dock = DockStyle.Right;
            _btnCsv.FlatStyle = FlatStyle.Flat; _btnCsv.BackColor = TileBg; _btnCsv.ForeColor = Color.White;
            _btnCsv.FlatAppearance.BorderColor = Color.FromArgb(70, 74, 84);
            _btnCsv.Click += OnCsvToggle;

            _btnRam = new Button();
            _btnRam.Text = "Libérer la RAM"; _btnRam.Width = 130; _btnRam.Dock = DockStyle.Right;
            _btnRam.FlatStyle = FlatStyle.Flat; _btnRam.BackColor = TileBg; _btnRam.ForeColor = GpuCol;
            _btnRam.FlatAppearance.BorderColor = Color.FromArgb(70, 74, 84);
            _btnRam.Click += OnCleanRam;

            bottom.Controls.Add(legend);
            bottom.Controls.Add(_btnRam);
            bottom.Controls.Add(_btnCsv);
            bottom.Controls.Add(close);

            Controls.Add(_spark);
            Controls.Add(bottom);
            Controls.Add(flow);

            FormClosing += (s, e) => { _timer.Stop(); _mon.Dispose(); };
        }

        private Panel Tile(string key, string caption, string value)
        {
            var p = new Panel();
            p.Size = new Size(166, 70);
            p.Margin = new Padding(6);
            p.BackColor = TileBg;

            var cap = new Label();
            cap.Text = caption;
            cap.Dock = DockStyle.Top;
            cap.Height = 22;
            cap.ForeColor = Color.FromArgb(150, 155, 165);
            cap.Font = new Font("Segoe UI", 8f);
            cap.Padding = new Padding(9, 6, 4, 0);

            var val = new Label();
            val.Text = value;
            val.Dock = DockStyle.Fill;
            val.ForeColor = Color.White;
            val.Font = new Font("Segoe UI Semibold", 13.5f);
            val.Padding = new Padding(9, 0, 4, 6);

            _vals[key] = val;
            p.Controls.Add(val);
            p.Controls.Add(cap);
            return p;
        }

        private void Tick()
        {
            if (_busy) return;
            _busy = true;
            Task.Run(() =>
            {
                HwSample s = _mon.Sample();
                if (IsDisposed) return;
                try { BeginInvoke((Action)(() => Apply(s))); } catch { }
            });
        }

        private void Apply(HwSample s)
        {
            _busy = false;
            Set("cpu", s.CpuLoad >= 0 ? s.CpuLoad.ToString("0") + " %" : "n/d", Heat(s.CpuLoad, 60, 85));
            Set("cputemp", double.IsNaN(s.CpuTempC) ? "n/d" : s.CpuTempC.ToString("0") + " °C",
                double.IsNaN(s.CpuTempC) ? Color.Gray : Heat(s.CpuTempC, 70, 90));
            Set("ram", s.RamTotalMB > 0
                ? (s.RamUsedMB / 1024.0).ToString("0.0") + " / " + (s.RamTotalMB / 1024.0).ToString("0.0") + " Go"
                : "n/d", Heat(s.RamLoad, 75, 90));
            Set("timer", s.TimerMs > 0 ? s.TimerMs.ToString("0.0") + " ms" : "n/d",
                s.TimerMs > 0 && s.TimerMs <= 1.05 ? GpuCol : Color.White);

            GpuInfo g = s.Gpu;
            if (g != null && g.Ok)
            {
                Set("gpu", g.Name, Color.White, 10.5f);
                Set("gputemp", g.TempC.ToString("0") + " °C", Heat(g.TempC, 70, 84));
                Set("gpuutil", g.Util.ToString("0") + " %", Heat(g.Util, 70, 90));
                Set("gpuclk", g.CoreMhz.ToString("0") + " / " + g.MemMhz.ToString("0") + " MHz", Color.White, 11f);
                Set("gpupwr", g.PowerW.ToString("0") + " W", Color.White);
                Set("vram", (g.VramUsedMB / 1024.0).ToString("0.0") + " / " + (g.VramTotalMB / 1024.0).ToString("0.0") + " Go",
                    Heat(g.VramTotalMB > 0 ? 100.0 * g.VramUsedMB / g.VramTotalMB : 0, 80, 93));
            }
            else if (!_mon.HasGpu)
            {
                Set("gpu", "GPU non-NVIDIA", Color.Gray, 10.5f);
            }

            Push(_cpuHist, s.CpuLoad >= 0 ? s.CpuLoad : 0);
            Push(_gpuHist, (g != null && g.Ok) ? g.Util : 0);
            _spark.Invalidate();

            if (_csvPath != null)
            {
                try
                {
                    var ci = System.Globalization.CultureInfo.InvariantCulture;
                    string row = string.Join(";", new[]
                    {
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        s.CpuLoad.ToString("0.0", ci),
                        s.RamUsedMB.ToString(ci),
                        double.IsNaN(s.CpuTempC) ? "" : s.CpuTempC.ToString("0.0", ci),
                        s.TimerMs.ToString("0.00", ci),
                        (g != null && g.Ok) ? g.TempC.ToString("0", ci) : "",
                        (g != null && g.Ok) ? g.Util.ToString("0", ci) : "",
                        (g != null && g.Ok) ? g.CoreMhz.ToString("0", ci) : "",
                        (g != null && g.Ok) ? g.PowerW.ToString("0.0", ci) : "",
                        (g != null && g.Ok) ? g.VramUsedMB.ToString(ci) : ""
                    }) + Environment.NewLine;
                    System.IO.File.AppendAllText(_csvPath, row);
                }
                catch { }
            }
        }

        private void OnCleanRam(object sender, EventArgs e)
        {
            _btnRam.Enabled = false;
            _btnRam.Text = "Nettoyage...";
            Task.Run(() =>
            {
                long freed = Sys.CleanMemory(null);
                if (IsDisposed) return;
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        _btnRam.Enabled = true;
                        _btnRam.Text = "Libérer la RAM";
                        MessageBox.Show(this, "Mémoire libérée : ~" + Math.Max(0, freed) + " Mo.",
                            "DesTinGOOD", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }));
                }
                catch { }
            });
        }

        private void OnCsvToggle(object sender, EventArgs e)
        {
            if (_csvPath == null)
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                _csvPath = System.IO.Path.Combine(desktop,
                    "bt-monitor-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv");
                System.IO.File.WriteAllText(_csvPath,
                    "horodatage;cpu_pct;ram_mo;cpu_temp_c;timer_ms;gpu_temp_c;gpu_pct;gpu_core_mhz;gpu_w;vram_mo" + Environment.NewLine);
                _btnCsv.Text = "Enregistrer CSV : ON";
                _btnCsv.ForeColor = Color.FromArgb(120, 230, 150);
                Text = "DesTinGOOD — Moniteur matériel (CSV en cours : " + System.IO.Path.GetFileName(_csvPath) + ")";
            }
            else
            {
                _csvPath = null;
                _btnCsv.Text = "Enregistrer CSV : OFF";
                _btnCsv.ForeColor = Color.White;
                Text = "DesTinGOOD — Moniteur matériel";
            }
        }

        private static void Push(Queue<double> q, double v)
        {
            q.Enqueue(v);
            while (q.Count > Hist) q.Dequeue();
        }

        private void Set(string key, string text, Color c) { Set(key, text, c, 13.5f); }
        private void Set(string key, string text, Color c, float size)
        {
            Label l;
            if (!_vals.TryGetValue(key, out l)) return;
            l.Text = text;
            l.ForeColor = c;
            if (Math.Abs(l.Font.Size - size) > 0.1f) l.Font = new Font("Segoe UI Semibold", size);
        }

        private static Color Heat(double v, double warn, double hot)
        {
            if (v < 0) return Color.Gray;
            if (v >= hot) return Color.FromArgb(240, 110, 110);
            if (v >= warn) return Color.FromArgb(240, 190, 90);
            return Color.FromArgb(120, 230, 150);
        }

        private void DrawSparklines(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = _spark.ClientRectangle;
            r.Inflate(-12, -12);
            if (r.Width <= 10 || r.Height <= 10) return;

            using (var grid = new Pen(Color.FromArgb(40, 44, 54)))
            {
                for (int i = 0; i <= 4; i++)
                {
                    int y = r.Top + r.Height * i / 4;
                    g.DrawLine(grid, r.Left, y, r.Right, y);
                }
            }
            DrawLine(g, r, _gpuHist, GpuCol);
            DrawLine(g, r, _cpuHist, Cyan);
        }

        private static void DrawLine(Graphics g, Rectangle r, Queue<double> data, Color color)
        {
            if (data.Count < 2) return;
            double[] a = data.ToArray();
            var pts = new PointF[a.Length];
            for (int i = 0; i < a.Length; i++)
            {
                float x = r.Left + (float)r.Width * i / (Hist - 1);
                float y = r.Bottom - (float)(r.Height * Math.Max(0, Math.Min(100, a[i])) / 100.0);
                pts[i] = new PointF(x, y);
            }
            using (var pen = new Pen(color, 1.6f))
                g.DrawLines(pen, pts);
        }
    }
}
