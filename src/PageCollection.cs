using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BTOptimizer
{
    // Page Collection : vitrine de badges / succes (facon FPSDoctor).
    internal class PageCollection : FpsPage
    {
        public PageCollection(DashboardForm host) : base(host)
        {
            Resize += (s, e) => Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            PaintTitle(g, "COLLECTION", "Ta vitrine de succès et de badges.");

            int L = 34, top = 96;
            // Vitrine (gauche).
            int vw = 300, vh = 300;
            FpsUi.PaintCard(g, new Rectangle(L, top, vw, vh), FpsUi.Card, FpsUi.Border, 14f);
            Image badge = Assets.BadgePremierSoin;
            if (badge != null) { int bs = 150; g.DrawImage(badge, L + (vw - bs) / 2, top + 50, bs, bs); }
            TextRenderer.DrawText(g, "Premiers Soins", FpsUi.H3, new Rectangle(L, top + 210, vw, 24), FpsUi.Ink, TextFormatFlags.HorizontalCenter);
            TextRenderer.DrawText(g, "Badge débloqué", FpsUi.Small, new Rectangle(L, top + 236, vw, 20), FpsUi.Neon, TextFormatFlags.HorizontalCenter);

            // Grille de slots (droite).
            // En-tête BADGES sous le sous-titre (évite la collision avec le texte de PaintTitle).
            int gx = L + vw + 30, gy = top + 22;
            TextRenderer.DrawText(g, "BADGES", FpsUi.H3, new Point(gx, top - 6), FpsUi.Ink, TextFormatFlags.NoPadding);
            using (var pen = new Pen(FpsUi.Neon, 2f)) g.DrawLine(pen, gx, top + 14, gx + 64, top + 14);
            int cell = 78, gap = 14, cols = 7;
            // Limite droite : évite de peindre des emplacements sous la mascotte (coin bas-droit).
            int right = Host != null ? Host.ContentRight(34) : ClientSize.Width - 34;
            for (int i = 0; i < 12; i++)
            {
                int col = i % cols, row = i / cols;
                var r = new Rectangle(gx + col * (cell + gap), gy + row * (cell + gap), cell, cell);
                if (gx + col * (cell + gap) + cell > right) continue;
                FpsUi.PaintCard(g, r, Color.FromArgb(20, 22, 21), FpsUi.Border, 10f);
            }
            TextRenderer.DrawText(g, "Débloque des badges en appliquant des optimisations et en lançant des Check Up+.",
                FpsUi.Small, new Point(gx, gy + 2 * (cell + gap) + 6), FpsUi.Dim, TextFormatFlags.NoPadding);
        }
    }
}
