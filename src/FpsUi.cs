using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BTOptimizer
{
    // ----------------------------------------------------------------------
    //  Primitives d'interface façon FPSDoctor : palette noir profond + vert
    //  néon #00FF88, cartes arrondies, interrupteurs, boutons pilule. Partagé
    //  par toutes les pages du shell.
    // ----------------------------------------------------------------------
    internal static class FpsUi
    {
        public static readonly Color BgMain = Color.FromArgb(9, 11, 10);
        public static readonly Color RailBg = Color.FromArgb(12, 14, 13);
        public static readonly Color Card   = Color.FromArgb(16, 18, 17);
        public static readonly Color CardHi = Color.FromArgb(22, 25, 23);
        public static readonly Color Border = Color.FromArgb(31, 34, 32);
        public static readonly Color Neon   = Color.FromArgb(0, 255, 136);
        public static readonly Color NeonDim= Color.FromArgb(0, 190, 110);
        public static readonly Color Ink    = Color.FromArgb(240, 242, 241);
        public static readonly Color Dim    = Color.FromArgb(155, 159, 155);
        public static readonly Color Dim2   = Color.FromArgb(120, 124, 120);
        public static readonly Color Warn   = Color.FromArgb(230, 175, 45);
        public static readonly Color Err    = Color.FromArgb(232, 84, 74);

        public static Font F(float size, bool semibold)
        {
            return new Font(semibold ? "Segoe UI Semibold" : "Segoe UI", size);
        }
        public static readonly Font H1    = new Font("Segoe UI Semibold", 21f);
        public static readonly Font H2    = new Font("Segoe UI Semibold", 12f);
        public static readonly Font H3    = new Font("Segoe UI Semibold", 10.5f);
        public static readonly Font Body  = new Font("Segoe UI", 9.5f);
        public static readonly Font Small = new Font("Segoe UI", 8.5f);
        public static readonly Font Tiny  = new Font("Segoe UI", 7.5f);
        public static readonly Font Num   = new Font("Segoe UI Semibold", 20f);
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

        /// <summary>Peint une carte arrondie (fond + bordure) sur toute la surface donnée.</summary>
        public static void PaintCard(Graphics g, Rectangle r, Color fill, Color border, float radius)
        {
            if (r.Width < 4 || r.Height < 4) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rf = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1f, r.Height - 1f);
            using (var path = Round(rf, radius))
            {
                using (var br = new SolidBrush(fill)) g.FillPath(br, path);
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
            var b = new Button();
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = Color.FromArgb(26, 29, 27);
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
    //  Interrupteur néon (toggle) façon FPSDoctor.
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
            Color track = _on ? FpsUi.Neon : Color.FromArgb(48, 51, 49);
            if (_locked) track = Color.FromArgb(40, 43, 41);
            using (var path = FpsUi.Round(r, r.Height / 2f))
            using (var br = new SolidBrush(track))
                g.FillPath(br, path);

            int d = Height - 8;
            int kx = _on ? Width - d - 4 : 4;
            Color knob = _on ? Color.FromArgb(8, 12, 10) : Color.FromArgb(180, 184, 180);
            using (var br = new SolidBrush(knob))
                g.FillEllipse(br, kx, 4, d, d);
        }
    }
}
