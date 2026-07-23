using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Interrupteur « pilule » de la charte noir/néon : OFF = piste sombre à bille
    /// blanche, ON = piste néon à bille noire. Clic, Espace ou Entrée pour basculer.
    /// SetCheckedSilent permet les mises à jour d'affichage sans déclencher l'évènement.
    /// </summary>
    internal sealed class NeonSwitch : Control
    {
        private bool _checked;
        private bool _hover;

        /// <summary>Déclenché UNIQUEMENT sur un geste utilisateur (pas par SetCheckedSilent).</summary>
        public event EventHandler CheckedChanged;

        public NeonSwitch()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(46, 22);
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        [System.ComponentModel.DefaultValue(false)]
        public bool Checked
        {
            get { return _checked; }
            set { if (_checked == value) return; _checked = value; Invalidate(); }
        }

        /// <summary>Change l'état SANS déclencher CheckedChanged (synchronisation d'affichage).</summary>
        public void SetCheckedSilent(bool value)
        {
            _checked = value;
            Invalidate();
        }

        /// <summary>Bascule utilisateur : change l'état PUIS notifie.</summary>
        public void Toggle()
        {
            _checked = !_checked;
            Invalidate();
            EventHandler h = CheckedChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && Enabled && ClientRectangle.Contains(e.Location)) Toggle();
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (Enabled && (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)) Toggle();
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }

        private static Color Lighten(Color c, int d)
        {
            return Color.FromArgb(Math.Min(255, c.R + d), Math.Min(255, c.G + d), Math.Min(255, c.B + d));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (var bg = new SolidBrush(Parent != null ? Parent.BackColor : Color.Black))
                g.FillRectangle(bg, ClientRectangle);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            float rad = r.Height / 2f;

            Color track = _checked ? Theme.OkColor
                        : (Theme.Dark ? Color.FromArgb(33, 33, 33) : Color.FromArgb(205, 209, 216));
            if (_hover && Enabled) track = Lighten(track, _checked ? 0 : 14);

            using (GraphicsPath p = Theme.RoundPath(r, rad))
            {
                using (var br = new SolidBrush(Enabled ? track : Color.FromArgb(110, track)))
                    g.FillPath(br, p);
                using (var pen = new Pen(Theme.Dark ? Color.FromArgb(23, 23, 23) : Color.FromArgb(180, 184, 190)))
                    g.DrawPath(pen, p);
            }

            // Bille : blanche sur piste sombre, NOIRE sur piste néon (charte).
            float d = r.Height - 6f;
            float x = _checked ? r.Right - d - 3f : r.X + 3f;
            using (var br = new SolidBrush(_checked ? Color.Black : Color.White))
                g.FillEllipse(br, x, r.Y + 3f, d, d);
            if (Focused && Enabled)
                using (var pen = new Pen(Color.FromArgb(110, Theme.InkColor)))
                    g.DrawEllipse(pen, x - 1.5f, r.Y + 1.5f, d + 3f, d + 3f);
        }
    }
}
