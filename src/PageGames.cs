using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    // Page Jeux : bibliotheque des jeux competitifs detectes + Mode Jeu (boost).
    internal class PageGames : FpsPage
    {
        private FlowLayoutPanel _flow;
        private Button _mode;
        private Label _empty;
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

            _flow = new FlowLayoutPanel();
            _flow.AutoScroll = true;
            _flow.BackColor = Color.Transparent;
            _flow.Padding = new Padding(28, 6, 20, 20);
            Controls.Add(_flow);

            _empty = FpsUi.Text("Analyse des jeux installés...", FpsUi.Body, FpsUi.Dim);
            _empty.AutoSize = false; _empty.SetBounds(40, 130, 400, 24);
            Controls.Add(_empty); _empty.BringToFront();

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
                try { BeginInvoke((Action)(() => Fill(games))); } catch { }
            });
        }

        private void Fill(List<GameScan.GameInfo> games)
        {
            _flow.SuspendLayout();
            _flow.Controls.Clear();
            int detected = 0;
            foreach (var g in games) { if (g.Detected) { _flow.Controls.Add(Card(g, true)); detected++; } }
            foreach (var g in games) { if (!g.Detected) _flow.Controls.Add(Card(g, false)); }
            _flow.ResumeLayout();
            _empty.Text = detected + " jeu(x) détecté(s) sur " + games.Count + " connus.";
            _empty.ForeColor = detected > 0 ? FpsUi.Neon : FpsUi.Dim;
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
                TextRenderer.DrawText(gr, "🎮", FpsUi.GlyphL, new Rectangle(0, 14, r.Width, 50), detected ? FpsUi.Neon : FpsUi.Dim2,
                    TextFormatFlags.HorizontalCenter);
                TextRenderer.DrawText(gr, g.Name, FpsUi.H3, new Rectangle(10, 74, r.Width - 20, 40), detected ? FpsUi.Ink : FpsUi.Dim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak);
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
            if (_empty != null) _empty.BringToFront();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            PaintTitle(e.Graphics, "BIBLIOTHÈQUE", null);
        }
    }
}
