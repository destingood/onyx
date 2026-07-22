using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    // Page Collection : succès/badges PERSISTANTS (une fois gagnés, ils le restent), avec styles
    // variés (forme + couleur par palier). Alimentés par l'état réel du PC ET des événements
    // (Check Up réalisés, Mode Jeu utilisé). Calcul en arrière-plan.
    internal class PageCollection : FpsPage
    {
        // Définitions des badges partagées : BadgeCatalog.All / .TierColor (id → affichage + condition).
        private BadgeCatalog.Stats _s;
        private bool _computing;
        private Button _btnExport;

        public PageCollection(DashboardForm host) : base(host)
        {
            _btnExport = FpsUi.GhostButton("Exporter en image");
            _btnExport.Size = new Size(160, 30);
            _btnExport.Click += (s, e) => ExportShowcase();
            Controls.Add(_btnExport);
            Resize += (s, e) => { PlaceBtn(); Invalidate(); };
        }

        private void PlaceBtn() { if (_btnExport != null) _btnExport.Location = new Point(ClientSize.Width - 34 - _btnExport.Width, 22); }

        public override void OnShown()
        {
            PlaceBtn();
            if (_s == null && !_computing) ComputeAsync();
            Invalidate();
        }

        private void ComputeAsync()
        {
            _computing = true;
            Task.Run(() =>
            {
                var st = new BadgeCatalog.Stats();
                try
                {
                    var tw = Catalog.All(); st.OptiTotal = tw.Count;
                    foreach (var t in tw) { if (t.Check == null) continue; bool? c = null; try { c = t.Check(); } catch { } if (c == true) st.OptiActive++; }
                    st.Health = st.OptiTotal > 0 ? (int)Math.Round(100.0 * st.OptiActive / st.OptiTotal) : 0;
                }
                catch { }
                try { var games = GameScan.Known(); GameScan.Detect(games); foreach (var g in games) if (g.Detected) st.GamesDet++; } catch { }
                st.Checkups = BadgeStore.Checkups;
                st.Boost = BadgeStore.BoostUsed || GameBoost.IsActive;

                // Persiste tout badge dont la condition est actuellement remplie (déclenche les toasts).
                BadgeCatalog.Evaluate(st);

                try { BeginInvoke((Action)(() => { _s = st; _computing = false; Invalidate(); })); } catch { _computing = false; }
            });
        }

        private bool IsUnlocked(BadgeCatalog.Badge b) { return BadgeStore.IsEarned(b.Id) || (_s != null && b.Ok(_s)); }
        private int Unlocked() { int n = 0; foreach (var b in BadgeCatalog.All) if (IsUnlocked(b)) n++; return n; }

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
            const int W = 980, H = 470;
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

                int cols = 6, cellW = 150, cellH = 150, x0 = 34, y0 = 112, gap = 8;
                for (int i = 0; i < BadgeCatalog.All.Length; i++)
                {
                    int col = i % cols, row = i / cols;
                    int cx = x0 + col * (cellW + gap), cy = y0 + row * (cellH + gap);
                    DrawBadgeCell(g, cx, cy, cellW, BadgeCatalog.All[i], IsUnlocked(BadgeCatalog.All[i]), true);
                }
                TextRenderer.DrawText(g, "Optimisé avec DesTinGOOD — le bloc opératoire de ton PC", FpsUi.Small,
                    new Rectangle(0, H - 30, W, 20), FpsUi.Dim, TextFormatFlags.HorizontalCenter);
            }
            return bmp;
        }

        internal void SeedDemoStats() { _s = new BadgeCatalog.Stats { OptiActive = 44, OptiTotal = 173, GamesDet = 3, Health = 78, Checkups = 6, Boost = true }; }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            int unlocked = Unlocked();
            PaintTitle(g, "COLLECTION", "Débloque des badges en soignant ton PC — " + unlocked + " / " + BadgeCatalog.All.Length + " obtenus. (Ils restent acquis.)");

            int L = 34, top = 100;
            // Vitrine (gauche) : le badge le plus élevé débloqué.
            int vw = 280, vh = 300;
            FpsUi.PaintCard(g, new Rectangle(L, top, vw, vh), FpsUi.Card, FpsUi.Border, 14f);
            BadgeCatalog.Badge best = null;
            for (int i = BadgeCatalog.All.Length - 1; i >= 0; i--) if (IsUnlocked(BadgeCatalog.All[i])) { best = BadgeCatalog.All[i]; break; }
            DrawBadgeShape(g, L + (vw - 150) / 2, top + 40, 150, best != null ? best.Glyph : "🩺",
                best != null, best != null ? BadgeCatalog.TierColor[best.Tier - 1] : FpsUi.Dim2, best != null ? best.Shape : 0, true);
            string vname = _s == null ? "Analyse en cours…" : best != null ? best.Name : "Aucun badge";
            string vsub = _s == null ? "" : best != null ? "Ton badge le plus élevé" : "Applique une optimisation pour commencer";
            TextRenderer.DrawText(g, vname, FpsUi.H2, new Rectangle(L, top + vh - 66, vw, 26), best != null ? FpsUi.Ink : FpsUi.Dim, TextFormatFlags.HorizontalCenter);
            TextRenderer.DrawText(g, vsub, FpsUi.Small, new Rectangle(L, top + vh - 38, vw, 20), best != null ? FpsUi.Neon : FpsUi.Dim2, TextFormatFlags.HorizontalCenter);

            // Grille de badges (droite) — plein espace (mascotte retirée).
            int gx = L + vw + 30;
            int right = ClientSize.Width - 34;
            TextRenderer.DrawText(g, "BADGES", FpsUi.H3, new Point(gx, top - 6), FpsUi.Ink, TextFormatFlags.NoPadding);
            using (var pen = new Pen(FpsUi.Neon, 2f)) g.DrawLine(pen, gx, top + 14, gx + 64, top + 14);

            int cellW = 150, cellH = 132, gap = 14, gy = top + 26;
            int cols = Math.Max(1, (right - gx + gap) / (cellW + gap));
            for (int i = 0; i < BadgeCatalog.All.Length; i++)
            {
                int col = i % cols, row = i / cols;
                int cx = gx + col * (cellW + gap), cy = gy + row * (cellH + gap);
                if (cx + cellW > right + 2) continue;
                DrawBadgeCell(g, cx, cy, cellW, BadgeCatalog.All[i], IsUnlocked(BadgeCatalog.All[i]), false);
            }
        }

        // Carte d'un badge : cadre + forme/glyphe + nom + critère.
        private void DrawBadgeCell(Graphics g, int cx, int cy, int cellW, BadgeCatalog.Badge b, bool ok, bool onImage)
        {
            int cellH = onImage ? 138 : 132;
            Color tier = BadgeCatalog.TierColor[b.Tier - 1];
            var cell = new Rectangle(cx, cy, cellW, cellH);
            FpsUi.PaintCard(g, cell, ok ? Color.FromArgb(15, 20, 17) : Color.FromArgb(14, 15, 14), ok ? Color.FromArgb(tier.R, tier.G, tier.B) : FpsUi.Border, 12f);
            DrawBadgeShape(g, cx + (cellW - 56) / 2, cy + 10, 56, b.Glyph, ok, tier, b.Shape, false);
            TextRenderer.DrawText(g, b.Name, FpsUi.H3, new Rectangle(cx + 4, cy + 70, cellW - 8, 18),
                ok ? FpsUi.Ink : FpsUi.Dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);

            if (ok)
            {
                TextRenderer.DrawText(g, b.Crit, FpsUi.Tiny, new Rectangle(cx + 6, cy + 92, cellW - 12, 28),
                    Color.FromArgb(tier.R, tier.G, tier.B), TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            }
            else if (_s != null)
            {
                // Badge verrouillé : progression vers le déblocage (motivant).
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

        // Forme du badge (hexagone/cercle/bouclier/étoile/losange) + emoji au centre.
        private void DrawBadgeShape(Graphics g, int x, int y, int size, string glyph, bool unlocked, Color tier, int shape, bool large)
        {
            Color fill = unlocked ? Color.FromArgb(30, tier.R, tier.G, tier.B) : Color.FromArgb(20, 22, 21);
            Color edge = unlocked ? tier : FpsUi.Border;
            using (var path = ShapePath(x, y, size, shape))
            using (var br = new SolidBrush(fill))
            using (var pen = new Pen(edge, unlocked ? 2f : 1f))
            {
                g.FillPath(br, path);
                g.DrawPath(pen, path);
            }
            TextRenderer.DrawText(g, glyph, large ? FpsUi.GlyphXL : FpsUi.GlyphL, new Rectangle(x, y, size, size),
                unlocked ? tier : FpsUi.Dim2, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private static GraphicsPath ShapePath(int x, int y, int size, int shape)
        {
            var p = new GraphicsPath();
            var c = new PointF(x + size / 2f, y + size / 2f);
            float r = size / 2f - 2;
            switch (shape)
            {
                case 1: // cercle
                    p.AddEllipse(c.X - r, c.Y - r, r * 2, r * 2);
                    break;
                case 2: // bouclier
                    {
                        float w = r * 1.7f, h = r * 2f, l = c.X - w / 2, t = c.Y - h / 2;
                        p.AddArc(l, t, w * 0.5f, h * 0.5f, 180, 90);
                        p.AddArc(l + w * 0.5f, t, w * 0.5f, h * 0.5f, 270, 90);
                        p.AddLine(l + w, t + h * 0.45f, c.X, t + h);
                        p.AddLine(c.X, t + h, l, t + h * 0.45f);
                        p.CloseFigure();
                        break;
                    }
                case 3: // étoile 5 branches
                    {
                        var pts = new PointF[10];
                        for (int i = 0; i < 10; i++)
                        {
                            double a = Math.PI / 5 * i - Math.PI / 2;
                            float rr = (i % 2 == 0) ? r : r * 0.44f;
                            pts[i] = new PointF(c.X + (float)Math.Cos(a) * rr, c.Y + (float)Math.Sin(a) * rr);
                        }
                        p.AddPolygon(pts);
                        break;
                    }
                case 4: // losange
                    p.AddPolygon(new[] { new PointF(c.X, c.Y - r), new PointF(c.X + r, c.Y), new PointF(c.X, c.Y + r), new PointF(c.X - r, c.Y) });
                    break;
                default: // hexagone (pointe en haut)
                    {
                        var pts = new PointF[6];
                        for (int i = 0; i < 6; i++) { double a = Math.PI / 180 * (60 * i - 90); pts[i] = new PointF(c.X + (float)Math.Cos(a) * r, c.Y + (float)Math.Sin(a) * r); }
                        p.AddPolygon(pts);
                        break;
                    }
            }
            return p;
        }
    }
}
