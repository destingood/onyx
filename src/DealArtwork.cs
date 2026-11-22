using System;
using System.Drawing;

namespace BTOptimizer
{
    internal static class DealArtwork
    {
        // Aucune interpolation vers une taille supérieure à la source, ni découpe des titres.
        internal static Rectangle Fit(Size image, Rectangle frame)
        {
            if (image.Width <= 0 || image.Height <= 0 || frame.Width <= 0 || frame.Height <= 0) return Rectangle.Empty;
            double scale = Math.Min(1, Math.Min((double)frame.Width / image.Width, (double)frame.Height / image.Height));
            int width = Math.Max(1, (int)Math.Round(image.Width * scale));
            int height = Math.Max(1, (int)Math.Round(image.Height * scale));
            return new Rectangle(frame.Left + (frame.Width - width) / 2, frame.Top + (frame.Height - height) / 2, width, height);
        }

        internal static Size CacheSize(Size image)
        {
            return Fit(image, new Rectangle(0, 0, 1280, 1280)).Size;
        }
    }
}
