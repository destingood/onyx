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
        private CheckBox _chkAutoTimer;
        private CheckBox _chkGuard;
        private bool _guardEventSuppressed;
        private RichTextBox _log;
        private Button _btnReco, _btnEsport, _btnAll, _btnNone, _btnRestore;
        private Button _btnApply, _btnRevert, _btnOpen, _btnReport, _btnMeasure, _btnLatency;
        private Button _btnMonitor, _btnAutoCompare, _btnOverclock, _btnDns;
        private Button _btnAuto, _btnBench, _btnMenu, _btnBoost;
        private ContextMenuStrip _menu;
        private ToolStripMenuItem _miPro;
        private TextBox _search;
        private readonly List<GroupBox> _groups = new List<GroupBox>();
        private HwProfile _hw;
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
            Log("Édition : " + License.Status(), License.IsPro ? 1 : 0);
            UpdateProUi();
            try { _hw = Hardware.Detect(); Log("Matériel : " + _hw.Summary(), 0); } catch { }
            if (!Sys.SameUser)
                Log("Élévation via un autre compte détectée : les réglages utilisateur visent bien le profil connecté.", 2);
            Log("Prêt. Aucune modification n'est faite avant de cliquer sur APPLIQUER.", 0);
            RefreshStates();
            Theme.Apply(this);
            Shown += OnShownWelcome;
        }

        private void OnShownWelcome(object sender, EventArgs e)
        {
            Shown -= OnShownWelcome;
            if (WelcomeForm.AlreadyShown) return;
            WelcomeForm.MarkShown();
            ShowWelcome();
        }

        private void ShowWelcome()
        {
            WelcomeForm.StartAction choice;
            using (var w = new WelcomeForm()) { w.ShowDialog(this); choice = w.Choice; }

            if (choice == WelcomeForm.StartAction.ApplyRecommended)
            {
                ApplyPreset(t => t.Recommended);
                List<Tweak> sel = Selection();
                if (sel.Count > 0)
                {
                    Log("Optimisation automatique (réglages recommandés)...", 0);
                    RunOperation(sel, true);
                }
            }
            else if (choice == WelcomeForm.StartAction.StartTrial)
            {
                using (var f = new LicenseKeyForm("")) f.ShowDialog(this);
                UpdateProUi();
            }
        }

        // ------------------------------------------------------------------
        //  Construction de l'interface
        // ------------------------------------------------------------------
        private void BuildUi()
        {
            Text = "BT Optimizer 7.6 — Latence, input lag, rapidité, overclock & DNS (Windows 10/11)";
            ClientSize = new Size(900, 800);
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

            _btnBoost = new Button();
            _btnBoost.Text = "▶ MODE JEU";
            _btnBoost.SetBounds(700, 12, 142, 38);
            _btnBoost.FlatStyle = FlatStyle.Flat;
            _btnBoost.FlatAppearance.BorderSize = 0;
            _btnBoost.BackColor = Color.FromArgb(0, 150, 90);
            _btnBoost.ForeColor = Color.White;
            _btnBoost.Font = new Font("Segoe UI Semibold", 9.5f);
            _btnBoost.Click += OnBoostToggle;

            _btnMenu = new Button();
            _btnMenu.Text = "☰";
            _btnMenu.SetBounds(850, 12, 36, 38);
            _btnMenu.FlatStyle = FlatStyle.Flat;
            _btnMenu.FlatAppearance.BorderColor = Color.FromArgb(70, 74, 84);
            _btnMenu.BackColor = HeaderBg;
            _btnMenu.ForeColor = Color.White;
            _btnMenu.Font = new Font("Segoe UI", 12f);
            _btnMenu.Click += (s, e) => _menu.Show(_btnMenu, new Point(0, _btnMenu.Height));

            _menu = new ContextMenuStrip();
            _menu.Items.Add("À propos de BT Optimizer", null, (s, e) => { using (var f = new AboutForm()) f.ShowDialog(this); });
            _miPro = new ToolStripMenuItem("Activer la version Pro / entrer une clé", null, (s, e) =>
            {
                using (var f = new LicenseKeyForm("")) f.ShowDialog(this);
                UpdateProUi();
            });
            _menu.Items.Add(_miPro);
            _menu.Items.Add("Guide de démarrage", null, (s, e) => ShowWelcome());
            _menu.Items.Add("Conditions d'utilisation", null, (s, e) => { using (var f = new LicenseForm()) f.ShowDialog(this); });
            var miDark = new ToolStripMenuItem("Thème sombre", null, (s, e) =>
            {
                Theme.Toggle();
                ((ToolStripMenuItem)s).Checked = Theme.Dark;
                Theme.Apply(this);
            });
            miDark.Checked = Theme.Dark;
            _menu.Items.Add(miDark);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add("Composants & diagnostic du système...", null, (s, e) => { using (var f = new SystemInfoForm(Log)) f.ShowDialog(this); });
            _menu.Items.Add("Programmes au démarrage...", null, (s, e) => { using (var f = new StartupForm(Log)) f.ShowDialog(this); });
            _menu.Items.Add("Services Windows...", null, (s, e) => { using (var f = new ServicesForm(Log)) f.ShowDialog(this); });
            _menu.Items.Add("Libérer la mémoire (RAM) maintenant", null, (s, e) =>
            {
                Log("Nettoyage de la mémoire...", 0);
                System.Threading.Tasks.Task.Run(() => Sys.CleanMemory(Log));
            });
            _menu.Items.Add("Réparer le réseau (Winsock / TCP-IP)...", null, (s, e) =>
            {
                if (MessageBox.Show(this,
                        "Réinitialiser la connexion réseau ?\n\n"
                        + "Vide le cache DNS, réinitialise Winsock et la pile TCP/IP.\n"
                        + "Règle la plupart des problèmes de connexion. Un REDÉMARRAGE sera nécessaire.",
                        "Réparer le réseau", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                    return;
                Log("Réparation réseau...", 0);
                System.Threading.Tasks.Task.Run(() => Sys.NetworkRepair(Log));
            });
            _menu.Items.Add("Nettoyage disque (fichiers temporaires)...", null, (s, e) => { using (var f = new CleanupForm(Log)) f.ShowDialog(this); });
            _menu.Items.Add("Réinitialiser TOUTES les optimisations (valeurs Windows)", null, OnResetAll);
            _menu.Items.Add("Ouvrir le dossier des sauvegardes", null, (s, e) => OnOpenClicked(s, e));
            _menu.Items.Add("Ouvrir le journal (fichier)", null, (s, e) =>
            {
                try
                {
                    string p = System.IO.Path.Combine(Application.StartupPath, "bt-optimizer-log.txt");
                    if (System.IO.File.Exists(p)) Process.Start("notepad.exe", "\"" + p + "\"");
                    else MessageBox.Show(this, "Aucun journal fichier pour l'instant.", "BT Optimizer", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch { }
            });

            _lblCount = new Label();
            _lblCount.Text = "";
            _lblCount.SetBounds(400, 6, 288, 26);
            _lblCount.Font = new Font("Segoe UI Semibold", 10f);
            _lblCount.ForeColor = Color.FromArgb(0, 210, 130);
            _lblCount.BackColor = HeaderBg;
            _lblCount.TextAlign = ContentAlignment.MiddleRight;

            _lblTimerRes = new Label();
            _lblTimerRes.SetBounds(400, 32, 288, 24);
            _lblTimerRes.TextAlign = ContentAlignment.MiddleRight;
            _lblTimerRes.BackColor = HeaderBg;
            _lblTimerRes.ForeColor = Color.FromArgb(165, 170, 180);
            _lblTimerRes.Font = new Font("Segoe UI", 9f);

            header.Controls.Add(title);
            header.Controls.Add(sub);
            header.Controls.Add(_lblCount);
            header.Controls.Add(_lblTimerRes);
            header.Controls.Add(_btnBoost);
            header.Controls.Add(_btnMenu);
            _btnBoost.BringToFront();
            _btnMenu.BringToFront();

            // Presets
            _btnReco = MakeButton("Preset : Recommandé", 16, 70, 160, 30, false);
            _btnEsport = MakeButton("Preset : eSport", 182, 70, 130, 30, false);
            _btnAll = MakeButton("Tout cocher", 318, 70, 110, 30, false);
            _btnNone = MakeButton("Tout décocher", 434, 70, 120, 30, false);
            _btnMeasure = MakeButton("Mesurer latence", 560, 70, 134, 30, false);
            _btnMeasure.ForeColor = Accent;
            _btnRestore = MakeButton("Restaurer une sauvegarde...", 700, 70, 184, 30, false);

            // Ligne : presets intelligents + recherche
            _btnAuto = MakeButton("Auto (adapté à mon PC)", 16, 104, 178, 28, false);
            _btnAuto.ForeColor = Accent;
            _btnBench = MakeButton("Preset : Benchmark", 200, 104, 150, 28, false);
            _search = new TextBox();
            _search.SetBounds(360, 105, 524, 26);
            _search.PlaceholderText = "Rechercher une optimisation (nom, catégorie, description)...";
            _search.TextChanged += (s, e) => FilterTweaks(_search.Text);

            // Zone déroulante des optimisations
            var panel = new Panel();
            panel.SetBounds(16, 138, 868, 358);
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
                _groups.Add(gb);
                panel.Controls.Add(gb);
                y += gb.Height + 8;
            }

            // Options
            _chkBackup = new CheckBox();
            _chkBackup.Text = "Sauvegarde .reg avant modification";
            _chkBackup.SetBounds(16, 504, 240, 22);
            _chkBackup.Checked = true;

            _chkPoint = new CheckBox();
            _chkPoint.Text = "Point de restauration système";
            _chkPoint.SetBounds(262, 504, 210, 22);
            _chkPoint.Checked = true;

            _chkGuard = new CheckBox();
            _chkGuard.Text = "GARDIEN : ré-appliquer mon profil à chaque démarrage (tâche planifiée)";
            _chkGuard.SetBounds(478, 504, 406, 22);
            _chkGuard.Checked = Sys.GuardExists();
            _chkGuard.CheckedChanged += OnGuardToggled;

            _chkTimer = new CheckBox();
            _chkTimer.Text = "Timer Windows 1 ms tant que l'app est ouverte (actif aussi réduite en zone de notification)";
            _chkTimer.SetBounds(16, 528, 550, 22);
            _chkTimer.CheckedChanged += OnTimerToggled;

            _chkAutoTimer = new CheckBox();
            _chkAutoTimer.Text = "Timer 1 ms AUTO dès qu'un jeu plein écran est détecté";
            _chkAutoTimer.SetBounds(572, 528, 312, 22);
            _chkAutoTimer.CheckedChanged += OnTimerToggled;

            // Boutons d'action
            _btnApply = MakeButton("APPLIQUER LA SÉLECTION", 16, 558, 268, 44, true);
            _btnApply.Font = new Font("Segoe UI Semibold", 10.5f);
            _btnRevert = MakeButton("Rétablir (sélection)", 292, 558, 176, 44, false);
            _btnOpen = MakeButton("Sauvegardes", 476, 558, 104, 44, false);
            _btnReport = MakeButton("Rapport", 588, 558, 96, 44, false);
            _btnLatency = MakeButton("Analyse latence", 692, 558, 192, 44, false);
            _btnLatency.ForeColor = Accent;

            // Ligne outils
            _btnMonitor = MakeButton("Moniteur matériel", 16, 610, 210, 32, false);
            _btnMonitor.ForeColor = Accent;
            _btnOverclock = MakeButton("Overclock auto", 234, 610, 150, 32, false);
            _btnOverclock.ForeColor = Color.FromArgb(180, 70, 20);
            _btnDns = MakeButton("DNS rapide", 392, 610, 130, 32, false);
            _btnDns.ForeColor = Accent;
            _btnAutoCompare = MakeButton("Comparer les 2 dernières mesures", 530, 610, 354, 32, false);

            // Journal
            _log = new RichTextBox();
            _log.SetBounds(16, 650, 868, 138);
            _log.ReadOnly = true;
            _log.BackColor = Color.White;
            _log.Font = new Font("Consolas", 8.5f);
            _log.BorderStyle = BorderStyle.FixedSingle;

            _btnReco.Click += (s, e) => ApplyPreset(t => t.Recommended);
            _btnEsport.Click += (s, e) => { if (RequirePro("Preset eSport")) ApplyPreset(t => t.Esport); };
            _btnAll.Click += (s, e) => ApplyPreset(t => true);
            _btnNone.Click += (s, e) => ApplyPreset(t => false);
            _btnAuto.Click += (s, e) => { if (RequirePro("Auto-tune")) OnAutoTune(s, e); };
            _btnBench.Click += (s, e) =>
            {
                if (!RequirePro("Preset Benchmark")) return;
                var ids = Hardware.BenchmarkIds(_tweaks);
                ApplyPreset(t => ids.Contains(t.Id));
                Log("Preset Benchmark : tout sélectionné sauf les tweaks sécurité (Spectre, VBS).", 0);
            };
            _btnApply.Click += OnApplyClicked;
            _btnRevert.Click += OnRevertClicked;
            _btnRestore.Click += OnRestoreClicked;
            _btnOpen.Click += OnOpenClicked;
            _btnReport.Click += OnReportClicked;
            _btnMeasure.Click += OnMeasureClicked;
            _btnLatency.Click += OnLatencyClicked;
            _btnMonitor.Click += (s, e) => { using (var f = new MonitorForm()) f.ShowDialog(this); };
            _btnAutoCompare.Click += OnAutoCompareClicked;
            _btnOverclock.Click += (s, e) => { if (RequirePro("Overclock")) using (var f = new OverclockForm(Log)) f.ShowDialog(this); };
            _btnDns.Click += (s, e) => { if (RequirePro("DNS rapide")) using (var f = new DnsForm(Log)) f.ShowDialog(this); };
            FormClosing += OnFormClosingCleanup;
            Resize += OnResizeToTray;

            // Zone de notification : réduire la fenêtre garde l'app (et le timer 1 ms) active.
            _tray = new NotifyIcon();
            try { _tray.Icon = Icon; } catch { }
            _tray.Text = "BT Optimizer";
            _tray.Visible = false;
            _tray.DoubleClick += (s, e) => RestoreFromTray();
            var trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Ouvrir BT Optimizer", null, (s, e) => RestoreFromTray());
            trayMenu.Items.Add("Quitter", null, (s, e) => { _tray.Visible = false; Close(); });
            _tray.ContextMenuStrip = trayMenu;

            // Toutes les 2 s : résolution timer réelle + détection jeu plein écran (mode AUTO).
            _uiTimer = new Timer();
            _uiTimer.Interval = 2000;
            _uiTimer.Tick += (s, e) => { UpdateTimerState(); UpdateTimerLabel(); };
            _uiTimer.Start();
            UpdateTimerLabel();

            Controls.AddRange(new Control[]
            {
                header, _btnReco, _btnEsport, _btnAll, _btnNone, _btnMeasure, _btnRestore,
                _btnAuto, _btnBench, _search,
                panel, _chkBackup, _chkPoint, _chkGuard, _chkTimer, _chkAutoTimer,
                _btnApply, _btnRevert, _btnOpen, _btnReport, _btnLatency,
                _btnMonitor, _btnOverclock, _btnDns, _btnAutoCompare, _log
            });
        }

        private void OnFormClosingCleanup(object sender, FormClosingEventArgs e)
        {
            if (GameBoost.IsActive) GameBoost.Deactivate(delegate (string m, int l) { });
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

        /// <summary>Renvoie true si Pro (ou si l'utilisateur active une licence à l'instant), sinon false.</summary>
        private bool RequirePro(string feature)
        {
            if (License.ProUnlocked) return true;
            using (var f = new LicenseKeyForm(feature)) f.ShowDialog(this);
            if (License.ProUnlocked) { UpdateProUi(); return true; }
            return false;
        }

        private void UpdateProUi()
        {
            if (_miPro == null) return;
            if (License.IsPro) _miPro.Text = "Édition Pro active (" + License.Licensee + ")";
            else if (License.TrialActive) _miPro.Text = "Essai Pro — " + License.TrialDaysLeft + " j restants · entrer une clé";
            else _miPro.Text = "Activer la version Pro / essai gratuit";
        }

        private void OnAutoTune(object sender, EventArgs e)
        {
            if (_hw == null) _hw = Hardware.Detect();
            var ids = Hardware.AutoTuneIds(_tweaks, _hw);
            ApplyPreset(t => ids.Contains(t.Id));
            Log("Auto-tune : " + _hw.Summary(), 0);
            Log("Sélection adaptée : " + ids.Count + " optimisation(s)"
                + (_hw.AllSsd ? " (SSD détecté → tweaks disque inclus)" : " (HDD présent → SysMain/Prefetch exclus)")
                + ". Sécurité et expérimental laissés à ton choix.", 1);
        }

        private void FilterTweaks(string query)
        {
            string q = (query ?? "").Trim().ToLowerInvariant();
            int py = 8;
            foreach (GroupBox gb in _groups)
            {
                int cy = 20, visible = 0;
                foreach (Control c in gb.Controls)
                {
                    CheckBox cb = c as CheckBox;
                    if (cb == null) continue;
                    var t = (Tweak)cb.Tag;
                    bool match = q.Length == 0
                        || (t.Name != null && t.Name.ToLowerInvariant().Contains(q))
                        || (t.Category != null && t.Category.ToLowerInvariant().Contains(q))
                        || (t.Desc != null && t.Desc.ToLowerInvariant().Contains(q));
                    cb.Visible = match;
                    if (match) { cb.Top = cy; cy += 24; visible++; }
                }
                gb.Visible = visible > 0;
                if (visible > 0) { gb.Height = cy + 6; gb.Top = py; py += gb.Height + 8; }
            }
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
            Button[] buttons = { _btnApply, _btnRevert, _btnRestore, _btnReco, _btnEsport, _btnAll, _btnNone, _btnMeasure, _btnReport, _btnLatency, _btnMonitor, _btnAutoCompare, _btnOverclock, _btnDns, _btnAuto, _btnBench, _btnBoost };
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

        private void OnAutoCompareClicked(object sender, EventArgs e)
        {
            string tools = System.IO.Path.Combine(Application.StartupPath, "tools");
            if (!System.IO.Directory.Exists(tools))
            {
                MessageBox.Show(this, "Aucun dossier « tools ». Lance d'abord une capture (Mesurer latence → ETW).",
                    "BT Optimizer", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var files = new System.IO.DirectoryInfo(tools).GetFiles("dpcisr-*.txt");
            if (files.Length < 2)
            {
                MessageBox.Show(this, "Il faut au moins 2 rapports DPC/ISR dans « tools » pour comparer.\n" +
                    "Fais deux captures (Mesurer latence → Oui à l'ETW), avant et après tes changements.",
                    "BT Optimizer", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Array.Sort(files, (a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));
            try
            {
                DpcIsrReport after = DpcIsrReport.Parse(files[0].FullName);   // le plus récent
                DpcIsrReport before = DpcIsrReport.Parse(files[1].FullName);  // le précédent
                Log("Comparaison auto : AVANT " + files[1].Name + "  →  APRÈS " + files[0].Name, 0);
                using (var f = new CompareForm(before, after)) f.ShowDialog(this);
            }
            catch (Exception ex)
            {
                Log("Comparaison impossible : " + ex.Message, 3);
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

        private void OnBoostToggle(object sender, EventArgs e)
        {
            _btnBoost.Enabled = false;
            bool activating = !GameBoost.IsActive;
            Task.Run(() =>
            {
                if (activating) GameBoost.Activate(Log); else GameBoost.Deactivate(Log);
                try { BeginInvoke((Action)(() =>
                {
                    _btnBoost.Enabled = true;
                    if (GameBoost.IsActive)
                    {
                        _btnBoost.Text = "■ MODE JEU ACTIF";
                        _btnBoost.BackColor = Color.FromArgb(200, 60, 40);
                    }
                    else
                    {
                        _btnBoost.Text = "▶ MODE JEU";
                        _btnBoost.BackColor = Color.FromArgb(0, 150, 90);
                    }
                })); } catch { }
            });
        }

        private void OnResetAll(object sender, EventArgs e)
        {
            if (MessageBox.Show(this,
                    "Tout réinitialiser ?\n\n"
                    + "• Rétablit les 62 optimisations aux valeurs par défaut de Windows\n"
                    + "• Retire le gardien de démarrage et l'OC GPU persistant\n"
                    + "• Réinitialise le GPU (power limit / fréquences constructeur)\n\n"
                    + "Utile pour repartir d'un état propre. Un redémarrage peut être nécessaire.",
                    "Réinitialiser toutes les optimisations",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                return;

            SetBusy(true);
            Log("Réinitialisation complète en cours...", 0);
            string exe = Application.ExecutablePath;
            List<Tweak> all = new List<Tweak>(_tweaks);
            Task.Run(() =>
            {
                Engine.Run(all, false, false, false, Log);
                try { Sys.SetGuard(false, exe, Log); } catch { }
                try { Sys.SetOcGuard(false, exe, Log); } catch { }
                try { Sys.ResetGpuLocks(Log); } catch { }
                BeginInvoke((Action)(() =>
                {
                    SetBusy(false);
                    if (_chkGuard != null) { _guardEventSuppressed = true; _chkGuard.Checked = false; _guardEventSuppressed = false; }
                    RefreshStates();
                    Log("Réinitialisation terminée.", 1);
                    MessageBox.Show(this, "Toutes les optimisations ont été rétablies aux valeurs Windows.\nUn redémarrage est conseillé.",
                        "BT Optimizer", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }));
            });
        }

        private void OnReportClicked(object sender, EventArgs e)
        {
            try
            {
                string html = Report.BuildHtml(_tweaks, _hw ?? Hardware.Detect());
                string path = System.IO.Path.Combine(Sys.BackupDesktop, "bt-optimizer-rapport.html");
                System.IO.File.WriteAllText(path, html, new System.Text.UTF8Encoding(false));
                Log("Rapport HTML enregistré : " + path, 1);
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log("Rapport impossible : " + ex.Message, 3);
            }
        }

        private void OnTimerToggled(object sender, EventArgs e)
        {
            UpdateTimerState();
        }

        /// <summary>Timer 1 ms effectif = case manuelle OU (mode AUTO + jeu plein écran détecté).</summary>
        private void UpdateTimerState()
        {
            bool fullscreen = _chkAutoTimer.Checked && Native.IsGameFullscreen();
            bool wanted = _chkTimer.Checked || fullscreen;
            if (wanted == Native.TimerActive) return;
            Native.SetTimer1ms(wanted);
            if (wanted && fullscreen && !_chkTimer.Checked)
                Log("Jeu plein écran détecté → timer 1 ms activé automatiquement.", 1);
            else if (wanted)
                Log("Timer Windows forcé à 1 ms.", 1);
            else
                Log("Timer Windows rendu au système.", 0);
        }

        private void OnGuardToggled(object sender, EventArgs e)
        {
            if (_guardEventSuppressed) return;
            if (_chkGuard.Checked && !License.ProUnlocked)
            {
                if (!RequirePro("Gardien de démarrage"))
                {
                    _guardEventSuppressed = true; _chkGuard.Checked = false; _guardEventSuppressed = false;
                    return;
                }
            }
            if (_chkGuard.Checked)
            {
                List<Tweak> sel = Selection();
                if (sel.Count == 0)
                {
                    MessageBox.Show(this, "Coche d'abord les optimisations à inclure dans ton profil,\npuis active le gardien.",
                        "BT Optimizer", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    _guardEventSuppressed = true;
                    _chkGuard.Checked = false;
                    _guardEventSuppressed = false;
                    return;
                }
                Sys.SaveProfile(sel.Select(t => t.Id).ToList());
                Log("Profil enregistré (" + sel.Count + " optimisation(s)) : " + Sys.ProfilePath, 0);
                if (!Sys.SetGuard(true, Application.ExecutablePath, Log))
                {
                    _guardEventSuppressed = true;
                    _chkGuard.Checked = false;
                    _guardEventSuppressed = false;
                }
            }
            else
            {
                Sys.SetGuard(false, Application.ExecutablePath, Log);
            }
        }
    }
}
