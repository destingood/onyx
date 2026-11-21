using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BTOptimizer
{
    // Page Laboratoire : modules optionnels (viseur, filtre couleur, compteur
    // FPS, overclock, speed test). Cartes style DTG.
    internal class PageLab : FpsPage
    {
        private readonly BufferedFlow _grid = new BufferedFlow();
        private ScrollWheelFilter _wheel;

        public PageLab(DashboardForm host) : base(host)
        {
            _grid.AutoScroll = true;
            _grid.BackColor = FpsUi.BgMain;
            _grid.Padding = new Padding(0, 0, 0, 16);
            Controls.Add(_grid);
            _wheel = new ScrollWheelFilter(_grid);
            Application.AddMessageFilter(_wheel);
            Build();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _wheel != null)
            {
                Application.RemoveMessageFilter(_wheel);
                _wheel = null;
            }
            base.Dispose(disposing);
        }

        private void Build()
        {
            AddCard("📊", "Benchmark FPS", "Compare tes FPS avant et après optimisation.", false, () => Host.OpenDialog(new BenchmarkFpsForm(Host.Log)));
            AddCard("⏱", "Latence DPC / ISR", "Mesure les interruptions et la latence de Windows.", false, () => Host.OpenDialog(new LiveMonForm(Host.Log)));
            AddCard("🧪", "Benchmark matériel", "Teste les performances de ton CPU et de ton GPU.", false, () => Host.OpenDialog(new BenchForm(Host.Log)));
            AddCard("📈", "Compteur FPS", "Observe les FPS de ton jeu en temps réel.", false, () => Host.OpenDialog(new FpsMonForm(Host.Log)));
            AddCard("🚄", "Test réseau", "Mesure le débit descendant et la latence.", false, () => Host.OpenDialog(new SpeedTestForm(Host.Log)));
            AddCard("🏁", "Objectif 500 FPS", "Vérifie les réglages de ton écran et de tes jeux.", false, () => Host.OpenDialog(new Fps500Form(Host.Log)));
            AddCard("📊", "Overlay matériel", "Affiche CPU, GPU et températures dans le jeu.", false, () => Host.OpenDialog(new StatsOverlayForm(Host.Log)));
            AddCard("🎮", "Test manette", "Vérifie la détection et la fréquence des entrées.", false, () => Host.OpenDialog(new ControllerForm(Host.Log)));
            AddCard("🎥", "Streaming", "Configure RTSS, OBS et NVIDIA pour le stream.", false, () => Host.OpenDialog(new StreamGuideForm(Host.Log)));
            AddCard("🧩", "BIOS & mémoire", "XMP, Resizable BAR et autres réglages BIOS.", false, () => Host.OpenDialog(new BiosGuideForm(Host.Log)));
            AddCard("🧽", "Nettoyage avancé", "Examine les caches de chaque application.", false, () => Host.OpenDialog(new Winapp2Form(Host.Log)));
            AddCard("⚡", "Overclock GPU", "Ajuste les fréquences de la carte graphique.", false, () => Host.OpenDialog(new OverclockForm(Host.Log)));
            AddCard("🎯", "Réticule", "Personnalise le viseur affiché sur tes jeux.", false, () => Host.OpenDialog(new CrosshairForm(Host.Log)));
            AddCard("🎨", "Couleurs & vibrance", "Ajuste les couleurs de ton écran.", false, () => Host.OpenDialog(new ColorFilterForm(Host.Log)));
            Resize += (s, e) => DoLayout();
        }

        private void AddCard(string glyph, string title, string sub, bool soon, Action onClick)
        {
            var card = new Panel();
            card.Tag = "labcard";
            card.Margin = new Padding(0, 0, 16, 16);
            card.BackColor = Color.Transparent;
            card.Cursor = soon ? Cursors.Default : Cursors.Hand;
            bool hover = false;
            card.MouseEnter += (s, e) => { hover = true; card.Invalidate(); };
            card.MouseLeave += (s, e) => { hover = false; card.Invalidate(); };
            card.Paint += (s, e) =>
            {
                Graphics g = e.Graphics;
                var r = ((Panel)s).ClientRectangle;
                FpsUi.PaintCard(g, r, hover && !soon ? FpsUi.CardHi : FpsUi.Card,
                    hover && !soon ? FpsUi.GoldDim : FpsUi.Border, 10f);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                Color ink = soon ? FpsUi.Dim2 : FpsUi.Ink;
                // Titre borné : sur les cartes « soon », il s'arrête avant la puce ARRIVE BIENTÔT
                // (sinon un titre long comme « OVERCLOCK MANETTE » passait sous la puce).
                int titleRight = soon ? r.Width - 122 : r.Width - 20;
                TextRenderer.DrawText(g, title, FpsUi.H3, new Rectangle(54, 20, Math.Max(40, titleRight - 54), 24), ink,
                    TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, sub, FpsUi.Body, new Rectangle(20, 56, r.Width - 40, 44), FpsUi.Dim,
                    TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, glyph, FpsUi.Glyph, new Rectangle(18, 17, 26, 28), soon ? FpsUi.Dim2 : FpsUi.Gold,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                if (!soon)
                    TextRenderer.DrawText(g, "Ouvrir  →", FpsUi.Small, new Point(20, r.Height - 30), FpsUi.Gold, TextFormatFlags.NoPadding);
                if (soon)
                {
                    string tag = "ARRIVE BIENTÔT";
                    var ts = TextRenderer.MeasureText(g, tag, FpsUi.Small);
                    var chip = new Rectangle(r.Width - ts.Width - 34, 18, ts.Width + 16, 22);
                    using (var path = FpsUi.Round(chip, 11f)) using (var br = new SolidBrush(Color.FromArgb(30, 33, 31))) g.FillPath(br, path);
                    TextRenderer.DrawText(g, tag, FpsUi.Small, chip, FpsUi.Dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            };
            if (!soon && onClick != null) card.Click += (s, e) => onClick();
            _grid.Controls.Add(card);
        }

        private void DoLayout()
        {
            if (_grid == null) return;
            _grid.SetBounds(34, 106, ClientSize.Width - 50, Math.Max(100, ClientSize.Height - 122));
            int available = Math.Max(280, _grid.ClientSize.Width - SystemInformation.VerticalScrollBarWidth);
            int cols = Math.Max(1, available / 300);
            int w = available / cols - 16;
            _grid.SuspendLayout();
            foreach (Control c in _grid.Controls)
            {
                c.Size = new Size(w, 144);
            }
            _grid.ResumeLayout();
        }

        public override void OnShown() { DoLayout(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            PaintTitle(e.Graphics, "PERFORMANCES & OUTILS", "Mesure les résultats, identifie les limites de ton PC et prépare tes sessions.");
        }
    }
}
