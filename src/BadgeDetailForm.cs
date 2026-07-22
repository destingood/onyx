using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>Fiche d'un badge (au clic) : badge en grand, statut/progression, objectif, et un
    /// bouton « Y aller » qui ouvre la page du shell où progresser vers ce badge.</summary>
    internal class BadgeDetailForm : Form
    {
        private readonly BadgeCatalog.Badge _b;
        private readonly BadgeCatalog.Stats _s;
        private readonly bool _ok;
        private readonly Action _goTo;

        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int sz);

        public BadgeDetailForm(BadgeCatalog.Badge b, BadgeCatalog.Stats s, bool ok, Action goTo)
        {
            _b = b; _s = s; _ok = ok; _goTo = goTo;
            Text = "DesTinGOOD — " + b.Name;
            ClientSize = new Size(400, 360);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = FpsUi.BgMain;
            Font = FpsUi.Body;
            DoubleBuffered = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var go = FpsUi.NeonButton("Y aller  →");
            go.SetBounds(24, 302, 200, 40);
            go.Click += (s2, e2) => { try { if (_goTo != null) _goTo(); } catch { } Close(); };
            var close = FpsUi.GhostButton("Fermer");
            close.SetBounds(300, 307, 76, 30);
            close.Click += (s2, e2) => Close();
            Controls.Add(go); Controls.Add(close);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int v = 1; DwmSetWindowAttribute(Handle, 20, ref v, 4); } catch { }   // barre de titre sombre
        }

        private static string PageName(int p)
        {
            switch (p) { case 1: return "Optimisations"; case 2: return "Jeux"; case 3: return "Check Up+"; default: return "l'app"; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            int w = ClientSize.Width;
            Color tier = BadgeCatalog.TierColor[Math.Max(0, Math.Min(2, _b.Tier - 1))];

            BadgeRender.DrawShape(g, (w - 120) / 2, 26, 120, _b.Glyph, _ok, tier, _b.Shape);
            TextRenderer.DrawText(g, _b.Name, FpsUi.H1, new Rectangle(0, 152, w, 34), _ok ? FpsUi.Ink : FpsUi.Dim, TextFormatFlags.HorizontalCenter);

            if (_ok)
            {
                TextRenderer.DrawText(g, "✔  Badge débloqué", FpsUi.H3, new Rectangle(0, 194, w, 22), tier, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
            }
            else if (_s != null)
            {
                TextRenderer.DrawText(g, "Progression : " + _b.ProgressLabel(_s), FpsUi.H3, new Rectangle(0, 190, w, 22), FpsUi.Dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
                var bar = new Rectangle(70, 220, w - 140, 8);
                using (var bg = new SolidBrush(Color.FromArgb(30, 33, 31))) g.FillRectangle(bg, bar);
                int fw = (int)(bar.Width * _b.Progress(_s));
                if (fw > 1) using (var fb = new SolidBrush(tier)) g.FillRectangle(fb, bar.X, bar.Y, fw, bar.Height);
            }

            TextRenderer.DrawText(g, "Objectif : " + _b.Crit, FpsUi.Body, new Rectangle(24, 250, w - 48, 22), FpsUi.Dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
            if (!_ok)
                TextRenderer.DrawText(g, "→ " + PageName(_b.Page) + " pour progresser", FpsUi.Small, new Rectangle(24, 276, w - 48, 20), FpsUi.NeonDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
        }
    }
}
