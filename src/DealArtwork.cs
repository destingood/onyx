using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;

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

        /// <summary>Vrai si la jaquette nette ne remplit pas son cadre (portrait, vignette trop petite).</summary>
        internal static bool LeavesBars(Rectangle drawn, Rectangle frame)
        {
            return drawn.Width < frame.Width - 2 || drawn.Height < frame.Height - 2;
        }

        /// <summary>Rectangle centré, au rapport de l'image, qui recouvre tout le cadre : celui du fond flou.</summary>
        internal static Rectangle Fill(Size image, Rectangle frame)
        {
            if (image.Width <= 0 || image.Height <= 0 || frame.Width <= 0 || frame.Height <= 0) return Rectangle.Empty;
            int width = frame.Width, height = frame.Height;
            if ((long)frame.Width * image.Height >= (long)frame.Height * image.Width)
                height = (int)(((long)frame.Width * image.Height + image.Width - 1) / image.Width);
            else
                width = (int)(((long)frame.Height * image.Width + image.Height - 1) / image.Height);
            return new Rectangle(frame.Left + (frame.Width - width) / 2, frame.Top + (frame.Height - height) / 2, width, height);
        }

        /// <summary>Taille de la miniature qui sert de fond : 32 px au plus, c'est son agrandissement qui la floute.</summary>
        internal static Size BackdropSize(Size image)
        {
            if (image.Width <= 0 || image.Height <= 0) return Size.Empty;
            double scale = 32.0 / Math.Max(image.Width, image.Height);
            return new Size(Math.Max(1, (int)Math.Round(image.Width * scale)), Math.Max(1, (int)Math.Round(image.Height * scale)));
        }

        // Une miniature par image : les jaquettes restent en cache, la table les suit sans les retenir.
        private static readonly ConditionalWeakTable<Image, Bitmap> _backdrops = new ConditionalWeakTable<Image, Bitmap>();

        private static Bitmap Backdrop(Image image)
        {
            return _backdrops.GetValue(image, source =>
            {
                Size size = BackdropSize(source.Size);
                var b = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(b))
                using (var wrap = new ImageAttributes())
                {
                    wrap.SetWrapMode(WrapMode.TileFlipXY);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(source, new Rectangle(Point.Empty, size), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, wrap);
                }
                return b;
            });
        }

        /// <summary>Jaquette nette, entière et jamais agrandie ; si elle laisse des bandes, la même image
        /// floutée et assombrie comble le cadre. L'appelant pose la découpe arrondie.</summary>
        internal static void Draw(Graphics g, Image image, Rectangle frame)
        {
            Rectangle sharp = Fit(image.Size, frame);
            if (sharp.IsEmpty) return;
            if (LeavesBars(sharp, frame))
            {
                Bitmap soft = Backdrop(image);
                var state = g.Save();
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                using (var wrap = new ImageAttributes())
                {
                    wrap.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(soft, Fill(soft.Size, frame), 0, 0, soft.Width, soft.Height, GraphicsUnit.Pixel, wrap);
                }
                g.Restore(state);
                using (var veil = new SolidBrush(Color.FromArgb(125, 8, 8, 10))) g.FillRectangle(veil, frame);
            }
            g.DrawImage(image, sharp, new Rectangle(Point.Empty, image.Size), GraphicsUnit.Pixel);
        }
    }
}
