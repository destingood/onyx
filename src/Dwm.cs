using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Chrome natif ONYX : force la barre de titre Windows en sombre et la teinte carbone
    /// (caption + bordure assorties au rail) — 95 % de l'effet d'une barre custom pour 2 %
    /// de son risque : Aero Snap, double-clic, multi-écrans et accessibilité restent 100 %
    /// natifs. Sur Windows 10, seuls les attributs supportés s'appliquent (échec silencieux).
    /// </summary>
    internal static class Dwm
    {
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int sz);

        private const int ImmersiveDarkMode = 20;   // Windows 10 1809+
        private const int BorderColor = 34;         // Windows 11
        private const int CaptionColor = 35;        // Windows 11
        private const int TextColor = 36;           // Windows 11

        /// <summary>Applique le chrome sombre à une fenêtre (à tout moment : avant ou après le handle).</summary>
        public static void Darken(Form f)
        {
            if (f == null) return;
            if (f.IsHandleCreated) Apply(f);
            else f.HandleCreated += (s, e) => Apply(f);
        }

        private static void Apply(Form f)
        {
            try
            {
                int on = 1;
                DwmSetWindowAttribute(f.Handle, ImmersiveDarkMode, ref on, 4);
                int caption = ColorRef(FpsUi.RailBg);
                DwmSetWindowAttribute(f.Handle, CaptionColor, ref caption, 4);
                int border = ColorRef(FpsUi.Border);
                DwmSetWindowAttribute(f.Handle, BorderColor, ref border, 4);
                int text = ColorRef(FpsUi.Ink);
                DwmSetWindowAttribute(f.Handle, TextColor, ref text, 4);
            }
            catch { }
        }

        private static int ColorRef(Color c) { return c.R | (c.G << 8) | (c.B << 16); }
    }
}
