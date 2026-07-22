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
        private sealed class Stats { public int OptiActive, OptiTotal, GamesDet, Health, Checkups; public bool Boost; }

        // shape : 0 hexagone · 1 cercle · 2 bouclier · 3 étoile · 4 losange
        private sealed class Badge
        {
            public string Id, Glyph, Name, Crit;
            public int Tier, Shape;
            public Func<Stats, bool> Ok;
            public Badge(string id, string g, string n, string c, int tier, int shape, Func<Stats, bool> ok)
            { Id = id; Glyph = g; Name = n; Crit = c; Tier = tier; Shape = shape; Ok = ok; }
        }

        private Stats _s;
        private bool _computing;
        private Button _btnExport;

        private static readonly Color[] TierColor =
        {
            Color.FromArgb(0, 255, 136),   // palier 1 — vert néon
            Color.FromArgb(0, 200, 255),   // palier 2 — cyan
            Color.FromArgb(255, 200, 60),  // palier 3 — or
        };

        private static readonly Badge[] _badges =
        {
            new Badge("premiers",   "🩹", "Premiers Soins",  "Applique 1 optimisation",    1, 0, s => s.OptiActive >= 1),
            new Badge("optimiseur", "🚀", "Optimiseur",      "15 optimisations actives",   1, 0, s => s.OptiActive >= 15),
            new Badge("chirurgien", "🔧", "Chirurgien",      "40 optimisations actives",   2, 2, s => s.OptiActive >= 40),
            new Badge("blinde",     "🛡", "Blindé",          "Santé du PC ≥ 60 %",         1, 2, s => s.Health >= 60),
            new Badge("perfect",    "🏆", "Perfectionniste", "Santé du PC ≥ 85 %",         3, 3, s => s.Health >= 85),
            new Badge("joueur",     "🎮", "Joueur",          "1 jeu détecté",              1, 1, s => s.GamesDet >= 1),
            new Badge("ludo",       "📚", "Ludothèque",      "4 jeux détectés",            2, 1, s => s.GamesDet >= 4),
            new Badge("modejeu",    "⚡", "Mode Jeu",        "Active le Mode Jeu",         1, 0, s => s.Boost),
            new Badge("infirmier",  "🩺", "Infirmier",       "1 Check Up réalisé",         1, 1, s => s.Checkups >= 1),
            new Badge("routine",    "💊", "Routine",         "5 Check Up réalisés",        2, 0, s => s.Checkups >= 5),
            new Badge("legende",    "💎", "Légende",         "40 opti · 85 % · jeu · Check Up", 3, 4, s => s.OptiActive >= 40 && s.Health >= 85 && s.GamesDet >= 1 && s.Checkups >= 1),
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
                st.Checkups = BadgeStore.Checkups;
                st.Boost = BadgeStore.BoostUsed || GameBoost.IsActive;

                // Persiste tout badge dont la condition est actuellement remplie.
                foreach (var b in _badges) { try { if (b.Ok(st)) BadgeStore.MarkEarned(b.Id); } catch { } }

                try { BeginInvoke((Action)(() => { _s = st; _computing = false; Invalidate(); })); } catch { _computing = false; }
            });
        }

        private bool IsUnlocked(Badge b) { return BadgeStore.IsEarned(b.Id) || (_s != null && b.Ok(_s)); }
        private int Unlocked() { int n = 0; foreach (var b in _badges) if (IsUnlocked(b)) n++; return n; }

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
                TextRenderer.DrawText(g, Unlocked() + " / " + _badges.Length + " badges   ·   santé " + _s.Health + " %   ·   " + _s.GamesDet + " jeu(x)   ·   " + _s.Checkups + " Check Up",
                    FpsUi.Body, new Point(40, 72), FpsUi.Dim, TextFormatFlags.NoPadding);

                int cols = 6, cellW = 150, cellH = 150, x0 = 34, y0 = 112, gap = 8;
                for (int i = 0; i < _badges.Length; i++)
                {
                    int col = i % cols, row = i / cols;
                    int cx = x0 + col * (cellW + gap), cy = y0 + row * (cellH + gap);
                    DrawBadgeCell(g, cx, cy, cellW, _badges[i], IsUnlocked(_badges[i]), true);
                }
                TextRenderer.DrawText(g, "Optimisé avec DesTinGOOD — le bloc opératoire de ton PC", FpsUi.Small,
                    new Rectangle(0, H - 30, W, 20), FpsUi.Dim, TextFormatFlags.HorizontalCenter);
            }
            return bmp;
        }

        internal void SeedDemoStats() { _s = new Stats { OptiActive = 44, OptiTotal = 173, GamesDet = 3, Health = 78, Checkups = 6, Boost = true }; }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            int unlocked = Unlocked();
            PaintTitle(g, "COLLECTION", "Débloque des badges en soignant ton PC — " + unlocked + " / " + _badges.Length + " obtenus. (Ils restent acquis.)");

            int L = 34, top = 100;
            // Vitrine (gauche) : le badge le plus élevé débloqué.
            int vw = 280, vh = 300;
            FpsUi.PaintCard(g, new Rectangle(L, top, vw, vh), FpsUi.Card, FpsUi.Border, 14f);
            Badge best = null;
            for (int i = _badges.Length - 1; i >= 0; i--) if (IsUnlocked(_badges[i])) { best = _badges[i]; break; }
            DrawBadgeShape(g, L + (vw - 150) / 2, top + 40, 150, best != null ? best.Glyph : "🩺",
                best != null, best != null ? TierColor[best.Tier - 1] : FpsUi.Dim2, best != null ? best.Shape : 0, true);
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
            for (int i = 0; i < _badges.Length; i++)
            {
                int col = i % cols, row = i / cols;
                int cx = gx + col * (cellW + gap), cy = gy + row * (cellH + gap);
                if (cx + cellW > right + 2) continue;
                DrawBadgeCell(g, cx, cy, cellW, _badges[i], IsUnlocked(_badges[i]), false);
            }
        }

        // Carte d'un badge : cadre + forme/glyphe + nom + critère.
        private void DrawBadgeCell(Graphics g, int cx, int cy, int cellW, Badge b, bool ok, bool onImage)
        {
            int cellH = onImage ? 138 : 132;
            Color tier = TierColor[b.Tier - 1];
            var cell = new Rectangle(cx, cy, cellW, cellH);
            FpsUi.PaintCard(g, cell, ok ? Color.FromArgb(15, 20, 17) : Color.FromArgb(14, 15, 14), ok ? Color.FromArgb(tier.R, tier.G, tier.B) : FpsUi.Border, 12f);
            DrawBadgeShape(g, cx + (cellW - 58) / 2, cy + 12, 58, b.Glyph, ok, tier, b.Shape, false);
            TextRenderer.DrawText(g, b.Name, FpsUi.H3, new Rectangle(cx + 4, cy + 76, cellW - 8, 18),
                ok ? FpsUi.Ink : FpsUi.Dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, b.Crit, FpsUi.Tiny, new Rectangle(cx + 6, cy + 96, cellW - 12, 30),
                ok ? Color.FromArgb(tier.R, tier.G, tier.B) : FpsUi.Dim2, TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
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
