using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>Rendu partagé d'un badge : forme selon son type (hexagone/cercle/bouclier/étoile/
    /// losange) + emoji centré sur le centre VISUEL de la forme. Utilisé par la page Collection,
    /// la fiche de détail et le toast de déblocage.</summary>
    internal static class BadgeRender
    {
        // Police emoji par taille en pixels (cache : une par taille de badge rencontrée).
        private static readonly Dictionary<int, Font> _fonts = new Dictionary<int, Font>();
        private static Font GlyphFont(int px)
        {
            Font f;
            if (!_fonts.TryGetValue(px, out f))
            {
                f = new Font("Segoe UI Emoji", Math.Max(8, px), FontStyle.Regular, GraphicsUnit.Pixel);
                _fonts[px] = f;
            }
            return f;
        }

        public static void DrawShape(Graphics g, int x, int y, int size, string glyph, bool unlocked, Color tier, int shape)
        {
            Color fill = unlocked ? Color.FromArgb(30, tier.R, tier.G, tier.B) : Color.FromArgb(20, 22, 21);
            Color edge = unlocked ? tier : FpsUi.Border;
            using (var path = ShapePath(x, y, size, shape))
            using (var br = new SolidBrush(fill))
            using (var pen = new Pen(edge, unlocked ? 2f : 1f))
            {
                g.FillPath(br, path);
                g.DrawPath(pen, path);
            }

            // Glyphe proportionnel à la surface UTILE de chaque forme (sinon l'emoji déborde de
            // l'hexagone/du losange), centré sur le centre VISUEL : le bouclier a sa masse en
            // haut (pointe en bas), son glyphe remonte donc un peu.
            float k; int dy;
            switch (shape)
            {
                case 1: k = 0.50f; dy = 0; break;                        // cercle
                case 2: k = 0.46f; dy = -(int)(size * 0.09f); break;     // bouclier (masse en haut)
                case 3: k = 0.34f; dy = 0; break;                        // étoile (branches fines)
                case 4: k = 0.38f; dy = 0; break;                        // losange (pointes)
                default: k = 0.44f; dy = 0; break;                       // hexagone
            }
            DrawGlyph(g, glyph, new Rectangle(x, y + dy, size, size), (int)(size * k), unlocked ? tier : FpsUi.Dim2);
        }

        /// <summary>Emoji centré dans un rectangle. NoPadding est indispensable : sans lui, GDI
        /// ajoute des marges asymétriques et l'encre part en bas à gauche du centre.</summary>
        public static void DrawGlyph(Graphics g, string glyph, Rectangle r, int px, Color color)
        {
            TextRenderer.DrawText(g, glyph, GlyphFont(px), r, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        public static GraphicsPath ShapePath(int x, int y, int size, int shape)
        {
            var p = new GraphicsPath();
            var c = new PointF(x + size / 2f, y + size / 2f);
            float r = size / 2f - 2;
            switch (shape)
            {
                case 1: // cercle
                    p.AddEllipse(c.X - r, c.Y - r, r * 2, r * 2);
                    break;
                case 2: // bouclier
                    {
                        float w = r * 1.7f, h = r * 2f, l = c.X - w / 2, t = c.Y - h / 2;
                        p.AddArc(l, t, w * 0.5f, h * 0.5f, 180, 90);
                        p.AddArc(l + w * 0.5f, t, w * 0.5f, h * 0.5f, 270, 90);
                        p.AddLine(l + w, t + h * 0.45f, c.X, t + h);
                        p.AddLine(c.X, t + h, l, t + h * 0.45f);
                        p.CloseFigure();
                        break;
                    }
                case 3: // étoile 5 branches
                    {
                        var pts = new PointF[10];
                        for (int i = 0; i < 10; i++)
                        {
                            double a = Math.PI / 5 * i - Math.PI / 2;
                            float rr = (i % 2 == 0) ? r : r * 0.44f;
                            pts[i] = new PointF(c.X + (float)Math.Cos(a) * rr, c.Y + (float)Math.Sin(a) * rr);
                        }
                        p.AddPolygon(pts);
                        break;
                    }
                case 4: // losange
                    p.AddPolygon(new[] { new PointF(c.X, c.Y - r), new PointF(c.X + r, c.Y), new PointF(c.X, c.Y + r), new PointF(c.X - r, c.Y) });
                    break;
                default: // hexagone (pointe en haut)
                    {
                        var pts = new PointF[6];
                        for (int i = 0; i < 6; i++) { double a = Math.PI / 180 * (60 * i - 90); pts[i] = new PointF(c.X + (float)Math.Cos(a) * r, c.Y + (float)Math.Sin(a) * r); }
                        p.AddPolygon(pts);
                        break;
                    }
            }
            return p;
        }
    }
}
