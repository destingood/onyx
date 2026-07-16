using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    internal class MainForm : Form
    {
        private readonly List<Tweak> _tweaks;
        private readonly List<CheckBox> _boxes = new List<CheckBox>();
        private readonly Dictionary<string, string> _baseText = new Dictionary<string, string>();

        private CheckBox _chkBackup;
        private CheckBox _chkPoint;
        private CheckBox _chkTimer;
        private RichTextBox _log;
        private Button _btnReco, _btnEsport, _btnAll, _btnNone, _btnRestore;
        private Button _btnApply, _btnRevert, _btnOpen, _btnReport, _btnMeasure, _btnLatency;
        private Label _lblCount, _lblTimerRes;
        private NotifyIcon _tray;
        private Timer _uiTimer;
        private bool _trayTipShown;
        private BenchResult _lastBench;

        private static readonly Color Accent    = Color.FromArgb(0, 150, 90);
        private static readonly Color HeaderBg  = Color.FromArgb(28, 30, 38);
        private static readonly Color ColInfo   = Color.FromArgb(110, 115, 125);
        private static readonly Color ColOk     = Color.FromArgb(0, 150, 90);
        private static readonly Color ColWarn   = Color.FromArgb(200, 130, 0);
        private static readonly Color ColErr    = Color.FromArgb(200, 40, 40);
        private static readonly Color ColActive = Color.FromArgb(0, 130, 0);

        public MainForm()
        {
            _tweaks = Catalog.All();
            BuildUi();
            Log("Système : " + Sys.OsDescription(), 0);
            if (!Sys.SameUser)
                Log("Élévation via un autre compte détectée : les réglages utilisateur visent bien le profil connecté.", 2);
            Log("Prêt. Aucune modification n'est faite avant de cliquer sur APPLIQUER.", 0);
            RefreshStates();
        }

        // ------------------------------------------------------------------
        //  Construction de l'interface
        // ------------------------------------------------------------------
        private void BuildUi()
        {
            Text = "BT Optimizer 4.1 — Latence, input lag & rapidité (Windows 10/11)";
            ClientSize = new Size(900, 748);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            Font = new Font("Segoe UI", 9f);
            BackColor = Color.FromArgb(245, 246, 248);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            // En-tête
            var header = new Panel();
            header.SetBounds(0, 0, 900, 62);
            header.BackColor = HeaderBg;

            var title = new Label();
            title.Text = "BT Optimizer";
            title.SetBounds(16, 8, 400, 30);
            title.Font = new Font("Segoe UI Semibold", 15f);
            title.ForeColor = Color.White;
            title.BackColor = HeaderBg;

            var sub = new Label();
            sub.Text = "Cochez les optimisations, survolez pour les détails, puis Appliquer. Tout est réversible (sauvegarde .reg automatique).";
            sub.SetBounds(18, 38, 860, 18);
            sub.Font = new Font("Segoe UI", 8.5f);
            sub.ForeColor = Color.FromArgb(170, 175, 185);
            sub.BackColor = HeaderBg;

            _lblCount = new Label();
            _lblCount.Text = "";
            _lblCount.SetBounds(640, 12, 244, 34);
            _lblCount.Font = new Font("Segoe UI Semibold", 10f);
            _lblCount.ForeColor = Color.FromArgb(0, 210, 130);
            _lblCount.BackColor = HeaderBg;
            _lblCount.TextAlign = ContentAlignment.MiddleRight;

            header.Controls.Add(title);
            header.Controls.Add(sub);
            header.Controls.Add(_lblCount);

            // Presets
            _btnReco = MakeButton("Preset : Recommandé", 16, 70, 160, 30, false);
            _btnEsport = MakeButton("Preset : eSport", 182, 70, 130, 30, false);
            _btnAll = MakeButton("Tout cocher", 318, 70, 110, 30, false);
            _btnNone = MakeButton("Tout décocher", 434, 70, 120, 30, false);
            _btnMeasure = MakeButton("Mesurer latence", 560, 70, 134, 30, false);
            _btnMeasure.ForeColor = Accent;
            _btnRestore = MakeButton("Restaurer une sauvegarde...", 700, 70, 184, 30, false);

            // Zone déroulante des optimisations
            var panel = new Panel();
            panel.SetBounds(16, 108, 868, 388);
            panel.AutoScroll = true;
            panel.BackColor = Color.White;
            panel.BorderStyle = BorderStyle.FixedSingle;

            var tip = new ToolTip();
            tip.AutoPopDelay = 20000;
            tip.InitialDelay = 350;

            int y = 8;
            foreach (string category in Cat.Order)
            {
                List<Tweak> items = _tweaks.Where(t => t.Category == category).ToList();
                if (items.Count == 0) continue;

                var gb = new GroupBox();
                gb.Text = category;
                gb.SetBounds(8, y, 826, 30 + items.Count * 24);
                gb.Font = new Font("Segoe UI Semibold", 9f);
                gb.ForeColor = Color.FromArgb(50, 70, 130);

                int i = 0;
                foreach (Tweak t in items)
                {
                    string text = t.Name;
                    if (t.Reboot) text += "  (redémarrage requis)";

                    var cb = new CheckBox();
                    cb.Text = text;
                    cb.SetBounds(12, 20 + i * 24, 790, 22);
                    cb.Font = new Font("Segoe UI", 9f);
                    cb.ForeColor = SystemColors.ControlText;
                    cb.Tag = t;
                    cb.Checked = t.Recommended;
                    tip.SetToolTip(cb, t.Desc);

                    _baseText[t.Id] = text;
                    _boxes.Add(cb);
                    gb.Controls.Add(cb);
                    i++;
                }
                panel.Controls.Add(gb);
                y += gb.Height + 8;
            }

            // Options
            _chkBackup = new CheckBox();
            _chkBackup.Text = "Sauvegarder le registre avant modification (.reg sur le Bureau)";
            _chkBackup.SetBounds(16, 504, 430, 22);
            _chkBackup.Checked = true;

            _chkPoint = new CheckBox();
            _chkPoint.Text = "Créer un point de restauration système";
            _chkPoint.SetBounds(460, 504, 300, 22);
            _chkPoint.Checked = true;

            _chkTimer = new CheckBox();
            _chkTimer.Text = "Forcer le timer Windows à 1 ms tant que l'app est ouverte (réduire l'app la garde active en zone de notification)";
            _chkTimer.SetBounds(16, 528, 620, 22);
            _chkTimer.CheckedChanged += OnTimerToggled;

            _lblTimerRes = new Label();
            _lblTimerRes.SetBounds(640, 528, 244, 22);
            _lblTimerRes.TextAlign = ContentAlignment.MiddleRight;
            _lblTimerRes.ForeColor = ColInfo;

            // Boutons d'action
            _btnApply = MakeButton("APPLIQUER LA SÉLECTION", 16, 558, 268, 44, true);
            _btnApply.Font = new Font("Segoe UI Semibold", 10.5f);
            _btnRevert = MakeButton("Rétablir (sélection)", 292, 558, 176, 44, false);
            _btnOpen = MakeButton("Sauvegardes", 476, 558, 104, 44, false);
            _btnReport = MakeButton("Rapport", 588, 558, 96, 44, false);
            _btnLatency = MakeButton("Analyse latence", 692, 558, 192, 44, false);
            _btnLatency.ForeColor = Accent;

            // Journal
            _log = new RichTextBox();
            _log.SetBounds(16, 612, 868, 122);
            _log.ReadOnly = true;
            _log.BackColor = Color.White;
            _log.Font = new Font("Consolas", 8.5f);
            _log.BorderStyle = BorderStyle.FixedSingle;

            _btnReco.Click += (s, e) => ApplyPreset(t => t.Recommended);
            _btnEsport.Click += (s, e) => ApplyPreset(t => t.Esport);
            _btnAll.Click += (s, e) => ApplyPreset(t => true);
            _btnNone.Click += (s, e) => ApplyPreset(t => false);
            _btnApply.Click += OnApplyClicked;
            _btnRevert.Click += OnRevertClicked;
            _btnRestore.Click += OnRestoreClicked;
            _btnOpen.Click += OnOpenClicked;
            _btnReport.Click += OnReportClicked;
            _btnMeasure.Click += OnMeasureClicked;
            _btnLatency.Click += OnLatencyClicked;
            FormClosing += OnFormClosingCleanup;
            Resize += OnResizeToTray;

            // Zone de notification : réduire la fenêtre garde l'app (et le timer 1 ms) active.
            _tray = new NotifyIcon();
            try { _tray.Icon = Icon; } catch { }
            _tray.Text = "BT Optimizer";
            _tray.Visible = false;
            _tray.DoubleClick += (s, e) => RestoreFromTray();
            var trayMenu = new ContextMenu();
            trayMenu.MenuItems.Add("Ouvrir BT Optimizer", (s, e) => RestoreFromTray());
            trayMenu.MenuItems.Add("Quitter", (s, e) => { _tray.Visible = false; Close(); });
            _tray.ContextMenu = trayMenu;

            // Rafraîchit l'affichage de la résolution timer réelle toutes les 2 s.
            _uiTimer = new Timer();
            _uiTimer.Interval = 2000;
            _uiTimer.Tick += (s, e) => UpdateTimerLabel();
            _uiTimer.Start();
            UpdateTimerLabel();

            Controls.AddRange(new Control[]
            {
                header, _btnReco, _btnEsport, _btnAll, _btnNone, _btnMeasure, _btnRestore,
                panel, _chkBackup, _chkPoint, _chkTimer, _lblTimerRes,
                _btnApply, _btnRevert, _btnOpen, _btnReport, _btnLatency, _log
            });
        }

        private void OnFormClosingCleanup(object sender, FormClosingEventArgs e)
        {
            Native.SetTimer1ms(false);
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        }

        private void OnResizeToTray(object sender, EventArgs e)
        {
            if (WindowState != FormWindowState.Minimized) return;
            Hide();
            _tray.Visible = true;
            if (!_trayTipShown)
            {
                _trayTipShown = true;
                string tip = Native.TimerActive
                    ? "Toujours actif — le timer 1 ms reste maintenu. Double-clic pour rouvrir."
                    : "Toujours actif en arrière-plan. Double-clic pour rouvrir.";
                _tray.ShowBalloonTip(2500, "BT Optimizer", tip, ToolTipIcon.Info);
            }
        }

        private void RestoreFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            _tray.Visible = false;
        }

        private void UpdateTimerLabel()
        {
            double ms = Native.CurrentTimerMs();
            if (ms <= 0) { _lblTimerRes.Text = ""; return; }
            _lblTimerRes.Text = "Timer système actuel : " + ms.ToString("0.0") + " ms";
            _lblTimerRes.ForeColor = (ms <= 1.05) ? ColOk : ColInfo;
        }

        private static Button MakeButton(string text, int x, int y, int w, int h, bool primary)
        {
            var b = new Button();
            b.Text = text;
            b.SetBounds(x, y, w, h);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = primary ? 0 : 1;
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            b.BackColor = primary ? Accent : Color.White;
            b.ForeColor = primary ? Color.White : Color.FromArgb(40, 44, 52);
            b.UseVisualStyleBackColor = false;
            return b;
        }

        // ------------------------------------------------------------------
        //  Journal (utilisable depuis un thread de fond)
        // ------------------------------------------------------------------
        private void Log(string message, int level)
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action)(() => Log(message, level)));
                return;
            }
            Color c = ColInfo;
            string tag = "INFO";
            if (level == 1) { c = ColOk; tag = "OK  "; }
            else if (level == 2) { c = ColWarn; tag = "ATT."; }
            else if (level == 3) { c = ColErr; tag = "ERR "; }

            string line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] [" + tag + "] " + message + Environment.NewLine;
            _log.SelectionStart = _log.TextLength;
            _log.SelectionLength = 0;
            _log.SelectionColor = c;
            _log.AppendText(line);
            _log.ScrollToCaret();
        }

        // ------------------------------------------------------------------
        //  États "déjà actif"
        // ------------------------------------------------------------------
        private void RefreshStates()
        {
            int active = 0;
            foreach (CheckBox cb in _boxes)
            {
                var t = (Tweak)cb.Tag;
                bool? state = null;
                if (t.Check != null)
                {
                    try { state = t.Check(); }
                    catch { state = null; }
                }
                if (state == true)
                {
                    active++;
                    cb.ForeColor = ColActive;
                    cb.Text = _baseText[t.Id] + "   [déjà actif]";
                }
                else
                {
                    cb.ForeColor = SystemColors.ControlText;
                    cb.Text = _baseText[t.Id];
                }
            }
            if (_lblCount != null)
                _lblCount.Text = "Actives : " + active + " / " + _boxes.Count;
        }

        private void ApplyPreset(Func<Tweak, bool> selector)
        {
            foreach (CheckBox cb in _boxes)
                cb.Checked = selector((Tweak)cb.Tag);
        }

        private List<Tweak> Selection()
        {
            return _boxes.Where(cb => cb.Checked).Select(cb => (Tweak)cb.Tag).ToList();
        }

        // ------------------------------------------------------------------
        //  Actions
        // ------------------------------------------------------------------
        private void OnApplyClicked(object sender, EventArgs e)
        {
            List<Tweak> sel = Selection();
            if (sel.Count == 0)
            {
                MessageBox.Show(this, "Aucune optimisation cochée.", "BT Optimizer",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string msg = "Appliquer " + sel.Count + " optimisation(s) ?";
            if (_chkBackup.Checked)
                msg += "\n\nUne sauvegarde du registre sera créée sur le Bureau.";
            if (MessageBox.Show(this, msg, "Confirmation",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            RunOperation(sel, true);
        }

        private void OnRevertClicked(object sender, EventArgs e)
        {
            List<Tweak> sel = Selection();
            if (sel.Count == 0)
            {
                MessageBox.Show(this, "Cochez les optimisations à rétablir.", "BT Optimizer",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string msg = "Remettre les valeurs par défaut de Windows pour " + sel.Count + " réglage(s) coché(s) ?";
            if (MessageBox.Show(this, msg, "Rétablir",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            RunOperation(sel, false);
        }

        private void RunOperation(List<Tweak> sel, bool apply)
        {
            SetBusy(true);
            bool doBackup = apply && _chkBackup.Checked;
            bool doPoint = apply && _chkPoint.Checked;
            Task.Run(() =>
            {
                EngineResult res = Engine.Run(sel, apply, doBackup, doPoint, Log);
                BeginInvoke((Action)(() => OnOperationDone(res)));
            });
        }

        private void OnOperationDone(EngineResult res)
        {
            SetBusy(false);
            RefreshStates();
            if (res.PrepFailed)
            {
                MessageBox.Show(this,
                    "Une erreur est survenue avant l'application :\n\n" + res.PrepError,
                    "BT Optimizer", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else if (res.RebootNeeded)
            {
                MessageBox.Show(this,
                    "Certaines modifications nécessitent un redémarrage pour prendre effet.",
                    "Redémarrage conseillé", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void SetBusy(bool busy)
        {
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            Button[] buttons = { _btnApply, _btnRevert, _btnRestore, _btnReco, _btnEsport, _btnAll, _btnNone, _btnMeasure, _btnReport, _btnLatency };
            foreach (Button b in buttons) b.Enabled = !busy;
            _chkTimer.Enabled = !busy;
        }

        // ------------------------------------------------------------------
        //  Mesure de latence (native 12 s + capture ETW xperf optionnelle)
        // ------------------------------------------------------------------
        private void OnMeasureClicked(object sender, EventArgs e)
        {
            bool doEtw = false;
            if (Bench.EtwAvailable(Application.StartupPath))
            {
                doEtw = MessageBox.Show(this,
                    "Lancer AUSSI la capture ETW DPC/ISR de 30 s (méthode xperf, comme tes scripts) ?\n\n" +
                    "Oui  = mesure rapide 12 s + capture ETW 30 s\nNon = mesure rapide 12 s seulement",
                    "Mesure de latence", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
            }
            Log("Mesure en cours — évite d'utiliser le PC pendant la mesure...", 0);
            SetBusy(true);
            string appDir = Application.StartupPath;
            bool etw = doEtw;
            Task.Run(() =>
            {
                BenchResult r = Bench.Run(10, Log);
                string etwReport = etw ? Bench.RunEtw(appDir, Log) : null;
                BeginInvoke((Action)(() => OnMeasureDone(r, etwReport)));
            });
        }

        private void OnMeasureDone(BenchResult r, string etwReport)
        {
            SetBusy(false);
            foreach (string line in r.ToText().Split(new[] { Environment.NewLine, "\n" }, StringSplitOptions.RemoveEmptyEntries))
                Log(line, 1);
            if (_lastBench != null)
            {
                Log(string.Format(
                    "Δ vs mesure précédente — timer : {0:+0.0;-0.0;0} ms, Sleep(1) max : {1:+0.00;-0.00;0} ms, %DPC moy : {2:+0.00;-0.00;0}",
                    r.TimerMs - _lastBench.TimerMs,
                    r.SleepMaxMs - _lastBench.SleepMaxMs,
                    (r.DpcAvg >= 0 && _lastBench.DpcAvg >= 0) ? r.DpcAvg - _lastBench.DpcAvg : 0), 2);
            }
            _lastBench = r;
            try
            {
                string hist = System.IO.Path.Combine(Sys.BackupDesktop, "bt-optimizer-mesures.txt");
                System.IO.File.AppendAllText(hist, r.ToText() + Environment.NewLine, System.Text.Encoding.UTF8);
                Log("Mesure ajoutée à l'historique : " + hist, 0);
            }
            catch { }
            if (etwReport != null)
            {
                Log("Rapport ETW DPC/ISR : " + etwReport, 1);
                ShowLatency(etwReport);
            }
        }

        private void ShowLatency(string reportPath)
        {
            try
            {
                DpcIsrReport rep = DpcIsrReport.Parse(reportPath);
                Log(string.Format("Analyse : verdict « {0} », pire DPC ≤ {1:0} µs ({2}), pire ISR ≤ {3:0} µs ({4}).",
                    rep.VerdictTitle, rep.MaxDpcUs, rep.MaxDpcModule, rep.MaxIsrUs, rep.MaxIsrModule),
                    rep.VerdictLevel == 1 ? 1 : 2);
                using (var f = new LatencyForm(rep)) f.ShowDialog(this);
            }
            catch (Exception ex)
            {
                Log("Analyse impossible (" + ex.Message + "). Ouverture du texte brut.", 2);
                try { Process.Start("notepad.exe", "\"" + reportPath + "\""); } catch { }
            }
        }

        private void OnLatencyClicked(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = "Rapports DPC/ISR xperf (*.txt)|*.txt|Tous les fichiers|*.*";
                string tools = System.IO.Path.Combine(Application.StartupPath, "tools");
                if (System.IO.Directory.Exists(tools))
                {
                    dlg.InitialDirectory = tools;
                    // Présélectionne le rapport le plus récent s'il y en a.
                    var files = new System.IO.DirectoryInfo(tools).GetFiles("dpcisr-*.txt");
                    if (files.Length > 0)
                    {
                        Array.Sort(files, (a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));
                        dlg.FileName = files[0].Name;
                    }
                }
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    ShowLatency(dlg.FileName);
            }
        }

        private void OnRestoreClicked(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Choisissez un dossier de sauvegarde bt-optimizer-backup-...";
                dlg.SelectedPath = Sys.BackupDesktop;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (MessageBox.Show(this,
                        "Réimporter toutes les clés .reg depuis :\n" + dlg.SelectedPath + " ?",
                        "Restaurer une sauvegarde",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;
                string folder = dlg.SelectedPath;
                SetBusy(true);
                Task.Run(() =>
                {
                    Sys.ImportBackup(folder, Log);
                    BeginInvoke((Action)(() => { SetBusy(false); RefreshStates(); }));
                });
            }
        }

        private void OnOpenClicked(object sender, EventArgs e)
        {
            try { Process.Start("explorer.exe", "\"" + Sys.BackupDesktop + "\""); }
            catch (Exception ex) { Log("Impossible d'ouvrir le dossier : " + ex.Message, 3); }
        }

        private void OnReportClicked(object sender, EventArgs e)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("===== RAPPORT BT OPTIMIZER 4.0 =====");
                sb.AppendLine("Date    : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("Système : " + Sys.OsDescription());
                sb.AppendLine("Timer   : " + Native.CurrentTimerMs().ToString("0.0") + " ms");
                sb.AppendLine();
                string currentCategory = null;
                foreach (CheckBox cb in _boxes)
                {
                    var t = (Tweak)cb.Tag;
                    if (t.Category != currentCategory)
                    {
                        currentCategory = t.Category;
                        sb.AppendLine("-- " + currentCategory + " --");
                    }
                    bool? state = null;
                    if (t.Check != null)
                    {
                        try { state = t.Check(); } catch { state = null; }
                    }
                    string tag = state.HasValue ? (state.Value ? "ACTIF  " : "inactif") : "  ?    ";
                    sb.AppendLine("  [" + tag + "] " + t.Name);
                }
                string path = System.IO.Path.Combine(Sys.BackupDesktop, "bt-optimizer-rapport.txt");
                System.IO.File.WriteAllText(path, sb.ToString(), System.Text.Encoding.UTF8);
                Log("Rapport enregistré : " + path, 1);
                Process.Start("notepad.exe", "\"" + path + "\"");
            }
            catch (Exception ex)
            {
                Log("Rapport impossible : " + ex.Message, 3);
            }
        }

        private void OnTimerToggled(object sender, EventArgs e)
        {
            Native.SetTimer1ms(_chkTimer.Checked);
            if (_chkTimer.Checked)
                Log("Timer Windows forcé à 1 ms (actif tant que la fenêtre reste ouverte).", 1);
            else
                Log("Timer Windows rendu au système.", 0);
        }
    }
}
