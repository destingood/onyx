using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    // Page Collection : vitrine de badges/succès qui se DÉBLOQUENT selon l'état réel du PC
    // (optimisations actives, jeux détectés, santé, mode jeu). 100 % local, calcul en arrière-plan.
    internal class PageCollection : FpsPage
    {
        private sealed class Stats { public int OptiActive, OptiTotal, GamesDet, Health; public bool Boost; }
        private sealed class Badge
        {
            public string Glyph, Name, Crit;
            public Func<Stats, bool> Ok;
            public Badge(string g, string n, string c, Func<Stats, bool> ok) { Glyph = g; Name = n; Crit = c; Ok = ok; }
        }

        private Stats _s;
        private bool _computing;
        private Button _btnExport;

        private static readonly Badge[] _badges =
        {
            new Badge("🩹", "Premiers Soins", "Applique 1 optimisation", s => s.OptiActive >= 1),
            new Badge("🚀", "Optimiseur", "15 optimisations actives", s => s.OptiActive >= 15),
            new Badge("🔧", "Chirurgien", "40 optimisations actives", s => s.OptiActive >= 40),
            new Badge("🛡", "Blindé", "Santé du PC ≥ 60 %", s => s.Health >= 60),
            new Badge("🏆", "Perfectionniste", "Santé du PC ≥ 85 %", s => s.Health >= 85),
            new Badge("🎮", "Joueur", "1 jeu détecté", s => s.GamesDet >= 1),
            new Badge("📚", "Ludothèque", "4 jeux détectés", s => s.GamesDet >= 4),
            new Badge("⚡", "Mode Jeu", "Active le Mode Jeu", s => s.Boost),
            new Badge("💎", "Légende", "40 opti · santé ≥ 85 · 1 jeu", s => s.OptiActive >= 40 && s.Health >= 85 && s.GamesDet >= 1),
        };

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
                var st = new Stats();
                try
                {
                    var tw = Catalog.All(); st.OptiTotal = tw.Count;
                    foreach (var t in tw) { if (t.Check == null) continue; bool? c = null; try { c = t.Check(); } catch { } if (c == true) st.OptiActive++; }
                    st.Health = st.OptiTotal > 0 ? (int)Math.Round(100.0 * st.OptiActive / st.OptiTotal) : 0;
                }
                catch { }
                try { var games = GameScan.Known(); GameScan.Detect(games); foreach (var g in games) if (g.Detected) st.GamesDet++; } catch { }
                try { st.Boost = GameBoost.IsActive; } catch { }
                try { BeginInvoke((Action)(() => { _s = st; _computing = false; Invalidate(); })); } catch { _computing = false; }
            });
        }

        private int Unlocked() { int n = 0; if (_s != null) foreach (var b in _badges) if (b.Ok(_s)) n++; return n; }

        // save_showcase_image (FPSDoctor) : compose la collection en une image partageable (PNG).
        private void ExportShowcase()
        {
            if (_s == null) { MessageBox.Show(FindForm(), "Analyse de la collection en cours — réessaie dans un instant.", "Collection", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            string path = null, err = null;
            try
            {
                const int W = 940, H = 460;
                using (var bmp = new Bitmap(W, H))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                        using (var bg = new SolidBrush(FpsUi.BgMain)) g.FillRectangle(bg, 0, 0, W, H);
                        using (var pen = new Pen(FpsUi.Neon, 2f)) g.DrawRectangle(pen, 6, 6, W - 13, H - 13);

                        TextRenderer.DrawText(g, "MA COLLECTION ", FpsUi.H1, new Point(38, 28), FpsUi.Ink, TextFormatFlags.NoPadding);
                        int wt = TextRenderer.MeasureText(g, "MA COLLECTION ", FpsUi.H1).Width;
                        TextRenderer.DrawText(g, "DesTinGOOD", FpsUi.H1, new Point(38 + wt, 28), FpsUi.Neon, TextFormatFlags.NoPadding);
                        TextRenderer.DrawText(g, Unlocked() + " / " + _badges.Length + " badges débloqués   ·   santé du PC " + _s.Health + " %   ·   " + _s.GamesDet + " jeu(x) détecté(s)",
                            FpsUi.Body, new Point(40, 74), FpsUi.Dim, TextFormatFlags.NoPadding);

                        int cols = 5, cellW = 168, cellH = 150, x0 = 40, y0 = 116, gap = 8;
                        for (int i = 0; i < _badges.Length; i++)
                        {
                            int col = i % cols, row = i / cols;
                            int cx = x0 + col * (cellW + gap), cy = y0 + row * (cellH + gap);
                            bool ok = _badges[i].Ok(_s);
                            DrawBadgeGlyph(g, cx + (cellW - 96) / 2, cy, 96, _badges[i].Glyph, ok, true);
                            TextRenderer.DrawText(g, _badges[i].Name, FpsUi.H3, new Rectangle(cx, cy + 100, cellW, 20),
                                ok ? FpsUi.Ink : FpsUi.Dim2, TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
                        }
                        TextRenderer.DrawText(g, "Optimisé avec DesTinGOOD — le bloc opératoire de ton PC", FpsUi.Small,
                            new Rectangle(0, H - 32, W, 20), FpsUi.Dim, TextFormatFlags.HorizontalCenter);
                    }
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

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            int unlocked = Unlocked();
            PaintTitle(g, "COLLECTION", "Débloque des badges en soignant ton PC — " + unlocked + " / " + _badges.Length + " obtenus.");

            int L = 34, top = 96;
            // Vitrine (gauche) : le plus haut badge débloqué.
            int vw = 280, vh = 300;
            FpsUi.PaintCard(g, new Rectangle(L, top, vw, vh), FpsUi.Card, FpsUi.Border, 14f);
            Badge best = null;
            if (_s != null) for (int i = _badges.Length - 1; i >= 0; i--) if (_badges[i].Ok(_s)) { best = _badges[i]; break; }

            if (best != null && best.Name == "Premiers Soins" && Assets.BadgePremierSoin != null)
            {
                int bs = 150; g.DrawImage(Assets.BadgePremierSoin, L + (vw - bs) / 2, top + 44, bs, bs);
            }
            else
            {
                DrawBadgeGlyph(g, L + (vw - 140) / 2, top + 44, 140, best != null ? best.Glyph : "🩺", best != null, true);
            }
            string vname = _s == null ? "Analyse en cours…" : best != null ? best.Name : "Aucun badge";
            string vsub = _s == null ? "" : best != null ? "Badge le plus élevé débloqué" : "Applique une optimisation pour commencer";
            TextRenderer.DrawText(g, vname, FpsUi.H2, new Rectangle(L, top + vh - 66, vw, 26), best != null ? FpsUi.Ink : FpsUi.Dim, TextFormatFlags.HorizontalCenter);
            TextRenderer.DrawText(g, vsub, FpsUi.Small, new Rectangle(L, top + vh - 38, vw, 20), best != null ? FpsUi.Neon : FpsUi.Dim2, TextFormatFlags.HorizontalCenter);

            // Grille de badges (droite).
            int gx = L + vw + 30;
            int right = Host != null ? Host.ContentRight(34) : ClientSize.Width - 34;
            TextRenderer.DrawText(g, "BADGES", FpsUi.H3, new Point(gx, top - 6), FpsUi.Ink, TextFormatFlags.NoPadding);
            using (var pen = new Pen(FpsUi.Neon, 2f)) g.DrawLine(pen, gx, top + 14, gx + 64, top + 14);

            int cellW = 150, cellH = 128, gap = 14, gy = top + 26;
            int cols = Math.Max(1, (right - gx + gap) / (cellW + gap));
            for (int i = 0; i < _badges.Length; i++)
            {
                int col = i % cols, row = i / cols;
                int cx = gx + col * (cellW + gap), cy = gy + row * (cellH + gap);
                if (cx + cellW > right) continue;
                bool ok = _s != null && _badges[i].Ok(_s);
                var cell = new Rectangle(cx, cy, cellW, cellH);
                FpsUi.PaintCard(g, cell, ok ? Color.FromArgb(16, 26, 21) : Color.FromArgb(15, 16, 15), ok ? FpsUi.Neon : FpsUi.Border, 12f);
                DrawBadgeGlyph(g, cx + (cellW - 56) / 2, cy + 12, 56, _badges[i].Glyph, ok, false);
                TextRenderer.DrawText(g, _badges[i].Name, FpsUi.H3, new Rectangle(cx + 4, cy + 74, cellW - 8, 18), ok ? FpsUi.Ink : FpsUi.Dim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, _badges[i].Crit, FpsUi.Tiny, new Rectangle(cx + 6, cy + 94, cellW - 12, 28), ok ? FpsUi.NeonDim : FpsUi.Dim2,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            }
        }

        // Pastille hexagonale avec l'emoji du badge (néon si débloqué, grisé sinon).
        private void DrawBadgeGlyph(Graphics g, int x, int y, int size, string glyph, bool unlocked, bool large)
        {
            var c = new PointF(x + size / 2f, y + size / 2f);
            float r = size / 2f - 2;
            var pts = new PointF[6];
            for (int i = 0; i < 6; i++)
            {
                double a = Math.PI / 180 * (60 * i - 90);   // pointe en haut
                pts[i] = new PointF(c.X + (float)Math.Cos(a) * r, c.Y + (float)Math.Sin(a) * r);
            }
            using (var br = new SolidBrush(unlocked ? Color.FromArgb(22, 40, 31) : Color.FromArgb(20, 22, 21))) g.FillPolygon(br, pts);
            using (var pen = new Pen(unlocked ? FpsUi.Neon : FpsUi.Border, unlocked ? 2f : 1f)) g.DrawPolygon(pen, pts);
            TextRenderer.DrawText(g, glyph, large ? FpsUi.GlyphXL : FpsUi.GlyphL, new Rectangle(x, y, size, size),
                unlocked ? FpsUi.Neon : FpsUi.Dim2, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
