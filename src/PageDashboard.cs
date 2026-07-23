using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    // Page d'accueil « QG » : greeting, jauge SANTE, cartes stats,
    // graphe systeme live, statut patient + badge.
    internal class PageDashboard : FpsPage
    {
        private readonly HwMonitor _mon = new HwMonitor();
        private readonly Timer _timer = new Timer();
        private readonly Queue<double> _cpu = new Queue<double>();
        private readonly Queue<double> _gpu = new Queue<double>();
        private readonly Queue<double> _ram = new Queue<double>();
        private const int Hist = 60;
        private bool _sampling;
        private int _health = -1, _activeOpti = 0;
        private double _curCpu = double.NaN, _curGpu = double.NaN, _curRam = double.NaN;

        private Panel _graph;
        private Label _nOpti, _nCheck, _nJeux;

        public PageDashboard(DashboardForm host) : base(host)
        {
            Build();
            _timer.Interval = 1000;
            _timer.Tick += (s, e) => Sample();
        }

        public override void OnShown()
        {
            DoLayout();
            ComputeHealth();
            Sample();
            _timer.Start();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible) { try { _timer.Stop(); } catch { } }
        }

        protected override void OnHandleDestroyed(EventArgs e) { try { _timer.Stop(); _mon.Dispose(); } catch { } base.OnHandleDestroyed(e); }

        private void Build()
        {
            _nOpti = MakeStat("🚀", "Optimisations actives", "OPTIMISATIONS", () => Host.Goto(1), 0);
            _nCheck = MakeStat("💊", "Optimisations au total", "VOIR TOUT", () => Host.Goto(1), 1);
            _nJeux = MakeStat("🎮", "Jeux boostés", "JEUX", () => Host.Goto(2), 2);

            _graph = new Panel();
            _graph.BackColor = Color.Transparent;
            _graph.Paint += PaintGraph;
            Controls.Add(_graph);

            var prem = FpsUi.NeonButton("◆  PASSER PRO");
            prem.Name = "prem";
            prem.Click += (s, e) => Host.OpenDialog(new LicenseKeyForm(""));
            Controls.Add(prem);

            Resize += (s, e) => { DoLayout(); Invalidate(); };
        }

        private Label MakeStat(string icon, string label, string btn, Action click, int slot)
        {
            var card = new Panel();
            card.BackColor = Color.Transparent;
            card.Tag = "stat" + slot;
            card.Paint += (s, e) => FpsUi.PaintCard(e.Graphics, ((Panel)s).ClientRectangle, FpsUi.Card, FpsUi.Border, 12f);

            var ic = FpsUi.Text(icon, FpsUi.Glyph, FpsUi.Ink); ic.Name = "ic"; ic.SetBounds(16, 14, 34, 34); ic.AutoSize = false;
            var num = FpsUi.Text("—", FpsUi.Num, FpsUi.Ink); num.Name = "num"; num.SetBounds(56, 12, 130, 36); num.AutoSize = false;
            var lab = FpsUi.Text(label, FpsUi.Body, FpsUi.Dim); lab.Name = "lab"; lab.SetBounds(18, 54, 230, 20); lab.AutoSize = false;
            var b = FpsUi.GhostButton(btn); b.Name = "btn"; b.SetBounds(16, 84, 210, 32); b.Click += (s, e) => click();

            card.Controls.Add(ic); card.Controls.Add(num); card.Controls.Add(lab); card.Controls.Add(b);
            Controls.Add(card);
            return num;
        }

        private void DoLayout()
        {
            int L = 34, top = 118, w = ClientSize.Width;
            int rightW = 300, rightX = w - 34 - rightW;
            int statsW = rightX - L - 24;
            int gap = 16, cardW = (statsW - gap * 2) / 3;
            foreach (Control c in Controls)
            {
                if (c.Tag is string && ((string)c.Tag).StartsWith("stat"))
                {
                    int slot = int.Parse(((string)c.Tag).Substring(4));
                    c.SetBounds(L + slot * (cardW + gap), top, cardW, 130);
                    LayoutStat(c);
                }
            }
            if (_graph != null) _graph.SetBounds(L, top + 146, statsW, 250);
            // Bouton premium sous le graphe (bas-gauche) : évite la mascotte (coin bas-droit).
            var prem = Controls["prem"];
            if (prem != null) prem.SetBounds(L, top + 146 + 250 + 16, statsW, 46);
        }

        // Ajuste les enfants d'une carte stat à sa largeur réelle (évite tout débordement
        // quand la fenêtre est étroite : les cartes rétrécissent, les contrôles suivent).
        private static void LayoutStat(Control card)
        {
            int w = card.Width;
            var ic = card.Controls["ic"]; if (ic != null) ic.SetBounds(16, 14, 34, 34);
            var num = card.Controls["num"]; if (num != null) num.SetBounds(56, 12, Math.Max(60, w - 72), 36);
            var lab = card.Controls["lab"]; if (lab != null) lab.SetBounds(18, 54, Math.Max(60, w - 32), 22);
            var b = card.Controls["btn"]; if (b != null) b.SetBounds(16, 84, Math.Max(60, w - 32), 32);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            string name = License.Licensee;
            if (string.IsNullOrEmpty(name)) name = Environment.UserName;
            if (string.IsNullOrEmpty(name)) name = "joueur";

            int L = 34;
            TextRenderer.DrawText(g, "Bonjour, ", FpsUi.H1, new Point(L, 30), FpsUi.Ink, TextFormatFlags.NoPadding);
            int wHi = TextRenderer.MeasureText(g, "Bonjour, ", FpsUi.H1).Width;
            TextRenderer.DrawText(g, name + " !", FpsUi.H1, new Point(L + wHi - 6, 30), FpsUi.Neon, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, "Bienvenue dans ton QG.", FpsUi.Body, new Point(L + 2, 74), FpsUi.Dim, TextFormatFlags.NoPadding);

            int rightW = 300, rightX = ClientSize.Width - 34 - rightW;
            int patientTop = 118;
            int patientBottom = Host != null ? Host.ContentBottom(34) : ClientSize.Height - 34;
            // Carte à hauteur proportionnée (plafonnée) — évite un panneau étiré maintenant que la
            // mascotte ne réserve plus le coin bas-droit.
            int patientH = Math.Min(440, Math.Max(320, patientBottom - patientTop));
            DrawPatient(g, rightX, patientTop, rightW, patientH);
        }

        // Carte santé unifiée : titre + anneau de SANTÉ + logo + verdict, hauteur dynamique.
        private void DrawPatient(Graphics g, int x, int y, int w, int h)
        {
            FpsUi.PaintCard(g, new Rectangle(x, y, w, h), FpsUi.Card, FpsUi.Border, 14f);
            TextRenderer.DrawText(g, "SANTÉ DE MON PC", FpsUi.H3, new Rectangle(x, y + 20, w, 22), FpsUi.Ink, TextFormatFlags.HorizontalCenter);

            int ring = 132;
            DrawHealthRing(g, x + (w - ring) / 2, y + 52, ring);

            // Logo DTG centré sous l'anneau ; ne s'affiche que si la carte est assez haute.
            int by = y + 52 + ring + 12;
            int statusY = y + h - 34;
            int bs = Math.Min(120, statusY - by - 6);
            if (bs >= 60) Logo.Draw(g, new RectangleF(x + (w - bs) / 2f, by, bs, bs), FpsUi.Neon, false);

            // Verdict aligné sur le vocabulaire historique du bilan (et les seuils de l'anneau).
            int hp = _health < 0 ? 0 : _health;
            string verdict = hp < 30 ? "À corriger" : hp < 60 ? "Moyen" : hp < 85 ? "Bon" : "Excellent";
            TextRenderer.DrawText(g, verdict, FpsUi.H2, new Rectangle(x, statusY, w, 24), FpsUi.Ink, TextFormatFlags.HorizontalCenter);
        }

        private void DrawHealthRing(Graphics g, int x, int y, int size)
        {
            int hp = _health < 0 ? 0 : _health;
            var rf = new RectangleF(x + 6, y + 6, size - 12, size - 12);
            using (var back = new Pen(Color.FromArgb(38, 40, 39), 8f)) g.DrawArc(back, rf, 0, 360);
            Color arc = hp < 30 ? FpsUi.Err : (hp < 60 ? FpsUi.Warn : FpsUi.Neon);
            using (var pen = new Pen(arc, 8f)) { pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; g.DrawArc(pen, rf, -90, 360f * hp / 100f); }
            TextRenderer.DrawText(g, hp + "%", FpsUi.Num, new Rectangle(x, y + size / 2 - 20, size, 34), FpsUi.Ink, TextFormatFlags.HorizontalCenter);
            TextRenderer.DrawText(g, "SANTÉ", FpsUi.Small, new Rectangle(x, y + size / 2 + 14, size, 16), FpsUi.Dim, TextFormatFlags.HorizontalCenter);
        }

        private void PaintGraph(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle r = _graph.ClientRectangle;
            FpsUi.PaintCard(g, r, FpsUi.Card, FpsUi.Border, 12f);
            int pad = 18;
            TextRenderer.DrawText(g, "UTILISATION SYSTÈME", FpsUi.H3, new Point(pad, 14), FpsUi.Ink, TextFormatFlags.NoPadding);
            Legend(g, r.Width - 320, 15, "RAM", _curRam, Color.FromArgb(120, 200, 120));
            Legend(g, r.Width - 210, 15, "CPU", _curCpu, Color.FromArgb(90, 200, 250));
            Legend(g, r.Width - 100, 15, "GPU", _curGpu, FpsUi.Neon);

            var plot = new Rectangle(pad, 48, r.Width - pad * 2, r.Height - 76);
            TextRenderer.DrawText(g, "100%", FpsUi.Small, new Point(pad, 44), FpsUi.Dim2, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, "0%", FpsUi.Small, new Point(pad, plot.Bottom - 2), FpsUi.Dim2, TextFormatFlags.NoPadding);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Series(g, plot, _ram, Color.FromArgb(120, 200, 120));
            Series(g, plot, _cpu, Color.FromArgb(90, 200, 250));
            Series(g, plot, _gpu, FpsUi.Neon);
        }

        private void Legend(Graphics g, int x, int y, string t, double val, Color c)
        {
            using (var pen = new Pen(c, 2.5f)) g.DrawLine(pen, x, y + 9, x + 18, y + 9);
            TextRenderer.DrawText(g, t, FpsUi.Small, new Point(x + 23, y), FpsUi.Dim, TextFormatFlags.NoPadding);
            int tw = TextRenderer.MeasureText(g, t, FpsUi.Small).Width;
            TextRenderer.DrawText(g, double.IsNaN(val) ? "—" : val.ToString("0") + " %", FpsUi.H3, new Point(x + 23 + tw + 5, y - 1), c, TextFormatFlags.NoPadding);
        }

        private void Series(Graphics g, Rectangle plot, Queue<double> q, Color c)
        {
            if (q.Count < 2) return;
            var arr = q.ToArray();
            var pts = new PointF[arr.Length];
            for (int i = 0; i < arr.Length; i++)
            {
                float fx = plot.Left + (float)i / (Hist - 1) * plot.Width;
                float v = (float)Math.Max(0, Math.Min(100, arr[i]));
                pts[i] = new PointF(fx, plot.Bottom - v / 100f * plot.Height);
            }
            // Remplissage translucide sous la courbe (aspect « aire »).
            var poly = new PointF[arr.Length + 2];
            Array.Copy(pts, poly, arr.Length);
            poly[arr.Length] = new PointF(pts[arr.Length - 1].X, plot.Bottom);
            poly[arr.Length + 1] = new PointF(pts[0].X, plot.Bottom);
            using (var br = new SolidBrush(Color.FromArgb(30, c.R, c.G, c.B))) g.FillPolygon(br, poly);
            using (var pen = new Pen(c, 1.8f)) g.DrawLines(pen, pts);
        }

        private void Sample()
        {
            if (_sampling) return;
            _sampling = true;
            Task.Run(() =>
            {
                double cpu = 0, gpu = 0, ram = 0;
                try { HwSample s = _mon.Sample(); cpu = s.CpuLoad < 0 ? 0 : s.CpuLoad; ram = s.RamLoad; gpu = s.Gpu != null && s.Gpu.Ok ? s.Gpu.Util : 0; }
                catch { }
                try { BeginInvoke((Action)(() => { _curCpu = cpu; _curGpu = gpu; _curRam = ram; Push(_cpu, cpu); Push(_gpu, gpu); Push(_ram, ram); if (_graph != null) _graph.Invalidate(); _sampling = false; })); }
                catch { _sampling = false; }
            });
        }

        private static void Push(Queue<double> q, double v) { q.Enqueue(v); while (q.Count > Hist) q.Dequeue(); }

        private void ComputeHealth()
        {
            // État partagé/caché (AppStats) : évite de recalculer ce que la page Collection calcule aussi.
            AppStats.Get(s =>
            {
                try { BeginInvoke((Action)(() => {
                    _activeOpti = s.OptiActive; _health = s.Health;
                    if (_nOpti != null) _nOpti.Text = s.OptiActive.ToString();
                    if (_nCheck != null) _nCheck.Text = s.OptiTotal.ToString();
                    if (_nJeux != null) _nJeux.Text = GameBoost.IsActive ? "1" : "0";
                    Invalidate();
                })); }
                catch { }
            });
        }
    }
}
