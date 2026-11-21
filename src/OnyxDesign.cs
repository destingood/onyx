using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BTOptimizer
{
    // Habillage opt-in : les pages qui ne l'utilisent pas (notamment Actus) restent inchangées.
    internal static class OnyxDesign
    {
        public static readonly Font Title = FpsUi.F(17f, true);
        public static readonly Font Display = FpsUi.F(30f, true);
        public static readonly Font Number = FpsUi.F(25f, true);
        public static readonly Color Edge = Color.FromArgb(83, 70, 46);

        public static void PaintCard(Graphics g, Rectangle r, Color fill, Color border, float radius)
        {
            if (r.Width < 4 || r.Height < 4) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new RectangleF(r.X + .5f, r.Y + .5f, r.Width - 1, r.Height - 1);
            Color top = Color.FromArgb(Math.Min(255, fill.R + 5), Math.Min(255, fill.G + 4), Math.Min(255, fill.B + 3));
            using (var path = FpsUi.Round(rect, radius))
            {
                using (var brush = new LinearGradientBrush(rect, top, fill, 90f)) g.FillPath(brush, path);
                using (var pen = new Pen(border)) g.DrawPath(pen, path);
            }
            using (var pen = new Pen(Color.FromArgb(20, FpsUi.Ink)))
                g.DrawLine(pen, rect.X + radius, rect.Y + 1, rect.Right - radius, rect.Y + 1);
        }

        public static void Heading(Graphics g, string title, string subtitle, int width)
        {
            string text = title.Length > 0 ? title.Substring(0, 1) + title.Substring(1).ToLowerInvariant() : title;
            using (var brush = new SolidBrush(FpsUi.Gold)) g.FillRectangle(brush, 34, 26, 3, 22);
            Text(g, text, Title, FpsUi.Ink, new Rectangle(48, 20, Math.Max(1, width - 82), 34));
            if (!string.IsNullOrEmpty(subtitle)) Text(g, subtitle, FpsUi.Body, FpsUi.Dim, new Rectangle(34, 60, Math.Max(1, width - 68), 22));
        }

        public static void Text(Graphics g, string text, Font font, Color color, Rectangle rect, bool wrap = false)
        {
            TextRenderer.DrawText(g, text, font, rect, color, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding
                | TextFormatFlags.EndEllipsis | (wrap ? TextFormatFlags.WordBreak : TextFormatFlags.SingleLine));
        }

        public static void IconWell(Graphics g, int icon, Rectangle rect, bool active = false)
        {
            using (var path = FpsUi.Round(rect, 10))
            {
                using (var b = new SolidBrush(FpsUi.BlendGold(FpsUi.RailBg, active ? .15f : .07f))) g.FillPath(b, path);
                using (var p = new Pen(active ? FpsUi.GoldDim : Edge)) g.DrawPath(p, path);
            }
            int pad = rect.Width >= 46 ? 11 : 7;
            NavIcons.Draw(g, icon, new RectangleF(rect.X + pad, rect.Y + pad, rect.Width - pad * 2, rect.Height - pad * 2), FpsUi.Gold);
        }

        public static Button Button(string text) { return MakeButton(text, 0); }
        public static Button AccentButton(string text) { return MakeButton(text, 1); }
        public static Button PrimaryButton(string text) { return MakeButton(text, 2); }

        private static Button MakeButton(string text, int emphasis)
        {
            var button = new OnyxButton(text, emphasis);
            button.BackColor = emphasis == 2 ? FpsUi.Gold : FpsUi.CardHi;
            button.ForeColor = emphasis == 2 ? FpsUi.BgMain : emphasis == 1 ? FpsUi.Gold : FpsUi.Ink;
            button.Font = emphasis > 0 ? FpsUi.H3 : FpsUi.Small;
            button.FlatAppearance.BorderSize = emphasis > 0 ? 1 : 0;
            button.FlatAppearance.BorderColor = emphasis > 0 ? FpsUi.GoldDim : FpsUi.Border;
            return button;
        }

        // Suit aussi les enfants des cartes : une étiquette ne doit pas couper le survol.
        public static void HoverCard(Control card)
        {
            bool hot = false;
            Action<Control> wire = null;
            wire = control =>
            {
                control.MouseEnter += (s, e) => { if (!hot) { hot = true; card.Invalidate(); } };
                control.MouseLeave += (s, e) =>
                {
                    if (card.IsDisposed || card.ClientRectangle.Contains(card.PointToClient(Cursor.Position))) return;
                    hot = false; card.Invalidate();
                };
                foreach (Control child in control.Controls) wire(child);
            };
            wire(card);
            card.Paint += (s, e) =>
            {
                if (!hot || card.Width < 4 || card.Height < 4) return;
                using (var path = FpsUi.Round(new RectangleF(.5f, .5f, card.Width - 1, card.Height - 1), 12))
                using (var pen = new Pen(FpsUi.GoldDim)) e.Graphics.DrawPath(pen, path);
            };
        }
    }

    internal sealed class OnyxButton : Button
    {
        private readonly int _emphasis;
        private float _hover;
        private bool _pressed;
        private Anim.Handle _transition;

        public OnyxButton(string text, int emphasis)
        {
            Text = text; _emphasis = emphasis;
            FlatStyle = FlatStyle.Flat; Cursor = Cursors.Hand; UseVisualStyleBackColor = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Width < 4 || Height < 4) return;
            var old = Region;
            using (var path = FpsUi.Round(new RectangleF(0, 0, Width, Height), 8)) Region = new Region(path);
            if (old != null) old.Dispose();
        }
        private void Hover(float target)
        {
            if (_transition != null) _transition.Cancelled = true;
            float start = _hover;
            _transition = Anim.Tween(140, t => { if (!IsDisposed) { _hover = start + (target - start) * t; Invalidate(); } });
        }
        protected override void OnMouseEnter(EventArgs e) { Hover(1); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _pressed = false; Hover(0); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _pressed = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { _pressed = false; Invalidate(); base.OnLostFocus(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { _pressed = true; Invalidate(); } base.OnKeyDown(e); }
        protected override void OnKeyUp(KeyEventArgs e) { _pressed = false; Invalidate(); base.OnKeyUp(e); }
        protected override void Dispose(bool disposing) { if (_transition != null) _transition.Cancelled = true; base.Dispose(disposing); }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width < 4 || Height < 4) return;
            var g = e.Graphics;
            g.Clear(BackColor);
            bool solid = _emphasis == 2;
            Color fill = !Enabled ? FpsUi.Card : solid ? (_pressed ? FpsUi.GoldDim : BackColor)
                : FpsUi.BlendGold(BackColor, _pressed ? .03f : _hover * .11f);
            Color border = Focused && ShowFocusCues ? FpsUi.Ink
                : _hover > .5f || FlatAppearance.BorderSize > 0 ? FlatAppearance.BorderColor : FpsUi.Border;
            OnyxDesign.PaintCard(g, ClientRectangle, fill, border, 8);
            if (solid && Enabled)
            {
                using (var path = FpsUi.Round(new RectangleF(1, 1, Width - 2, Height - 2), 7))
                using (var brush = new LinearGradientBrush(ClientRectangle, Color.FromArgb((int)(50 + _hover * 40), Color.White), Color.Transparent, 90f)) g.FillPath(brush, path);
            }
            Rectangle rect = Rectangle.Inflate(ClientRectangle, Width < 60 ? -2 : -8, -2);
            if (_pressed) rect.Offset(0, 1);
            TextRenderer.DrawText(g, Text, Font, rect, Enabled ? ForeColor : FpsUi.Dim2,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }
}
