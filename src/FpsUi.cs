using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BTOptimizer
{
    // ----------------------------------------------------------------------
    //  Primitives d'interface ONYX : carbone chaud + or champagne, cartes
    //  arrondies, interrupteurs, boutons pilule. Partagé par toutes les
    //  pages du shell.
    // ----------------------------------------------------------------------
    internal static class FpsUi
    {
        // Palette ONYX « Carbone & Or » : noirs chauds (jamais bleutés) + or champagne.
        // Une seule source de vérité : changer l'accent ici le change dans tout le shell
        // (logo, rail, anneau de santé, boutons, overlay).
        //
        // RÈGLE SÉMANTIQUE : l'or SIGNE (marque, nav active, CTA, focus) mais ne code
        // JAMAIS un état. Les verdicts parlent leur langue universelle : Ok = émeraude,
        // Warn = orange vif (bien distinct du champagne), Err = rouge.
        public static readonly Color BgMain  = Color.FromArgb(11, 10, 9);      // #0B0A09
        public static readonly Color RailBg  = Color.FromArgb(16, 14, 12);     // #100E0C (.frame)
        public static readonly Color Card    = Color.FromArgb(22, 19, 15);     // #16130F
        public static readonly Color CardHi  = Color.FromArgb(32, 28, 22);     // #201C16 (survol)
        public static readonly Color Border  = Color.FromArgb(43, 37, 29);     // #2B251D
        public static readonly Color Gold    = Color.FromArgb(227, 183, 92);   // #E3B75C (accent identité)
        public static readonly Color GoldDim = Color.FromArgb(176, 139, 70);   // #B08B46
        public static readonly Color Ink     = Color.FromArgb(243, 237, 226);  // #F3EDE2 (ivoire)
        public static readonly Color Dim     = Color.FromArgb(180, 170, 154);  // #B4AA9A
        public static readonly Color Dim2    = Color.FromArgb(134, 125, 111);  // #867D6F
        public static readonly Color Ok      = Color.FromArgb(76, 196, 140);   // #4CC48C (émeraude sobre)
        public static readonly Color Warn    = Color.FromArgb(240, 148, 54);   // #F09436 (orange vif ≠ or)
        public static readonly Color Err     = Color.FromArgb(232, 90, 80);    // #E85A50

        /// <summary>Or translucide (survols, fonds actifs, halos) — alpha 0-255.</summary>
        public static Color Accent(int alpha) { return Color.FromArgb(alpha, Gold); }

        /// <summary>Mélange opaque fond+or (les FlatAppearance de WinForms refusent l'alpha).</summary>
        public static Color BlendGold(Color bg, float amount)
        {
            return Color.FromArgb(
                (int)(bg.R + (Gold.R - bg.R) * amount),
                (int)(bg.G + (Gold.G - bg.G) * amount),
                (int)(bg.B + (Gold.B - bg.B) * amount));
        }

        // Polices ONYX : Marcellus (display/logo — l'or gravé), Ubuntu (titres/nav),
        // Inter (corps). Toutes libres (OFL / Ubuntu Font Licence).
        public static Font F(float size, bool semibold)
        {
            return semibold ? Fonts.Make(Fonts.Ubuntu, size, FontStyle.Bold, "Segoe UI Semibold")
                            : Fonts.Make(Fonts.Inter, size, FontStyle.Regular, "Segoe UI");
        }
        public static readonly Font H1    = Fonts.Make(Fonts.Ubuntu, 20f,  FontStyle.Bold,    "Segoe UI Semibold");
        public static readonly Font H2    = Fonts.Make(Fonts.Ubuntu, 12f,  FontStyle.Bold,    "Segoe UI Semibold");
        public static readonly Font H3    = Fonts.Make(Fonts.Ubuntu, 10.5f, FontStyle.Bold,   "Segoe UI Semibold");
        public static readonly Font Body  = Fonts.Make(Fonts.Inter,  9f,   FontStyle.Regular, "Segoe UI");
        public static readonly Font Small = Fonts.Make(Fonts.Inter,  8.5f, FontStyle.Regular, "Segoe UI");
        public static readonly Font Tiny  = Fonts.Make(Fonts.Inter,  7.5f, FontStyle.Regular, "Segoe UI");
        public static readonly Font Num   = Fonts.Make(Fonts.Ubuntu, 20f,  FontStyle.Bold,    "Segoe UI Semibold");
        public static readonly Font Display  = Fonts.Make(Fonts.Marcellus, 22f, FontStyle.Regular, "Georgia");
        public static readonly Font DisplayL = Fonts.Make(Fonts.Marcellus, 30f, FontStyle.Regular, "Georgia");
        public static readonly Font Glyph = new Font("Segoe UI Emoji", 15f);
        public static readonly Font GlyphL  = new Font("Segoe UI Emoji", 30f);
        public static readonly Font GlyphXL = new Font("Segoe UI Emoji", 42f);

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = radius * 2f;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        private static Color Lighten(Color c, int by)
        {
            return Color.FromArgb(c.A, Math.Min(255, c.R + by), Math.Min(255, c.G + by), Math.Min(255, c.B + by));
        }

        /// <summary>Peint une carte arrondie (fond + bordure) sur toute la surface donnée. Léger dégradé
        /// vertical (haut plus clair) + liseré haut ivoire chaud : donne de la PROFONDEUR sur fond
        /// carbone (pas d'ombre possible sur du noir).</summary>
        public static void PaintCard(Graphics g, Rectangle r, Color fill, Color border, float radius)
        {
            if (r.Width < 4 || r.Height < 4) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rf = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1f, r.Height - 1f);
            using (var path = Round(rf, radius))
            {
                using (var br = new LinearGradientBrush(new RectangleF(rf.X, rf.Y - 1, rf.Width, rf.Height + 2), Lighten(fill, 8), fill, 90f))
                    g.FillPath(br, path);
                using (var pen = new Pen(border)) g.DrawPath(pen, path);
                // liseré supérieur légèrement plus clair = arête éclairée (teinte chaude, pas blanc froid).
                using (var pen = new Pen(Color.FromArgb(20, 255, 238, 200)))
                    g.DrawLine(pen, rf.X + radius, rf.Y + 1f, rf.Right - radius, rf.Y + 1f);
            }
        }

        /// <summary>Panneau-carte arrondie (fond transparent, peinture custom).</summary>
        public static Panel CardPanel(float radius)
        {
            var p = new Panel();
            p.BackColor = Color.Transparent;
            p.Paint += (s, e) => PaintCard(e.Graphics, ((Panel)s).ClientRectangle, Card, Border, radius);
            return p;
        }

        public static Button GhostButton(string text)
        {
            var b = new Button();
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = Color.FromArgb(31, 27, 22);
            // Retours au survol / à l'appui : un bouton qui ne réagit pas fait « maquette ».
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(44, 39, 31);
            b.FlatAppearance.MouseDownBackColor = Color.FromArgb(23, 20, 16);
            b.ForeColor = Ink;
            b.Font = Small;
            b.Cursor = Cursors.Hand;
            b.UseVisualStyleBackColor = false;
            return b;
        }

        public static Button GoldButton(string text)
        {
            var b = new Button();
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Gold;
            b.FlatAppearance.BorderSize = 1;
            b.BackColor = Card;
            // L'accent « chauffe » au survol, puis s'enfonce à l'appui.
            b.FlatAppearance.MouseOverBackColor = BlendGold(Card, 0.16f);
            b.FlatAppearance.MouseDownBackColor = BlendGold(Card, 0.07f);
            b.ForeColor = Gold;
            b.Font = H3;
            b.Cursor = Cursors.Hand;
            b.UseVisualStyleBackColor = false;
            return b;
        }

        /// <summary>Capitales gravées : interlettrage manuel (GDI ne sait pas espacer), centré
        /// verticalement sur cy. Renvoie le X de fin (pour enchaîner un élément à droite).</summary>
        public static float DrawTracked(Graphics g, string text, Font f, Color c, float x, float cy, float tracking)
        {
            const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
            foreach (char ch in text)
            {
                string s = ch.ToString();
                Size sz = TextRenderer.MeasureText(g, s, f, new Size(int.MaxValue, int.MaxValue), flags);
                TextRenderer.DrawText(g, s, f, new Point((int)Math.Round(x), (int)Math.Round(cy - sz.Height / 2f)), c, flags);
                x += sz.Width + tracking;
            }
            return x - tracking;
        }

        /// <summary>Largeur d'un texte gravé (même métrique que DrawTracked).</summary>
        public static float MeasureTracked(Graphics g, string text, Font f, float tracking)
        {
            const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
            float w = 0;
            foreach (char ch in text)
                w += TextRenderer.MeasureText(g, ch.ToString(), f, new Size(int.MaxValue, int.MaxValue), flags).Width + tracking;
            return Math.Max(0, w - tracking);
        }

        public static Label Text(string t, Font f, Color c)
        {
            var l = new Label();
            l.Text = t; l.Font = f; l.ForeColor = c;
            l.BackColor = Color.Transparent;
            l.AutoSize = true;
            return l;
        }
    }

    // ----------------------------------------------------------------------
    //  Interrupteur (toggle) style ONYX.
    // ----------------------------------------------------------------------
    internal class ToggleSwitch : Control
    {
        private bool _on;
        private bool _locked;

        public event EventHandler Toggled;

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool On
        {
            get { return _on; }
            set { if (_on != value) { _on = value; Invalidate(); } }
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Locked
        {
            get { return _locked; }
            set { _locked = value; Cursor = value ? Cursors.No : Cursors.Hand; Invalidate(); }
        }

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw
                     | ControlStyles.SupportsTransparentBackColor, true);
            Size = new Size(46, 24);
            Cursor = Cursors.Hand;
            BackColor = Color.Transparent;
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (_locked) return;
            _on = !_on;
            Invalidate();
            if (Toggled != null) Toggled(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(1, 1, Width - 2, Height - 2);
            Color track = _on ? FpsUi.Gold : Color.FromArgb(52, 46, 38);
            if (_locked) track = Color.FromArgb(43, 38, 31);
            using (var path = FpsUi.Round(r, r.Height / 2f))
            using (var br = new SolidBrush(track))
                g.FillPath(br, path);

            int d = Height - 8;
            int kx = _on ? Width - d - 4 : 4;
            Color knob = _on ? Color.FromArgb(16, 13, 9) : Color.FromArgb(188, 180, 166);
            using (var br = new SolidBrush(knob))
                g.FillEllipse(br, kx, 4, d, d);
        }
    }
}
