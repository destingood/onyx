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
        // Palette ONYX : carbone chaud et or champagne, avec des couleurs d'état distinctes.
        // Une seule source de vérité : changer l'accent ici le change dans tout le shell
        // (logo, rail, anneau de santé, boutons, overlay).
        //
        // RÈGLE SÉMANTIQUE : l'or SIGNE (marque, nav active, CTA, focus) mais ne code
        // JAMAIS un état. Les verdicts parlent leur langue universelle : Ok = émeraude,
        // Warn = orange vif (bien distinct du champagne), Err = rouge.
        public static readonly Color BgMain  = Color.FromArgb(17, 17, 16);
        public static readonly Color RailBg  = Color.FromArgb(22, 22, 20);
        public static readonly Color Card    = Color.FromArgb(28, 28, 25);
        public static readonly Color CardHi  = Color.FromArgb(37, 37, 33);
        public static readonly Color Border  = Color.FromArgb(56, 55, 49);
        public static readonly Color Gold    = Color.FromArgb(227, 183, 92);
        public static readonly Color GoldDim = Color.FromArgb(176, 139, 70);
        public static readonly Color Ink     = Color.FromArgb(243, 237, 226);
        public static readonly Color Dim     = Color.FromArgb(184, 181, 171);
        public static readonly Color Dim2    = Color.FromArgb(152, 149, 138);
        public static readonly Color GraphSecondary = Color.FromArgb(214, 205, 189);
        public static readonly Color Violet = Color.FromArgb(170, 154, 255); // couleur de catégorie (sources), pas accent du thème
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

        // Segoe UI est disponible nativement avec Windows et garde les mêmes métriques
        // dans GDI (TextRenderer) et GDI+. Marcellus reste réservé à la marque.
        public static Font F(float size, bool semibold)
        {
            return new Font(semibold ? "Segoe UI Semibold" : "Segoe UI", size, FontStyle.Regular);
        }
        public static readonly Font H1    = F(20f, true);
        public static readonly Font H2    = F(12f, true);
        public static readonly Font H3    = F(10.5f, true);
        public static readonly Font Body  = F(9.5f, false);
        public static readonly Font Small = F(8.5f, false);
        public static readonly Font Tiny  = F(8f, false);
        public static readonly Font Num   = F(20f, true);
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

        /// <summary>Surface mate et contour discret, communs à toutes les pages.</summary>
        public static void PaintCard(Graphics g, Rectangle r, Color fill, Color border, float radius)
        {
            if (r.Width < 4 || r.Height < 4) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rf = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1f, r.Height - 1f);
            using (var path = Round(rf, radius))
            {
                using (var br = new SolidBrush(fill))
                    g.FillPath(br, path);
                using (var pen = new Pen(border)) g.DrawPath(pen, path);
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
            var b = new SurfaceButton();
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = CardHi;
            // Retours au survol / à l'appui : un bouton qui ne réagit pas fait « maquette ».
            b.FlatAppearance.MouseOverBackColor = BlendGold(CardHi, 0.08f);
            b.FlatAppearance.MouseDownBackColor = Card;
            b.ForeColor = Ink;
            b.Font = Small;
            b.Cursor = Cursors.Hand;
            b.UseVisualStyleBackColor = false;
            return b;
        }

        public static Button GoldButton(string text)
        {
            var b = new SurfaceButton();
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

        public static Button PrimaryButton(string text)
        {
            var b = GoldButton(text);
            b.BackColor = Gold;
            b.ForeColor = BgMain;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(242, 205, 135);
            b.FlatAppearance.MouseDownBackColor = GoldDim;
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
            // Sans ça, WinForms lit « & » comme un raccourci clavier : « Fichiers temporaires &
            // caches » s'affichait « Fichiers temporaires  caches ». Aucun de nos libellés n'est
            // un accélérateur.
            l.UseMnemonic = false;
            return l;
        }
    }

    /// <summary>Garde le comportement natif du bouton (clavier, DialogResult, accessibilité)
    /// avec un rendu commun aux actions des pages. Les couleurs restent personnalisables.</summary>
    internal sealed class SurfaceButton : Button
    {
        private bool _hover, _pressed;

        public SurfaceButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Width < 4 || Height < 4) return;
            var old = Region;
            using (var path = FpsUi.Round(new RectangleF(0, 0, Width, Height), 7f))
                Region = new Region(path);
            if (old != null) old.Dispose();
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = _pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _pressed = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { _pressed = false; Invalidate(); base.OnLostFocus(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { _pressed = true; Invalidate(); } base.OnKeyDown(e); }
        protected override void OnKeyUp(KeyEventArgs e) { _pressed = false; Invalidate(); base.OnKeyUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width < 4 || Height < 4) return;
            var g = e.Graphics;
            g.Clear(BackColor);
            Color fill = !Enabled ? FpsUi.Card : _pressed ? FlatAppearance.MouseDownBackColor
                : _hover ? FlatAppearance.MouseOverBackColor : BackColor;
            if (fill.IsEmpty) fill = BackColor;
            Color line = Focused && ShowFocusCues ? FpsUi.Gold
                : FlatAppearance.BorderSize > 0 ? FlatAppearance.BorderColor : fill;
            FpsUi.PaintCard(g, ClientRectangle, fill, line, 7f);
            var textBounds = Rectangle.Inflate(ClientRectangle, Width < 60 ? -2 : -6, -2);
            if (_pressed) textBounds.Offset(0, 1);
            TextRenderer.DrawText(g, Text, Font, textBounds, Enabled ? ForeColor : FpsUi.Dim2,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
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
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            AccessibleRole = AccessibleRole.CheckButton;
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space && Enabled && !_locked)
            {
                OnClick(EventArgs.Empty);
                e.Handled = e.SuppressKeyPress = true;
            }
            base.OnKeyUp(e);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (_locked || !Enabled) return;
            Focus();
            _on = !_on;
            Invalidate();
            if (Toggled != null) Toggled(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(1, 1, Width - 2, Height - 2);
            Color track = _on ? FpsUi.Gold : FpsUi.Border;
            if (_locked || !Enabled) track = FpsUi.CardHi;
            using (var path = FpsUi.Round(r, r.Height / 2f))
            using (var br = new SolidBrush(track))
                g.FillPath(br, path);

            int d = Height - 8;
            int kx = _on ? Width - d - 4 : 4;
            Color knob = _on ? FpsUi.BgMain : FpsUi.Dim;
            using (var br = new SolidBrush(knob))
                g.FillEllipse(br, kx, 4, d, d);
            if (Focused && ShowFocusCues)
                using (var path = FpsUi.Round(r, r.Height / 2f))
                using (var pen = new Pen(FpsUi.Ink)) g.DrawPath(pen, path);
        }
    }
}
