using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BTOptimizer
{
    // ----------------------------------------------------------------------
    //  Primitives d'interface style DTG : palette noir profond + vert
    //  néon #00FF88, cartes arrondies, interrupteurs, boutons pilule. Partagé
    //  par toutes les pages du shell.
    // ----------------------------------------------------------------------
    internal static class FpsUi
    {
        // Palette Fluide : noirs légèrement bleutés + accent indigo #818CF8.
        // Une seule source de vérité : changer l'accent ici le change dans tout le shell
        // (logo, rail, anneau de santé, boutons, overlay).
        public static readonly Color BgMain = Color.FromArgb(8, 8, 12);      // #08080C
        public static readonly Color RailBg = Color.FromArgb(13, 13, 20);    // #0D0D14 (.frame)
        public static readonly Color Card   = Color.FromArgb(16, 16, 24);    // #101018
        public static readonly Color CardHi = Color.FromArgb(26, 26, 40);    // #1A1A28 (survol)
        public static readonly Color Border = Color.FromArgb(34, 34, 47);    // #22222F
        public static readonly Color Neon   = Color.FromArgb(129, 140, 248); // #818CF8 (accent)
        public static readonly Color NeonDim= Color.FromArgb(102, 112, 214); // #6670D6
        public static readonly Color Ink    = Color.FromArgb(255, 255, 255); // #FFFFFF
        public static readonly Color Dim    = Color.FromArgb(176, 176, 176); // #B0B0B0
        public static readonly Color Dim2   = Color.FromArgb(137, 137, 137); // #898989
        public static readonly Color Warn   = Color.FromArgb(255, 208, 0);   // #FFD000
        public static readonly Color Err    = Color.FromArgb(255, 107, 107); // #FF6B6B

        // Polices Fluide : Ubuntu (titres/nav), Inter (corps), Garet (display) — toutes libres.
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
        public static readonly Font Garet = Fonts.Make(Fonts.Garet,  22f,  FontStyle.Regular, "Segoe UI Semibold");
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
        /// vertical (haut plus clair) + liseré haut : donne de la PROFONDEUR sur fond noir (pas d'ombre
        /// possible sur du noir pur).</summary>
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
                // liseré supérieur légèrement plus clair = arête éclairée.
                using (var pen = new Pen(Color.FromArgb(22, 255, 255, 255)))
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
            b.BackColor = Color.FromArgb(28, 28, 42);
            // Retours au survol / à l'appui : un bouton qui ne réagit pas fait « maquette ».
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(40, 40, 58);
            b.FlatAppearance.MouseDownBackColor = Color.FromArgb(20, 20, 30);
            b.ForeColor = Ink;
            b.Font = Small;
            b.Cursor = Cursors.Hand;
            b.UseVisualStyleBackColor = false;
            return b;
        }

        public static Button NeonButton(string text)
        {
            var b = new Button();
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Neon;
            b.FlatAppearance.BorderSize = 1;
            b.BackColor = Card;
            // L'accent « chauffe » au survol, puis s'enfonce à l'appui.
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(34, 32, 72);
            b.FlatAppearance.MouseDownBackColor = Color.FromArgb(22, 21, 48);
            b.ForeColor = Neon;
            b.Font = H3;
            b.Cursor = Cursors.Hand;
            b.UseVisualStyleBackColor = false;
            return b;
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
    //  Interrupteur néon (toggle) style DTG.
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
            Color track = _on ? FpsUi.Neon : Color.FromArgb(48, 48, 62);
            if (_locked) track = Color.FromArgb(40, 40, 52);
            using (var path = FpsUi.Round(r, r.Height / 2f))
            using (var br = new SolidBrush(track))
                g.FillPath(br, path);

            int d = Height - 8;
            int kx = _on ? Width - d - 4 : 4;
            Color knob = _on ? Color.FromArgb(10, 10, 16) : Color.FromArgb(180, 180, 190);
            using (var br = new SolidBrush(knob))
                g.FillEllipse(br, kx, 4, d, d);
        }
    }
}
