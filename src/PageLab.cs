using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BTOptimizer
{
    // Page Laboratoire : modules optionnels (viseur, filtre couleur, compteur
    // FPS, overclock, speed test). Cartes facon FPSDoctor.
    internal class PageLab : FpsPage
    {
        public PageLab(DashboardForm host) : base(host)
        {
            Build();
        }

        private void Build()
        {
            AddCard("🎯", "CROSSHAIR", "Réticule personnalisé par-dessus tes jeux.", false, () => Host.OpenDialog(new CrosshairForm(Host.Log)));
            AddCard("🎨", "FILTRE DE COULEUR", "Vibrance / saturation façon panneau NVIDIA.", false, () => Host.OpenDialog(new ColorFilterForm(Host.Log)));
            AddCard("📈", "COMPTEUR FPS", "FPS en direct par jeu (façon PresentMon).", false, () => Host.OpenDialog(new FpsMonForm(Host.Log)));
            AddCard("⚡", "OVERCLOCK GPU", "Overclock automatique et sûr de la carte.", false, () => Host.OpenDialog(new OverclockForm(Host.Log)));
            AddCard("🚄", "SPEED TEST", "Débit descendant + latence réels.", false, () => Host.OpenDialog(new SpeedTestForm(Host.Log)));
            AddCard("🎮", "TEST MANETTE", "Détection + polling rate réel de ta manette.", false, () => Host.OpenDialog(new ControllerForm(Host.Log)));
            Resize += (s, e) => DoLayout();
        }

        private void AddCard(string glyph, string title, string sub, bool soon, Action onClick)
        {
            var card = new Panel();
            card.Tag = "labcard";
            card.BackColor = Color.Transparent;
            card.Cursor = soon ? Cursors.Default : Cursors.Hand;
            card.Paint += (s, e) =>
            {
                Graphics g = e.Graphics;
                var r = ((Panel)s).ClientRectangle;
                FpsUi.PaintCard(g, r, soon ? Color.FromArgb(13, 15, 14) : FpsUi.Card, FpsUi.Border, 14f);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                Color ink = soon ? FpsUi.Dim2 : FpsUi.Ink;
                // Titre borné : sur les cartes « soon », il s'arrête avant la puce ARRIVE BIENTÔT
                // (sinon un titre long comme « OVERCLOCK MANETTE » passait sous la puce).
                int titleRight = soon ? r.Width - 122 : r.Width - 20;
                TextRenderer.DrawText(g, title, FpsUi.H2, new Rectangle(22, 18, Math.Max(40, titleRight - 22), 24), ink,
                    TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, sub, FpsUi.Small, new Rectangle(22, 48, r.Width - 44, 20), FpsUi.Dim, TextFormatFlags.NoPadding);
                // Grande icone centrale.
                TextRenderer.DrawText(g, glyph, FpsUi.GlyphXL, new Rectangle(0, 40, r.Width, r.Height - 40), soon ? FpsUi.Dim2 : FpsUi.Ink,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
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
            Controls.Add(card);
        }

        private void DoLayout()
        {
            int L = 34, top = 110, gap = 18, cols = 3;
            int w = (ClientSize.Width - L * 2 - gap * (cols - 1)) / cols;
            int count = 0;
            foreach (Control c in Controls) if (c.Tag is string && (string)c.Tag == "labcard") count++;
            int rows = Math.Max(1, (count + cols - 1) / cols);
            // Hauteur des cartes calculée pour remplir l'espace AU-DESSUS de la mascotte
            // (coin bas-droit) : plus de chevauchement de la dernière rangée en fenêtre basse.
            int bottom = Host != null ? Host.ContentBottom(20) : ClientSize.Height - 20;
            int hgt = Math.Max(150, (bottom - top - gap * (rows - 1)) / rows);
            int i = 0;
            foreach (Control c in Controls)
            {
                if (!(c.Tag is string) || (string)c.Tag != "labcard") continue;
                int col = i % cols, row = i / cols;
                c.SetBounds(L + col * (w + gap), top + row * (hgt + gap), w, hgt);
                i++;
            }
        }

        public override void OnShown() { DoLayout(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            PaintTitle(e.Graphics, "LABORATOIRE", "Expérimente des modules optionnels pour personnaliser ton expérience.");
        }
    }
}
