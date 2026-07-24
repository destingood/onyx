using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace BTOptimizer
{
    /// <summary>
    /// Logo ONYX : un anneau d'or (le « O », l'écrin) serti de la frametime qui devient
    /// parfaitement plate — le chaos (stutter) à gauche, la ligne idéale à droite, le point
    /// de mesure au bout. La promesse mesurable du produit (« Mesuré, pas promis. ») gravée
    /// dans le métal. Vectoriel → net à toute taille (rail, icône de fenêtre, à propos).
    /// </summary>
    internal static class Logo
    {
        /// <summary>Dessine la marque dans 'box'. withPlate = plaque arrondie carbone + liseré or (icône).</summary>
        public static void Draw(Graphics g, RectangleF box, Color accent, bool withPlate)
        {
            var sm = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
            float x = box.X, y = box.Y, w = box.Width, h = box.Height;

            if (withPlate)
            {
                using (var path = RoundRect(box, w * 0.22f))
                using (var bg = new SolidBrush(Color.FromArgb(14, 12, 10)))
                {
                    g.FillPath(bg, path);
                    using (var pen = new Pen(Color.FromArgb(150, accent), Math.Max(1f, w * 0.045f))) g.DrawPath(pen, path);
                }
                // La gravure respire à l'intérieur de la plaque.
                float inset = w * 0.14f;
                x += inset; y += inset; w -= inset * 2; h -= inset * 2;
            }

            float cx = x + w / 2f, cy = y + h / 2f;
            float ring = Math.Max(1.5f, w * 0.085f);          // épaisseur du métal
            float rad = (Math.Min(w, h) - ring) / 2f;         // rayon au centre du trait

            // L'anneau : or plein, puis reflet clair en haut-gauche = métal poli.
            var rr = new RectangleF(cx - rad, cy - rad, rad * 2, rad * 2);
            using (var pen = new Pen(accent, ring)) g.DrawEllipse(pen, rr);
            using (var pen = new Pen(Color.FromArgb(170, Glint(accent)), ring * 0.72f))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                g.DrawArc(pen, rr, 195f, 95f);
            }

            // La frametime sertie : chaos → plat, contenue dans l'anneau.
            float ix = cx - rad, iw = rad * 2;                // fenêtre horizontale intérieure
            var line = new[]
            {
                new PointF(ix + .18f * iw, cy + .02f * iw),
                new PointF(ix + .27f * iw, cy - .13f * iw),
                new PointF(ix + .36f * iw, cy + .12f * iw),
                new PointF(ix + .44f * iw, cy - .07f * iw),
                new PointF(ix + .52f * iw, cy + .04f * iw),
                new PointF(ix + .58f * iw, cy),
                new PointF(ix + .80f * iw, cy)
            };
            using (var pen = new Pen(accent, Math.Max(1.4f, w * 0.062f)))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                g.DrawLines(pen, line);
            }
            // Le point d'arrivée : la mesure stabilisée.
            float r = Math.Max(1.4f, w * 0.052f);
            using (var b = new SolidBrush(accent))
                g.FillEllipse(b, ix + .80f * iw - r, cy - r, r * 2, r * 2);

            g.SmoothingMode = sm;
        }

        /// <summary>Reflet métallique dérivé de l'accent (poussé vers l'ivoire).</summary>
        private static Color Glint(Color c)
        {
            return Color.FromArgb(
                Math.Min(255, c.R + 45),
                Math.Min(255, c.G + 52),
                Math.Min(255, c.B + 78));
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
