using System;
using System.Drawing;
using System.Windows.Forms;

namespace BTOptimizer
{
    // Page Consultation : « Le Doc » — assistant symptôme → soin. On choisit ce qui cloche,
    // Le Doc ouvre directement le bon outil. Alimenté par HelpCatalog (partagé avec HelpNavForm).
    internal class PageConsultation : FpsPage
    {
        private FlowLayoutPanel _flow;
        private bool _built;

        public PageConsultation(DashboardForm host) : base(host)
        {
            _flow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 4, 0, 20)
            };
            Controls.Add(_flow);
            Resize += (s, e) => DoLayout();
        }

        public override void OnShown()
        {
            if (!_built) { Populate(); _built = true; }
            DoLayout();
        }

        private void Populate()
        {
            _flow.SuspendLayout();
            string lastGroup = null;
            foreach (HelpCatalog.Entry e in HelpCatalog.Entries(Host.Log))
            {
                if (e.Group != lastGroup) { _flow.Controls.Add(MakeHeader(e.Group)); lastGroup = e.Group; }
                _flow.Controls.Add(MakeCard(e));
            }
            _flow.ResumeLayout();
        }

        private Control MakeHeader(string text)
        {
            var h = new Panel { Height = 34, Margin = new Padding(0, 10, 0, 2), BackColor = Color.Transparent, Tag = "hdr" };
            h.Paint += (s, e) =>
            {
                Graphics g = e.Graphics;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                TextRenderer.DrawText(g, text.ToUpperInvariant(), FpsUi.H3, new Point(2, 8), FpsUi.Neon, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                int w = TextRenderer.MeasureText(g, text.ToUpperInvariant(), FpsUi.H3, Size.Empty, TextFormatFlags.NoPrefix).Width;
                using (var pen = new Pen(FpsUi.Neon, 2f)) g.DrawLine(pen, 2, 30, 2 + w, 30);
                using (var pen = new Pen(FpsUi.Border)) g.DrawLine(pen, 2 + w + 8, 30, h.Width - 2, 30);
            };
            return h;
        }

        private Control MakeCard(HelpCatalog.Entry entry)
        {
            var card = new Panel { Height = 56, Margin = new Padding(0, 5, 0, 0), BackColor = Color.Transparent, Cursor = Cursors.Hand, Tag = "card" };
            bool[] hover = { false };
            card.Paint += (s, e) =>
            {
                Graphics g = e.Graphics;
                var r = ((Panel)s).ClientRectangle;
                FpsUi.PaintCard(g, r, hover[0] ? FpsUi.CardHi : FpsUi.Card, hover[0] ? FpsUi.Neon : FpsUi.Border, 10f);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                TextRenderer.DrawText(g, entry.Symptom, FpsUi.H3, new Rectangle(16, 9, r.Width - 150, 22), FpsUi.Ink,
                    TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, "→  " + entry.Tool, FpsUi.Small, new Rectangle(16, 31, r.Width - 60, 18), FpsUi.NeonDim,
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, "⟶", FpsUi.H2, new Rectangle(r.Width - 44, 0, 34, r.Height), hover[0] ? FpsUi.Neon : FpsUi.Dim2,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };
            card.MouseEnter += (s, e) => { hover[0] = true; card.Invalidate(); };
            card.MouseLeave += (s, e) => { hover[0] = false; card.Invalidate(); };
            card.Click += (s, e) => { try { Host.OpenDialog(entry.Open()); } catch { } };
            return card;
        }

        private void DoLayout()
        {
            if (_flow == null) return;
            int L = 20, top = 100;
            int right = Host != null ? Host.ContentRight(34) : ClientSize.Width - 34;   // laisse la place au Doc (mascotte)
            int bottom = ClientSize.Height - 24;
            _flow.SetBounds(L, top, Math.Max(320, right - L), Math.Max(120, bottom - top));
            int cardW = _flow.ClientSize.Width - _flow.Padding.Horizontal - 6;
            foreach (Control c in _flow.Controls) c.Width = Math.Max(260, cardW);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            PaintTitle(e.Graphics, "CONSULTATION",
                "Le Doc t'oriente : choisis ton symptôme, j'ouvre directement le bon soin.");
        }
    }
}
