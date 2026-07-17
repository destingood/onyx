using System;
using System.Collections;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>Fenêtre d'analyse DPC/ISR façon LatencyMon : verdict, tuiles, tableau des pilotes.</summary>
    internal class LatencyForm : Form
    {
        private Panel _verdict;
        private Label _verdictTitle, _verdictDetail;
        private FlowLayoutPanel _tiles;
        private ListView _grid;
        private Label _footer;
        private DpcIsrReport _report;

        private static readonly Color Bg      = Color.FromArgb(245, 246, 248);
        private static readonly Color Green   = Color.FromArgb(0, 150, 90);
        private static readonly Color Orange  = Color.FromArgb(205, 133, 0);
        private static readonly Color Red     = Color.FromArgb(200, 45, 45);
        private static readonly Color TileBg  = Color.FromArgb(28, 30, 38);

        public LatencyForm(DpcIsrReport report)
        {
            _report = report;
            Build();
            Populate();
            Theme.Apply(this);
        }

        private void Build()
        {
            Text = "DesTinGOOD — Analyse de latence DPC/ISR";
            ClientSize = new Size(960, 660);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(760, 520);
            BackColor = Bg;
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            _verdict = new Panel();
            _verdict.Dock = DockStyle.Top;
            _verdict.Height = 92;
            _verdict.Padding = new Padding(18, 12, 18, 12);

            _verdictTitle = new Label();
            _verdictTitle.Dock = DockStyle.Top;
            _verdictTitle.Height = 34;
            _verdictTitle.Font = new Font("Segoe UI Semibold", 15f);
            _verdictTitle.ForeColor = Color.White;

            _verdictDetail = new Label();
            _verdictDetail.Dock = DockStyle.Fill;
            _verdictDetail.Font = new Font("Segoe UI", 9.5f);
            _verdictDetail.ForeColor = Color.FromArgb(235, 238, 240);

            _verdict.Controls.Add(_verdictDetail);
            _verdict.Controls.Add(_verdictTitle);

            _tiles = new FlowLayoutPanel();
            _tiles.Dock = DockStyle.Top;
            _tiles.Height = 92;
            _tiles.Padding = new Padding(14, 10, 14, 6);
            _tiles.BackColor = Bg;

            _grid = new ListView();
            _grid.Dock = DockStyle.Fill;
            _grid.View = View.Details;
            _grid.FullRowSelect = true;
            _grid.GridLines = true;
            _grid.MultiSelect = false;
            _grid.HideSelection = false;
            _grid.OwnerDraw = false;
            _grid.Font = new Font("Segoe UI", 9f);
            _grid.Columns.Add("Pilote", 150);
            _grid.Columns.Add("Description", 260);
            _grid.Columns.Add("DPC (nb)", 80, HorizontalAlignment.Right);
            _grid.Columns.Add("DPC max µs", 90, HorizontalAlignment.Right);
            _grid.Columns.Add("ISR (nb)", 80, HorizontalAlignment.Right);
            _grid.Columns.Add("ISR max µs", 90, HorizontalAlignment.Right);
            _grid.Columns.Add("Temps total µs", 100, HorizontalAlignment.Right);
            _grid.ColumnClick += OnColumnClick;

            var bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 46;
            bottom.Padding = new Padding(14, 7, 14, 7);

            var btnOpen = MakeBtn("Ouvrir un autre rapport…", 190);
            btnOpen.Dock = DockStyle.Left;
            btnOpen.Click += OnOpenOther;

            var btnExport = MakeBtn("Exporter le résumé…", 170);
            btnExport.Dock = DockStyle.Left;
            btnExport.Click += OnExport;

            var btnCompare = MakeBtn("Comparer avec (AVANT)…", 190);
            btnCompare.Dock = DockStyle.Left;
            btnCompare.Click += OnCompare;

            var btnClose = MakeBtn("Fermer", 110);
            btnClose.Dock = DockStyle.Right;
            btnClose.Click += (s, e) => Close();

            _footer = new Label();
            _footer.Dock = DockStyle.Fill;
            _footer.TextAlign = ContentAlignment.MiddleCenter;
            _footer.ForeColor = Color.FromArgb(110, 115, 125);

            bottom.Controls.Add(_footer);
            bottom.Controls.Add(btnCompare);
            bottom.Controls.Add(btnExport);
            bottom.Controls.Add(btnOpen);
            bottom.Controls.Add(btnClose);

            Controls.Add(_grid);
            Controls.Add(_tiles);
            Controls.Add(_verdict);
            Controls.Add(bottom);
        }

        private static Button MakeBtn(string text, int w)
        {
            var b = new Button();
            b.Text = text;
            b.Width = w;
            b.Height = 32;
            b.Margin = new Padding(4, 0, 4, 0);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            b.BackColor = Color.White;
            return b;
        }

        private Panel MakeTile(string caption, string value, Color valueColor)
        {
            var p = new Panel();
            p.Size = new Size(178, 74);
            p.Margin = new Padding(4, 2, 4, 2);
            p.BackColor = TileBg;

            var cap = new Label();
            cap.Text = caption;
            cap.Dock = DockStyle.Top;
            cap.Height = 24;
            cap.ForeColor = Color.FromArgb(165, 170, 180);
            cap.Font = new Font("Segoe UI", 8.5f);
            cap.Padding = new Padding(10, 6, 6, 0);

            var val = new Label();
            val.Text = value;
            val.Dock = DockStyle.Fill;
            val.ForeColor = valueColor;
            val.Font = new Font("Segoe UI Semibold", 15f);
            val.Padding = new Padding(10, 0, 6, 6);

            p.Controls.Add(val);
            p.Controls.Add(cap);
            return p;
        }

        private void Populate()
        {
            Color vc = _report.VerdictLevel == 1 ? Green : (_report.VerdictLevel == 2 ? Orange : Red);
            _verdict.BackColor = vc;
            _verdictTitle.BackColor = vc;
            _verdictTitle.Text = _report.VerdictTitle;
            _verdictDetail.Text = _report.VerdictDetail;

            _tiles.Controls.Clear();
            _tiles.Controls.Add(MakeTile("Pire DPC", "≤ " + _report.MaxDpcUs.ToString("0") + " µs", TileColor(_report.MaxDpcUs)));
            _tiles.Controls.Add(MakeTile("Pire ISR", "≤ " + _report.MaxIsrUs.ToString("0") + " µs", TileColor(_report.MaxIsrUs)));
            _tiles.Controls.Add(MakeTile("Total DPC", _report.TotalDpc.ToString("#,0"), Color.White));
            _tiles.Controls.Add(MakeTile("Total ISR", _report.TotalIsr.ToString("#,0"), Color.White));
            _tiles.Controls.Add(MakeTile("Durée trace", _report.DurationSec.ToString("0.0") + " s", Color.White));

            _grid.BeginUpdate();
            _grid.Items.Clear();
            foreach (DriverStat d in _report.Drivers)
            {
                var it = new ListViewItem(d.Module);
                it.SubItems.Add(d.Description);
                it.SubItems.Add(d.DpcCount.ToString("#,0"));
                it.SubItems.Add(d.DpcMaxUs > 0 ? d.DpcMaxUs.ToString("0") : "-");
                it.SubItems.Add(d.IsrCount.ToString("#,0"));
                it.SubItems.Add(d.IsrMaxUs > 0 ? d.IsrMaxUs.ToString("0") : "-");
                it.SubItems.Add((d.DpcTotalUs + d.IsrTotalUs).ToString("#,0"));
                if (d.WorstUs > 1000) { it.ForeColor = Red; it.Font = new Font(_grid.Font, FontStyle.Bold); }
                else if (d.WorstUs > 500) it.ForeColor = Orange;
                _grid.Items.Add(it);
            }
            _grid.EndUpdate();

            _footer.Text = "Source : " + Path.GetFileName(_report.SourceFile)
                + "   ·   Clique un en-tête de colonne pour trier   ·   µs = microsecondes (borne haute du pire intervalle observé)";
        }

        private static Color TileColor(double us)
        {
            if (us <= 0) return Color.White;
            if (us <= 500) return Color.FromArgb(120, 230, 170);
            if (us <= 1000) return Color.FromArgb(245, 190, 90);
            return Color.FromArgb(245, 120, 120);
        }

        // ---------------- tri des colonnes ----------------
        private int _sortCol = -1;
        private bool _asc = true;

        private void OnColumnClick(object sender, ColumnClickEventArgs e)
        {
            if (e.Column == _sortCol) _asc = !_asc;
            else { _sortCol = e.Column; _asc = false; } // 1er clic = décroissant (les pires en haut)
            _grid.ListViewItemSorter = new RowComparer(e.Column, _asc);
            _grid.Sort();
        }

        private class RowComparer : IComparer
        {
            private readonly int _col;
            private readonly bool _asc;
            public RowComparer(int col, bool asc) { _col = col; _asc = asc; }
            public int Compare(object x, object y)
            {
                var a = (ListViewItem)x;
                var b = (ListViewItem)y;
                string sa = a.SubItems[_col].Text;
                string sb = b.SubItems[_col].Text;
                int r;
                double da, db;
                if (TryNum(sa, out da) && TryNum(sb, out db))
                    r = da.CompareTo(db);
                else
                    r = string.Compare(sa, sb, StringComparison.OrdinalIgnoreCase);
                return _asc ? r : -r;
            }
            private static bool TryNum(string s, out double v)
            {
                s = s.Replace(" ", "").Replace(",", "").Replace("-", "0");
                return double.TryParse(s, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out v);
            }
        }

        // ---------------- boutons ----------------
        private void OnOpenOther(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = "Rapports DPC/ISR (*.txt)|*.txt|Tous les fichiers|*.*";
                string tools = Path.Combine(Application.StartupPath, "tools");
                if (Directory.Exists(tools)) dlg.InitialDirectory = tools;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    _report = DpcIsrReport.Parse(dlg.FileName);
                    Populate();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Lecture impossible :\n" + ex.Message, "DesTinGOOD",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void OnCompare(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "Choisir le rapport AVANT (référence)";
                dlg.Filter = "Rapports DPC/ISR (*.txt)|*.txt|Tous les fichiers|*.*";
                string tools = Path.Combine(Application.StartupPath, "tools");
                if (Directory.Exists(tools)) dlg.InitialDirectory = tools;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    DpcIsrReport before = DpcIsrReport.Parse(dlg.FileName);
                    using (var f = new CompareForm(before, _report)) f.ShowDialog(this);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Comparaison impossible :\n" + ex.Message, "DesTinGOOD",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void OnExport(object sender, EventArgs e)
        {
            try
            {
                string outPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    "bt-analyse-latence.txt");
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("=== ANALYSE DE LATENCE DPC/ISR ===");
                sb.AppendLine("Source  : " + _report.SourceFile);
                sb.AppendLine("Durée   : " + _report.DurationSec.ToString("0.0") + " s");
                sb.AppendLine("Verdict : " + _report.VerdictTitle);
                sb.AppendLine(_report.VerdictDetail);
                sb.AppendLine("Total DPC : " + _report.TotalDpc + "   Total ISR : " + _report.TotalIsr);
                sb.AppendLine();
                sb.AppendLine(string.Format("{0,-16} {1,8} {2,10} {3,8} {4,10} {5,12}  {6}",
                    "Pilote", "DPC", "DPCmax", "ISR", "ISRmax", "Total us", "Description"));
                foreach (DriverStat d in _report.Drivers)
                    sb.AppendLine(string.Format("{0,-16} {1,8} {2,10:0} {3,8} {4,10:0} {5,12}  {6}",
                        d.Module, d.DpcCount, d.DpcMaxUs, d.IsrCount, d.IsrMaxUs,
                        d.DpcTotalUs + d.IsrTotalUs, d.Description));
                File.WriteAllText(outPath, sb.ToString(), System.Text.Encoding.UTF8);
                MessageBox.Show(this, "Résumé enregistré :\n" + outPath, "DesTinGOOD",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Export impossible :\n" + ex.Message, "DesTinGOOD",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
