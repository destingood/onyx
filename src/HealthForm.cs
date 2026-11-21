using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    internal class HealthForm : Form
    {
        private readonly Action<string, int> _log;
        private readonly BufferedPanel _gauge = new BufferedPanel(), _summary = new BufferedPanel(), _rows;
        private readonly OnyxScrollPanel _results = new OnyxScrollPanel();
        private readonly Button _btnScan, _btnClose;
        private readonly Button[] _filters = new Button[3];
        private readonly List<Finding> _findings = new List<Finding>();
        private readonly List<FindingRow> _cards = new List<FindingRow>();
        private List<int> _history = new List<int>();
        private bool _busy, _recordHistory = true;
        private int _score = -1, _filter;
        private string _message = "Lance un bilan pour examiner ton PC.", _evolution = "";
        private DateTime _completed;
        private static readonly Color Accent = FpsUi.Gold, Warn = FpsUi.Warn, Bad = FpsUi.Err;
        private sealed class Finding { public string Text; public int Severity; public Func<Form> Open; }

        public HealthForm(Action<string, int> log)
        {
            _log = log;
            Text = "ONYX — Bilan de santé"; BackColor = FpsUi.BgMain; ForeColor = FpsUi.Ink; Font = FpsUi.Body;
            ClientSize = new Size(1080, 760); MinimumSize = new Size(936, 739);
            StartPosition = FormStartPosition.CenterParent; MinimizeBox = false;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            _gauge.Paint += PaintGauge; _summary.Paint += PaintSummary;
            Controls.Add(_gauge); Controls.Add(_summary);
            string[] labels = { "Tous les contrôles", "À examiner", "Conformes" };
            for (int i = 0; i < 3; i++)
            {
                int index = i; _filters[i] = OnyxDesign.Button(labels[i]); _filters[i].Font = FpsUi.H3;
                _filters[i].Click += (s, e) => { _filter = index; FilterResults(); }; Controls.Add(_filters[i]);
            }
            _rows = _results.Content;
            _rows.Paint += (s, e) => {
                if (_busy || _cards.Count == 0) OnyxDesign.Text(e.Graphics, _message, FpsUi.Body, FpsUi.Dim, new Rectangle(22, 26, _rows.Width - 44, 80), true);
                else if (!_cards.Any(c => Matches(c.Item))) OnyxDesign.Text(e.Graphics, "Aucun contrôle dans cette catégorie.", FpsUi.Body, FpsUi.Dim, new Rectangle(22, 26, _rows.Width - 44, 50), true);
            };
            Controls.Add(_results); Dwm.Darken(this);
            _btnScan = OnyxDesign.PrimaryButton("Refaire le bilan"); _btnScan.Click += (s, e) => Scan(); Controls.Add(_btnScan);
            _btnClose = OnyxDesign.Button("Fermer"); _btnClose.Click += (s, e) => Close(); Controls.Add(_btnClose); CancelButton = _btnClose;
            Resize += (s, e) => DoLayout(); Shown += (s, e) => Scan();
            _history = LoadHistory(); FilterResults(); DoLayout();
        }
        private void DoLayout()
        {
            if (_btnScan == null) return;
            _gauge.SetBounds(28, 88, 236, 190); _summary.SetBounds(280, 88, ClientSize.Width - 308, 190);
            for (int i = 0; i < 3; i++) _filters[i].SetBounds(28 + i * 180, 298, 168, 38);
            _results.SetBounds(28, 354, ClientSize.Width - 40, Math.Max(140, ClientSize.Height - 430));
            _btnScan.SetBounds(28, ClientSize.Height - 58, 186, 36); _btnClose.SetBounds(ClientSize.Width - 138, ClientSize.Height - 58, 110, 36);
            LayoutRows(); Invalidate();
        }
        private bool Matches(Finding item) { return _filter == 0 || (_filter == 1 ? item.Severity > 0 : item.Severity == 0); }
        private void LayoutRows()
        {
            int width = Math.Max(300, _results.ContentWidth), top = 0;
            foreach (var card in _cards)
            {
                bool show = !_busy && Matches(card.Item); card.Visible = show;
                if (!show) continue;
                int textHeight = TextRenderer.MeasureText(card.Item.Text, FpsUi.Body, new Size(width - 214, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Height;
                int height = Math.Max(78, textHeight + 32);
                card.SetBounds(0, top, width, height); top += height + 10;
            }
            _results.ContentHeight = Math.Max(120, top);
            _rows.Invalidate();
        }
        private void FilterResults()
        {
            string[] labels = { "Tous les contrôles", "À examiner", "Conformes" };
            int[] counts = { _findings.Count, _findings.Count(f => f.Severity > 0), _findings.Count(f => f.Severity == 0) };
            for (int i = 0; i < 3; i++)
            {
                _filters[i].Text = labels[i] + (_score < 0 || _busy ? "" : "  ·  " + counts[i]);
                _filters[i].Enabled = !_busy;
                _filters[i].BackColor = i == _filter ? FpsUi.BlendGold(FpsUi.Card, .12f) : FpsUi.Card;
                _filters[i].ForeColor = i == _filter ? FpsUi.Gold : FpsUi.Dim;
                _filters[i].FlatAppearance.BorderSize = i == _filter ? 1 : 0; _filters[i].FlatAppearance.BorderColor = FpsUi.GoldDim;
            }
            _results.ScrollTo(0); LayoutRows();
        }
        private void SetBusy(bool busy)
        {
            _busy = busy; _btnScan.Enabled = !busy; _btnScan.Text = busy ? "Analyse en cours…" : "Refaire le bilan";
            FilterResults(); _gauge.Invalidate(); _summary.Invalidate(); Invalidate();
        }
        private void Scan()
        {
            if (_busy || IsDisposed) return;
            _score = -1; _filter = 0; _completed = default(DateTime); _evolution = "";
            _message = "Analyse en cours : stabilité, disques, réseau et réglages…";
            _findings.Clear(); foreach (var c in _cards) c.Dispose(); _cards.Clear();
            SetBusy(true);
            Task.Run(() =>
            {
                try
                {
                    int score; var findings = Compute(out score);
                    try { BeginInvoke((Action)(() => { if (IsDisposed) return; _score = score; Render(findings); })); } catch { }
                }
                catch (Exception ex)
                {
                    try { BeginInvoke((Action)(() => { if (IsDisposed) return; _message = "Le bilan n’a pas pu se terminer. Relance l’analyse."; SetBusy(false); _log?.Invoke("Bilan santé : " + ex.Message, 2); })); } catch { }
                }
            });
        }
        private void OpenFinding(Finding finding)
        {
            if (_busy || finding.Open == null) return;
            try { using (Form panel = finding.Open()) panel.ShowDialog(this); }
            catch (Exception ex) { _log?.Invoke("Ouverture du panneau : " + ex.Message, 2); }
            Scan();
        }
        private string Grade { get { return _score < 0 ? (_busy ? "Analyse en cours" : "Bilan indisponible") : _score >= 90 ? "Excellent" : _score >= 75 ? "Bon" : _score >= 55 ? "À surveiller" : "À examiner"; } }
        private Color ScoreColor { get { return _score < 0 || _score >= 90 ? Accent : _score >= 75 ? FpsUi.Ok : _score >= 55 ? Warn : Bad; } }
        private void PaintGauge(object sender, PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            OnyxDesign.PaintCard(g, _gauge.ClientRectangle, FpsUi.Card, OnyxDesign.Edge, 12);
            OnyxDesign.Text(g, "SCORE INDICATIF", FpsUi.Small, FpsUi.Dim, new Rectangle(20, 16, 200, 22));
            var ring = new Rectangle(68, 46, 100, 100);
            using (var pen = new Pen(FpsUi.Border, 6)) g.DrawArc(pen, ring, 135, 270);
            if (_score > 0) using (var pen = new Pen(ScoreColor, 6) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(pen, ring, 135, 270f * _score / 100);
            TextRenderer.DrawText(g, _score < 0 ? "—" : _score.ToString(), OnyxDesign.Number, new Rectangle(68, 69, 100, 44), ScoreColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, "/ 100", FpsUi.Small, new Rectangle(68, 116, 100, 22), FpsUi.Dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, Grade, FpsUi.H3, new Rectangle(10, 160, 216, 24), FpsUi.Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
        }
        private void PaintSummary(object sender, PaintEventArgs e)
        {
            var g = e.Graphics; int w = _summary.Width;
            OnyxDesign.PaintCard(g, _summary.ClientRectangle, FpsUi.Card, FpsUi.Border, 12);
            OnyxDesign.Text(g, _busy ? "VÉRIFICATION DU PC" : "RÉSULTATS DU BILAN", FpsUi.H3, FpsUi.Ink, new Rectangle(20, 17, w - 40, 24));
            if (_score < 0)
            {
                OnyxDesign.Text(g, _message, FpsUi.Body, FpsUi.Dim, new Rectangle(20, 61, w - 40, 64), true);
                return;
            }
            int[] counts = { _findings.Count(f => f.Severity == 2), _findings.Count(f => f.Severity == 1), _findings.Count(f => f.Severity == 0) };
            string[] labels = { "Prioritaires", "À surveiller", "Conformes" }; Color[] colors = { Bad, Warn, FpsUi.Ok };
            int cw = (w - 40) / 3;
            for (int i = 0; i < 3; i++)
            {
                int x = 20 + i * cw;
                OnyxDesign.Text(g, counts[i].ToString(), OnyxDesign.Number, counts[i] > 0 ? colors[i] : FpsUi.Dim, new Rectangle(x, 54, cw, 42));
                OnyxDesign.Text(g, labels[i], FpsUi.Small, FpsUi.Dim, new Rectangle(x, 103, cw, 22));
            }
            using (var pen = new Pen(FpsUi.Border)) g.DrawLine(pen, 20, 139, w - 20, 139);
            OnyxDesign.Text(g, _evolution, FpsUi.Small, FpsUi.Dim, new Rectangle(20, 156, w - 210, 22));
            PaintHistory(g, new Rectangle(w - 170, 152, 148, 20));
        }
        private void PaintHistory(Graphics g, Rectangle plot)
        {
            if (_history.Count == 0) return;
            var recent = _history.Skip(Math.Max(0, _history.Count - 20)).ToArray();
            var points = new PointF[recent.Length];
            for (int i = 0; i < points.Length; i++) points[i] = new PointF(plot.Left + (points.Length == 1 ? plot.Width : i * plot.Width / (float)(points.Length - 1)), plot.Bottom - recent[i] / 100f * plot.Height);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (points.Length > 1) using (var pen = new Pen(FpsUi.Gold, 1.5f)) g.DrawLines(pen, points);
            var last = points[points.Length - 1]; using (var brush = new SolidBrush(FpsUi.Gold)) g.FillEllipse(brush, last.X - 2, last.Y - 2, 4, 4);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            OnyxDesign.Text(e.Graphics, "Bilan de santé", OnyxDesign.Title, FpsUi.Ink, new Rectangle(28, 20, 500, 34));
            OnyxDesign.Text(e.Graphics, "Les contrôles prioritaires apparaissent en premier. Ouvre un résultat pour l’examiner.", FpsUi.Small, FpsUi.Dim, new Rectangle(28, 58, ClientSize.Width - 56, 22));
            string footer = _completed == default(DateTime) || _busy ? "Le bilan analyse le PC sans appliquer de correction." : "Bilan terminé à " + _completed.ToString("HH:mm") + " · Aucune correction automatique";
            OnyxDesign.Text(e.Graphics, footer, FpsUi.Small, FpsUi.Dim, new Rectangle(232, ClientSize.Height - 48, ClientSize.Width - 400, 24));
        }
        private void Render(List<Finding> findings)
        {
            _findings.AddRange(findings); _completed = DateTime.Now;
            var previous = LoadHistory();
            int delta = previous.Count > 0 ? _score - previous[previous.Count - 1] : 0;
            _evolution = previous.Count == 0 ? "Premier bilan" : delta == 0 ? "Stable depuis le dernier bilan" : (delta > 0 ? "+" : "") + delta + " points depuis le dernier bilan";
            if (_recordHistory) SaveHistory(_score);
            _history = new List<int>(previous) { _score };
            foreach (var finding in findings)
            {
                var row = new FindingRow(finding); row.Click += (s, e) => OpenFinding(finding);
                _cards.Add(row); _rows.Controls.Add(row);
            }
            _log?.Invoke("Bilan santé PC : score " + _score + "/100 (" + Grade + "), " + findings.Count(f => f.Severity == 2) + " prioritaire(s), " + findings.Count(f => f.Severity == 1) + " à surveiller.", findings.Any(f => f.Severity == 2) ? 2 : findings.Any(f => f.Severity == 1) ? 0 : 1);
            SetBusy(false);
        }
        private sealed class FindingRow : Button
        {
            internal readonly Finding Item;
            private bool _hot;
            internal FindingRow(Finding item)
            {
                Item = item; Text = item.Text; AccessibleName = (item.Severity == 2 ? "Prioritaire. " : item.Severity == 1 ? "À surveiller. " : "Conforme. ") + item.Text;
                BackColor = FpsUi.BgMain; Cursor = item.Open == null ? Cursors.Default : Cursors.Hand;
                FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; TabStop = item.Open != null;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                MouseEnter += (s, e) => { _hot = true; Invalidate(); }; MouseLeave += (s, e) => { _hot = false; Invalidate(); };
                GotFocus += (s, e) => Invalidate(); LostFocus += (s, e) => Invalidate();
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; g.Clear(BackColor);
                OnyxDesign.PaintCard(g, ClientRectangle, FpsUi.Card, (Item.Open != null && _hot) || (Focused && ShowFocusCues) ? FpsUi.GoldDim : FpsUi.Border, 10);
                Color color = Item.Severity == 2 ? Bad : Item.Severity == 1 ? Warn : FpsUi.Ok;
                string label = Item.Severity == 2 ? "PRIORITAIRE" : Item.Severity == 1 ? "À SURVEILLER" : "CONFORME";
                using (var brush = new SolidBrush(color)) g.FillEllipse(brush, 18, Height / 2 - 3, 6, 6);
                TextRenderer.DrawText(g, label, FpsUi.Tiny, new Rectangle(34, 0, 102, Height), color, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, Item.Text, FpsUi.Body, new Rectangle(146, 12, Width - 214, Height - 24), FpsUi.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                if (Item.Open != null) TextRenderer.DrawText(g, "↗", FpsUi.H2, new Rectangle(Width - 50, 0, 28, Height), FpsUi.Gold, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
#if BTTEST
        internal static HealthForm Preview(Action<string, int> log) { return new HealthForm(log) { _recordHistory = false }; }
        internal bool BusyForTest { get { return _busy; } }
        internal void ValidateHealthUi()
        {
            if (_score < 0 || _findings.Count == 0) throw new InvalidOperationException("Aucun résultat réel pour le bilan.");
            for (int i = 0; i < _filters.Length; i++)
            {
                _filters[i].PerformClick();
                if (_cards.Count(c => c.Visible) != _findings.Count(Matches)) throw new InvalidOperationException("Filtre santé incorrect.");
            }
            _filters[0].PerformClick();
            _results.ValidateScrolling();
            if (_results.Bottom > _btnScan.Top || !ClientRectangle.Contains(_summary.Bounds)) throw new InvalidOperationException("Chevauchement dans le bilan.");
        }
        internal void PreviewFilter(int filter) { _filters[filter].PerformClick(); }
#endif
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

        private static string HistoryPath()
        {
            return AppPaths.File("bt-health-history.txt");
        }

        internal static List<int> LoadHistory()
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
                    if (parts.Length >= 2 && int.TryParse(parts[1], out v) && v >= 0 && v <= 100) scores.Add(v);
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
