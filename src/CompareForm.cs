using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>Comparaison avant/après de deux rapports DPC/ISR : tuiles de delta + tableau par pilote.</summary>
    internal class CompareForm : Form
    {
        private readonly DpcIsrReport _before;
        private readonly DpcIsrReport _after;

        private static readonly Color Bg     = Color.FromArgb(245, 246, 248);
        private static readonly Color TileBg = Color.FromArgb(28, 30, 38);
        private static readonly Color Green  = Color.FromArgb(0, 150, 90);
        private static readonly Color Orange = Color.FromArgb(205, 133, 0);
        private static readonly Color Red    = Color.FromArgb(200, 45, 45);

        public CompareForm(DpcIsrReport before, DpcIsrReport after)
        {
            _before = before;
            _after = after;
            Build();
            Theme.Apply(this);
        }

        private void Build()
        {
            Text = "Fluide — Comparaison AVANT / APRÈS";
            ClientSize = new Size(980, 620);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(780, 480);
            BackColor = Bg;
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            double wb = _before.WorstUs, wa = _after.WorstUs;
            double delta = wa - wb;
            bool better = delta < 0;

            var banner = new Panel();
            banner.Dock = DockStyle.Top;
            banner.Height = 84;
            banner.Padding = new Padding(18, 10, 18, 10);
            banner.BackColor = better || Math.Abs(delta) < 0.5 ? Green : (wa <= 500 ? Orange : Red);

            var bTitle = new Label();
            bTitle.Dock = DockStyle.Top;
            bTitle.Height = 32;
            bTitle.Font = new Font("Segoe UI Semibold", 14.5f);
            bTitle.ForeColor = Color.White;
            bTitle.BackColor = banner.BackColor;
            if (Math.Abs(delta) < 0.5)
                bTitle.Text = "STABLE — pire latence inchangée (≤ " + wa.ToString("0") + " µs)";
            else if (better)
                bTitle.Text = "AMÉLIORATION — pire latence : " + wb.ToString("0") + " µs → " + wa.ToString("0") + " µs (" + delta.ToString("+0;-0") + " µs)";
            else
                bTitle.Text = "DÉGRADATION — pire latence : " + wb.ToString("0") + " µs → " + wa.ToString("0") + " µs (+" + delta.ToString("0") + " µs)";

            var bSub = new Label();
            bSub.Dock = DockStyle.Fill;
            bSub.Font = new Font("Segoe UI", 9f);
            bSub.ForeColor = Color.FromArgb(235, 238, 240);
            bSub.BackColor = banner.BackColor;
            bSub.Text = "AVANT : " + Path.GetFileName(_before.SourceFile) + "  (" + _before.DurationSec.ToString("0.0") + " s, "
                + _before.TotalDpc.ToString("#,0") + " DPC)      APRÈS : " + Path.GetFileName(_after.SourceFile)
                + "  (" + _after.DurationSec.ToString("0.0") + " s, " + _after.TotalDpc.ToString("#,0") + " DPC)"
                + "      NB : compare des traces prises dans des conditions similaires (repos vs repos, jeu vs jeu).";

            banner.Controls.Add(bSub);
            banner.Controls.Add(bTitle);

            var tiles = new FlowLayoutPanel();
            tiles.Dock = DockStyle.Top;
            tiles.Height = 92;
            tiles.Padding = new Padding(14, 10, 14, 6);

            tiles.Controls.Add(Tile("Pire DPC", _before.MaxDpcUs, _after.MaxDpcUs, " µs"));
            tiles.Controls.Add(Tile("Pire ISR", _before.MaxIsrUs, _after.MaxIsrUs, " µs"));
            tiles.Controls.Add(Tile("DPC / seconde",
                _before.DurationSec > 0 ? _before.TotalDpc / _before.DurationSec : 0,
                _after.DurationSec > 0 ? _after.TotalDpc / _after.DurationSec : 0, ""));
            tiles.Controls.Add(Tile("ISR / seconde",
                _before.DurationSec > 0 ? _before.TotalIsr / _before.DurationSec : 0,
                _after.DurationSec > 0 ? _after.TotalIsr / _after.DurationSec : 0, ""));

            var grid = new ListView();
            grid.Dock = DockStyle.Fill;
            grid.View = View.Details;
            grid.FullRowSelect = true;
            grid.GridLines = true;
            grid.HideSelection = false;
            grid.Columns.Add("Pilote", 150);
            grid.Columns.Add("Description", 250);
            grid.Columns.Add("Pire AVANT µs", 100, HorizontalAlignment.Right);
            grid.Columns.Add("Pire APRÈS µs", 100, HorizontalAlignment.Right);
            grid.Columns.Add("Δ µs", 80, HorizontalAlignment.Right);
            grid.Columns.Add("Évén. AVANT", 90, HorizontalAlignment.Right);
            grid.Columns.Add("Évén. APRÈS", 90, HorizontalAlignment.Right);

            foreach (DpcIsrReport.ModuleDelta m in DpcIsrReport.Compare(_before, _after))
            {
                var it = new ListViewItem(m.Module);
                it.SubItems.Add(m.Description);
                it.SubItems.Add(m.WorstBefore > 0 ? m.WorstBefore.ToString("0") : "-");
                it.SubItems.Add(m.WorstAfter > 0 ? m.WorstAfter.ToString("0") : "-");
                it.SubItems.Add(m.Delta.ToString("+0;-0;0"));
                it.SubItems.Add(m.EventsBefore.ToString("#,0"));
                it.SubItems.Add(m.EventsAfter.ToString("#,0"));
                if (m.Delta <= -8) it.ForeColor = Green;
                else if (m.Delta >= 64) { it.ForeColor = Red; it.Font = new Font(grid.Font, FontStyle.Bold); }
                else if (m.Delta >= 8) it.ForeColor = Orange;
                grid.Items.Add(it);
            }

            var bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 44;
            bottom.Padding = new Padding(14, 6, 14, 6);
            var close = new Button();
            close.Text = "Fermer";
            close.Width = 110;
            close.Dock = DockStyle.Right;
            close.FlatStyle = FlatStyle.Flat;
            close.BackColor = Color.White;
            close.Click += (s, e) => Close();
            var hint = new Label();
            hint.Dock = DockStyle.Fill;
            hint.TextAlign = ContentAlignment.MiddleLeft;
            hint.ForeColor = Color.FromArgb(110, 115, 125);
            hint.Text = "Vert = pilote amélioré · Orange/Rouge = pilote dégradé · Δ = APRÈS − AVANT (négatif = mieux)";
            bottom.Controls.Add(hint);
            bottom.Controls.Add(close);

            Controls.Add(grid);
            Controls.Add(tiles);
            Controls.Add(banner);
            Controls.Add(bottom);
        }

        private Panel Tile(string caption, double before, double after, string unit)
        {
            var p = new Panel();
            p.Size = new Size(224, 74);
            p.Margin = new Padding(4, 2, 4, 2);
            p.BackColor = TileBg;

            var cap = new Label();
            cap.Text = caption;
            cap.Dock = DockStyle.Top;
            cap.Height = 22;
            cap.ForeColor = Color.FromArgb(165, 170, 180);
            cap.Font = new Font("Segoe UI", 8.5f);
            cap.Padding = new Padding(10, 5, 6, 0);

            var val = new Label();
            double d = after - before;
            val.Text = before.ToString("0") + unit + "  →  " + after.ToString("0") + unit;
            val.Dock = DockStyle.Top;
            val.Height = 28;
            val.ForeColor = Color.White;
            val.Font = new Font("Segoe UI Semibold", 12.5f);
            val.Padding = new Padding(10, 0, 6, 0);

            var dl = new Label();
            dl.Text = (Math.Abs(d) < 0.5) ? "stable" : d.ToString("+0;-0") + unit;
            dl.Dock = DockStyle.Fill;
            dl.ForeColor = (Math.Abs(d) < 0.5) ? Color.FromArgb(165, 170, 180)
                        : (d < 0 ? Color.FromArgb(120, 230, 170) : Color.FromArgb(245, 150, 110));
            dl.Font = new Font("Segoe UI", 9f);
            dl.Padding = new Padding(10, 0, 6, 4);

            p.Controls.Add(dl);
            p.Controls.Add(val);
            p.Controls.Add(cap);
            return p;
        }
    }
}
