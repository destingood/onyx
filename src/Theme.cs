using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Thème clair/sombre appliqué récursivement. L'applicateur reconnaît les
    /// conventions de couleur déjà présentes dans l'app (bannières sombres, boutons
    /// accent) pour les préserver, et ne recolore que le « texte par défaut ».
    /// Défaut = clair : tant qu'on ne bascule pas, l'apparence est inchangée.
    /// </summary>
    internal static class Theme
    {
        public static bool Dark { get; private set; }

        // Repères des couleurs codées en dur dans les fenêtres.
        private static readonly Color HeaderBg = Color.FromArgb(28, 30, 38);
        private static readonly Color AccentRef = Color.FromArgb(0, 150, 90);

        // Tokens (basculent avec le thème).
        private static Color Bg, Panel, Ink, InkDim, Line, GroupInk, FieldBg;

        private static string StorePath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-theme.txt"); } }

        static Theme()
        {
            // Sombre par défaut (identité gaming DesTinGOOD) ; le choix de l'utilisateur,
            // une fois fait, est respecté (bt-theme.txt).
            try
            {
                Dark = File.Exists(StorePath)
                    ? File.ReadAllText(StorePath).Trim() == "dark"
                    : true;
            }
            catch { Dark = true; }
            LoadTokens();
        }

        private static void LoadTokens()
        {
            if (Dark)
            {
                Bg = Color.FromArgb(22, 24, 29); Panel = Color.FromArgb(30, 33, 40);
                Ink = Color.FromArgb(212, 218, 224); InkDim = Color.FromArgb(140, 147, 156);
                Line = Color.FromArgb(56, 61, 71); GroupInk = Color.FromArgb(120, 150, 210);
                FieldBg = Color.FromArgb(26, 29, 35);
            }
            else
            {
                Bg = Color.FromArgb(245, 246, 248); Panel = Color.White;
                Ink = Color.FromArgb(45, 49, 57); InkDim = Color.FromArgb(110, 115, 125);
                Line = Color.FromArgb(200, 204, 210); GroupInk = Color.FromArgb(50, 70, 130);
                FieldBg = Color.White;
            }
        }

        public static void Toggle()
        {
            Dark = !Dark;
            LoadTokens();
            try { File.WriteAllText(StorePath, Dark ? "dark" : "light"); } catch { }
        }

        private static bool Near(Color a, Color b)
        {
            return Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) < 26;
        }
        private static bool IsGrayish(Color c)
        {
            return Math.Abs(c.R - c.G) < 18 && Math.Abs(c.G - c.B) < 18;
        }

        public static void Apply(Control root)
        {
            try { Walk(root, false); } catch { }
        }

        private static void Walk(Control c, bool inHeader)
        {
            bool header = inHeader || ((c is Panel) && Near(c.BackColor, HeaderBg));

            if (header)
            {
                if (c is Panel) c.BackColor = HeaderBg;
                if (c is Label)
                    c.ForeColor = (c.Font != null && c.Font.Size >= 12.5f) ? Color.White : Color.FromArgb(170, 175, 185);
            }
            else if (c is Form)
            {
                c.BackColor = Bg;
            }
            else if (c is TextBox || c is RichTextBox)
            {
                c.BackColor = FieldBg; c.ForeColor = Ink;
            }
            else if (c is ListView)
            {
                c.BackColor = Panel; c.ForeColor = Ink;
            }
            else if (c is CheckedListBox)
            {
                c.BackColor = Panel; c.ForeColor = Ink;
            }
            else if (c is ComboBox)
            {
                c.BackColor = FieldBg; c.ForeColor = Ink;
            }
            else if (c is NumericUpDown)
            {
                c.BackColor = FieldBg; c.ForeColor = Ink;
            }
            else if (c is Button)
            {
                var b = (Button)c;
                if (!Near(b.BackColor, AccentRef)) // laisse les boutons accent tels quels
                {
                    b.BackColor = Panel; b.ForeColor = Ink;
                    try { b.FlatAppearance.BorderColor = Line; } catch { }
                }
            }
            else if (c is GroupBox)
            {
                c.ForeColor = GroupInk;
            }
            else if (c is CheckBox || c is RadioButton)
            {
                if (IsGrayish(c.ForeColor) || Near(c.ForeColor, SystemColors.ControlText)) c.ForeColor = Ink;
            }
            else if (c is Label)
            {
                // On ne touche qu'au texte "par défaut" (gris/noir), pas aux libellés colorés (accent/statut).
                if (IsGrayish(c.ForeColor)) c.ForeColor = c.ForeColor.GetBrightness() < 0.5f ? Ink : InkDim;
            }
            else if (c is Panel || c is FlowLayoutPanel || c is TableLayoutPanel)
            {
                if (!Near(c.BackColor, HeaderBg)) c.BackColor = Bg;
            }

            foreach (Control ch in c.Controls) Walk(ch, header);
        }
    }
}
