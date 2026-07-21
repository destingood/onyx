using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    // Page Systeme : moniteur materiel live (CPU / GPU / RAM) facon FPSDoctor.
    internal class PageSystem : FpsPage
    {
        private readonly HwMonitor _mon = new HwMonitor();
        private readonly Timer _timer = new Timer();
        private readonly Queue<double> _cpu = new Queue<double>();
        private readonly Queue<double> _gpu = new Queue<double>();
        private readonly Queue<double> _ram = new Queue<double>();
        private const int Hist = 80;
        private bool _sampling;
        private HwSample _last;

        public PageSystem(DashboardForm host) : base(host)
        {
            _timer.Interval = 1000;
            _timer.Tick += (s, e) => Sample();
            Resize += (s, e) => Invalidate();
        }

        public override void OnShown() { Sample(); _timer.Start(); }
        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (!Visible) { try { _timer.Stop(); } catch { } } }
        protected override void OnHandleDestroyed(EventArgs e) { try { _timer.Stop(); _mon.Dispose(); } catch { } base.OnHandleDestroyed(e); }

        private void Sample()
        {
            if (_sampling) return;
            _sampling = true;
            Task.Run(() =>
            {
                HwSample s = null;
                try { s = _mon.Sample(); } catch { }
                try { BeginInvoke((Action)(() =>
                {
                    if (s != null) { _last = s; Push(_cpu, s.CpuLoad < 0 ? 0 : s.CpuLoad); Push(_gpu, s.Gpu != null && s.Gpu.Ok ? s.Gpu.Util : 0); Push(_ram, s.RamLoad); }
                    _sampling = false; Invalidate();
                })); }
                catch { _sampling = false; }
            });
        }

        private static void Push(Queue<double> q, double v) { q.Enqueue(v); while (q.Count > Hist) q.Dequeue(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            PaintTitle(g, "GÉNÉRAL", null);

            int L = 34, top = 74, gap = 16;
            int rightW = 250;
            int cardH = (ClientSize.Height - top - 34 - gap * 2) / 3;
            int graphW = ClientSize.Width - L * 2 - rightW - gap;

            HwSample s = _last;
            DrawMetric(g, L, top, graphW, cardH, rightW, "PROCESSEUR", _cpu, Color.FromArgb(120, 200, 120),
                s != null && s.CpuLoad >= 0 ? s.CpuLoad : double.NaN,
                s != null && !double.IsNaN(s.CpuTempC) ? s.CpuTempC : double.NaN,
                "CPU", s != null ? Environment.ProcessorCount + " threads" : "");
            int r2 = top + cardH + gap;
            double gu = s != null && s.Gpu != null && s.Gpu.Ok ? s.Gpu.Util : double.NaN;
            double gt = s != null && s.Gpu != null && s.Gpu.Ok ? s.Gpu.TempC : double.NaN;
            DrawMetric(g, L, r2, graphW, cardH, rightW, "CARTE GRAPHIQUE", _gpu, FpsUi.Neon, gu, gt, "GPU",
                s != null && s.Gpu != null && s.Gpu.Ok ? s.Gpu.Name : "n/d");
            int r3 = r2 + cardH + gap;
            string ramDetail = s != null && s.RamTotalMB > 0 ? (s.RamUsedMB / 1024.0).ToString("0.0") + " / " + (s.RamTotalMB / 1024.0).ToString("0.0") + " Go" : "";
            DrawMetric(g, L, r3, graphW, cardH, rightW, "MÉMOIRE RAM", _ram, Color.FromArgb(90, 200, 250),
                s != null ? s.RamLoad : double.NaN, double.NaN, "RAM", ramDetail);
        }

        private void DrawMetric(Graphics g, int x, int y, int gw, int h, int rw, string title, Queue<double> q, Color col,
            double util, double temp, string tag, string detail)
        {
            FpsUi.PaintCard(g, new Rectangle(x, y, gw, h), FpsUi.Card, FpsUi.Border, 12f);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            TextRenderer.DrawText(g, title, FpsUi.H3, new Point(x + 16, y + 12), FpsUi.Ink, TextFormatFlags.NoPadding);
            var plot = new Rectangle(x + 16, y + 44, gw - 32, h - 66);
            TextRenderer.DrawText(g, "100%", FpsUi.Small, new Point(plot.Right - 34, y + 40), FpsUi.Dim2, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, "0%", FpsUi.Small, new Point(x + 16, plot.Bottom - 2), FpsUi.Dim2, TextFormatFlags.NoPadding);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (q.Count >= 2)
            {
                var arr = q.ToArray(); var pts = new PointF[arr.Length];
                for (int i = 0; i < arr.Length; i++)
                {
                    float fx = plot.Left + (float)i / (Hist - 1) * plot.Width;
                    float v = (float)Math.Max(0, Math.Min(100, arr[i]));
                    pts[i] = new PointF(fx, plot.Bottom - v / 100f * plot.Height);
                }
                using (var pen = new Pen(col, 1.8f)) g.DrawLines(pen, pts);
            }

            // Carte detail a droite.
            int dx = x + gw + 16;
            FpsUi.PaintCard(g, new Rectangle(dx, y, rw, h), FpsUi.Card, FpsUi.Border, 12f);
            TextRenderer.DrawText(g, tag, FpsUi.H3, new Point(dx + 16, y + 14), FpsUi.Ink, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, "UTILISATION", FpsUi.Small, new Point(dx + 16, y + 48), FpsUi.Dim, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, double.IsNaN(util) ? "n/d" : util.ToString("0") + " %", FpsUi.H2, new Point(dx + 16, y + 64),
                double.IsNaN(util) ? FpsUi.Dim : (util < 60 ? FpsUi.Neon : util < 85 ? FpsUi.Warn : FpsUi.Err), TextFormatFlags.NoPadding);
            if (!double.IsNaN(temp))
            {
                TextRenderer.DrawText(g, "TEMPÉRATURE", FpsUi.Small, new Point(dx + 16, y + 96), FpsUi.Dim, TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, temp.ToString("0") + " °C", FpsUi.H2, new Point(dx + 16, y + 112),
                    temp < 70 ? FpsUi.Neon : temp < 84 ? FpsUi.Warn : FpsUi.Err, TextFormatFlags.NoPadding);
            }
            if (!string.IsNullOrEmpty(detail))
                TextRenderer.DrawText(g, detail, FpsUi.Small, new Rectangle(dx + 16, y + h - 28, rw - 32, 20), FpsUi.Dim, TextFormatFlags.NoPadding);
        }
    }
}
