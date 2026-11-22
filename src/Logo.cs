using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace BTOptimizer
{
    /// <summary>Identité ONYX : O facetté et signal stabilisé. Tracés partagés par l'UI et les exports.</summary>
    internal static class Logo
    {
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);

        public static void Draw(Graphics g, RectangleF box, Color accent, bool withPlate)
        {
            if (box.Width <= 0 || box.Height <= 0) return;
            var state = g.Save();
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float size = Math.Min(box.Width, box.Height);
                g.TranslateTransform(box.X + (box.Width - size) / 2, box.Y + (box.Height - size) / 2);
                g.ScaleTransform(size / 100f, size / 100f);
                if (withPlate)
                {
                    using (var plate = RoundRect(new RectangleF(1, 1, 98, 98), 22))
                    using (var fill = new LinearGradientBrush(new RectangleF(0, 0, 100, 100), Color.FromArgb(36, 34, 29), Color.FromArgb(12, 13, 13), 65f))
                    {
                        g.FillPath(fill, plate);
                        using (var edge = new Pen(Color.FromArgb(97, 82, 53), 1.2f)) g.DrawPath(edge, plate);
                    }
                    g.TranslateTransform(13, 13); g.ScaleTransform(.74f, .74f);
                }
                // Une bande pleine conserve la silhouette à 16 px, sans traits décoratifs fragiles.
                using (var ring = new GraphicsPath(FillMode.Alternate))
                {
                    ring.AddPolygon(Octagon(5, 95, 25));
                    ring.AddPolygon(Octagon(17, 83, 31));
                    using (var metal = new LinearGradientBrush(new RectangleF(0, 0, 100, 100), Light(accent), accent, 55f)) g.FillPath(metal, ring);
                }
                if (size >= 32)
                {
                    using (var facet = new SolidBrush(Color.FromArgb(65, Color.Black)))
                        g.FillPolygon(facet, new[] { new PointF(95, 75), new PointF(75, 95), new PointF(25, 95), new PointF(31, 83), new PointF(69, 83), new PointF(83, 69) });
                }
                // Un seul pic, puis une ligne stable : lisible même dans la zone de notification.
                using (var signal = new Pen(accent, size < 24 ? 9f : 7f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLines(signal, new[] { new PointF(27, 52), new PointF(36, 52), new PointF(43, 35), new PointF(54, 65), new PointF(62, 48), new PointF(73, 48) });
            }
            finally { g.Restore(state); }
        }

        /// <summary>Logotype géométrique sans dépendance à une police, ratio 4,55:1.</summary>
        public static void DrawWordmark(Graphics g, RectangleF box, Color color)
        {
            if (box.Width <= 0 || box.Height <= 0) return;
            var state = g.Save();
            try
            {
                float scale = Math.Min(box.Width / 182f, box.Height / 40f);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TranslateTransform(box.X, box.Y + (box.Height - 40 * scale) / 2);
                g.ScaleTransform(scale, scale);
                using (var pen = new Pen(color, 5.5f) { LineJoin = LineJoin.Bevel, StartCap = LineCap.Square, EndCap = LineCap.Square })
                {
                    g.DrawPolygon(pen, new[] { new PointF(11, 4), new PointF(29, 4), new PointF(36, 11), new PointF(36, 29), new PointF(29, 36), new PointF(11, 36), new PointF(4, 29), new PointF(4, 11) });
                    g.DrawLines(pen, new[] { new PointF(53, 36), new PointF(53, 4), new PointF(85, 36), new PointF(85, 4) });
                    g.DrawLines(pen, new[] { new PointF(101, 4), new PointF(117, 21), new PointF(133, 4) });
                    g.DrawLine(pen, 117, 21, 117, 36);
                    g.DrawLine(pen, 149, 4, 178, 36); g.DrawLine(pen, 178, 4, 149, 36);
                }
            }
            finally { g.Restore(state); }
        }
        public static Icon MakeIcon(int size, Color accent)
        {
            using (var bmp = new Bitmap(size, size))
            {
                using (var g = Graphics.FromImage(bmp)) { g.Clear(Color.Transparent); Draw(g, new RectangleF(0, 0, size, size), accent, true); }
                IntPtr handle = bmp.GetHicon();
                try { using (var borrowed = Icon.FromHandle(handle)) return (Icon)borrowed.Clone(); }
                finally { DestroyIcon(handle); }
            }
        }
        private static PointF[] Octagon(float low, float high, float cut)
        {
            return new[] { new PointF(cut, low), new PointF(100 - cut, low), new PointF(high, cut), new PointF(high, 100 - cut),
                new PointF(100 - cut, high), new PointF(cut, high), new PointF(low, 100 - cut), new PointF(low, cut) };
        }
        private static Color Light(Color c) { return Color.FromArgb(Math.Min(255, c.R + 25), Math.Min(255, c.G + 33), Math.Min(255, c.B + 51)); }
        private static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            var p = new GraphicsPath(); float d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }
    }
}
