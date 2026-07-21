using System;
using System.Drawing;
using System.Windows.Forms;

namespace BTOptimizer
{
    // Page Consultation : « Le Doc » (assistant), reserve aux offres premium.
    internal class PageConsultation : FpsPage
    {
        public PageConsultation(DashboardForm host) : base(host)
        {
            var hist = new Panel();
            hist.Name = "hist"; hist.BackColor = Color.Transparent;   // Name (pas Tag) : DoLayout fait Controls["hist"]
            hist.Paint += (s, e) =>
            {
                var r = ((Panel)s).ClientRectangle;
                FpsUi.PaintCard(e.Graphics, r, FpsUi.Card, FpsUi.Border, 12f);
                TextRenderer.DrawText(e.Graphics, "HISTORIQUE", FpsUi.H3, new Point(16, 14), FpsUi.Ink, TextFormatFlags.NoPadding);
                TextRenderer.DrawText(e.Graphics, "Aucune consultation", FpsUi.Body, new Rectangle(0, 60, r.Width, 20), FpsUi.Dim, TextFormatFlags.HorizontalCenter);
            };
            Controls.Add(hist);

            var input = new TextBox();
            input.Name = "input";
            input.Text = "Décris tes symptômes...";
            input.ForeColor = FpsUi.Dim2; input.BackColor = FpsUi.Card;
            input.BorderStyle = BorderStyle.FixedSingle; input.Font = FpsUi.Body;
            input.Enabled = false;
            Controls.Add(input);

            Resize += (s, e) => DoLayout();
        }

        public override void OnShown() { DoLayout(); }

        private void DoLayout()
        {
            var hist = Controls["hist"]; var input = Controls["input"];
            if (hist == null) return;
            int L = 34, top = 96;
            int right = Host != null ? Host.ContentRight(34) : ClientSize.Width - 34;
            hist.SetBounds(L, top, 300, ClientSize.Height - top - 34);
            if (input != null) input.SetBounds(L + 320, ClientSize.Height - 62, Math.Max(140, right - (L + 320)), 30);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            PaintTitle(g, "CONSULTATIONS", null);
            int right = Host != null ? Host.ContentRight(34) : ClientSize.Width - 34;
            TextRenderer.DrawText(g, "Le Doc est réservé aux offres Traitement Intensif et Accès à Vie.",
                FpsUi.Body, new Rectangle(354, ClientSize.Height - 96, Math.Max(160, right - 354), 22), FpsUi.Dim, TextFormatFlags.HorizontalCenter);
        }
    }
}
