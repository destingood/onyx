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

        // --- Animation d'entrée (l'anneau se remplit, les compteurs montent) -------------
        private readonly Timer _animTimer = new Timer();
        private float _animT = 1f;                    // 0 = début, 1 = état final
        private int _tOpti, _tTotal, _tJeux;          // valeurs cibles des compteurs
        private bool _statsReady;
        private readonly HashSet<Control> _hover = new HashSet<Control>();   // cartes sous la souris

        /// <summary>Animations désactivées si l'utilisateur a coupé les effets Windows
        /// (accessibilité) ou sous le harnais de capture, qui doit voir l'état final.</summary>
        private static bool MotionEnabled
        {
            get
            {
                try { return SystemInformation.UIEffectsEnabled && Environment.GetEnvironmentVariable("BT_UITEST") != "1"; }
                catch { return false; }
            }
        }

        /// <summary>Décélération douce (ease-out cubique) : rapide au départ, posée à l'arrivée.</summary>
        private static float Ease(float t) { float u = 1f - t; return 1f - u * u * u; }

        public PageDashboard(DashboardForm host) : base(host)
        {
            Build();
            _timer.Interval = 1000;
            _timer.Tick += (s, e) => Sample();
            _animTimer.Interval = 16;                 // ~60 images/s
            _animTimer.Tick += (s, e) => AnimTick();
        }

        public override void OnShown()
        {
            DoLayout();
            _animT = MotionEnabled ? 0f : 1f;
            ApplyCounters();
            ComputeHealth();
            Sample();
            _timer.Start();
            if (MotionEnabled) _animTimer.Start();
        }

        private void AnimTick()
        {
            _animT += 0.05f;                          // ~340 ms au total
            if (_animT >= 1f) { _animT = 1f; _animTimer.Stop(); }
            ApplyCounters();
            Invalidate();
        }

        /// <summary>Compteurs : de 0 à leur valeur, suivant la même courbe que l'anneau.</summary>
        private void ApplyCounters()
        {
            if (!_statsReady) return;
            float e = Ease(_animT);
            if (_nOpti != null) _nOpti.Text = ((int)Math.Round(_tOpti * e)).ToString();
            if (_nCheck != null) _nCheck.Text = ((int)Math.Round(_tTotal * e)).ToString();
            if (_nJeux != null) _nJeux.Text = ((int)Math.Round(_tJeux * e)).ToString();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible) { try { _timer.Stop(); _animTimer.Stop(); } catch { } }
        }

        protected override void OnHandleDestroyed(EventArgs e) { try { _timer.Stop(); _animTimer.Stop(); _mon.Dispose(); } catch { } base.OnHandleDestroyed(e); }

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
            // Au survol : fond éclairci + liseré néon (la carte réagit sous la souris).
            card.Paint += (s, e) =>
            {
                var p = (Panel)s;
                bool hot = _hover.Contains(p);
                FpsUi.PaintCard(e.Graphics, p.ClientRectangle,
                                hot ? FpsUi.CardHi : FpsUi.Card,
                                hot ? FpsUi.NeonDim : FpsUi.Border, 12f);
            };

            var ic = FpsUi.Text(icon, FpsUi.Glyph, FpsUi.Ink); ic.Name = "ic"; ic.SetBounds(16, 14, 34, 34); ic.AutoSize = false;
            var num = FpsUi.Text("—", FpsUi.Num, FpsUi.Ink); num.Name = "num"; num.SetBounds(56, 12, 130, 36); num.AutoSize = false;
            var lab = FpsUi.Text(label, FpsUi.Body, FpsUi.Dim); lab.Name = "lab"; lab.SetBounds(18, 54, 230, 20); lab.AutoSize = false;
            var b = FpsUi.GhostButton(btn); b.Name = "btn"; b.SetBounds(16, 84, 210, 32); b.Click += (s, e) => click();

            card.Controls.Add(ic); card.Controls.Add(num); card.Controls.Add(lab); card.Controls.Add(b);
            Controls.Add(card);
            WireHover(card, card);   // après l'ajout des enfants : ils captent aussi la souris
            return num;
        }

        /// <summary>Suit le survol sur la carte ET ses enfants (un enfant sous la souris
        /// déclencherait sinon un MouseLeave de la carte, d'où le test de position réelle).</summary>
        private void WireHover(Control root, Panel card)
        {
            root.MouseEnter += (s, e) => { if (_hover.Add(card)) card.Invalidate(); };
            root.MouseLeave += (s, e) =>
            {
                try
                {
                    if (card.ClientRectangle.Contains(card.PointToClient(Cursor.Position))) return;
                }
                catch { }
                if (_hover.Remove(card)) card.Invalidate();
            };
            foreach (Control ch in root.Controls) WireHover(ch, card);
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
            // L'anneau se remplit à l'ouverture ; la COULEUR reste celle du score final
            // (sinon elle virerait rouge → orange → vert pendant le remplissage).
            int shown = (int)Math.Round(hp * Ease(_animT));
            var rf = new RectangleF(x + 6, y + 6, size - 12, size - 12);
            using (var back = new Pen(Color.FromArgb(38, 40, 39), 8f)) g.DrawArc(back, rf, 0, 360);
            Color arc = hp < 30 ? FpsUi.Err : (hp < 60 ? FpsUi.Warn : FpsUi.Neon);
            if (shown > 0)
                using (var pen = new Pen(arc, 8f)) { pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; g.DrawArc(pen, rf, -90, 360f * shown / 100f); }
            TextRenderer.DrawText(g, shown + "%", FpsUi.Num, new Rectangle(x, y + size / 2 - 20, size, 34), FpsUi.Ink, TextFormatFlags.HorizontalCenter);
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
            // Courbe lissée (spline) + aire en dégradé qui s'efface vers le bas : la lecture
            // est plus douce qu'une ligne brisée, et l'aire donne de la profondeur.
            const float tension = 0.4f;              // au-delà, la spline « rebondit »
            if (plot.Height > 1)
            {
                using (var path = new GraphicsPath())
                {
                    path.AddCurve(pts, tension);
                    path.AddLine(pts[arr.Length - 1].X, plot.Bottom, pts[0].X, plot.Bottom);
                    path.CloseFigure();
                    using (var br = new LinearGradientBrush(
                        new Rectangle(plot.X, plot.Y, Math.Max(1, plot.Width), plot.Height),
                        Color.FromArgb(70, c.R, c.G, c.B), Color.FromArgb(0, c.R, c.G, c.B), 90f))
                        g.FillPath(br, path);
                }
            }
            using (var pen = new Pen(c, 1.8f)) { pen.LineJoin = LineJoin.Round; g.DrawCurve(pen, pts, tension); }
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
                    // Cibles de l'animation : les compteurs y montent (ou s'y posent
                    // directement si les effets sont coupés / si l'entrée est terminée).
                    _tOpti = s.OptiActive; _tTotal = s.OptiTotal; _tJeux = GameBoost.IsActive ? 1 : 0;
                    _statsReady = true;
                    ApplyCounters();
                    Invalidate();
                })); }
                catch { }
            });
        }
    }
}
