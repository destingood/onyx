using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace BTOptimizer
{
    /// <summary>
    /// Logo DesTinGOOD : croix médicale (santé / « bloc opératoire ») fusionnée avec un éclair
    /// (vitesse / FPS). Dessiné en vectoriel → net à toute taille (rail, icône de fenêtre, à propos).
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
                using (var bg = new LinearGradientBrush(box, Color.FromArgb(20, 24, 22), Color.FromArgb(9, 12, 11), 60f))
                {
                    g.FillPath(bg, path);
                    using (var pen = new Pen(Color.FromArgb(120, accent), Math.Max(1f, w * 0.045f))) g.DrawPath(pen, path);
                }
            }

            // Croix médicale (néon vif) — deux barres arrondies centrées.
            float t = w * 0.15f;                         // demi-épaisseur d'une barre
            float cx = x + w * 0.5f, cy = y + h * 0.5f;
            float armV = h * 0.33f, armH = w * 0.33f;
            var vert = new RectangleF(cx - t, cy - armV, t * 2, armV * 2);
            var horz = new RectangleF(cx - armH, cy - t, armH * 2, t * 2);
            using (var cb = new SolidBrush(accent))
            using (var pv = RoundRect(vert, t * 0.5f))
            using (var ph = RoundRect(horz, t * 0.5f))
            { g.FillPath(cb, pv); g.FillPath(cb, ph); }

            // Éclair PAR-DESSUS, blanc électrique + fin liseré sombre → « vitesse » lisible sur la croix.
            var bolt = new[]
            {
                new PointF(x + .585f * w, y + .16f * h), new PointF(x + .40f * w, y + .53f * h),
                new PointF(x + .515f * w, y + .53f * h), new PointF(x + .43f * w, y + .85f * h),
                new PointF(x + .64f * w, y + .46f * h), new PointF(x + .525f * w, y + .46f * h),
                new PointF(x + .61f * w, y + .16f * h)
            };
            using (var bb = new SolidBrush(Color.FromArgb(240, 244, 255, 250))) g.FillPolygon(bb, bolt);
            using (var bp = new Pen(Color.FromArgb(150, 6, 10, 8), Math.Max(1f, w * 0.02f))) g.DrawPolygon(bp, bolt);

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
