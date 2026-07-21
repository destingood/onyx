using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// FPS EN DIRECT façon PresentMon : FPS réels, frametime moyen, 1% low et pics de
    /// chaque application qui présente des images (ETW, sans overlay ni injection).
    /// Verdict face à la fréquence de l'écran principal (objectif 500 Hz = 500 FPS).
    /// </summary>
    internal class FpsMonForm : Form
    {
        private readonly Action<string, int> _log;
        private FpsEtw _etw;
        private Timer _refresh;
        private int _selectedPid = -1;      // -1 : sélection automatique (meilleur candidat jeu)
        private bool _manualSelect;
        private int _screenHz;

        private Panel _banner;
        private Label _bannerTitle, _bannerDetail;
        private FlowLayoutPanel _tiles;
        private Panel _graph;
        private ListView _grid;
        private Label _footer;
        private double[] _graphData = new double[0];

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
            Text = "DesTinGOOD — FPS EN DIRECT (par jeu, façon PresentMon)";
            ClientSize = new Size(940, 640);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(760, 520);
            BackColor = Bg;
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            _banner = new Panel { Dock = DockStyle.Top, Height = 86, Padding = new Padding(18, 10, 18, 10), BackColor = TileBg };
            _bannerTitle = new Label
            {
                Dock = DockStyle.Top, Height = 40,
                Font = new Font("Segoe UI Semibold", 18f), ForeColor = Color.White, BackColor = Color.Transparent,
                Text = "Démarrage de la mesure..."
            };
            _bannerDetail = new Label
            {
                Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.FromArgb(235, 238, 240), BackColor = Color.Transparent,
                Text = "Lance un jeu : chaque image présentée est comptée (ETW, zéro impact, aucun overlay)."
            };
            _banner.Controls.Add(_bannerDetail);
            _banner.Controls.Add(_bannerTitle);

            _tiles = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 92, Padding = new Padding(14, 10, 14, 6), BackColor = Bg };

            _graph = new Panel { Dock = DockStyle.Top, Height = 110, BackColor = Color.FromArgb(18, 20, 26), Padding = new Padding(0) };
            _graph.Paint += OnGraphPaint;
            var graphHost = new Panel { Dock = DockStyle.Top, Height = 118, Padding = new Padding(14, 0, 14, 8), BackColor = Bg };
            graphHost.Controls.Add(_graph);
            _graph.Dock = DockStyle.Fill;

            _grid = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true,
                MultiSelect = false, HideSelection = false, Font = new Font("Segoe UI", 9f)
            };
            _grid.Columns.Add("Application", 200);
            _grid.Columns.Add("PID", 70, HorizontalAlignment.Right);
            _grid.Columns.Add("FPS", 80, HorizontalAlignment.Right);
            _grid.Columns.Add("1% low", 80, HorizontalAlignment.Right);
            _grid.Columns.Add("Frametime moy. ms", 120, HorizontalAlignment.Right);
            _grid.Columns.Add("Pire frame ms", 100, HorizontalAlignment.Right);
            _grid.Columns.Add("Images totales", 110, HorizontalAlignment.Right);
            _grid.ItemSelectionChanged += (s, e) =>
            {
                if (!e.IsSelected) return;
                int pid;
                if (int.TryParse(e.Item.SubItems[1].Text, out pid)) { _selectedPid = pid; _manualSelect = true; }
            };

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(14, 7, 14, 7) };
            var btnAuto = MakeBtn("Sélection auto", 130);
            btnAuto.Dock = DockStyle.Left;
            btnAuto.Click += (s, e) => { _manualSelect = false; _selectedPid = -1; };
            var btnReset = MakeBtn("Remettre à zéro", 140);
            btnReset.Dock = DockStyle.Left;
            btnReset.Click += (s, e) => { if (_etw != null) _etw.Reset(); };
            var btnClose = MakeBtn("Fermer", 110);
            btnClose.Dock = DockStyle.Right;
            btnClose.Click += (s, e) => Close();
            _footer = new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(110, 115, 125),
                Text = "FPS présentés (côté CPU, façon PresentMon) · clique une ligne pour suivre cette application"
            };
            bottom.Controls.Add(_footer);
            bottom.Controls.Add(btnReset);
            bottom.Controls.Add(btnAuto);
            bottom.Controls.Add(btnClose);

            Controls.Add(_grid);
            Controls.Add(graphHost);
            Controls.Add(_tiles);
            Controls.Add(_banner);
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

        private Panel MakeTile(string caption, string value, Color valueColor)
        {
            var p = new Panel { Size = new Size(174, 74), Margin = new Padding(4, 2, 4, 2), BackColor = TileBg };
            var cap = new Label
            {
                Text = caption, Dock = DockStyle.Top, Height = 24,
                ForeColor = Color.FromArgb(165, 170, 180), Font = new Font("Segoe UI", 8.5f),
                Padding = new Padding(10, 6, 6, 0)
            };
            var val = new Label
            {
                Text = value, Dock = DockStyle.Fill, ForeColor = valueColor,
                Font = new Font("Segoe UI Semibold", 14f), Padding = new Padding(10, 0, 6, 6)
            };
            p.Controls.Add(val);
            p.Controls.Add(cap);
            return p;
        }

        private void StartSession()
        {
            _etw = new FpsEtw();
            if (!_etw.Start())
            {
                _banner.BackColor = Red;
                _bannerTitle.Text = "Mesure impossible";
                _bannerDetail.Text = "Cause : " + (_etw.LastError ?? "inconnue")
                    + ". Lance DesTinGOOD en administrateur.";
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

            // Bannière + verdict face à l'écran
            if (sel == null)
            {
                _banner.BackColor = TileBg;
                _bannerTitle.Text = "En attente d'images...";
                _bannerDetail.Text = "Lance un jeu (ou n'importe quelle app 3D) : ses FPS apparaîtront ici tout seuls.";
            }
            else
            {
                _bannerTitle.Text = sel.Name + " — " + sel.Fps.ToString("0") + " FPS";
                if (_screenHz > 0)
                {
                    double ratio = sel.Fps / _screenHz;
                    if (ratio >= 0.95)
                    {
                        _banner.BackColor = Green;
                        _bannerDetail.Text = "Objectif atteint : ton écran " + _screenHz + " Hz est exploité à fond ("
                            + (ratio * 100).ToString("0") + " %). 1% low : " + sel.OnePctLowFps.ToString("0") + " FPS.";
                    }
                    else if (ratio >= 0.5)
                    {
                        _banner.BackColor = Orange;
                        _bannerDetail.Text = sel.Fps.ToString("0") + " FPS sur un écran " + _screenHz
                            + " Hz (" + (ratio * 100).ToString("0") + " %) — vérifie la limite de FPS du jeu (panneau 🎯), sinon c'est le CPU/GPU qui plafonne.";
                    }
                    else
                    {
                        _banner.BackColor = TileBg;
                        _bannerDetail.Text = sel.Fps.ToString("0") + " FPS · 1% low " + sel.OnePctLowFps.ToString("0")
                            + " · frametime moyen " + sel.AvgMs.ToString("0.00") + " ms.";
                    }
                }
                else
                    _bannerDetail.Text = "1% low : " + sel.OnePctLowFps.ToString("0") + " FPS · frametime moyen " + sel.AvgMs.ToString("0.00") + " ms.";
            }

            // Tuiles
            _tiles.SuspendLayout();
            _tiles.Controls.Clear();
            if (sel != null)
            {
                _tiles.Controls.Add(MakeTile("FPS (1 s)", sel.Fps.ToString("0"), Color.White));
                _tiles.Controls.Add(MakeTile("1% low", sel.OnePctLowFps > 0 ? sel.OnePctLowFps.ToString("0") : "…", ColorFor1PctLow(sel)));
                _tiles.Controls.Add(MakeTile("Frametime moyen", sel.AvgMs.ToString("0.00") + " ms", Color.White));
                _tiles.Controls.Add(MakeTile("Pire frame (1 s)", sel.WorstMs.ToString("0.0") + " ms", WorstColor(sel.WorstMs)));
                _tiles.Controls.Add(MakeTile("Images totales", sel.Total.ToString("#,0"), Color.White));
            }
            _tiles.ResumeLayout();

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
                it.SubItems.Add(st.AvgMs.ToString("0.00"));
                it.SubItems.Add(st.WorstMs.ToString("0.0"));
                it.SubItems.Add(st.Total.ToString("#,0"));
                if (st.Pid == _selectedPid) { it.Font = new Font(_grid.Font, FontStyle.Bold); it.Selected = true; }
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
            double r = st.Fps > 0 ? st.OnePctLowFps / st.Fps : 1;
            if (r >= 0.7) return Theme.AccentColor;
            if (r >= 0.4) return Color.FromArgb(245, 190, 90);
            return Color.FromArgb(245, 120, 120);   // gros écart moyenne/1% low = stutter
        }

        private static Color WorstColor(double ms)
        {
            if (ms <= 8) return Theme.AccentColor;
            if (ms <= 25) return Color.White;
            if (ms <= 50) return Color.FromArgb(245, 190, 90);
            return Color.FromArgb(245, 120, 120);
        }

        private void OnGraphPaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            Rectangle rc = _graph.ClientRectangle;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (_graphData.Length < 2)
            {
                TextRenderer.DrawText(g, "frametimes (une barre = une image)", Font, rc,
                    Color.FromArgb(110, 115, 125), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            double max = 1;
            foreach (double v in _graphData) if (v > max) max = v;
            double target = _screenHz > 0 ? 1000.0 / _screenHz : 0;   // ex. 2 ms à 500 Hz
            if (target > 0 && target * 3 > max) max = target * 3;

            // Ligne cible (frametime idéal de l'écran)
            if (target > 0)
            {
                float ty = (float)(rc.Height - (target / max) * (rc.Height - 6)) - 3;
                using (var pen = new Pen(Color.FromArgb(90, Theme.OkColor), 1f) { DashStyle = DashStyle.Dash })
                    g.DrawLine(pen, 0, ty, rc.Width, ty);
                TextRenderer.DrawText(g, target.ToString("0.0") + " ms (" + _screenHz + " Hz)",
                    new Font("Segoe UI", 7.5f), new Point(4, (int)ty - 15), Theme.OkColor);
            }

            float w = Math.Max(1f, (float)rc.Width / _graphData.Length);
            for (int i = 0; i < _graphData.Length; i++)
            {
                double v = _graphData[i];
                float h = (float)((v / max) * (rc.Height - 6));
                float x = i * w;
                Color c = target > 0 && v > target * 2.5 ? Color.FromArgb(245, 120, 120)
                        : (target > 0 && v > target * 1.5 ? Color.FromArgb(245, 190, 90) : Color.FromArgb(0, 190, 255));
                using (var brush = new SolidBrush(Color.FromArgb(200, c)))
                    g.FillRectangle(brush, x, rc.Height - h - 3, Math.Max(1f, w - 0.5f), h);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_refresh != null) { _refresh.Stop(); _refresh.Dispose(); _refresh = null; }
                if (_etw != null) { _etw.Dispose(); _etw = null; if (_log != null) _log("FPS en direct : mesure arrêtée.", 0); }
            }
            base.Dispose(disposing);
        }
    }
}
