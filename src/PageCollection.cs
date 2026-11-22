using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace BTOptimizer
{
    internal class PageCollection : FpsPage
    {
        private BadgeCatalog.Stats _s;
        private bool _computing, _preview;
        private int _filter;
        private readonly Button _export, _detail;
        private readonly Button[] _filters = new Button[3];
        private readonly OnyxScrollPanel _scroll;
        private readonly BufferedPanel _showcase;
        private readonly Label _count, _empty;
        private readonly List<BadgeCard> _cards = new List<BadgeCard>();
        private static readonly string[] FilterNames = { "Tous", "Débloqués", "À obtenir" };
        internal static string TierName(int tier) { return tier == 1 ? "BRONZE" : tier == 2 ? "ARGENT" : "OR"; }

        public PageCollection(DashboardForm host) : base(host)
        {
            _export = OnyxDesign.Button("Exporter ma collection");
            _export.Click += (s, e) => ExportShowcase(); Controls.Add(_export);
            _showcase = new BufferedPanel { BackColor = FpsUi.BgMain };
            _showcase.Paint += PaintShowcase; Controls.Add(_showcase);
            _detail = OnyxDesign.AccentButton("Voir mon badge");
            _detail.Click += (s, e) => { var best = Best(); if (best != null) ShowBadge(best); };
            _showcase.Controls.Add(_detail);
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                _filters[i] = OnyxDesign.Button(FilterNames[i]);
                _filters[i].Click += (s, e) => { _filter = index; RefreshCollection(); };
                Controls.Add(_filters[i]);
            }
            _count = new Label { ForeColor = FpsUi.Dim, Font = FpsUi.Small }; Controls.Add(_count);
            _scroll = new OnyxScrollPanel(); Controls.Add(_scroll);
            foreach (var badge in BadgeCatalog.All)
            {
                var card = new BadgeCard(this, badge);
                card.Click += (s, e) => ShowBadge(card.Badge);
                _cards.Add(card); _scroll.Content.Controls.Add(card);
            }
            _empty = new Label { Text = "Aucun badge dans cette vue.", ForeColor = FpsUi.Dim, Font = FpsUi.Body, AutoSize = true };
            _scroll.Content.Controls.Add(_empty);
            Resize += (s, e) => { DoLayout(); Invalidate(); };
            RefreshCollection();
        }
        public override void OnShown()
        {
            DoLayout();
            if (!_preview && !_computing) ComputeAsync();
        }
        private void ComputeAsync()
        {
            _computing = true;
            AppStats.Get(a =>
            {
                var stats = new BadgeCatalog.Stats { OptiActive = a.OptiActive, OptiTotal = a.OptiTotal,
                    GamesDet = a.GamesDet, Health = a.Health, Checkups = BadgeStore.Checkups,
                    Boost = BadgeStore.BoostUsed || GameBoost.IsActive };
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        _computing = false;
                        if (IsDisposed || _preview) return;
                        _s = stats;
#if !BTTEST
                        BadgeCatalog.Evaluate(stats);
#endif
                        RefreshCollection();
                    }));
                }
                catch { _computing = false; }
            });
        }
        private bool IsUnlocked(BadgeCatalog.Badge badge) { return (!_preview && BadgeStore.IsEarned(badge.Id)) || badge.Ok(_s); }
        private int Unlocked() { int n = 0; foreach (var b in BadgeCatalog.All) if (IsUnlocked(b)) n++; return n; }
        private BadgeCatalog.Badge Best()
        {
            BadgeCatalog.Badge best = null;
            foreach (var b in BadgeCatalog.All) if (IsUnlocked(b) && (best == null || b.Tier >= best.Tier)) best = b;
            return best;
        }
        private bool Matches(BadgeCatalog.Badge b) { return _filter == 0 || (_filter == 1 ? IsUnlocked(b) : !IsUnlocked(b)); }
        private void ShowBadge(BadgeCatalog.Badge b)
        {
            using (var detail = new BadgeDetailForm(b, _s, IsUnlocked(b), () => Host.Goto(b.Page))) detail.ShowDialog(FindForm());
        }
        private void RefreshCollection()
        {
            int visible = 0;
            foreach (var card in _cards)
            {
                card.Visible = Matches(card.Badge); if (Matches(card.Badge)) visible++;
                card.AccessibleDescription = card.Badge.Crit + (IsUnlocked(card.Badge) ? ", débloqué" : ", " + card.Badge.ProgressLabel(_s));
                card.Invalidate();
            }
            for (int i = 0; i < 3; i++)
            {
                _filters[i].ForeColor = i == _filter ? FpsUi.Gold : FpsUi.Dim;
                _filters[i].BackColor = i == _filter ? FpsUi.CardHi : FpsUi.Card;
            }
            _count.Text = visible + " badges  ·  " + Unlocked() + " débloqués sur " + BadgeCatalog.All.Length;
            _empty.Visible = visible == 0; _detail.Enabled = Best() != null; _export.Enabled = _s != null;
            _scroll.ScrollTo(0); DoLayout(); _showcase.Invalidate(); Invalidate();
        }
        private void DoLayout()
        {
            if (_scroll == null) return;
            _export.SetBounds(Math.Max(34, Width - 234), 20, 200, 34);
            _showcase.SetBounds(34, 100, 260, 420); _detail.SetBounds(22, 359, 216, 38);
            int left = 314, width = Math.Max(200, Width - left - 34), fw = (width - 16) / 3;
            for (int i = 0; i < 3; i++) _filters[i].SetBounds(left + i * (fw + 8), 100, fw, 38);
            _count.SetBounds(left, 153, width, 22);
            _scroll.SetBounds(left, 187, width + 20, Math.Max(150, Height - 205));
            int columns = Math.Max(2, _scroll.ContentWidth / 186), cw = (_scroll.ContentWidth - (columns - 1) * 12) / columns;
            int n = 0;
            foreach (var card in _cards)
            {
                if (!Matches(card.Badge)) continue;
                card.SetBounds(n % columns * (cw + 12), n / columns * 224, cw, 212); n++;
            }
            _empty.Location = new Point(12, 25);
            _scroll.ContentHeight = Math.Max(100, ((n + columns - 1) / columns) * 224);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            OnyxDesign.Heading(e.Graphics, "Collection", "Tes réussites, gravées dans le carbone. Chaque badge débloqué reste acquis.", Width - 210);
            OnyxDesign.Text(e.Graphics, "6 NOUVEAUX BADGES", FpsUi.H3, FpsUi.Gold, new Rectangle(34, 546, 260, 24));
            OnyxDesign.Text(e.Graphics, "Prise en main · Équipe complète\nExplorateur · Archiviste\nRégulier · Équilibre", FpsUi.Body, FpsUi.Dim, new Rectangle(34, 580, 260, 74), true);
        }
        private void PaintShowcase(object sender, PaintEventArgs e)
        {
            var g = e.Graphics; var best = Best(); var tier = best == null ? FpsUi.Dim : BadgeCatalog.TierColor[best.Tier - 1];
            OnyxDesign.PaintCard(g, _showcase.ClientRectangle, FpsUi.Card, OnyxDesign.Edge, 16);
            Center(g, "TA VITRINE ONYX", FpsUi.Small, FpsUi.Gold, new Rectangle(12, 24, 236, 22));
            BadgeRender.DrawShape(g, 52, 57, 156, best == null ? "💎" : best.Glyph, best != null, tier, best == null ? 4 : best.Shape);
            Center(g, best == null ? (_s == null ? "Analyse en cours…" : "L’aventure commence") : best.Name, FpsUi.H2, FpsUi.Ink, new Rectangle(14, 229, 232, 28));
            Center(g, best == null ? "Ton prochain succès t’attend" : TierName(best.Tier) + "  ·  DÉBLOQUÉ", FpsUi.Small, tier, new Rectangle(14, 265, 232, 22));
            Center(g, Unlocked() + " / " + BadgeCatalog.All.Length + " badges obtenus", FpsUi.Body, FpsUi.Dim, new Rectangle(14, 305, 232, 24));
            ProgressBar(g, new Rectangle(22, 337, 216, 4), (float)Unlocked() / BadgeCatalog.All.Length, FpsUi.Gold);
        }
        internal static void Center(Graphics g, string text, Font font, Color color, Rectangle rect)
        {
            TextRenderer.DrawText(g, text, font, rect, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }
        internal static void ProgressBar(Graphics g, Rectangle rect, float progress, Color color)
        {
            using (var brush = new SolidBrush(FpsUi.Border)) g.FillRectangle(brush, rect);
            int width = (int)(rect.Width * Math.Max(0, Math.Min(1, progress)));
            if (width > 0) using (var brush = new SolidBrush(color)) g.FillRectangle(brush, rect.X, rect.Y, width, rect.Height);
        }
        private void PaintBadge(Graphics g, int width, BadgeCatalog.Badge b, bool hot)
        {
            g.Clear(FpsUi.BgMain);
            bool ok = IsUnlocked(b); var tier = BadgeCatalog.TierColor[b.Tier - 1];
            OnyxDesign.PaintCard(g, new Rectangle(0, 0, width, 212), hot ? FpsUi.CardHi : FpsUi.Card, hot ? FpsUi.Gold : ok ? Color.FromArgb(tier.R / 2, tier.G / 2, tier.B / 2) : FpsUi.Border, 12);
            OnyxDesign.Text(g, TierName(b.Tier), FpsUi.Tiny, ok ? tier : FpsUi.Dim, new Rectangle(14, 12, width - 28, 18));
            BadgeRender.DrawShape(g, (width - 80) / 2, 33, 80, b.Glyph, ok, tier, b.Shape);
            Center(g, b.Name, FpsUi.H3, FpsUi.Ink, new Rectangle(8, 123, width - 16, 24));
            Center(g, ok ? "DÉBLOQUÉ" : _s == null ? "À OBTENIR" : b.ProgressLabel(_s), FpsUi.Small, ok ? tier : FpsUi.Dim, new Rectangle(8, 153, width - 16, 24));
            ProgressBar(g, new Rectangle(16, 190, width - 32, 4), ok ? 1 : b.Progress(_s), ok ? tier : FpsUi.GoldDim);
        }
        private void ExportShowcase()
        {
            if (_s == null) return;
            using (var dialog = new SaveFileDialog { Title = "Exporter ma collection ONYX", Filter = "Image PNG|*.png", FileName = "ONYX-collection.png", DefaultExt = "png", AddExtension = true })
            {
                if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
                try { using (var bmp = RenderShowcase()) bmp.Save(dialog.FileName, System.Drawing.Imaging.ImageFormat.Png); }
                catch (Exception ex) { MessageBox.Show(FindForm(), "Export impossible : " + ex.Message, "Collection", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }
        internal Bitmap RenderShowcase()
        {
            const int columns = 5, cw = 196, gap = 12, pad = 28;
            int width = pad * 2 + columns * cw + (columns - 1) * gap, height = 130 + ((BadgeCatalog.All.Length + columns - 1) / columns) * 224;
            var result = new Bitmap(width, height);
            using (var g = Graphics.FromImage(result))
            {
                g.Clear(FpsUi.BgMain);
                OnyxDesign.Text(g, "COLLECTION ONYX", OnyxDesign.Title, FpsUi.Gold, new Rectangle(pad, 24, width - pad * 2, 36));
                OnyxDesign.Text(g, Unlocked() + " / " + BadgeCatalog.All.Length + " badges obtenus · Bronze, argent et or", FpsUi.Body, FpsUi.Dim, new Rectangle(pad, 70, width - pad * 2, 28));
                for (int i = 0; i < BadgeCatalog.All.Length; i++)
                {
                    // Chaque cellule utilise ses coordonnées locales, y compris le texte GDI.
                    using (var cell = new Bitmap(cw, 212))
                    {
                        using (var cg = Graphics.FromImage(cell)) { cg.Clear(FpsUi.BgMain); PaintBadge(cg, cw, BadgeCatalog.All[i], false); }
                        g.DrawImageUnscaled(cell, pad + i % columns * (cw + gap), 118 + i / columns * 224);
                    }
                }
            }
            return result;
        }
        internal void SeedDemoStats()
        {
            _preview = true;
            _s = new BadgeCatalog.Stats { OptiActive = 44, OptiTotal = 173, GamesDet = 6, Health = 78, Checkups = 6, Boost = true };
            RefreshCollection();
        }
        internal void ScrollGridTo(int y) { _scroll.ScrollTo(y); }
#if BTTEST
        internal void PreviewBadgeStats(BadgeCatalog.Stats stats)
        {
            _preview = true; _s = stats; RefreshCollection();
        }
        internal void ValidateBadgeUi()
        {
            for (int f = 0; f < 3; f++)
            {
                _filters[f].PerformClick();
                foreach (var card in _cards) if (card.Visible != Matches(card.Badge)) throw new InvalidOperationException("Filtre de badges incorrect.");
            }
            _filters[0].PerformClick(); _scroll.ValidateScrolling();
            foreach (var card in _cards) if (card.Right > _scroll.ContentWidth) throw new InvalidOperationException("Badge hors grille.");
            var ids = new HashSet<string>();
            foreach (var b in BadgeCatalog.All) if (!ids.Add(b.Id) || b.Target <= 0 || b.Progress(new BadgeCatalog.Stats()) != 0) throw new InvalidOperationException("Catalogue invalide.");
            var before = new BadgeCatalog.Stats(); var after = new BadgeCatalog.Stats();
            foreach (string id in new[] { "prise_en_main", "equipe", "explorateur", "archiviste", "regulier", "equilibre" })
            {
                var b = BadgeCatalog.ById(id);
                if (b == null) throw new InvalidOperationException("Badge manquant : " + id);
                before.OptiActive = before.GamesDet = before.Health = before.Checkups = b.Target - 1;
                after.OptiActive = after.GamesDet = after.Health = after.Checkups = b.Target;
                if (b == null || b.Ok(before) || !b.Ok(after)) throw new InvalidOperationException("Seuil incorrect : " + id);
            }
        }
#endif
        private sealed class BadgeCard : Button
        {
            internal readonly BadgeCatalog.Badge Badge;
            private readonly PageCollection _page;
            private bool _hot;
            internal BadgeCard(PageCollection page, BadgeCatalog.Badge badge)
            {
                _page = page; Badge = badge; AccessibleName = badge.Name; Cursor = Cursors.Hand;
                FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; BackColor = FpsUi.BgMain;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
                MouseEnter += (s, e) => { _hot = true; Invalidate(); };
                MouseLeave += (s, e) => { _hot = false; Invalidate(); };
                GotFocus += (s, e) => Invalidate(); LostFocus += (s, e) => Invalidate();
            }
            protected override void OnPaint(PaintEventArgs e) { _page.PaintBadge(e.Graphics, Width, Badge, _hot || Focused); }
        }
    }
}
