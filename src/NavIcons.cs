using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace BTOptimizer
{
    /// <summary>
    /// Icônes de la barre latérale, dessinées AU TRAIT (vectoriel) plutôt qu'en emoji.
    /// Les emoji dépendent de la police système, se colorent mal et se centrent mal ; ici tout est
    /// tracé en GDI+ dans un repère logique 24×24 mis à l'échelle → net à n'importe quelle taille
    /// et à n'importe quel DPI, et la couleur suit l'état (actif / survol / repos).
    /// Aucun fichier d'asset : rien à embarquer, rien à charger.
    /// </summary>
    internal static class NavIcons
    {
        public const int Dashboard = 0, Optimisations = 1, Jeux = 2, CheckUp = 3,
                         Laboratoire = 4, Collection = 5, Consultation = 6, Systeme = 7, Outils = 8;

        /// <summary>Dessine l'icône <paramref name="id"/> centrée dans <paramref name="box"/>.</summary>
        public static void Draw(Graphics g, int id, RectangleF box, Color color)
        {
            SmoothingMode oldSm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            GraphicsState st = g.Save();
            try
            {
                float s = Math.Min(box.Width, box.Height) / 24f;
                g.TranslateTransform(box.X + (box.Width - 24f * s) / 2f, box.Y + (box.Height - 24f * s) / 2f);
                g.ScaleTransform(s, s);

                using (var p = new Pen(color, 1.7f))
                using (var fill = new SolidBrush(color))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round;
                    Paint(g, p, fill, id);
                }
            }
            catch { }
            finally { g.Restore(st); g.SmoothingMode = oldSm; }
        }

        private static void Paint(Graphics g, Pen p, Brush fill, int id)
        {
            switch (id)
            {
                case Dashboard:      // maison
                    g.DrawLines(p, Pts(4, 11.5f, 12, 4.5f, 20, 11.5f));
                    g.DrawLines(p, Pts(6.2f, 10, 6.2f, 19.5f, 17.8f, 19.5f, 17.8f, 10));
                    g.DrawLines(p, Pts(10, 19.5f, 10, 14, 14, 14, 14, 19.5f));
                    break;

                case Optimisations:  // fusée
                    g.DrawLines(p, Pts(12, 3, 15.8f, 9, 15.8f, 15.5f, 12, 18.5f, 8.2f, 15.5f, 8.2f, 9, 12, 3));
                    g.DrawEllipse(p, 10.4f, 8.4f, 3.2f, 3.2f);
                    g.DrawLines(p, Pts(8.2f, 13, 5, 17, 8.2f, 16.6f));
                    g.DrawLines(p, Pts(15.8f, 13, 19, 17, 15.8f, 16.6f));
                    g.DrawLine(p, 12, 18.5f, 12, 21);
                    break;

                case Jeux:           // manette
                    using (var body = Round(new RectangleF(2.5f, 8f, 19f, 10f), 5f)) g.DrawPath(p, body);
                    g.DrawLine(p, 6.6f, 13f, 9.6f, 13f);
                    g.DrawLine(p, 8.1f, 11.5f, 8.1f, 14.5f);
                    g.DrawEllipse(p, 14.4f, 10.9f, 2.2f, 2.2f);
                    g.DrawEllipse(p, 16.9f, 13.4f, 2.2f, 2.2f);
                    break;

                case CheckUp:        // cœur + tracé cardiaque
                    using (var h = new GraphicsPath())
                    {
                        h.AddBezier(12, 19.5f, 3.5f, 13.5f, 5.5f, 5.5f, 12, 9.2f);
                        h.AddBezier(12, 9.2f, 18.5f, 5.5f, 20.5f, 13.5f, 12, 19.5f);
                        g.DrawPath(p, h);
                    }
                    g.DrawLines(p, Pts(6.5f, 12.6f, 9.2f, 12.6f, 10.6f, 10.2f, 12.6f, 15f, 14f, 12.6f, 17.5f, 12.6f));
                    break;

                case Laboratoire:    // fiole
                    g.DrawLine(p, 9, 3.5f, 15, 3.5f);
                    g.DrawLines(p, Pts(10, 3.5f, 10, 10, 5, 18.5f, 19, 18.5f, 14, 10, 14, 3.5f));
                    g.DrawLine(p, 7.2f, 14.5f, 16.8f, 14.5f);
                    break;

                case Collection:     // trophée
                    g.DrawLines(p, Pts(8, 4, 8, 9.5f, 12, 13.5f, 16, 9.5f, 16, 4, 8, 4));
                    g.DrawArc(p, 3.6f, 4.4f, 5f, 6f, 90, 180);
                    g.DrawArc(p, 15.4f, 4.4f, 5f, 6f, 270, 180);
                    g.DrawLine(p, 12, 13.5f, 12, 17);
                    g.DrawLines(p, Pts(9, 20, 12, 17, 15, 20));
                    g.DrawLine(p, 8.2f, 20, 15.8f, 20);
                    break;

                case Consultation:   // stéthoscope
                    g.DrawLine(p, 6.5f, 3.8f, 6.5f, 9);
                    g.DrawLine(p, 12.5f, 3.8f, 12.5f, 9);
                    g.DrawArc(p, 6.5f, 6f, 6f, 8f, 0, 180);
                    g.DrawLine(p, 9.5f, 14f, 9.5f, 15.5f);
                    g.DrawLines(p, Pts(9.5f, 15.5f, 14.5f, 15.5f, 14.5f, 14.5f));
                    g.DrawEllipse(p, 12.6f, 10.4f, 6.6f, 6.6f);
                    break;

                case Systeme:        // engrenage
                    g.DrawEllipse(p, 8.2f, 8.2f, 7.6f, 7.6f);
                    for (int k = 0; k < 8; k++)
                    {
                        double a = k * Math.PI / 4.0;
                        float cs = (float)Math.Cos(a), sn = (float)Math.Sin(a);
                        g.DrawLine(p, 12 + cs * 7.6f, 12 + sn * 7.6f, 12 + cs * 10.6f, 12 + sn * 10.6f);
                    }
                    break;

                case Outils:         // trois points
                    g.FillEllipse(fill, 4.6f, 10.8f, 2.6f, 2.6f);
                    g.FillEllipse(fill, 10.7f, 10.8f, 2.6f, 2.6f);
                    g.FillEllipse(fill, 16.8f, 10.8f, 2.6f, 2.6f);
                    break;
            }
        }

        // Confort : construit un tableau de points depuis une suite de coordonnées x,y.
        private static PointF[] Pts(params float[] v)
        {
            var a = new PointF[v.Length / 2];
            for (int i = 0; i < a.Length; i++) a[i] = new PointF(v[i * 2], v[i * 2 + 1]);
            return a;
        }

        private static GraphicsPath Round(RectangleF r, float rad)
        {
            var path = new GraphicsPath();
            float d = rad * 2f;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
