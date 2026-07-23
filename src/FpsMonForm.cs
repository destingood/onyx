using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// FPS EN DIRECT façon PresentMon : FPS réels, frametime moyen, 1% / 0.1% low et
    /// pics de chaque application qui présente des images (ETW, sans overlay ni
    /// injection). Verdict face à la fréquence de l'écran principal, gros compteur,
    /// graphique gradué des frametimes, export CSV et mode compact épinglable
    /// au-dessus du jeu.
    /// </summary>
    internal class FpsMonForm : Form
    {
        private readonly Action<string, int> _log;
        private FpsEtw _etw;
        private Timer _refresh;
        private int _selectedPid = -1;      // -1 : sélection automatique (meilleur candidat jeu)
        private bool _manualSelect;
        private string _selectedName = "";
        private int _screenHz;

        private Panel _banner;
        private Label _bannerTitle, _bannerDetail, _bannerFps;
        private FlowLayoutPanel _tiles;
        private Label _v1, _v01, _vAvg, _vWorst, _vTotal;
        private Panel _graph, _graphHost, _bottom;
        private ListView _grid;
        private Label _footer;
        private Button _btnAuto, _btnReset, _btnCsv, _btnCompact;
        private Font _boldRow;
        private double[] _graphData = new double[0];
        private double _graphMax = 1;       // échelle lissée (évite les sauts à chaque seconde)

        private bool _compact;
        private Rectangle _prevBounds;

        private static readonly Color Bg     = Color.FromArgb(245, 246, 248);
        private static readonly Color Green  = Color.FromArgb(0, 150, 90);
        private static readonly Color Orange = Color.FromArgb(205, 133, 0);
        private static readonly Color Red    = Color.FromArgb(200, 45, 45);
        private static readonly Color TileBg = Color.FromArgb(28, 30, 38);

        public FpsMonForm(Action<string, int> log)
        {
            _log = log;
            try
            {
                foreach (var d in DisplayInfo.Query())
                    if (d.Primary) { _screenHz = d.CurrentHz; break; }
            }
            catch { }
            Build();
            Theme.Apply(this);
            Load += (s, e) => StartSession();
        }

        private void Build()
        {
            Text = "Fluide — FPS EN DIRECT (par jeu, façon PresentMon)";
            ClientSize = new Size(940, 640);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(760, 520);
            BackColor = Bg;
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            _banner = new Panel { Dock = DockStyle.Top, Height = 86, Padding = new Padding(18, 10, 14, 10), BackColor = TileBg };
            _bannerTitle = new Label
            {
                Dock = DockStyle.Top, Height = 40,
                Font = new Font("Segoe UI Semibold", 18f), ForeColor = Color.White, BackColor = Color.Transparent,
                AutoEllipsis = true, Text = "Démarrage de la mesure..."
            };
            _bannerDetail = new Label
            {
                Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.FromArgb(235, 238, 240), BackColor = Color.Transparent,
                AutoEllipsis = true,
                Text = "Lance un jeu : chaque image présentée est comptée (ETW, zéro impact, aucun overlay)."
            };
            _bannerFps = new Label
            {
                Dock = DockStyle.Right, Width = 215, TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("Segoe UI Semibold", 30f), ForeColor = Color.White, BackColor = Color.Transparent,
                Text = "—"
            };
            _banner.Controls.Add(_bannerDetail);
            _banner.Controls.Add(_bannerTitle);
            _banner.Controls.Add(_bannerFps);

            // Tuiles construites une seule fois (mise à jour du texte à chaque seconde).
            _tiles = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 92, Padding = new Padding(14, 10, 14, 6), BackColor = Bg };
            _v1 = MakeTile("1% low (20 s)");
            _v01 = MakeTile("0.1% low (20 s)");
            _vAvg = MakeTile("Frametime moyen");
            _vWorst = MakeTile("Pire frame (1 s)");
            _vTotal = MakeTile("Images totales");

            _graph = new Panel { Dock = DockStyle.Fill };
            _graph.Paint += OnGraphPaint;
            _graph.Resize += (s, e) => _graph.Invalidate();
            _graphHost = new Panel { Dock = DockStyle.Top, Height = 128, Padding = new Padding(14, 0, 14, 8), BackColor = Bg };
            _graphHost.Controls.Add(_graph);

            _grid = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
                MultiSelect = false, HideSelection = false, Font = new Font("Segoe UI", 9f)
            };
            _grid.Columns.Add("Application", 190);
            _grid.Columns.Add("PID", 60, HorizontalAlignment.Right);
            _grid.Columns.Add("FPS", 75, HorizontalAlignment.Right);
            _grid.Columns.Add("1% low", 75, HorizontalAlignment.Right);
            _grid.Columns.Add("0.1% low", 75, HorizontalAlignment.Right);
            _grid.Columns.Add("Frametime moy. ms", 115, HorizontalAlignment.Right);
            _grid.Columns.Add("Pire frame ms", 95, HorizontalAlignment.Right);
            _grid.Columns.Add("Images totales", 105, HorizontalAlignment.Right);
            _boldRow = new Font(_grid.Font, FontStyle.Bold);
            _grid.ItemSelectionChanged += (s, e) =>
            {
                if (!e.IsSelected) return;
                int pid;
                if (int.TryParse(e.Item.SubItems[1].Text, out pid)) { _selectedPid = pid; _manualSelect = true; }
            };

            _bottom = new Panel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(14, 7, 14, 7) };
            _btnAuto = MakeBtn("Sélection auto", 118);
            _btnAuto.Dock = DockStyle.Left;
            _btnAuto.Click += (s, e) => { _manualSelect = false; _selectedPid = -1; };
            _btnReset = MakeBtn("Remettre à zéro", 128);
            _btnReset.Dock = DockStyle.Left;
            _btnReset.Click += (s, e) => { if (_etw != null) _etw.Reset(); _graphMax = 1; };
            _btnCsv = MakeBtn("Exporter CSV", 112);
            _btnCsv.Dock = DockStyle.Left;
            _btnCsv.Click += (s, e) => ExportCsv();
            // Cette fenêtre EST la mesure FPS/frametime intégrée (façon PresentMon). Le bouton
            // n'est qu'un « aller plus loin » optionnel vers CapFrameX (capture labo).
            var btnCfx = MakeBtn("Aller plus loin : CapFrameX", 200);
            btnCfx.Dock = DockStyle.Left;
            LibScan.WireToolButton(btnCfx, this, _log, "Aller plus loin : CapFrameX", new[] { "CXWorld.CapFrameX" });
            _btnCompact = MakeBtn("Mode compact ", 138);
            _btnCompact.Dock = DockStyle.Right;
            _btnCompact.Click += (s, e) => ToggleCompact();
            var btnClose = MakeBtn("Fermer", 92);
            btnClose.Dock = DockStyle.Right;
            btnClose.Click += (s, e) => Close();
            _footer = new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(110, 115, 125), AutoEllipsis = true,
                Text = "FPS présentés (côté CPU, façon PresentMon) · clique une ligne pour suivre cette application"
            };
            _bottom.Controls.Add(_footer);
            _bottom.Controls.Add(btnCfx);
            _bottom.Controls.Add(_btnCsv);
            _bottom.Controls.Add(_btnReset);
            _bottom.Controls.Add(_btnAuto);
            _bottom.Controls.Add(btnClose);
            _bottom.Controls.Add(_btnCompact);

            Controls.Add(_grid);
            Controls.Add(_graphHost);
            Controls.Add(_tiles);
            Controls.Add(_banner);
            Controls.Add(_bottom);
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

        /// <summary>Tuile arrondie sombre ; renvoie le label de valeur (mis à jour chaque seconde).</summary>
        private Label MakeTile(string caption)
        {
            var p = new Panel { Size = new Size(174, 74), Margin = new Padding(4, 2, 4, 2), BackColor = TileBg };
            p.Paint += OnPaintTile;
            p.Resize += (s, e) => p.Invalidate();
            var cap = new Label
            {
                Text = caption, Dock = DockStyle.Top, Height = 24, BackColor = Color.Transparent,
                ForeColor = Color.FromArgb(165, 170, 180), Font = new Font("Segoe UI", 8.5f),
                Padding = new Padding(10, 6, 6, 0)
            };
            var val = new Label
            {
                Text = "…", Dock = DockStyle.Fill, ForeColor = Color.White, BackColor = Color.Transparent,
                Font = new Font("Segoe UI Semibold", 14f), Padding = new Padding(10, 0, 6, 6)
            };
            p.Controls.Add(val);
            p.Controls.Add(cap);
            _tiles.Controls.Add(p);
            return val;
        }

        private void OnPaintTile(object sender, PaintEventArgs e)
        {
            var p = (Panel)sender;
            Rectangle r = p.ClientRectangle;
            using (var br = new SolidBrush(p.Parent != null ? p.Parent.BackColor : Bg))
                e.Graphics.FillRectangle(br, r);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rf = new RectangleF(0.5f, 0.5f, r.Width - 1f, r.Height - 1f);
            using (var path = Theme.RoundPath(rf, 8f))
            {
                using (var br = new SolidBrush(TileBg)) e.Graphics.FillPath(br, path);
                using (var pen = new Pen(Color.FromArgb(55, 60, 72))) e.Graphics.DrawPath(pen, path);
            }
        }

        private void StartSession()
        {
            _etw = new FpsEtw();
            if (!_etw.Start())
            {
                _banner.BackColor = Red;
                _bannerTitle.Text = "Mesure impossible";
                _bannerFps.Text = "—";
                _bannerDetail.Text = "Cause : " + (_etw.LastError ?? "inconnue")
                    + ". Lance Fluide en administrateur.";
                if (_log != null) _log("FPS en direct : session ETW refusée (" + (_etw.LastError ?? "?") + ").", 2);
                _etw.Dispose();
                _etw = null;
                return;
            }
            if (_log != null) _log("FPS en direct : mesure démarrée (DXGI + D3D9, façon PresentMon)"
                + (_screenHz > 0 ? " — écran principal " + _screenHz + " Hz." : "."), 1);

            _refresh = new Timer { Interval = 1000 };
            _refresh.Tick += (s, e) => Repaint();
            _refresh.Start();
        }

        private static bool IsSystemProc(string name)
        {
            string low = (name ?? "").ToLowerInvariant();
            return low.StartsWith("dwm") || low.StartsWith("explorer") || low.StartsWith("btoptimizer")
                || low.StartsWith("dotnet") || low.StartsWith("searchhost") || low.StartsWith("shellexperiencehost")
                || low.StartsWith("startmenuexperiencehost") || low.StartsWith("textinputhost");
        }

        private void Repaint()
        {
            if (_etw == null) return;
            List<FpsEtw.ProcStat> stats = _etw.Snapshot(1000);

            // Sélection : manuelle si demandée et toujours vivante, sinon meilleur candidat « jeu ».
            FpsEtw.ProcStat sel = null;
            if (_manualSelect)
                foreach (var st in stats) if (st.Pid == _selectedPid) { sel = st; break; }
            if (sel == null)
            {
                _manualSelect = false;
                foreach (var st in stats) if (!IsSystemProc(st.Name)) { sel = st; break; }
                if (sel == null && stats.Count > 0) sel = stats[0];
                if (sel != null) _selectedPid = sel.Pid;
            }
            _selectedName = sel != null ? sel.Name : "";

            // Bannière : gros FPS + verdict face à l'écran.
            if (sel == null)
            {
                _banner.BackColor = TileBg;
                _bannerTitle.Text = "En attente d'images...";
                _bannerFps.Text = "—";
                _bannerDetail.Text = "Lance un jeu (ou n'importe quelle app 3D) : ses FPS apparaîtront ici tout seuls.";
            }
            else
            {
                _bannerTitle.Text = sel.Name;
                _bannerFps.Text = sel.Fps.ToString("0");
                string lows = "1% low " + (sel.OnePctLowFps > 0 ? sel.OnePctLowFps.ToString("0") : "…")
                    + (sel.TenthPctLowFps > 0 ? " · 0.1% low " + sel.TenthPctLowFps.ToString("0") : "");
                if (_screenHz > 0)
                {
                    double ratio = sel.Fps / _screenHz;
                    if (ratio >= 0.95)
                    {
                        _banner.BackColor = Green;
                        _bannerDetail.Text = "Objectif atteint : écran " + _screenHz + " Hz exploité à "
                            + (ratio * 100).ToString("0") + " % · " + lows + ".";
                    }
                    else if (ratio >= 0.5)
                    {
                        _banner.BackColor = Orange;
                        _bannerDetail.Text = (ratio * 100).ToString("0") + " % de l'écran " + _screenHz
                            + " Hz — vérifie la limite de FPS du jeu (panneau ), sinon c'est le CPU/GPU qui plafonne · " + lows + ".";
                    }
                    else
                    {
                        _banner.BackColor = TileBg;
                        _bannerDetail.Text = lows + " · frametime moyen " + sel.AvgMs.ToString("0.00") + " ms.";
                    }
                }
                else
                    _bannerDetail.Text = lows + " · frametime moyen " + sel.AvgMs.ToString("0.00") + " ms.";
            }

            // Tuiles (mise à jour sans reconstruction : zéro clignotement).
            if (sel != null)
            {
                _v1.Text = sel.OnePctLowFps > 0 ? sel.OnePctLowFps.ToString("0") + " FPS" : "…";
                _v1.ForeColor = ColorFor1PctLow(sel);
                _v01.Text = sel.TenthPctLowFps > 0 ? sel.TenthPctLowFps.ToString("0") + " FPS" : "…";
                _v01.ForeColor = sel.TenthPctLowFps > 0 ? ColorForRatio(sel.TenthPctLowFps, sel.Fps) : Color.White;
                _vAvg.Text = sel.AvgMs.ToString("0.00") + " ms";
                _vAvg.ForeColor = Color.White;
                _vWorst.Text = sel.WorstMs.ToString("0.0") + " ms";
                _vWorst.ForeColor = WorstColor(sel.WorstMs);
                _vTotal.Text = sel.Total.ToString("#,0");
                _vTotal.ForeColor = Color.White;
            }
            else
            {
                _v1.Text = _v01.Text = _vAvg.Text = _vWorst.Text = _vTotal.Text = "…";
                _v1.ForeColor = _v01.ForeColor = _vWorst.ForeColor = Color.White;
            }

            // Graphique frametimes
            _graphData = sel != null ? _etw.RecentFrametimes(sel.Pid, 600) : new double[0];
            _graph.Invalidate();

            // Tableau
            _grid.BeginUpdate();
            _grid.Items.Clear();
            foreach (var st in stats)
            {
                var it = new ListViewItem(st.Name);
                it.SubItems.Add(st.Pid.ToString());
                it.SubItems.Add(st.Fps.ToString("0"));
                it.SubItems.Add(st.OnePctLowFps > 0 ? st.OnePctLowFps.ToString("0") : "-");
                it.SubItems.Add(st.TenthPctLowFps > 0 ? st.TenthPctLowFps.ToString("0") : "-");
                it.SubItems.Add(st.AvgMs.ToString("0.00"));
                it.SubItems.Add(st.WorstMs.ToString("0.0"));
                it.SubItems.Add(st.Total.ToString("#,0"));
                if (st.Pid == _selectedPid) { it.Font = _boldRow; it.Selected = true; }
                if (IsSystemProc(st.Name)) it.ForeColor = Color.FromArgb(130, 135, 145);
                _grid.Items.Add(it);
            }
            _grid.EndUpdate();

            _footer.Text = stats.Count + " application(s) présentent des images · FPS présentés côté CPU (façon PresentMon), sans overlay"
                + (_screenHz > 0 ? " · écran principal : " + _screenHz + " Hz" : "");
        }

        private Color ColorFor1PctLow(FpsEtw.ProcStat st)
        {
            if (st.OnePctLowFps <= 0) return Color.White;
            return ColorForRatio(st.OnePctLowFps, st.Fps);
        }

        /// <summary>Vert si le low reste proche de la moyenne (fluide), rouge si gros écart (stutter).</summary>
        private static Color ColorForRatio(double low, double fps)
        {
            double r = fps > 0 ? low / fps : 1;
            if (r >= 0.7) return Color.FromArgb(120, 230, 170);
            if (r >= 0.4) return Color.FromArgb(245, 190, 90);
            return Color.FromArgb(245, 120, 120);
        }

        private static Color WorstColor(double ms)
        {
            if (ms <= 8) return Color.FromArgb(120, 230, 170);
            if (ms <= 25) return Color.White;
            if (ms <= 50) return Color.FromArgb(245, 190, 90);
            return Color.FromArgb(245, 120, 120);
        }

        // ------------------------------------------------------------------
        //  Graphique : carte sombre arrondie, grille graduée en ms, ligne
        //  cible de l'écran, une barre par image (échelle lissée).
        // ------------------------------------------------------------------
        private void OnGraphPaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            Rectangle rc = _graph.ClientRectangle;
            if (rc.Width < 20 || rc.Height < 20) return;

            using (var br = new SolidBrush(_graphHost.BackColor)) g.FillRectangle(br, rc);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var card = new RectangleF(0.5f, 0.5f, rc.Width - 1f, rc.Height - 1f);
            using (var path = Theme.RoundPath(card, 8f))
            {
                using (var br = new SolidBrush(Color.FromArgb(16, 18, 24))) g.FillPath(br, path);
                using (var pen = new Pen(Color.FromArgb(55, 60, 72))) g.DrawPath(pen, path);
            }

            var inner = new RectangleF(8f, 6f, rc.Width - 16f, rc.Height - 12f);
            if (_graphData.Length < 2)
            {
                TextRenderer.DrawText(g, "frametimes (une barre = une image)", Font, rc,
                    Color.FromArgb(110, 115, 125), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            double needed = 1;
            foreach (double v in _graphData) if (v > needed) needed = v;
            double target = _screenHz > 0 ? 1000.0 / _screenHz : 0;   // ex. 2 ms à 500 Hz
            if (target > 0 && target * 3 > needed) needed = target * 3;
            // Échelle lissée : monte instantanément, redescend en douceur.
            _graphMax = needed > _graphMax ? needed : Math.Max(needed, _graphMax * 0.85);
            double max = _graphMax;

            // Grille horizontale graduée en ms.
            double step = 200;
            double[] steps = { 0.5, 1, 2, 5, 10, 20, 50, 100, 200 };
            foreach (double s in steps) if (max / s <= 4.5) { step = s; break; }
            using (var gridPen = new Pen(Color.FromArgb(38, 42, 52)))
            using (var gridFont = new Font("Segoe UI", 7f))
            {
                for (double v = step; v < max * 0.98; v += step)
                {
                    float gy = (float)(inner.Bottom - (v / max) * inner.Height);
                    g.DrawLine(gridPen, inner.Left, gy, inner.Right, gy);
                    TextRenderer.DrawText(g, v.ToString("0.#") + " ms", gridFont,
                        new Point((int)inner.Right - 44, (int)gy - 13), Color.FromArgb(95, 102, 115),
                        TextFormatFlags.Right | TextFormatFlags.NoPadding);
                }
            }

            // Ligne cible (frametime idéal de l'écran).
            if (target > 0)
            {
                float ty = (float)(inner.Bottom - (target / max) * inner.Height);
                using (var pen = new Pen(Color.FromArgb(110, 0, 210, 130), 1f) { DashStyle = DashStyle.Dash })
                    g.DrawLine(pen, inner.Left, ty, inner.Right, ty);
                using (var tf = new Font("Segoe UI", 7.5f))
                    TextRenderer.DrawText(g, target.ToString("0.0") + " ms (" + _screenHz + " Hz)",
                        tf, new Point((int)inner.Left + 2, (int)ty - 15), Color.FromArgb(0, 210, 130));
            }

            // Barres : une par image (bleu = fluide, orange/rouge = micro-saccade).
            float w = Math.Max(1f, inner.Width / _graphData.Length);
            for (int i = 0; i < _graphData.Length; i++)
            {
                double v = _graphData[i];
                float h = (float)((v / max) * inner.Height);
                float x = inner.Left + i * w;
                Color c = target > 0 && v > target * 2.5 ? Color.FromArgb(245, 120, 120)
                        : (target > 0 && v > target * 1.5 ? Color.FromArgb(245, 190, 90) : Color.FromArgb(0, 190, 255));
                using (var brush = new SolidBrush(Color.FromArgb(200, c)))
                    g.FillRectangle(brush, x, inner.Bottom - h, Math.Max(1f, w - 0.5f), h);
            }
        }

        // ------------------------------------------------------------------
        //  Mode compact : petite fenêtre épinglée au-dessus du jeu (bannière
        //  seule : nom, gros FPS, verdict), réversible d'un clic.
        // ------------------------------------------------------------------
        private void ToggleCompact()
        {
            _compact = !_compact;
            _tiles.Visible = !_compact;
            _graphHost.Visible = !_compact;
            _grid.Visible = !_compact;
            _btnAuto.Visible = _btnReset.Visible = _btnCsv.Visible = _footer.Visible = !_compact;
            if (_compact)
            {
                _prevBounds = Bounds;
                MinimumSize = new Size(430, 150);
                FormBorderStyle = FormBorderStyle.SizableToolWindow;
                TopMost = true;
                ClientSize = new Size(470, _banner.Height + _bottom.Height);
                _btnCompact.Text = "Mode complet";
            }
            else
            {
                FormBorderStyle = FormBorderStyle.Sizable;
                TopMost = false;
                MinimumSize = new Size(760, 520);
                Bounds = _prevBounds;
                _btnCompact.Text = "Mode compact ";
            }
        }

        /// <summary>Exporte les frametimes conservés (~20 s) de l'application suivie en CSV.</summary>
        private void ExportCsv()
        {
            if (_etw == null || _selectedPid < 0)
            {
                _footer.Text = "Rien à exporter : aucune application suivie.";
                return;
            }
            double[] fts = _etw.RecentFrametimes(_selectedPid, 20000);
            if (fts.Length == 0)
            {
                _footer.Text = "Rien à exporter : aucune frame enregistrée pour l'instant.";
                return;
            }
            try
            {
                string safe = _selectedName.Replace(".exe", "");
                foreach (char bad in System.IO.Path.GetInvalidFileNameChars()) safe = safe.Replace(bad, '_');
                string path = System.IO.Path.Combine(Sys.BackupDesktop,
                    "bt-frametimes-" + safe + "-" + DateTime.Now.ToString("HHmmss") + ".csv");
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("frame;frametime_ms;fps_instantane");
                for (int i = 0; i < fts.Length; i++)
                    sb.AppendLine((i + 1) + ";" + fts[i].ToString("0.000") + ";" + (fts[i] > 0 ? (1000.0 / fts[i]).ToString("0.0") : ""));
                System.IO.File.WriteAllText(path, sb.ToString(), System.Text.Encoding.UTF8);
                _footer.Text = fts.Length + " frametimes exportés : " + path;
                if (_log != null) _log("FPS en direct : " + fts.Length + " frametimes exportés (" + path + ").", 1);
            }
            catch (Exception ex)
            {
                _footer.Text = "Export impossible : " + ex.Message;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_refresh != null) { _refresh.Stop(); _refresh.Dispose(); _refresh = null; }
                if (_etw != null) { _etw.Dispose(); _etw = null; if (_log != null) _log("FPS en direct : mesure arrêtée.", 0); }
                if (_boldRow != null) { _boldRow.Dispose(); _boldRow = null; }
            }
            base.Dispose(disposing);
        }
    }
}
