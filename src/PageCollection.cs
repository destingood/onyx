using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    // Page Collection : succès/badges PERSISTANTS (une fois gagnés, ils le restent), avec styles
    // variés (forme + couleur par palier). La grille est SCROLLABLE (gère n'importe quel nombre de
    // badges à toute taille de fenêtre). Alimentée par l'état réel du PC ET des événements.
    internal class PageCollection : FpsPage
    {
        private BadgeCatalog.Stats _s;
        private bool _computing;
        private Button _btnExport;
        private Panel _grid;
        private ScrollWheelFilter _wheel;
        private int _cols = 1;
        private Rectangle _vitrineRect;
        private BadgeCatalog.Badge _best;
        private readonly List<KeyValuePair<Rectangle, BadgeCatalog.Badge>> _hits = new List<KeyValuePair<Rectangle, BadgeCatalog.Badge>>();

        private const int L = 34, TopY = 100, VW = 280, VH = 300, CellW = 150, CellH = 132, Gap = 14;

        public PageCollection(DashboardForm host) : base(host)
        {
            _btnExport = FpsUi.GhostButton("Exporter en image");
            _btnExport.Size = new Size(160, 30);
            _btnExport.Click += (s, e) => ExportShowcase();
            Controls.Add(_btnExport);

            _grid = new CollectionGrid { BackColor = FpsUi.BgMain };
            _grid.Paint += GridPaint;
            _grid.MouseClick += GridClick;
            _grid.MouseMove += GridMove;
            Controls.Add(_grid);
            _wheel = new ScrollWheelFilter(_grid);
            try { Application.AddMessageFilter(_wheel); } catch { }

            MouseClick += OnVitrineClick;   // clic sur la vitrine (badge le plus élevé)
            Resize += (s, e) => { PlaceBtn(); LayoutGrid(); Invalidate(); };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _wheel != null) { try { Application.RemoveMessageFilter(_wheel); } catch { } _wheel = null; }
            base.Dispose(disposing);
        }

        private void PlaceBtn() { if (_btnExport != null) _btnExport.Location = new Point(ClientSize.Width - 34 - _btnExport.Width, 22); }

        private void LayoutGrid()
        {
            if (_grid == null) return;
            int gx = L + VW + 30, gridTop = TopY + 26;
            int w = Math.Max(120, ClientSize.Width - 34 - gx);
            int h = Math.Max(120, ClientSize.Height - gridTop - 20);
            _grid.SetBounds(gx, gridTop, w, h);
            _cols = Math.Max(1, (w - 18 + Gap) / (CellW + Gap));   // -18 : réserve la barre de défilement
            int rows = (BadgeCatalog.All.Length + _cols - 1) / _cols;
            _grid.AutoScrollMinSize = new Size(0, rows * (CellH + Gap) + 4);
            _grid.Invalidate();
        }

        public override void OnShown()
        {
            PlaceBtn(); LayoutGrid();
            if (_s == null && !_computing) ComputeAsync();
            Invalidate();
        }

        private void ComputeAsync()
        {
            _computing = true;
            AppStats.Get(a =>
            {
                var st = new BadgeCatalog.Stats
                {
                    OptiActive = a.OptiActive, OptiTotal = a.OptiTotal,
                    GamesDet = a.GamesDet, Health = a.Health,
                    Checkups = BadgeStore.Checkups,
                    Boost = BadgeStore.BoostUsed || GameBoost.IsActive
                };
                BadgeCatalog.Evaluate(st);   // persiste les badges remplis (déclenche les toasts)
                try { BeginInvoke((Action)(() => { _s = st; _computing = false; Invalidate(); if (_grid != null) _grid.Invalidate(); })); }
                catch { _computing = false; }
            });
        }

        private bool IsUnlocked(BadgeCatalog.Badge b) { return BadgeStore.IsEarned(b.Id) || (_s != null && b.Ok(_s)); }
        private int Unlocked() { int n = 0; foreach (var b in BadgeCatalog.All) if (IsUnlocked(b)) n++; return n; }

        // Badge « le plus élevé » = plus haut palier débloqué (puis dernier dans l'ordre).
        private BadgeCatalog.Badge Best()
        {
            BadgeCatalog.Badge best = null;
            foreach (var b in BadgeCatalog.All) if (IsUnlocked(b) && (best == null || b.Tier >= best.Tier)) best = b;
            return best;
        }

        private void OnVitrineClick(object sender, MouseEventArgs e)
        {
            if (_best != null && _vitrineRect.Contains(e.Location))
                using (var f = new BadgeDetailForm(_best, _s, true, () => Host.Goto(_best.Page))) f.ShowDialog(FindForm());
        }

        private void GridClick(object sender, MouseEventArgs e)
        {
            foreach (var kv in _hits)
                if (kv.Key.Contains(e.Location))
                {
                    var b = kv.Value;
                    using (var f = new BadgeDetailForm(b, _s, IsUnlocked(b), () => Host.Goto(b.Page))) f.ShowDialog(FindForm());
                    return;
                }
        }

        private void GridMove(object sender, MouseEventArgs e)
        {
            bool over = false; foreach (var kv in _hits) if (kv.Key.Contains(e.Location)) { over = true; break; }
            _grid.Cursor = over ? Cursors.Hand : Cursors.Default;
        }

        private void GridPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            // PAS de TranslateTransform ici : TextRenderer (GDI) l'ignore, donc dès qu'on défile,
            // textes et glyphes se détachent des cartes (formes décalées, logos hors cases). On
            // décale les coordonnées à la main — valable pour le GDI+ ET le texte GDI.
            var scroll = _grid.AutoScrollPosition;   // négatif quand on a défilé

            _hits.Clear();
            for (int i = 0; i < BadgeCatalog.All.Length; i++)
            {
                int col = i % _cols, row = i / _cols;
                int cx = col * (CellW + Gap) + scroll.X, cy = row * (CellH + Gap) + scroll.Y;
                var rc = new Rectangle(cx, cy, CellW, CellH);
                _hits.Add(new KeyValuePair<Rectangle, BadgeCatalog.Badge>(rc, BadgeCatalog.All[i]));
                if (cy + CellH < 0 || cy > _grid.ClientSize.Height) continue;   // hors zone visible
                DrawBadgeCell(g, cx, cy, CellW, BadgeCatalog.All[i], IsUnlocked(BadgeCatalog.All[i]), false);
            }
        }

        // ---- export image (save_showcase_image) ----
        private void ExportShowcase()
        {
            if (_s == null) { MessageBox.Show(FindForm(), "Analyse de la collection en cours — réessaie dans un instant.", "Collection", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            string path = null, err = null;
            try
            {
                using (var bmp = RenderShowcase())
                {
                    string dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    path = System.IO.Path.Combine(dir, "DesTinGOOD-collection.png");
                    bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            catch (Exception ex) { err = ex.Message; }
            if (path != null)
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
                catch { MessageBox.Show(FindForm(), "Image enregistrée sur le Bureau :\n" + path, "Collection", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            }
            else MessageBox.Show(FindForm(), "Export impossible :\n\n" + err, "Collection", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        internal Bitmap RenderShowcase()
        {
            int cols = 6, cellW = 150, cellH = 150, x0 = 34, y0 = 112, gap = 8;
            int rows = (BadgeCatalog.All.Length + cols - 1) / cols;
            int W = x0 * 2 + cols * cellW + (cols - 1) * gap, H = y0 + rows * (cellH + gap) + 34;
            var bmp = new Bitmap(W, H);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                using (var bg = new SolidBrush(FpsUi.BgMain)) g.FillRectangle(bg, 0, 0, W, H);
                using (var pen = new Pen(FpsUi.Neon, 2f)) g.DrawRectangle(pen, 6, 6, W - 13, H - 13);
                TextRenderer.DrawText(g, "MA COLLECTION ", FpsUi.H1, new Point(38, 26), FpsUi.Ink, TextFormatFlags.NoPadding);
                int wt = TextRenderer.MeasureText(g, "MA COLLECTION ", FpsUi.H1).Width;
                TextRenderer.DrawText(g, "DesTinGOOD", FpsUi.H1, new Point(38 + wt, 26), FpsUi.Neon, TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, Unlocked() + " / " + BadgeCatalog.All.Length + " badges   ·   santé " + _s.Health + " %   ·   " + _s.GamesDet + " jeu(x)   ·   " + _s.Checkups + " Check Up",
                    FpsUi.Body, new Point(40, 72), FpsUi.Dim, TextFormatFlags.NoPadding);

                for (int i = 0; i < BadgeCatalog.All.Length; i++)
                {
                    int col = i % cols, row = i / cols;
                    int cx = x0 + col * (cellW + gap), cy = y0 + row * (cellH + gap);
                    DrawBadgeCell(g, cx, cy, cellW, BadgeCatalog.All[i], IsUnlocked(BadgeCatalog.All[i]), true);
                }
                TextRenderer.DrawText(g, "Optimisé avec DesTinGOOD — le bloc opératoire de ton PC", FpsUi.Small,
                    new Rectangle(0, H - 28, W, 20), FpsUi.Dim, TextFormatFlags.HorizontalCenter);
            }
            return bmp;
        }

        internal void SeedDemoStats() { _s = new BadgeCatalog.Stats { OptiActive = 44, OptiTotal = 173, GamesDet = 3, Health = 78, Checkups = 6, Boost = true }; }

        /// <summary>Défile la grille à y (px) — pour le harnais de test visuel (régression : les
        /// textes des badges doivent rester DANS leurs cartes une fois la grille défilée).</summary>
        internal void ScrollGridTo(int y) { if (_grid != null) { _grid.AutoScrollPosition = new Point(0, y); _grid.Invalidate(); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            int unlocked = Unlocked();
            PaintTitle(g, "COLLECTION", "Débloque des badges en soignant ton PC — " + unlocked + " / " + BadgeCatalog.All.Length + " obtenus. (Ils restent acquis.)");

            // Vitrine (gauche) : le badge le plus élevé débloqué + résumé.
            _vitrineRect = new Rectangle(L, TopY, VW, VH);
            FpsUi.PaintCard(g, _vitrineRect, FpsUi.Card, FpsUi.Border, 14f);
            _best = Best();
            var tier = _best != null ? BadgeCatalog.TierColor[_best.Tier - 1] : FpsUi.Dim2;
            BadgeRender.DrawShape(g, L + (VW - 150) / 2, TopY + 34, 150, _best != null ? _best.Glyph : "🩺",
                _best != null, tier, _best != null ? _best.Shape : 0, true);
            string vname = _s == null ? "Analyse en cours…" : _best != null ? _best.Name : "Aucun badge";
            string vsub = _s == null ? "" : _best != null ? "Ton badge le plus élevé — clique" : "Applique une optimisation pour commencer";
            TextRenderer.DrawText(g, vname, FpsUi.H2, new Rectangle(L, TopY + VH - 96, VW, 26), _best != null ? FpsUi.Ink : FpsUi.Dim, TextFormatFlags.HorizontalCenter);
            TextRenderer.DrawText(g, vsub, FpsUi.Small, new Rectangle(L, TopY + VH - 68, VW, 18), _best != null ? FpsUi.Neon : FpsUi.Dim2, TextFormatFlags.HorizontalCenter);
            // Petit résumé chiffré sous la vitrine.
            if (_s != null)
                TextRenderer.DrawText(g, unlocked + " / " + BadgeCatalog.All.Length + " badges  ·  santé " + _s.Health + " %",
                    FpsUi.Tiny, new Rectangle(L, TopY + VH - 44, VW, 16), FpsUi.Dim, TextFormatFlags.HorizontalCenter);

            // En-tête de la grille (la grille elle-même est le panneau scrollable _grid).
            int gx = L + VW + 30;
            TextRenderer.DrawText(g, "BADGES", FpsUi.H3, new Point(gx, TopY - 6), FpsUi.Ink, TextFormatFlags.NoPadding);
            using (var pen = new Pen(FpsUi.Neon, 2f)) g.DrawLine(pen, gx, TopY + 14, gx + 64, TopY + 14);
        }

        // Carte d'un badge : cadre + forme/glyphe + nom + critère (ou progression si verrouillé).
        private void DrawBadgeCell(Graphics g, int cx, int cy, int cellW, BadgeCatalog.Badge b, bool ok, bool onImage)
        {
            int cellH = onImage ? 138 : 132;
            Color tier = BadgeCatalog.TierColor[b.Tier - 1];
            var cell = new Rectangle(cx, cy, cellW, cellH);
            FpsUi.PaintCard(g, cell, ok ? Color.FromArgb(15, 20, 17) : Color.FromArgb(14, 15, 14), ok ? Color.FromArgb(tier.R, tier.G, tier.B) : FpsUi.Border, 12f);
            BadgeRender.DrawShape(g, cx + (cellW - 56) / 2, cy + 10, 56, b.Glyph, ok, tier, b.Shape, false);
            TextRenderer.DrawText(g, b.Name, FpsUi.H3, new Rectangle(cx + 4, cy + 70, cellW - 8, 18),
                ok ? FpsUi.Ink : FpsUi.Dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);

            if (ok)
            {
                TextRenderer.DrawText(g, b.Crit, FpsUi.Tiny, new Rectangle(cx + 6, cy + 92, cellW - 12, 28),
                    Color.FromArgb(tier.R, tier.G, tier.B), TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            }
            else if (_s != null)
            {
                TextRenderer.DrawText(g, b.ProgressLabel(_s), FpsUi.Small, new Rectangle(cx, cy + 90, cellW, 16),
                    FpsUi.Dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
                var bar = new Rectangle(cx + 18, cy + cellH - 20, cellW - 36, 6);
                using (var bg = new SolidBrush(Color.FromArgb(30, 33, 31))) g.FillRectangle(bg, bar);
                int fw = (int)(bar.Width * b.Progress(_s));
                if (fw > 1) using (var fb = new SolidBrush(Color.FromArgb(165, tier.R, tier.G, tier.B))) g.FillRectangle(fb, bar.X, bar.Y, fw, bar.Height);
            }
            else
            {
                TextRenderer.DrawText(g, b.Crit, FpsUi.Tiny, new Rectangle(cx + 6, cy + 92, cellW - 12, 28),
                    FpsUi.Dim2, TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            }
        }
    }

    // Panneau de grille scrollable + double-bufferé (défilement fluide de dizaines de badges).
    internal class CollectionGrid : Panel
    {
        public CollectionGrid() { DoubleBuffered = true; AutoScroll = true; }
    }
}
