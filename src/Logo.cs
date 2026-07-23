using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace BTOptimizer
{
    /// <summary>
    /// Logo Fluide : une frametime qui devient parfaitement plate — le chaos (stutter)
    /// à gauche, la ligne idéale à droite, le point de mesure au bout. C'est la promesse
    /// mesurable du produit (« Mesuré, pas promis. »). Vectoriel → net à toute taille
    /// (rail, icône de fenêtre, à propos). Même géométrie que le favicon de la landing.
    /// </summary>
    internal static class Logo
    {
        /// <summary>Dessine la marque dans 'box'. withPlate = plaque arrondie sombre + liseré néon (icône).</summary>
        public static void Draw(Graphics g, RectangleF box, Color accent, bool withPlate)
        {
            var sm = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
            float x = box.X, y = box.Y, w = box.Width, h = box.Height;

            if (withPlate)
            {
                using (var path = RoundRect(box, w * 0.22f))
                using (var bg = new SolidBrush(Color.FromArgb(11, 14, 17)))
                {
                    g.FillPath(bg, path);
                    using (var pen = new Pen(Color.FromArgb(150, accent), Math.Max(1f, w * 0.045f))) g.DrawPath(pen, path);
                }
            }

            // La frametime : pics (stutter) qui se résolvent en ligne parfaitement plate.
            var line = new[]
            {
                new PointF(x + .14f * w,  y + .52f * h),
                new PointF(x + .205f * w, y + .33f * h),
                new PointF(x + .27f * w,  y + .67f * h),
                new PointF(x + .33f * w,  y + .40f * h),
                new PointF(x + .39f * w,  y + .57f * h),
                new PointF(x + .445f * w, y + .50f * h),
                new PointF(x + .80f * w,  y + .50f * h)
            };
            using (var pen = new Pen(accent, Math.Max(1.5f, w * 0.075f)))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                g.DrawLines(pen, line);
            }
            // Le point d'arrivée : la mesure stabilisée.
            float r = Math.Max(1.5f, w * 0.05f);
            using (var b = new SolidBrush(accent))
                g.FillEllipse(b, x + .80f * w - r, y + .50f * h - r, r * 2, r * 2);

            g.SmoothingMode = sm;
        }

        /// <summary>Icône de fenêtre/tray dessinée à partir du logo. (Un HICON est laissé vivant pour
        /// la durée de l'app — négligeable.)</summary>
        public static Icon MakeIcon(int size, Color accent)
        {
            using (var bmp = new Bitmap(size, size))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    Draw(g, new RectangleF(0, 0, size, size), accent, true);
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        private static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
