using System;
using System.Drawing;
using System.Windows.Forms;

namespace BTOptimizer
{
    // Vue défilante opt-in : piste carbone, poignée or et contenu accessible au clavier.
    internal sealed class OnyxScrollPanel : BufferedPanel, IMessageFilter
    {
        internal readonly BufferedPanel Content = new BufferedPanel();
        private readonly ScrollTrack _track;
        private int _offset, _contentHeight;
        internal int ContentWidth { get { return Math.Max(1, Width - 20); } }
        internal int Maximum { get { return Math.Max(0, _contentHeight - Height); } }
        internal int Offset { get { return _offset; } }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal int ContentHeight
        {
            get { return _contentHeight; }
            set { _contentHeight = Math.Max(0, value); ScrollTo(_offset); }
        }

        internal OnyxScrollPanel()
        {
            BackColor = Content.BackColor = FpsUi.BgMain;
            Controls.Add(Content); _track = new ScrollTrack(this); Controls.Add(_track);
            WatchFocus(Content);
            Application.AddMessageFilter(this);
        }
        private void WatchFocus(Control control)
        {
            control.GotFocus += (s, e) => EnsureVisible(control);
            control.ControlAdded += (s, e) => WatchFocus(e.Control);
            foreach (Control child in control.Controls) WatchFocus(child);
        }
        private void EnsureVisible(Control control)
        {
            if (!control.Visible || control == Content || !control.IsHandleCreated) return;
            var p = PointToClient(control.PointToScreen(Point.Empty));
            if (p.Y < 0) ScrollTo(_offset + p.Y);
            else if (p.Y + control.Height > Height) ScrollTo(_offset + p.Y + control.Height - Height);
        }
        internal void ScrollTo(int position)
        {
            _offset = Math.Max(0, Math.Min(Maximum, position));
            Content.SetBounds(0, -_offset, ContentWidth, _contentHeight);
            if (_track != null)
            {
                _track.SetBounds(Width - 12, 0, 12, Height);
                _track.Visible = Maximum > 0; _track.Invalidate();
            }
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); ScrollTo(_offset); }
        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != 0x020A || !IsHandleCreated || !Visible || Maximum == 0) return false;
            var source = Control.FromChildHandle(m.HWnd);
            if (source == null || source.FindForm() != FindForm()) return false;
            if (!ClientRectangle.Contains(PointToClient(Control.MousePosition))) return false;
            int delta = (short)(((long)m.WParam >> 16) & 0xffff);
            ScrollTo(_offset - delta); return true;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) Application.RemoveMessageFilter(this);
            base.Dispose(disposing);
        }
#if BTTEST
        internal void ValidateScrolling()
        {
            if (Maximum == 0) return;
            _track.PreviewKey(Keys.End);
            if (Offset != Maximum || Content.Bottom != Height) throw new InvalidOperationException("Le bas du contenu est inaccessible.");
            _track.PreviewKey(Keys.Home);
            if (Offset != 0) throw new InvalidOperationException("La touche Début ne remonte pas le contenu.");
            _track.PreviewDrag();
            if (Offset != Maximum) throw new InvalidOperationException("La poignée ne défile pas jusqu’en bas.");
            ScrollTo(0);
        }
#endif
        private sealed class ScrollTrack : Control
        {
            private readonly OnyxScrollPanel _view;
            private int _grab;
            private bool _dragging, _hover;
            internal ScrollTrack(OnyxScrollPanel view)
            {
                _view = view; TabStop = true; AccessibleRole = AccessibleRole.ScrollBar; AccessibleName = "Défilement vertical";
                Cursor = Cursors.Hand; BackColor = FpsUi.BgMain;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            }
            private Rectangle Thumb
            {
                get
                {
                    int height = Math.Min(Height, Math.Max(32, (int)((long)Height * Height / Math.Max(1, _view.ContentHeight))));
                    int y = _view.Maximum == 0 ? 0 : (int)((long)_view.Offset * (Height - height) / _view.Maximum);
                    return new Rectangle(3, y, 6, height);
                }
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(BackColor); var thumb = Thumb;
                if (thumb.Height < 1) return;
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var path = FpsUi.Round(thumb, 3))
                using (var brush = new SolidBrush(_hover || _dragging || Focused ? FpsUi.Gold : FpsUi.GoldDim)) e.Graphics.FillPath(brush, path);
            }
            protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e); if (e.Button != MouseButtons.Left) return;
                Focus(); var thumb = Thumb;
                if (e.Y >= thumb.Top && e.Y <= thumb.Bottom) { _grab = e.Y - thumb.Top; _dragging = true; Capture = true; }
                else _view.ScrollTo(_view.Offset + (e.Y < thumb.Top ? -_view.Height : _view.Height));
                Invalidate();
            }
            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                if (_dragging) _view.ScrollTo((int)((long)(e.Y - _grab) * _view.Maximum / Math.Max(1, Height - Thumb.Height)));
            }
            protected override void OnMouseUp(MouseEventArgs e) { _dragging = false; Capture = false; Invalidate(); base.OnMouseUp(e); }
            protected override void OnMouseCaptureChanged(EventArgs e) { if (!Capture) _dragging = false; base.OnMouseCaptureChanged(e); }
            protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Up || keyData == Keys.Down || keyData == Keys.PageUp || keyData == Keys.PageDown || keyData == Keys.Home || keyData == Keys.End || base.IsInputKey(keyData); }
            protected override void OnKeyDown(KeyEventArgs e)
            {
                int next = _view.Offset;
                switch (e.KeyCode)
                {
                    case Keys.Up: next -= 40; break; case Keys.Down: next += 40; break;
                    case Keys.PageUp: next -= _view.Height; break; case Keys.PageDown: next += _view.Height; break;
                    case Keys.Home: next = 0; break; case Keys.End: next = _view.Maximum; break;
                    default: base.OnKeyDown(e); return;
                }
                _view.ScrollTo(next); e.Handled = true;
            }
            protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
            protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
#if BTTEST
            internal void PreviewKey(Keys key) { OnKeyDown(new KeyEventArgs(key)); }
            internal void PreviewDrag()
            {
                OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, 6, Thumb.Height / 2, 0));
                OnMouseMove(new MouseEventArgs(MouseButtons.Left, 0, 6, Height + Thumb.Height, 0));
                OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, 6, Height, 0));
            }
#endif
        }
    }
}
