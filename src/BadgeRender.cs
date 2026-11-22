using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>Rendu partagé d'un badge : forme selon son type (hexagone/cercle/bouclier/étoile/
    /// losange) et emblème vectoriel centré sur la forme. Utilisé par la page Collection,
    /// la fiche de détail et le toast de déblocage.</summary>
    internal static class BadgeRender
    {
        // Emblèmes vectoriels : mêmes tracés dans la collection, les exports et les notifications.
        public static void DrawShape(Graphics g, int x, int y, int size, string glyph, bool unlocked, Color tier, int shape)
        {
            if (size < 8) return;
            var state = g.Save(); g.SmoothingMode = SmoothingMode.AntiAlias;
            Color metal = unlocked ? tier : Color.FromArgb(90, 92, 89);
            using (var shadow = ShapePath(x + 1, y + 3, size, shape))
            using (var brush = new SolidBrush(Color.FromArgb(80, Color.Black))) g.FillPath(brush, shadow);
            using (var outer = ShapePath(x, y, size, shape))
            {
                using (var brush = new LinearGradientBrush(new Rectangle(x, y, size, size), Mix(metal, Color.White, .35f), Mix(metal, Color.Black, .7f), 55f)) g.FillPath(brush, outer);
                using (var pen = new Pen(unlocked ? metal : FpsUi.Border, Math.Max(1, size / 80f))) g.DrawPath(pen, outer);
            }
            int inset = Math.Max(4, size / 12);
            using (var inner = ShapePath(x + inset, y + inset, size - inset * 2, shape))
            {
                using (var brush = new LinearGradientBrush(new Rectangle(x, y, size, size), unlocked ? Mix(FpsUi.Card, tier, .14f) : FpsUi.Card, FpsUi.RailBg, 90f)) g.FillPath(brush, inner);
                using (var pen = new Pen(Color.FromArgb(unlocked ? 130 : 55, metal), Math.Max(1, size / 120f))) g.DrawPath(pen, inner);
            }
            int symbol = (int)(size * (shape == 3 ? .35f : shape == 4 ? .40f : .46f));
            int dy = shape == 2 ? -(int)(size * .05f) : 0;
            DrawGlyph(g, glyph, new Rectangle(x, y + dy, size, size), symbol, metal);
            g.Restore(state);
        }

        public static void DrawGlyph(Graphics g, string glyph, Rectangle r, int px, Color color)
        {
            var state = g.Save();
            g.TranslateTransform(r.X + (r.Width - px) / 2f, r.Y + (r.Height - px) / 2f);
            g.ScaleTransform(px / 32f, px / 32f); g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(color, 1.9f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var brush = new SolidBrush(color))
            {
                switch (glyph)
                {
                    case "🔧": case "🔩":
                        g.DrawArc(pen, 16, 2, 13, 13, 0, 265); g.DrawLines(pen, new[] { new PointF(17, 12), new PointF(4, 25), new PointF(7, 28), new PointF(21, 15) });
                        g.DrawLine(pen, 29, 8, 23, 10); g.DrawLine(pen, 23, 10, 22, 3); break;
                    case "🚀":
                        g.DrawBezier(pen, 10, 20, 9, 12, 18, 3, 27, 4); g.DrawBezier(pen, 27, 4, 28, 13, 20, 23, 12, 22);
                        g.DrawEllipse(pen, 18, 9, 5, 5); g.DrawLines(pen, new[] { new Point(10, 14), new Point(4, 16), new Point(4, 23), new Point(10, 20) });
                        g.DrawLines(pen, new[] { new Point(18, 22), new Point(16, 28), new Point(9, 28), new Point(12, 22) }); g.DrawLine(pen, 8, 24, 4, 28); break;
                    case "⚙": case "💯":
                        g.DrawEllipse(pen, 7, 7, 18, 18); g.DrawEllipse(pen, 12, 12, 8, 8);
                        for (int i = 0; i < 8; i++) { double a = i * Math.PI / 4; g.DrawLine(pen, 16 + (float)Math.Cos(a) * 10, 16 + (float)Math.Sin(a) * 10, 16 + (float)Math.Cos(a) * 14, 16 + (float)Math.Sin(a) * 14); } break;
                    case "🛡": case "🩺":
                        g.DrawPolygon(pen, new[] { new Point(16, 3), new Point(27, 7), new Point(25, 21), new Point(16, 29), new Point(7, 21), new Point(5, 7) });
                        g.DrawLines(pen, new[] { new Point(10, 15), new Point(14, 19), new Point(22, 11) }); break;
                    case "❤":
                        using (var path = new GraphicsPath()) { path.AddBezier(16, 10, 6, -1, -1, 13, 16, 28); path.AddBezier(16, 28, 33, 13, 26, -1, 16, 10); g.DrawPath(pen, path); }
                        g.DrawLines(pen, new[] { new Point(7, 16), new Point(12, 16), new Point(15, 11), new Point(18, 21), new Point(21, 16), new Point(26, 16) }); break;
                    case "🏆":
                        g.DrawLines(pen, new[] { new Point(8, 4), new Point(24, 4), new Point(22, 17), new Point(16, 21), new Point(10, 17), new Point(8, 4) });
                        g.DrawArc(pen, 2, 6, 10, 11, 80, 190); g.DrawArc(pen, 20, 6, 10, 11, 270, 190); g.DrawLine(pen, 16, 21, 16, 28); g.DrawLine(pen, 10, 28, 22, 28); break;
                    case "🌟": case "🏅":
                        using (var star = ShapePath(4, 4, 24, 3)) g.DrawPath(pen, star);
                        g.DrawLine(pen, 3, 3, 5, 5); g.DrawLine(pen, 27, 3, 29, 1); g.DrawLine(pen, 28, 28, 30, 30); break;
                    case "🎮":
                        using (var path = new GraphicsPath()) { path.AddBezier(9, 9, 3, 8, 0, 25, 6, 25); path.AddLine(6, 25, 12, 21); path.AddLine(12, 21, 20, 21); path.AddBezier(20, 21, 34, 33, 30, 9, 23, 9); path.CloseFigure(); g.DrawPath(pen, path); }
                        g.DrawLine(pen, 7, 16, 13, 16); g.DrawLine(pen, 10, 13, 10, 19); g.FillEllipse(brush, 21, 13, 3, 3); g.FillEllipse(brush, 24, 17, 3, 3); break;
                    case "📚": case "🗄": case "archive":
                        g.DrawRectangle(pen, 4, 4, 6, 24); g.DrawRectangle(pen, 13, 4, 6, 24); g.DrawPolygon(pen, new[] { new Point(21, 6), new Point(26, 5), new Point(31, 27), new Point(26, 28) });
                        g.DrawLine(pen, 5, 9, 9, 9); g.DrawLine(pen, 14, 23, 18, 23); break;
                    case "🎯": case "🔍":
                        g.DrawEllipse(pen, 4, 3, 20, 20); g.DrawEllipse(pen, 10, 9, 8, 8); g.DrawLine(pen, 21, 21, 29, 29); break;
                    case "⚡":
                        g.DrawPolygon(pen, new[] { new Point(18, 2), new Point(5, 18), new Point(15, 18), new Point(12, 30), new Point(28, 12), new Point(18, 12) }); break;
                    case "📋": case "calendar":
                        using (var path = FpsUi.Round(new Rectangle(5, 6, 22, 23), 3)) g.DrawPath(pen, path);
                        g.DrawLine(pen, 10, 2, 10, 10); g.DrawLine(pen, 22, 2, 22, 10); g.DrawLine(pen, 5, 13, 27, 13);
                        g.DrawLines(pen, new[] { new Point(10, 21), new Point(14, 25), new Point(22, 17) }); break;
                    case "🔬":
                        g.DrawPolygon(pen, new[] { new Point(13, 3), new Point(20, 6), new Point(14, 18), new Point(8, 15) }); g.DrawArc(pen, 9, 10, 18, 18, -80, 240); g.DrawLine(pen, 5, 28, 27, 28); g.DrawLine(pen, 5, 21, 15, 21); break;
                    case "sliders":
                        for (int i = 0; i < 3; i++) { int y = 7 + i * 9, x = i == 1 ? 21 : 11; g.DrawLine(pen, 3, y, 29, y); g.FillRectangle(brush, x - 2, y - 4, 4, 8); } break;
                    case "squad":
                        g.DrawEllipse(pen, 12, 3, 8, 8); g.DrawArc(pen, 7, 14, 18, 18, 180, 180); g.DrawEllipse(pen, 2, 9, 6, 6); g.DrawEllipse(pen, 24, 9, 6, 6); g.DrawArc(pen, 0, 19, 10, 12, 180, 120); g.DrawArc(pen, 22, 19, 10, 12, 240, 120); break;
                    case "compass":
                        g.DrawEllipse(pen, 3, 3, 26, 26); g.DrawPolygon(pen, new[] { new Point(23, 8), new Point(19, 19), new Point(8, 24), new Point(12, 12) }); g.DrawLine(pen, 12, 12, 19, 19); break;
                    case "balance":
                        g.DrawLine(pen, 16, 3, 16, 28); g.DrawLine(pen, 5, 8, 27, 8); g.DrawLine(pen, 10, 28, 22, 28);
                        g.DrawPolygon(pen, new[] { new Point(7, 9), new Point(2, 20), new Point(12, 20) }); g.DrawPolygon(pen, new[] { new Point(25, 9), new Point(20, 20), new Point(30, 20) }); break;
                    default:
                        g.DrawPolygon(pen, new[] { new Point(8, 5), new Point(24, 5), new Point(30, 13), new Point(16, 29), new Point(2, 13) }); g.DrawLine(pen, 2, 13, 30, 13); g.DrawLines(pen, new[] { new Point(8, 5), new Point(12, 13), new Point(16, 29), new Point(20, 13), new Point(24, 5) }); break;
                }
            }
            g.Restore(state);
        }

        private static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
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
