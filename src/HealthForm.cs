using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// 🏥 Santé de mon PC : un score global sur 100 qui agrège les contrôles rapides de tous
    /// les panneaux (crashs, thermique, réglages néfastes, boutiques, bibliothèques, disque,
    /// réseau, optimisations). Chaque point à corriger ouvre le panneau concerné en un clic.
    /// </summary>
    internal class HealthForm : Form
    {
        private readonly Action<string, int> _log;
        private Label _scoreLabel, _grade, _sub;
        private Panel _gauge, _chart;
        private List<int> _history = new List<int>();
        private ListView _list;
        private Button _btnScan, _btnOpen, _btnClose;
        private int _score = -1;

        private static readonly Color Accent = Color.FromArgb(0, 150, 90);
        private static readonly Color Warn = Color.FromArgb(200, 110, 0);
        private static readonly Color Bad = Color.FromArgb(200, 60, 40);

        private class Finding
        {
            public string Text; public int Severity;   // 0 ok, 1 attention, 2 grave
            public Func<Form> Open;                     // panneau à ouvrir (facultatif)
        }
        private readonly List<Finding> _findings = new List<Finding>();

        public HealthForm(Action<string, int> log)
        {
            _log = log;
            Build();
            Scan();
            Theme.Apply(this);
        }

        // Polices possédées par la fenêtre, libérées à la fermeture (les Control ne libèrent
        // PAS leur Font eux-mêmes) : évite une fuite de handles à chaque ouverture du bilan.
        private readonly System.Collections.Generic.List<Font> _fonts = new System.Collections.Generic.List<Font>();
        private Font Own(Font f) { _fonts.Add(f); return f; }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { foreach (Font f in _fonts) { try { f.Dispose(); } catch { } } _fonts.Clear(); }
            base.Dispose(disposing);
        }

        private void Build()
        {
            Text = "DesTinGOOD — Santé de mon PC";
            ClientSize = new Size(680, 520);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = Color.FromArgb(245, 246, 248);
            Font = Own(new Font("Segoe UI", 9f));
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  🏥 Santé de mon PC — le bilan en un coup d'œil",
                Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = Own(new Font("Segoe UI Semibold", 12.5f)), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            // Jauge de score (gros nombre à gauche).
            _gauge = new Panel { Location = new Point(18, 66), Size = new Size(180, 120), BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
            _scoreLabel = new Label { Text = "…", Location = new Point(0, 14), Size = new Size(180, 62), Font = Own(new Font("Segoe UI", 42f, FontStyle.Bold)), ForeColor = Accent, TextAlign = ContentAlignment.MiddleCenter };
            _grade = new Label { Text = "", Location = new Point(0, 80), Size = new Size(180, 30), Font = Own(new Font("Segoe UI Semibold", 13f)), ForeColor = Color.FromArgb(60, 64, 72), TextAlign = ContentAlignment.MiddleCenter };
            _gauge.Controls.Add(_scoreLabel); _gauge.Controls.Add(_grade);
            Controls.Add(_gauge);

            _sub = new Label
            {
                Location = new Point(212, 70), Size = new Size(450, 56), ForeColor = Color.FromArgb(60, 64, 72),
                Font = Own(new Font("Segoe UI", 9.5f))
            };
            Controls.Add(_sub);

            // Mini-graphique de tendance des scores (historique).
            _chart = new Panel { Location = new Point(212, 128), Size = new Size(450, 58), BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
            _chart.Paint += DrawHistoryChart;
            Controls.Add(_chart);

            _list = new ListView
            {
                Location = new Point(18, 200), Size = new Size(644, 258),
                View = View.Details, FullRowSelect = true, GridLines = false, MultiSelect = false
            };
            _list.Columns.Add("État", 52);
            _list.Columns.Add("Contrôle", 592);
            _list.DoubleClick += (s, e) => OpenSelected();
            Controls.Add(_list);

            _btnScan = MakeBtn("Refaire le bilan", 18, 470, 150, 38, false);
            _btnScan.Click += (s, e) => Scan();
            _btnOpen = MakeBtn("Ouvrir le panneau du point sélectionné", 178, 470, 320, 38, true);
            _btnOpen.Click += (s, e) => OpenSelected();
            _btnClose = MakeBtn("Fermer", 572, 470, 90, 38, false);
            _btnClose.Click += (s, e) => Close();
            Controls.Add(_btnScan); Controls.Add(_btnOpen); Controls.Add(_btnClose);
        }

        private Button MakeBtn(string text, int x, int y, int w, int h, bool primary)
        {
            var b = new Button
            {
                Text = text, Location = new Point(x, y), Size = new Size(w, h), FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Color.FromArgb(40, 44, 52),
                Font = Own(primary ? new Font("Segoe UI Semibold", 9.5f) : new Font("Segoe UI", 9f))
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            return b;
        }

        private void SetBusy(bool busy)
        {
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            _btnScan.Enabled = !busy; _btnOpen.Enabled = !busy; _list.Enabled = !busy;
        }

        private void OpenSelected()
        {
            if (_list.SelectedItems.Count != 1) return;
            var f = _list.SelectedItems[0].Tag as Finding;
            if (f == null || f.Open == null) return;
            try { using (Form panel = f.Open()) panel.ShowDialog(this); }
            catch (Exception ex) { if (_log != null) _log("Ouverture du panneau : " + ex.Message, 2); }
            Scan();
        }

        private void Scan()
        {
            SetBusy(true);
            _scoreLabel.Text = "…"; _grade.Text = ""; _sub.Text = "Bilan en cours (crashs, thermique, réglages, disque, réseau)...";
            _list.Items.Clear();
            Task.Run(() =>
            {
                try
                {
                    var findings = Compute(out _score);
                    UiSafe.Post(this, () => Render(findings));
                }
                catch { UiSafe.Post(this, () => SetBusy(false)); }   // bilan lourd : ne pas figer si un sous-système lève
            });
        }

        // ------------------------------------------------------------------
        //  Calcul du score (contrôles rapides, agrégés)
        // ------------------------------------------------------------------
        private List<Finding> Compute(out int score)
        {
            var f = new List<Finding>();
            int s = 100;

            // 1. Crashs & pilote GPU (14 j)
            int nvl = CrashScan.GpuDriverErrors(14);
            int bsod = CrashScan.Bsod(14), whea = CrashScan.Whea(14), hard = CrashScan.HardResets(14);
            if (nvl > 0) { s -= 15; f.Add(New(2, "Pilote GPU : " + (nvl >= 200 ? "200+" : nvl.ToString()) + " erreur(s) en 14 j — piste n°1 des crashs de jeux", () => new StabilityForm(_log))); }
            if (bsod > 0 || whea > 0 || hard > 0) { s -= 15; f.Add(New(2, "Signes matériels : " + bsod + " écran(s) bleu(s), " + whea + " WHEA, " + hard + " coupure(s) brute(s)", () => new StabilityForm(_log))); }
            if (nvl == 0 && bsod == 0 && whea == 0 && hard == 0) f.Add(New(0, "Stabilité : aucun crash ni signe matériel sur 14 jours", () => new StabilityForm(_log)));

            // 2. Réglages néfastes
            int bad = 0; try { foreach (Checkup.Item it in Checkup.Analyze()) if (it.Problem) bad++; } catch { }
            if (bad > 0) { s -= Math.Min(25, bad * 10); f.Add(New(2, bad + " réglage(s) néfaste(s) d'un ancien optimiseur (à annuler)", () => new CheckupForm(_log))); }
            else f.Add(New(0, "Aucun réglage néfaste laissé par un autre outil", () => new CheckupForm(_log)));

            // 3. Boutiques / crashs (causes logicielles)
            int shop = 0; try { foreach (ShopFix.Item it in ShopFix.Analyze()) if (it.Problem) shop++; } catch { }
            if (shop > 0) { s -= Math.Min(12, shop * 3); f.Add(New(1, shop + " cause(s) possible(s) de boutiques infinies / crashs (HAGS, services, OC...)", () => new ShopFixForm(_log))); }
            else f.Add(New(0, "Boutiques & lancement des jeux : rien à signaler", () => new ShopFixForm(_log)));

            // 4. Bibliothèques de jeu essentielles manquantes
            int libMissing = 0;
            try { foreach (LibScan.LibItem it in LibScan.Items()) { if (!it.Essential) continue; bool ok; try { ok = it.Installed(); } catch { ok = false; } if (!ok) libMissing++; } } catch { }
            if (libMissing > 0) { s -= Math.Min(12, libMissing * 4); f.Add(New(1, libMissing + " bibliothèque(s) de jeu manquante(s) (vcruntime, DirectX...) — jeux qui refusent de démarrer", () => new LibsForm(_log))); }
            else f.Add(New(0, "Bibliothèques de jeu essentielles : toutes présentes", () => new LibsForm(_log)));

            // 5. Espace disque système
            bool lowDisk = false; string diskDetail = "";
            try
            {
                var sys = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
                double freeGB = sys.AvailableFreeSpace / 1073741824.0;
                double pct = sys.TotalSize > 0 ? (double)sys.AvailableFreeSpace / sys.TotalSize * 100 : 100;
                if (pct < 8 || freeGB < 15) { lowDisk = true; diskDetail = sys.Name + " " + freeGB.ToString("0") + " Go libres (" + pct.ToString("0") + " %)"; }
            }
            catch { }
            if (lowDisk) { s -= 10; f.Add(New(1, "Disque système presque plein : " + diskDetail + " — Windows ralentit", () => new DiskForm(_log))); }
            else f.Add(New(0, "Espace disque système : suffisant", () => new DiskForm(_log)));

            // 5b. Santé S.M.A.R.T. des disques (natif, sans pilote noyau) : un disque en fin de
            //     vie est un RISQUE DE PERTE DE DONNÉES — la pénalité la plus lourde du bilan.
            try
            {
                string diskBad;
                int badDisks = DiskForm.UnhealthyDisks(out diskBad);
                if (badDisks > 0) { s -= 25; f.Add(New(2, "Disque en fin de vie (S.M.A.R.T. : " + diskBad + ") — SAUVEGARDE tes données et remplace-le", () => new DiskForm(_log))); }
                else f.Add(New(0, "Santé S.M.A.R.T. des disques : tous sains", () => new DiskForm(_log)));
            }
            catch { }

            // 6. Écran sous sa fréquence max (60 Hz sur un 144/240 Hz : gros gain de fluidité manqué)
            try
            {
                var modes = DisplayInfo.Query();
                var below = modes.Where(m => m.BelowMax).ToList();
                if (below.Count > 0)
                {
                    s -= 8;
                    DisplayInfo.DisplayMode m0 = below[0];
                    f.Add(New(1, below.Count + " écran(s) sous leur fréquence max (ex. " + m0.CurrentHz + " Hz au lieu de "
                        + m0.MaxHz + ") — fluidité perdue", () => new DisplayForm(_log)));
                }
                else if (modes.Count > 0)
                    f.Add(New(0, "Écran(s) à leur fréquence maximale", () => new DisplayForm(_log)));
            }
            catch { }

            // 7. Réseau (petit test de gigue/perte)
            int loss; double jitter, avg;
            QuickPing("1.1.1.1", out avg, out jitter, out loss);
            if (loss >= 5 || jitter > 15 || avg < 0) { s -= 10; f.Add(New(1, "Réseau instable : " + (avg < 0 ? "injoignable" : avg.ToString("0") + " ms, gigue " + jitter.ToString("0.#") + " ms, perte " + loss + " %"), () => new NetworkForm(_log))); }
            else f.Add(New(0, "Réseau : latence stable (gigue " + jitter.ToString("0.#") + " ms, perte " + loss + " %)", () => new NetworkForm(_log)));

            // 7. Optimisations recommandées appliquées (informatif, léger)
            int reco = 0, recoOn = 0;
            try { foreach (Tweak t in Catalog.All()) if (t.Recommended) { reco++; try { if (t.Check != null && t.Check() == true) recoOn++; } catch { } } } catch { }
            if (reco > 0 && recoOn < reco / 2) { s -= 8; f.Add(New(1, "Optimisations recommandées : " + recoOn + "/" + reco + " appliquées — clique sur ⚡ TOUT OPTIMISER", null)); }
            else f.Add(New(0, "Optimisations recommandées : " + recoOn + "/" + reco + " appliquées", null));

            score = Math.Max(0, Math.Min(100, s));
            // Graves d'abord, puis attention, puis OK.
            return f.OrderByDescending(x => x.Severity).ToList();
        }

        private static Finding New(int sev, string text, Func<Form> open) { return new Finding { Severity = sev, Text = text, Open = open }; }

        private static void QuickPing(string host, out double avg, out double jitter, out int lossPct)
        {
            var times = new List<long>(); int loss = 0;
            try
            {
                using (var ping = new Ping())
                    for (int i = 0; i < 8; i++)
                    {
                        try { PingReply r = ping.Send(host, 800); if (r != null && r.Status == IPStatus.Success) times.Add(r.RoundtripTime); else loss++; }
                        catch { loss++; }
                    }
            }
            catch { }
            lossPct = loss * 100 / 8;
            if (times.Count == 0) { avg = -1; jitter = 0; return; }
            avg = times.Average();
            double j = 0; for (int i = 1; i < times.Count; i++) j += Math.Abs(times[i] - times[i - 1]);
            jitter = times.Count > 1 ? j / (times.Count - 1) : 0;
        }

        // ------------------------------------------------------------------
        //  Rendu
        // ------------------------------------------------------------------
        private void Render(List<Finding> findings)
        {
            _findings.Clear(); _findings.AddRange(findings);

            Color c = _score >= 90 ? Accent : _score >= 75 ? Color.FromArgb(90, 150, 60) : _score >= 55 ? Warn : Bad;
            string grade = _score >= 90 ? "Excellent" : _score >= 75 ? "Bon" : _score >= 55 ? "Moyen" : "À corriger";
            _scoreLabel.Text = _score.ToString(); _scoreLabel.ForeColor = c;
            _grade.Text = grade; _grade.ForeColor = c;
            _gauge.BackColor = Color.White;

            int graves = findings.Count(x => x.Severity == 2);
            int attn = findings.Count(x => x.Severity == 1);
            _sub.Text = graves + attn == 0
                ? "✔ Ton PC est en pleine forme pour jouer. Rien à corriger — profite du jeu.\n\nAstuce : refais ce bilan après une grosse mise à jour Windows ou de pilote."
                : (graves > 0 ? graves + " point(s) GRAVE(S)" : "") + (graves > 0 && attn > 0 ? " et " : "") + (attn > 0 ? attn + " point(s) d'attention" : "")
                  + ".\n\nDouble-clique un point (ou sélectionne-le et « Ouvrir le panneau ») pour aller le corriger. "
                  + "Les points graves sont en haut.";

            // Historique : compare au bilan précédent AVANT d'enregistrer celui-ci.
            List<int> previous = LoadHistory();
            if (previous.Count > 0)
            {
                int delta = _score - previous[previous.Count - 1];
                string arrow = delta > 0 ? "▲ +" + delta : delta < 0 ? "▼ " + delta : "= stable";
                _sub.Text += "\n\nÉvolution : " + arrow + " depuis le dernier bilan (voir le graphique).";
            }
            SaveHistory(_score);
            _history = LoadHistory();          // inclut le score qu'on vient d'enregistrer
            if (_chart != null) _chart.Invalidate();

            _list.Items.Clear();
            foreach (Finding fi in findings)
            {
                var it = new ListViewItem(fi.Severity == 2 ? "⛔" : fi.Severity == 1 ? "⚠" : "✔") { Tag = fi };
                it.SubItems.Add(fi.Text + (fi.Open != null ? "   →" : ""));
                it.ForeColor = fi.Severity == 2 ? Bad : fi.Severity == 1 ? Warn : Color.FromArgb(40, 44, 52);
                _list.Items.Add(it);
            }
            if (_log != null) _log("Bilan santé PC : score " + _score + "/100 (" + grade + "), "
                + graves + " grave(s), " + attn + " attention(s).", graves > 0 ? 2 : (attn > 0 ? 0 : 1));
            SetBusy(false);
        }

        // Mini-graphique : courbe des derniers scores (0-100), point courant mis en avant.
        private void DrawHistoryChart(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int w = _chart.ClientSize.Width, h = _chart.ClientSize.Height;
            int padL = 6, padR = 6, padT = 6, padB = 6;

            using (var grid = new Pen(Color.FromArgb(232, 235, 238)))
                for (int i = 0; i <= 4; i++) { int y = padT + (h - padT - padB) * i / 4; g.DrawLine(grid, padL, y, w - padR, y); }

            var pts = _history;
            if (pts == null || pts.Count == 0)
            {
                using (var f = new Font("Segoe UI", 8.5f))
                    g.DrawString("Aucun historique — refais un bilan pour voir la tendance.", f, Brushes.Gray, padL + 2, h / 2 - 8);
                return;
            }

            int n = Math.Min(20, pts.Count);
            var recent = pts.GetRange(pts.Count - n, n);
            float plotW = w - padL - padR, plotH = h - padT - padB;
            Func<int, float> xAt = i => n <= 1 ? padL + plotW / 2 : padL + plotW * i / (n - 1);
            Func<int, float> yAt = v => padT + plotH * (100 - Math.Max(0, Math.Min(100, v))) / 100f;

            Color line = recent[recent.Count - 1] >= 75 ? Accent : recent[recent.Count - 1] >= 55 ? Warn : Bad;
            if (n >= 2)
                using (var pen = new Pen(line, 2f))
                    for (int i = 1; i < n; i++)
                        g.DrawLine(pen, xAt(i - 1), yAt(recent[i - 1]), xAt(i), yAt(recent[i]));

            for (int i = 0; i < n; i++)
            {
                float x = xAt(i), y = yAt(recent[i]);
                bool last = i == n - 1;
                using (var br = new SolidBrush(last ? line : Color.FromArgb(150, line)))
                    g.FillEllipse(br, x - (last ? 3.5f : 2f), y - (last ? 3.5f : 2f), last ? 7 : 4, last ? 7 : 4);
            }
            using (var f = new Font("Segoe UI Semibold", 8.5f))
            using (var lineBrush = new SolidBrush(line))
            {
                g.DrawString("100", f, Brushes.Silver, w - padR - 22, padT - 2);
                g.DrawString(recent[recent.Count - 1].ToString(), f, lineBrush, padL + 2, padT - 2);
            }
        }

        // ------------------------------------------------------------------
        //  Historique des scores (fichier local, à côté de l'app)
        // ------------------------------------------------------------------
        private static string HistoryPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-health-history.txt");
        }

        private static List<int> LoadHistory()
        {
            var scores = new List<int>();
            try
            {
                string p = HistoryPath();
                if (!File.Exists(p)) return scores;
                foreach (string line in File.ReadAllLines(p))
                {
                    string[] parts = line.Split('\t');
                    int v;
                    if (parts.Length >= 2 && int.TryParse(parts[1], out v)) scores.Add(v);
                }
            }
            catch { }
            return scores;
        }

        private static void SaveHistory(int score)
        {
            try
            {
                // Horodatage lisible sans dépendre d'une date interdite : via WMI heure locale.
                File.AppendAllText(HistoryPath(), Now() + "\t" + score + Environment.NewLine);
            }
            catch { }
        }

        private static string Now()
        {
            try { return DateTime.Now.ToString("yyyy-MM-dd HH:mm"); }
            catch { return "?"; }
        }
    }
}
