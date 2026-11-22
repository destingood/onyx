using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BTOptimizer
{
    internal class BadgeDetailForm : Form
    {
        private readonly BadgeCatalog.Badge _badge;
        private readonly BadgeCatalog.Stats _stats;
        private readonly bool _unlocked;
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int sz);

        public BadgeDetailForm(BadgeCatalog.Badge badge, BadgeCatalog.Stats stats, bool unlocked, Action goTo)
        {
            _badge = badge; _stats = stats; _unlocked = unlocked;
            Text = "ONYX — " + badge.Name;
            ClientSize = new Size(480, 480); FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterParent;
            BackColor = FpsUi.BgMain; Font = FpsUi.Body; DoubleBuffered = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            var go = OnyxDesign.AccentButton("Ouvrir " + PageName(badge.Page));
            go.SetBounds(24, 418, 284, 40);
            go.Click += (s, e) => { Close(); if (goTo != null) goTo(); };
            var close = OnyxDesign.Button("Fermer"); close.SetBounds(320, 418, 136, 40);
            close.Click += (s, e) => Close(); close.DialogResult = DialogResult.Cancel;
            Controls.Add(go); Controls.Add(close); CancelButton = close;
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int v = 1; DwmSetWindowAttribute(Handle, 20, ref v, 4); } catch { }
        }
        private static string PageName(int page) { return page == 1 ? "les optimisations" : page == 2 ? "mes jeux" : page == 3 ? "le bilan" : "ONYX"; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics; var tier = BadgeCatalog.TierColor[_badge.Tier - 1]; int w = ClientSize.Width;
            OnyxDesign.PaintCard(g, new Rectangle(24, 20, w - 48, 378), FpsUi.Card, OnyxDesign.Edge, 16);
            PageCollection.Center(g, "COLLECTION ONYX  /  " + PageCollection.TierName(_badge.Tier), FpsUi.Small, tier, new Rectangle(40, 37, w - 80, 22));
            BadgeRender.DrawShape(g, (w - 148) / 2, 70, 148, _badge.Glyph, _unlocked, tier, _badge.Shape);
            PageCollection.Center(g, _badge.Name, OnyxDesign.Title, FpsUi.Ink, new Rectangle(36, 229, w - 72, 34));
            PageCollection.Center(g, _unlocked ? "DÉBLOQUÉ · ACQUIS DÉFINITIVEMENT" : _stats == null ? "À OBTENIR" : "PROGRESSION  " + _badge.ProgressLabel(_stats), FpsUi.Small, _unlocked ? tier : FpsUi.Dim, new Rectangle(36, 272, w - 72, 24));
            PageCollection.ProgressBar(g, new Rectangle(66, 308, w - 132, 5), _unlocked ? 1 : _badge.Progress(_stats), tier);
            TextRenderer.DrawText(g, _badge.Crit, FpsUi.Body, new Rectangle(48, 332, w - 96, 48), FpsUi.Dim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
        }
    }
}
