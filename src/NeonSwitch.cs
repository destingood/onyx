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
        private float _t;              // position animée : 0 = OFF, 1 = ON
        private Anim.Handle _anim;

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
            set { if (_checked == value) return; _checked = value; _t = value ? 1f : 0f; Invalidate(); }
        }

        /// <summary>Change l'état SANS déclencher CheckedChanged (synchronisation d'affichage).</summary>
        public void SetCheckedSilent(bool value)
        {
            _checked = value;
            _t = value ? 1f : 0f;   // synchro d'affichage = saut immédiat (pas d'animation de masse au chargement)
            Invalidate();
        }

        /// <summary>Bascule utilisateur : change l'état PUIS notifie.</summary>
        public void Toggle()
        {
            _checked = !_checked;
            AnimateTo(_checked);
            EventHandler h = CheckedChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        // Glissement de la bille + remplissage néon progressif (~160 ms) sur bascule
        // utilisateur. Animations coupées => bascule immédiate.
        private void AnimateTo(bool on)
        {
            float target = on ? 1f : 0f;
            if (_anim != null) _anim.Cancelled = true;
            if (!Anim.On) { _t = target; Invalidate(); return; }
            float from = _t;
            _anim = Anim.Tween(160, delegate (float p) { _t = from + (target - from) * p; Invalidate(); });
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

        private static Color Blend(Color a, Color b, float t)
        {
            if (t <= 0f) return a;
            if (t >= 1f) return b;
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (var bg = new SolidBrush(Parent != null ? Parent.BackColor : Color.Black))
                g.FillRectangle(bg, ClientRectangle);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            float rad = r.Height / 2f;

            Color offTrack = Theme.Dark ? Color.FromArgb(33, 33, 33) : Color.FromArgb(205, 209, 216);
            Color track = Blend(offTrack, Theme.OkColor, _t);
            if (_hover && Enabled) track = Lighten(track, (int)((1f - _t) * 14));   // survol surtout visible côté OFF

            using (GraphicsPath p = Theme.RoundPath(r, rad))
            {
                using (var br = new SolidBrush(Enabled ? track : Color.FromArgb(110, track)))
                    g.FillPath(br, p);
                using (var pen = new Pen(Theme.Dark ? Color.FromArgb(23, 23, 23) : Color.FromArgb(180, 184, 190)))
                    g.DrawPath(pen, p);
            }

            // Bille : glisse de gauche à droite ; blanche côté OFF, NOIRE côté ON (charte).
            float d = r.Height - 6f;
            float offX = r.X + 3f, onX = r.Right - d - 3f;
            float x = offX + (onX - offX) * _t;
            using (var br = new SolidBrush(Blend(Color.White, Color.Black, _t)))
                g.FillEllipse(br, x, r.Y + 3f, d, d);
            if (Focused && Enabled)
                using (var pen = new Pen(Color.FromArgb(110, Theme.InkColor)))
                    g.DrawEllipse(pen, x - 1.5f, r.Y + 1.5f, d + 3f, d + 3f);
        }
    }
}
