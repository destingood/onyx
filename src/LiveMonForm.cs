using System;
using System.Collections;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Moniteur de latence EN DIRECT façon LatencyMon : session ETW noyau temps réel,
    /// durée exacte de chaque DPC/ISR par pilote (rafraîchi chaque seconde), sonde de
    /// réveil 1 ms (ce que subit un jeu) et verdict coloré. Zéro fichier, zéro xperf.
    /// </summary>
    internal class LiveMonForm : Form
    {
        private readonly Action<string, int> _log;
        private EtwLive _etw;
        private WakeupProbe _probe;
        private bool _timerWasActive;

        private Panel _verdict;
        private Label _verdictTitle, _verdictDetail;
        private FlowLayoutPanel _tiles;
        private ListView _grid;
        private Label _footer;
        private Timer _refresh;
        private long _lastEvents;
        private DateTime _lastTick = DateTime.UtcNow;

        // Tuiles construites UNE fois, mises à jour par tick (évite une fuite de handles GDI :
        // avant, Repaint recréait 6 Panel + 12 Label + 12 Font chaque seconde sans les libérer).
        private Label[] _capLbl, _valLbl;
        private Font _tileCapFont, _tileValFont, _boldRow;

        private static readonly Color Bg     = Color.FromArgb(245, 246, 248);
        private static readonly Color Green  = Color.FromArgb(79, 70, 229);
        private static readonly Color Orange = Color.FromArgb(205, 133, 0);
        private static readonly Color Red    = Color.FromArgb(200, 45, 45);
        private static readonly Color TileBg = Color.FromArgb(28, 30, 38);

        public LiveMonForm(Action<string, int> log)
        {
            _log = log;
            Build();
            Theme.Apply(this);
            Load += (s, e) => StartSession();
        }

        private void Build()
        {
            Text = "Fluide — Latence EN DIRECT (DPC/ISR par pilote, précision LatencyMon)";
            ClientSize = new Size(1180, 680);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(1160, 540);
            BackColor = Bg;
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            _verdict = new Panel { Dock = DockStyle.Top, Height = 92, Padding = new Padding(18, 12, 18, 12), BackColor = TileBg };
            _verdictTitle = new Label
            {
                Dock = DockStyle.Top, Height = 34,
                Font = new Font("Segoe UI Semibold", 15f), ForeColor = Color.White, BackColor = Color.Transparent,
                Text = "Démarrage de la session noyau..."
            };
            _verdictDetail = new Label
            {
                Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.FromArgb(235, 238, 240), BackColor = Color.Transparent,
                Text = "Chaque DPC et chaque ISR est mesuré individuellement (horloge QPC) et attribué à son pilote."
            };
            _verdict.Controls.Add(_verdictDetail);
            _verdict.Controls.Add(_verdictTitle);

            _tiles = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 92, Padding = new Padding(14, 10, 14, 6), BackColor = Bg };

            _grid = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true,
                MultiSelect = false, HideSelection = false, Font = new Font("Segoe UI", 9f)
            };
            _grid.Columns.Add("Pilote", 160);
            _grid.Columns.Add("Description", 250);
            _grid.Columns.Add("DPC (nb)", 90, HorizontalAlignment.Right);
            _grid.Columns.Add("DPC max µs", 90, HorizontalAlignment.Right);
            _grid.Columns.Add("DPC moy µs", 90, HorizontalAlignment.Right);
            _grid.Columns.Add("ISR (nb)", 80, HorizontalAlignment.Right);
            _grid.Columns.Add("ISR max µs", 90, HorizontalAlignment.Right);
            _grid.Columns.Add("CPU total ms", 95, HorizontalAlignment.Right);
            _grid.ColumnClick += OnColumnClick;

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(14, 7, 14, 7) };
            var btnReset = MakeBtn("Remettre à zéro", 140);
            btnReset.Dock = DockStyle.Left;
            btnReset.Click += (s, e) => { if (_etw != null) _etw.Reset(); if (_probe != null) _probe.Reset(); _lastEvents = 0; };
            var btnExport = MakeBtn("Exporter le résumé…", 170);
            btnExport.Dock = DockStyle.Left;
            btnExport.Click += OnExport;
            // Cette fenêtre EST la mesure de latence intégrée (comme LatencyMon). Le bouton
            // n'est qu'un « aller plus loin » optionnel vers l'outil de bureau.
            var btnTool = MakeBtn("Aller plus loin : LatencyMon", 210);
            btnTool.Dock = DockStyle.Left;
            LibScan.WireToolButton(btnTool, this, _log, "Aller plus loin : LatencyMon", new[] { "Resplendence.LatencyMon" });
            var btnClose = MakeBtn("Fermer", 110);
            btnClose.Dock = DockStyle.Right;
            btnClose.Click += (s, e) => Close();

            _footer = new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(110, 115, 125),
                Text = "µs = microsecondes · mesure exacte par événement, pas d'échantillonnage"
            };

            bottom.Controls.Add(_footer);
            bottom.Controls.Add(btnTool);
            bottom.Controls.Add(btnExport);
            bottom.Controls.Add(btnReset);
            bottom.Controls.Add(btnClose);

            Controls.Add(_grid);
            Controls.Add(_tiles);

            // Polices partagées + tuiles bâties une fois (anti-fuite GDI, voir BuildTiles).
            _tileCapFont = new Font("Segoe UI", 8.5f);
            _tileValFont = new Font("Segoe UI Semibold", 14f);
            _boldRow = new Font(_grid.Font, FontStyle.Bold);
            BuildTiles();
            Controls.Add(_verdict);
            Controls.Add(bottom);
        }

        private static Button MakeBtn(string text, int w)
        {
            var b = new Button
            {
                Text = text, Width = w, Height = 32, Margin = new Padding(4, 0, 4, 0),
                FlatStyle = FlatStyle.Flat, BackColor = Color.White
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            return b;
        }

        // Construit les 6 tuiles UNE seule fois ; Repaint ne fait ensuite que changer le texte.
        private void BuildTiles()
        {
            _capLbl = new Label[6];
            _valLbl = new Label[6];
            _tiles.SuspendLayout();
            _tiles.Controls.Clear();
            for (int i = 0; i < 6; i++)
            {
                var p = new Panel { Size = new Size(183, 74), Margin = new Padding(4, 2, 4, 2), BackColor = TileBg };
                var cap = new Label
                {
                    Dock = DockStyle.Top, Height = 24, ForeColor = Color.FromArgb(165, 170, 180),
                    Font = _tileCapFont, Padding = new Padding(10, 6, 6, 0)
                };
                var val = new Label
                {
                    Dock = DockStyle.Fill, ForeColor = Color.White,
                    Font = _tileValFont, Padding = new Padding(10, 0, 6, 6)
                };
                p.Controls.Add(val);
                p.Controls.Add(cap);
                _capLbl[i] = cap; _valLbl[i] = val;
                _tiles.Controls.Add(p);
            }
            _tiles.ResumeLayout();
        }

        private void SetTile(int i, string caption, string value, Color valueColor)
        {
            _capLbl[i].Text = caption;
            _valLbl[i].Text = value;
            _valLbl[i].ForeColor = valueColor;
        }

        // ------------------------------------------------------------------
        //  Session
        // ------------------------------------------------------------------
        private void StartSession()
        {
            _timerWasActive = Native.TimerActive;
            Native.SetTimer1ms(true);              // la sonde de réveil a besoin du tick 1 ms

            _etw = new EtwLive();
            if (!_etw.Start())
            {
                _verdict.BackColor = Red;
                _verdictTitle.Text = "Session noyau impossible";
                _verdictDetail.Text = "Cause : " + (_etw.LastError ?? "inconnue")
                    + ". Lance Fluide en administrateur (clic droit → Exécuter en tant qu'administrateur).";
                if (_log != null) _log("Latence en direct : session ETW refusée (" + (_etw.LastError ?? "?") + ").", 2);
                _etw.Dispose();
                _etw = null;
                return;
            }

            _probe = new WakeupProbe();
            _probe.Start();

            if (_log != null) _log("Latence en direct : session ETW noyau démarrée (DPC + interruptions, "
                + KernelModules.Count + " modules noyau cartographiés).", 1);

            _refresh = new Timer { Interval = 1000 };
            _refresh.Tick += (s, e) => Repaint();
            _refresh.Start();
        }

        private void Repaint()
        {
            if (_etw == null) return;
            DpcIsrReport rep = _etw.Snapshot();

            // Verdict (mêmes seuils que l'analyse xperf : <500 vert, <1000 orange, sinon rouge)
            Color vc = rep.VerdictLevel == 1 ? Green : (rep.VerdictLevel == 2 ? Orange : Red);
            _verdict.BackColor = vc;
            _verdictTitle.Text = rep.VerdictTitle;
            string worst = (rep.MaxDpcUs >= rep.MaxIsrUs) ? (rep.MaxDpcModule + " (DPC)") : (rep.MaxIsrModule + " (ISR)");
            _verdictDetail.Text = rep.WorstUs <= 0
                ? "En attente d'événements..."
                : "Pire latence noyau mesurée : " + rep.WorstUs.ToString("0") + " µs (exact), causée par " + worst
                  + ".  Repère : < 500 µs = très bien, > 1000 µs = à corriger.";

            // Tuiles
            long events = rep.TotalDpc + rep.TotalIsr;
            double secs = Math.Max(0.001, (DateTime.UtcNow - _lastTick).TotalSeconds);
            double rate = Math.Max(0, (events - _lastEvents) / secs);
            _lastEvents = events;
            _lastTick = DateTime.UtcNow;

            double probeMax = 0, probeAvg = 0, probeP99 = 0;
            if (_probe != null) _probe.Read(out probeMax, out probeAvg, out probeP99);
            EtwLive.HardFaultInfo hf = _etw.HardFaults();

            SetTile(0, "Pire DPC — " + rep.MaxDpcModule, rep.MaxDpcUs.ToString("0") + " µs", TileColor(rep.MaxDpcUs));
            SetTile(1, "Pire ISR — " + rep.MaxIsrModule, rep.MaxIsrUs.ToString("0") + " µs", TileColor(rep.MaxIsrUs));
            SetTile(2, "Défauts de page durs" + (hf.Count > 0 ? " — pire " + hf.WorstMs.ToString("0.0") + " ms" : ""),
                hf.Count.ToString("#,0"), HardFaultColor(hf));
            SetTile(3, "Réveil 1 ms (p99 · max)",
                probeP99.ToString("0") + " · " + probeMax.ToString("0") + " µs", TileColor(probeP99 > 0 ? probeP99 : probeMax));
            SetTile(4, "Événements / s", rate.ToString("#,0"), Color.White);
            SetTile(5, "Durée", rep.DurationSec.ToString("0") + " s", Color.White);

            // Tableau des pilotes
            _grid.BeginUpdate();
            _grid.Items.Clear();
            foreach (DriverStat d in rep.Drivers)
            {
                var it = new ListViewItem(d.Module);
                it.SubItems.Add(d.Description);
                it.SubItems.Add(d.DpcCount.ToString("#,0"));
                it.SubItems.Add(d.DpcMaxUs > 0 ? d.DpcMaxUs.ToString("0") : "-");
                it.SubItems.Add(d.DpcCount > 0 ? ((double)d.DpcTotalUs / d.DpcCount).ToString("0.0") : "-");
                it.SubItems.Add(d.IsrCount.ToString("#,0"));
                it.SubItems.Add(d.IsrMaxUs > 0 ? d.IsrMaxUs.ToString("0") : "-");
                it.SubItems.Add(((d.DpcTotalUs + d.IsrTotalUs) / 1000.0).ToString("0.0"));
                if (d.WorstUs > 1000) { it.ForeColor = Red; it.Font = _boldRow; }
                else if (d.WorstUs > 500) it.ForeColor = Orange;
                _grid.Items.Add(it);
            }
            if (_sortCol >= 0) _grid.Sort();
            _grid.EndUpdate();

            int lost = _etw.EventsLost;
            _footer.Text = events.ToString("#,0") + " événements mesurés"
                + (lost > 0 ? " · " + lost + " perdus (tampons)" : " · 0 perdu")
                + " · durée exacte par événement (ETW noyau, horloge QPC) · clique un en-tête pour trier";
        }

        private static Color TileColor(double us)
        {
            if (us <= 0) return Color.White;
            if (us <= 500) return Color.FromArgb(120, 230, 170);
            if (us <= 1000) return Color.FromArgb(245, 190, 90);
            return Color.FromArgb(245, 120, 120);
        }

        private static Color HardFaultColor(EtwLive.HardFaultInfo hf)
        {
            if (hf.Count == 0) return Color.FromArgb(120, 230, 170);         // aucun accès disque forcé
            if (hf.WorstMs >= 50) return Color.FromArgb(245, 120, 120);      // gros stutter potentiel
            if (hf.WorstMs >= 10) return Color.FromArgb(245, 190, 90);
            return Color.White;
        }

        // ---------------- tri ----------------
        private int _sortCol = -1;
        private bool _asc;

        private void OnColumnClick(object sender, ColumnClickEventArgs e)
        {
            if (e.Column == _sortCol) _asc = !_asc;
            else { _sortCol = e.Column; _asc = false; }
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
                if (TryNum(sa, out da) && TryNum(sb, out db)) r = da.CompareTo(db);
                else r = string.Compare(sa, sb, StringComparison.OrdinalIgnoreCase);
                return _asc ? r : -r;
            }
            private static bool TryNum(string s, out double v)
            {
                s = s.Replace(" ", "").Replace(",", "").Replace("-", "0");
                return double.TryParse(s, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out v);
            }
        }

        // ---------------- export ----------------
        private void OnExport(object sender, EventArgs e)
        {
            if (_etw == null) return;
            try
            {
                DpcIsrReport rep = _etw.Snapshot();
                double probeMax = 0, probeAvg = 0, probeP99 = 0;
                if (_probe != null) _probe.Read(out probeMax, out probeAvg, out probeP99);
                EtwLive.HardFaultInfo hf = _etw.HardFaults();

                string outPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    "bt-latence-live.txt");
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("=== LATENCE EN DIRECT (ETW noyau temps réel, durées exactes) ===");
                sb.AppendLine("Durée   : " + rep.DurationSec.ToString("0.0") + " s");
                sb.AppendLine("Verdict : " + rep.VerdictTitle);
                sb.AppendLine("Pire DPC : " + rep.MaxDpcUs.ToString("0") + " µs (" + rep.MaxDpcModule + ")"
                             + "   Pire ISR : " + rep.MaxIsrUs.ToString("0") + " µs (" + rep.MaxIsrModule + ")");
                sb.AppendLine("Réveil timer 1 ms : p99 " + probeP99.ToString("0") + " µs, max " + probeMax.ToString("0")
                             + " µs, moyen " + probeAvg.ToString("0.0") + " µs");
                sb.AppendLine("Défauts de page durs : " + hf.Count
                             + (hf.Count > 0 ? " (pire " + hf.WorstMs.ToString("0.0") + " ms par " + hf.WorstProcess
                                             + " ; top : " + hf.Top + ")" : " (aucun accès disque forcé — parfait)"));
                sb.AppendLine("Total DPC : " + rep.TotalDpc + "   Total ISR : " + rep.TotalIsr
                             + "   Perdus : " + _etw.EventsLost);
                sb.AppendLine();
                sb.AppendLine(string.Format("{0,-18} {1,10} {2,10} {3,10} {4,8} {5,10} {6,12}  {7}",
                    "Pilote", "DPC", "DPCmax µs", "DPCmoy µs", "ISR", "ISRmax µs", "CPU ms", "Description"));
                foreach (DriverStat d in rep.Drivers)
                    sb.AppendLine(string.Format("{0,-18} {1,10} {2,10:0} {3,10:0.0} {4,8} {5,10:0} {6,12:0.0}  {7}",
                        d.Module, d.DpcCount, d.DpcMaxUs,
                        d.DpcCount > 0 ? (double)d.DpcTotalUs / d.DpcCount : 0,
                        d.IsrCount, d.IsrMaxUs, (d.DpcTotalUs + d.IsrTotalUs) / 1000.0, d.Description));
                File.WriteAllText(outPath, sb.ToString(), System.Text.Encoding.UTF8);
                MessageBox.Show(this, "Résumé enregistré :\n" + outPath, "Fluide",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Export impossible :\n" + ex.Message, "Fluide",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_refresh != null) { _refresh.Stop(); _refresh.Dispose(); _refresh = null; }
                if (_probe != null) { _probe.Dispose(); _probe = null; }
                if (_etw != null) { _etw.Dispose(); _etw = null; if (_log != null) _log("Latence en direct : session ETW arrêtée.", 0); }
                if (_tileCapFont != null) { _tileCapFont.Dispose(); _tileCapFont = null; }
                if (_tileValFont != null) { _tileValFont.Dispose(); _tileValFont = null; }
                if (_boldRow != null) { _boldRow.Dispose(); _boldRow = null; }
                if (!_timerWasActive) Native.SetTimer1ms(false);
            }
            base.Dispose(disposing);
        }
    }
}
