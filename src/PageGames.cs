using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    // Page Jeux : bibliotheque des jeux detectes (grand catalogue) + recherche + Mode Jeu (boost).
    internal class PageGames : FpsPage
    {
        private FlowLayoutPanel _flow;
        private Button _mode;
        private Button _prio;
        private TextBox _search;
        private string _query = "";
        private string _subtitle = "Analyse des jeux installés…";
        private List<GameScan.GameInfo> _all;
        private bool _loaded;
        private WheelFilter _wheel;

        public PageGames(DashboardForm host) : base(host)
        {
            Build();
        }

        private void Build()
        {
            _mode = FpsUi.NeonButton("▶  MODE JEU");
            _mode.Width = 150; _mode.Height = 34;
            _mode.Click += (s, e) => ToggleBoost();
            Controls.Add(_mode);

            _prio = FpsUi.GhostButton("⚙  Priorité par jeu");
            _prio.Width = 160; _prio.Height = 34;
            _prio.Click += (s, e) => Host.OpenDialog(new GameProfileForm(Host.Log));
            Controls.Add(_prio);

            _search = new TextBox();
            try { _search.PlaceholderText = "Rechercher un jeu…"; } catch { }
            _search.BackColor = FpsUi.Card; _search.ForeColor = FpsUi.Ink;
            _search.BorderStyle = BorderStyle.FixedSingle; _search.Font = FpsUi.Small;
            _search.SetBounds(400, 26, 200, 26);
            _search.TextChanged += (s, e) => { _query = _search.Text.Trim().ToLowerInvariant(); if (_all != null) Render(); };
            Controls.Add(_search);

            _flow = new BufferedFlow();      // double-bufferé → défilement fluide, sans scintillement
            _flow.AutoScroll = true;
            _flow.BackColor = Color.Transparent;
            _flow.Padding = new Padding(28, 6, 20, 20);
            Controls.Add(_flow);

            // La molette défile la grille même quand le curseur est sur une carte (le message va
            // normalement au contrôle qui a le focus, pas à celui sous le curseur).
            _wheel = new WheelFilter(_flow);
            try { Application.AddMessageFilter(_wheel); } catch { }

            Resize += (s, e) => DoLayout();
        }

        public override void OnShown()
        {
            DoLayout();
            UpdateModeButton();
            if (_loaded) return;
            _loaded = true;
            Task.Run(() =>
            {
                List<GameScan.GameInfo> games = new List<GameScan.GameInfo>();
                try { games = GameScan.Known(); GameScan.Detect(games); } catch { }
                try { BeginInvoke((Action)(() => { _all = games; Render(); })); } catch { }
            });
        }

        private bool Match(GameScan.GameInfo g)
        {
            if (_query.Length == 0) return true;
            return g.Name != null && g.Name.ToLowerInvariant().Contains(_query);
        }

        private void Render()
        {
            if (_all == null || _flow == null) return;
            _flow.SuspendLayout();
            var old = new Control[_flow.Controls.Count];
            _flow.Controls.CopyTo(old, 0);
            _flow.Controls.Clear();

            var det = new List<GameScan.GameInfo>();
            var other = new List<GameScan.GameInfo>();
            foreach (var g in _all) { if (!Match(g)) continue; if (g.Detected) det.Add(g); else other.Add(g); }

            Control last = null;
            if (det.Count > 0)
            {
                AddHeader("DÉTECTÉS SUR CE PC", det.Count, FpsUi.Neon);
                foreach (var g in det) { last = Card(g, true); _flow.Controls.Add(last); }
            }
            if (other.Count > 0)
            {
                if (last != null) _flow.SetFlowBreak(last, true);
                AddHeader(_query.Length > 0 ? "AUTRES RÉSULTATS" : "BIBLIOTHÈQUE", other.Count, FpsUi.Dim);
                foreach (var g in other) _flow.Controls.Add(Card(g, false));
            }

            _flow.ResumeLayout();
            foreach (Control c in old) { try { c.Dispose(); } catch { } }  // libère les handles GDI

            // Démarre tout de suite le chargement des jaquettes des jeux détectés (priorité à ce
            // qui est visible en haut) ; les autres se chargeront à la demande au défilement.
            foreach (var g in det) if (g.SteamId > 0) GameArt.Get(g.SteamId, null);

            int total = _all.Count, shownDet = det.Count;
            if (_query.Length > 0)
                _subtitle = (det.Count + other.Count) + " résultat(s) pour « " + _search.Text.Trim() + " »   ·   " + shownDet + " détecté(s)";
            else
                _subtitle = shownDet + " jeu(x) détecté(s) sur " + total + " jeux connus";
            Invalidate();
        }

        private void AddHeader(string text, int count, Color col)
        {
            var h = new Label();
            h.Text = text + "   (" + count + ")";
            h.Font = FpsUi.Small; h.ForeColor = col; h.BackColor = Color.Transparent;
            h.AutoSize = false; h.Height = 30;
            h.Width = Math.Max(200, _flow.ClientSize.Width - 60);
            h.TextAlign = ContentAlignment.MiddleLeft;
            h.Margin = new Padding(10, 12, 10, 2);
            _flow.Controls.Add(h);
            _flow.SetFlowBreak(h, true);
        }

        private Control Card(GameScan.GameInfo g, bool detected)
        {
            // Tuile « affiche » verticale (comme la bibliothèque Steam) : jaquette officielle 600x900.
            // (Le flux parent est composité WS_EX_COMPOSITED → pas besoin de double-buffer par carte.)
            var card = new Panel();
            card.Size = new Size(170, 252);
            card.Margin = new Padding(9);
            card.BackColor = Color.Transparent;
            card.Paint += (s, e) =>
            {
                Graphics gr = e.Graphics;
                gr.SmoothingMode = SmoothingMode.AntiAlias;
                gr.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                var rr = new Rectangle(0, 0, ((Panel)s).ClientRectangle.Width - 1, ((Panel)s).ClientRectangle.Height - 1);

                Image img = g.SteamId > 0
                    ? GameArt.Get(g.SteamId, () => { try { if (card.IsHandleCreated) card.BeginInvoke((Action)card.Invalidate); } catch { } })
                    : null;

                using (var clip = Round(rr, 12))
                {
                    var save = gr.Clip; gr.SetClip(clip);
                    if (img != null)
                    {
                        DrawCover(gr, img, rr);
                        if (!detected) using (var veil = new SolidBrush(Color.FromArgb(155, 9, 11, 10))) gr.FillRectangle(veil, rr);
                        var scrim = new Rectangle(0, rr.Height - 54, rr.Width + 1, 55);   // dégradé bas → statut lisible
                        using (var lg = new LinearGradientBrush(scrim, Color.FromArgb(0, 9, 11, 10), Color.FromArgb(210, 9, 11, 10), 90f))
                            gr.FillRectangle(lg, scrim);
                    }
                    else
                    {
                        using (var bg = new SolidBrush(detected ? FpsUi.Card : Color.FromArgb(13, 15, 14))) gr.FillRectangle(bg, rr);
                        TextRenderer.DrawText(gr, "🎮", FpsUi.GlyphL, new Rectangle(0, 66, rr.Width, 54), detected ? FpsUi.Neon : FpsUi.Dim2,
                            TextFormatFlags.HorizontalCenter);
                        TextRenderer.DrawText(gr, g.Name, FpsUi.H3, new Rectangle(10, 126, rr.Width - 20, 80), detected ? FpsUi.Ink : FpsUi.Dim,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                    }
                    gr.Clip = save; save.Dispose();
                }

                // Statut en bas (sur le dégradé).
                string tag = detected ? "● DÉTECTÉ" : "non installé";
                Color tc = detected ? FpsUi.Neon : FpsUi.Dim2;
                TextRenderer.DrawText(gr, tag, FpsUi.Small, new Rectangle(0, rr.Height - 26, rr.Width, 18), tc,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);

                // Pastille du launcher (coin haut-gauche).
                if (detected && !string.IsNullOrEmpty(g.Store))
                {
                    Size ts = TextRenderer.MeasureText(g.Store, FpsUi.Tiny);
                    var chip = new Rectangle(8, 8, ts.Width + 12, 17);
                    using (var b = new SolidBrush(Color.FromArgb(200, 0, 0, 0))) gr.FillRectangle(b, chip);
                    TextRenderer.DrawText(gr, g.Store, FpsUi.Tiny, chip, FpsUi.Neon,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }

                using (var pen = new Pen(detected ? FpsUi.Border : Color.FromArgb(26, 28, 27), 1f))
                using (var bp = Round(rr, 12)) gr.DrawPath(pen, bp);
            };
            var tt = new ToolTip(); tt.SetToolTip(card, g.Name + " — clic pour la fiche détaillée");
            card.Cursor = Cursors.Hand;
            card.Click += (s, e) => { try { using (var f = new GameDetailForm(g, Host.Log)) f.ShowDialog(FindForm()); } catch { } };
            return card;
        }

        // Rectangle à coins arrondis (les 4).
        private static GraphicsPath Round(Rectangle r, int rad)
        {
            var p = new GraphicsPath();
            int d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // Dessine l'image en « cover » (remplit dst, recadrage centré).
        private static void DrawCover(Graphics g, Image img, Rectangle dst)
        {
            try
            {
                float sc = Math.Max((float)dst.Width / img.Width, (float)dst.Height / img.Height);
                int w = (int)Math.Ceiling(img.Width * sc), h = (int)Math.Ceiling(img.Height * sc);
                int x = dst.X + (dst.Width - w) / 2, y = dst.Y + (dst.Height - h) / 2;
                var old = g.InterpolationMode; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(img, new Rectangle(x, y, w, h));
                g.InterpolationMode = old;
            }
            catch { }
        }

        private void ToggleBoost()
        {
            _mode.Enabled = false;
            bool activating = !GameBoost.IsActive;
            Task.Run(() =>
            {
                try { if (activating) GameBoost.Activate(Host.Log); else GameBoost.Deactivate(Host.Log); } catch { }
                try { BeginInvoke((Action)(() => { _mode.Enabled = true; UpdateModeButton(); })); } catch { }
            });
        }

        private void UpdateModeButton()
        {
            if (GameBoost.IsActive)
            {
                _mode.Text = "■  MODE JEU ACTIF";
                _mode.ForeColor = FpsUi.Err;
                _mode.FlatAppearance.BorderColor = FpsUi.Err;
            }
            else
            {
                _mode.Text = "▶  MODE JEU";
                _mode.ForeColor = FpsUi.Neon;
                _mode.FlatAppearance.BorderColor = FpsUi.Neon;
            }
        }

        private void DoLayout()
        {
            if (_flow == null) return;
            _flow.SetBounds(20, 112, ClientSize.Width - 40, ClientSize.Height - 112);
            if (_mode != null) _mode.Location = new Point(ClientSize.Width - 34 - _mode.Width, 22);
            if (_prio != null && _mode != null) _prio.Location = new Point(_mode.Left - 12 - _prio.Width, 22);
            if (_search != null && _prio != null) _search.SetBounds(Math.Max(180, _prio.Left - 12 - 200), 26, 200, 26);
            // Les en-têtes de section suivent la largeur du flux.
            foreach (Control c in _flow.Controls) if (c is Label) c.Width = Math.Max(200, _flow.ClientSize.Width - 60);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            PaintTitle(e.Graphics, "BIBLIOTHÈQUE", _subtitle);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _wheel != null) { try { Application.RemoveMessageFilter(_wheel); } catch { } _wheel = null; }
            base.Dispose(disposing);
        }

        // Molette → défile le flux visible même quand le curseur est sur une carte enfant.
        private class WheelFilter : IMessageFilter
        {
            private const int WM_MOUSEWHEEL = 0x020A;
            private readonly ScrollableControl _target;
            public WheelFilter(ScrollableControl target) { _target = target; }
            public bool PreFilterMessage(ref Message m)
            {
                if (m.Msg != WM_MOUSEWHEEL || _target == null) return false;
                try
                {
                    if (!_target.IsHandleCreated || !_target.Visible || !_target.VerticalScroll.Visible) return false;
                    Point p = _target.PointToClient(Control.MousePosition);
                    if (!_target.ClientRectangle.Contains(p)) return false;
                    int delta = (short)(((long)m.WParam >> 16) & 0xFFFF);
                    var ap = _target.AutoScrollPosition;
                    _target.AutoScrollPosition = new Point(-ap.X, -ap.Y - delta);
                    return true;   // consommé
                }
                catch { return false; }
            }
        }
    }

    // FlowLayoutPanel composité (WS_EX_COMPOSITED) : rend toute la grille + ses cartes enfants dans
    // un seul back-buffer → défilement fluide sans scintillement, tout en gardant la transparence.
    internal class BufferedFlow : FlowLayoutPanel
    {
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x02000000; return cp; }  // WS_EX_COMPOSITED
        }
    }
}
