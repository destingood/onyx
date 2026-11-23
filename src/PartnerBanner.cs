using System;
using System.Drawing;
using System.Windows.Forms;

namespace BTOptimizer
{
    internal sealed class PartnerBanner : BufferedPanel
    {
        private Monetisation.Campaign _campaign;
        private string _dismissedId;
        private readonly Button _open, _dismiss;
        private readonly Timer _expiry = new Timer { Interval = 60000 };
        internal event Action VisibilityChanged;
        internal bool Active { get { return _campaign != null; } }
        internal PartnerBanner()
        {
            BackColor = FpsUi.BgMain;
            _open = OnyxDesign.AccentButton("Découvrir  ↗");
            _open.Click += (s, e) =>
            {
                var active = Monetisation.ActiveCampaign(MonetisationRuntime.Settings, DateTimeOffset.UtcNow);
                if (active != null && _campaign != null && active.Id == _campaign.Id) PageBonsPlans.Ouvre(active.Url);
                else RefreshCampaign();
            };
            _dismiss = OnyxDesign.Button("×"); _dismiss.AccessibleName = "Masquer le partenaire pour cette session";
            _dismiss.Click += (s, e) => { _dismissedId = _campaign == null ? null : _campaign.Id; RefreshCampaign(); };
            Controls.Add(_open); Controls.Add(_dismiss);
            Resize += (s, e) => { _dismiss.SetBounds(Width - 42, 10, 28, 28); _open.SetBounds(Width - 204, 24, 150, 32); };
            _expiry.Tick += (s, e) => RefreshCampaign();
            RefreshCampaign();
        }
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (_expiry == null) return;
            if (Visible && Active) _expiry.Start(); else _expiry.Stop();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) _expiry.Dispose();
            base.Dispose(disposing);
        }
        internal void RefreshCampaign()
        {
            _campaign = Monetisation.ActiveCampaign(MonetisationRuntime.Settings, DateTimeOffset.UtcNow);
            if (_campaign != null && _campaign.Id == _dismissedId) _campaign = null;
            Visible = Active; Invalidate(); if (VisibilityChanged != null) VisibilityChanged();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); if (_campaign == null) return;
            var g = e.Graphics;
            OnyxDesign.PaintCard(g, ClientRectangle, FpsUi.Card, OnyxDesign.Edge, 10);
            OnyxDesign.Text(g, "SPONSORISÉ · " + _campaign.Advertiser, FpsUi.Tiny, FpsUi.Gold, new Rectangle(16, 8, Width - 236, 17));
            OnyxDesign.Text(g, _campaign.Title, FpsUi.H3, FpsUi.Ink, new Rectangle(16, 27, Width - 236, 23));
            OnyxDesign.Text(g, _campaign.Description, FpsUi.Small, FpsUi.Dim, new Rectangle(16, 51, Width - 236, 18));
        }
#if BTTEST
        internal void ValidateUi()
        {
            if (Active && (!ClientRectangle.Contains(_open.Bounds) || _open.Bounds.IntersectsWith(_dismiss.Bounds))) throw new InvalidOperationException("Actions du partenaire hors cadre.");
        }
        internal void PreviewDismiss() { _dismiss.PerformClick(); }
#endif
    }
}
