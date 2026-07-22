using System;
using System.Collections.Generic;
using System.Drawing;
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

            _flow = new FlowLayoutPanel();
            _flow.AutoScroll = true;
            _flow.BackColor = Color.Transparent;
            _flow.Padding = new Padding(28, 6, 20, 20);
            Controls.Add(_flow);

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
            var card = new Panel();
            card.Size = new Size(220, 150);
            card.Margin = new Padding(10);
            card.BackColor = Color.Transparent;
            card.Paint += (s, e) =>
            {
                Graphics gr = e.Graphics;
                var r = ((Panel)s).ClientRectangle;
                FpsUi.PaintCard(gr, r, detected ? FpsUi.Card : Color.FromArgb(13, 15, 14), detected ? FpsUi.Border : Color.FromArgb(26, 28, 27), 12f);
                gr.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                // Pastille du launcher (jeux détectés uniquement).
                if (detected && !string.IsNullOrEmpty(g.Store))
                    TextRenderer.DrawText(gr, g.Store, FpsUi.Tiny, new Rectangle(10, 10, r.Width - 20, 14), FpsUi.NeonDim,
                        TextFormatFlags.Left | TextFormatFlags.NoPrefix);

                TextRenderer.DrawText(gr, "🎮", FpsUi.GlyphL, new Rectangle(0, 22, r.Width, 46), detected ? FpsUi.Neon : FpsUi.Dim2,
                    TextFormatFlags.HorizontalCenter);
                TextRenderer.DrawText(gr, g.Name, FpsUi.H3, new Rectangle(10, 74, r.Width - 20, 40), detected ? FpsUi.Ink : FpsUi.Dim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                string tag = detected ? "DÉTECTÉ" : "non installé";
                Color tc = detected ? FpsUi.Neon : FpsUi.Dim2;
                TextRenderer.DrawText(gr, tag, FpsUi.Small, new Rectangle(0, r.Height - 26, r.Width, 18), tc, TextFormatFlags.HorizontalCenter);
            };
            var tt = new ToolTip(); tt.SetToolTip(card, g.Uncap ?? g.Name);
            card.Cursor = Cursors.Hand;
            card.Click += (s, e) => MessageBox.Show(FindForm(),
                g.Name + "\n\n" + (g.Uncap ?? "Passe la limite de FPS à 500/illimité et coupe la V-Sync dans les options du jeu."),
                "Débloquer les FPS", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return card;
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
    }
}
