using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
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
        private CheckBox _chkAutoBoost;
        private bool _autoBoostEngaged;   // le MODE JEU a été enclenché automatiquement (à couper seul)
        private bool _boostBusy;          // une (dé)activation est en cours
        private CheckBox _chkGuard;
        private bool _guardEventSuppressed;
        private RichTextBox _log;
        private Button _btnReco, _btnEsport, _btnAll, _btnNone, _btnRestore;
        private Button _btnApply, _btnRevert, _btnOpen, _btnReport, _btnMeasure, _btnLatency;
        private Button _btnMonitor, _btnAutoCompare, _btnOverclock, _btnDns;
        private Button _btnAuto, _btnBench, _btnMenu, _btnBoost, _btnLatMin, _btnFps500;
        private Button _btnOneClick;
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

        private Panel _header;
        private readonly Font _fontBrand = new Font("Segoe UI Semibold", 16f);
        private readonly Font _fontTag = new Font("Segoe UI", 8.5f);
        private readonly Font _fontChip = new Font("Segoe UI Semibold", 7.5f);

        /// <summary>Version courte (« 10.7 ») dérivée de l'assembly : une seule source de vérité.</summary>
        private static readonly string AppVer = ReadVersion();
        private static string ReadVersion()
        {
            try
            {
                Version v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                return v.Major + "." + v.Minor;
            }
            catch { return "?"; }
        }

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
            CheckProfileDriftAtStartup();   // anti-régression : profil annulé par une MAJ Windows ?
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
            Text = "DesTinGOOD Optimizer " + AppVer + " — 500 FPS, latence minimale, input lag, overclock & DNS (Windows 10/11)";
            ClientSize = new Size(900, 868);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            Font = new Font("Segoe UI", 9f);
            BackColor = Color.FromArgb(245, 246, 248);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            // En-tête : marque peinte (wordmark dégradé, puces version/édition, tagline).
            var header = new Panel();
            header.SetBounds(0, 0, 900, 62);
            header.BackColor = HeaderBg;
            _header = header;
            header.Paint += OnPaintHeader;

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
            _menu.Items.Add("À propos de DesTinGOOD", null, (s, e) => { using (var f = new AboutForm()) f.ShowDialog(this); });
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
            // --- Les panneaux vedettes (500 FPS, mesure temps réel) ---
            _menu.Items.Add("🎯 Objectif 500 FPS (écran 500 Hz)...", null, OnFps500Open);
            _menu.Items.Add("📈 FPS EN DIRECT (par jeu, façon PresentMon)...", null,
                (s, e) => { using (var f = new FpsMonForm(Log)) f.ShowDialog(this); });
            _menu.Items.Add("⏱ Latence EN DIRECT (DPC/ISR par pilote, précision LatencyMon)...", null,
                (s, e) => { using (var f = new LiveMonForm(Log)) f.ShowDialog(this); });
            _menu.Items.Add("Guide latence & perf (checklist input lag)...", null, (s, e) => { using (var f = new LatencyGuideForm(Log)) f.ShowDialog(this); });
            _menu.Items.Add(new ToolStripSeparator());
            // --- Outils système ---
            _menu.Items.Add("🏥 SANTÉ DE MON PC — le bilan en un coup d'œil (score /100)...", null,
                (s, e) => { using (var f = new HealthForm(Log)) f.ShowDialog(this); });
            _menu.Items.Add("🧪 Benchmark rapide (puissance CPU / mémoire / disque)...", null,
                (s, e) => { using (var f = new BenchForm(Log)) f.ShowDialog(this); });
            _menu.Items.Add("Composants & diagnostic du système...", null, (s, e) => { using (var f = new SystemInfoForm(Log)) f.ShowDialog(this); });
            _menu.Items.Add("Audio & enceintes (périphériques, améliorations)...", null, (s, e) => { using (var f = new AudioForm(Log)) f.ShowDialog(this); });
            _menu.Items.Add("Gestionnaire de périphériques (détecte les erreurs)...", null, (s, e) => { using (var f = new DeviceManagerForm(Log)) f.ShowDialog(this); });
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
            _menu.Items.Add("🛒 Boutiques qui chargent à l'infini / jeux qui crashent (Steam / Game Pass)...", null,
                (s, e) => { using (var f = new ShopFixForm(Log)) f.ShowDialog(this); });
            _menu.Items.Add("📦 Bibliothèques de jeu manquantes (vcruntime, DirectX...) & applis...", null,
                (s, e) => { using (var f = new LibsForm(Log)) f.ShowDialog(this); });
            _menu.Items.Add("🩺 Stabilité : qu'est-ce qui a planté sur ce PC ? (14 jours)...", null,
                (s, e) => { using (var f = new StabilityForm(Log)) f.ShowDialog(this); });
            _menu.Items.Add("🏁 Prêt pour le match ? (checklist réseau / timer / GPU)...", null,
                (s, e) => { using (var f = new TournamentForm(Log)) f.ShowDialog(this); });
            _menu.Items.Add("🖥️ Réglages d'écran (fréquence max, VRR/G-Sync, HDR)...", null,
                (s, e) => { using (var f = new DisplayForm(Log)) f.ShowDialog(this); });
            _menu.Items.Add("🎮 Priorité CPU par jeu (booste ton jeu principal)...", null,
                (s, e) => { using (var f = new GameProfileForm(Log)) f.ShowDialog(this); });
            _menu.Items.Add("💾 Jeux & disques (SSD/HDD, espace, chargements)...", null,
                (s, e) => { using (var f = new DiskForm(Log)) f.ShowDialog(this); });
            _menu.Items.Add("🔧 Réparer l'intégrité de Windows (DISM + SFC, si crashs persistants)...", null, OnRepairWindows);
            _menu.Items.Add("🖴 Optimiser les lecteurs (TRIM SSD / défrag HDD)...", null, OnOptimizeDrives);
            _menu.Items.Add("🔁 Points de restauration (filet de sécurité système)...", null,
                (s, e) => { using (var f = new RestoreForm(Log)) f.ShowDialog(this); });
            _menu.Items.Add("🧹 Réglages néfastes d'autres optimiseurs (à annuler)...", null,
                (s, e) => { using (var f = new CheckupForm(Log)) f.ShowDialog(this); });
            _menu.Items.Add("🌡️ Températures & throttling (ta carte bride-t-elle ?)...", null,
                (s, e) => { using (var f = new ThermalForm(Log)) f.ShowDialog(this); });
            _menu.Items.Add("🔍 Qui ralentit mon PC ? (processus & logiciels de fond)...", null,
                (s, e) => { using (var f = new BloatForm(Log)) f.ShowDialog(this); });
            _menu.Items.Add("📶 Qualité réseau en jeu (le lag vient de chez toi ou du FAI ?)...", null,
                (s, e) => { using (var f = new NetworkForm(Log)) f.ShowDialog(this); });
            _menu.Items.Add("🖱️ Fréquence réelle de la souris (ton 1000 Hz est-il vrai ?)...", null,
                (s, e) => { using (var f = new MouseForm(Log)) f.ShowDialog(this); });
            _menu.Items.Add("Nettoyage disque (fichiers temporaires)...", null, (s, e) => { using (var f = new CleanupForm(Log)) f.ShowDialog(this); });
            _menu.Items.Add(new ToolStripSeparator());
            // --- Maintenance ---
            _menu.Items.Add("Re-vérifier l'état des optimisations (re-scan)", null,
                (s, e) => { RefreshStates(); Log("États re-vérifiés : les mentions [déjà actif] sont à jour.", 0); });
            _menu.Items.Add("🛡 Mon profil a-t-il été annulé (Windows Update) ? — vérifier / ré-appliquer", null, OnCheckDrift);
            _menu.Items.Add("Exporter mon profil d'optimisations (fichier)...", null, OnExportProfile);
            _menu.Items.Add("Importer un profil d'optimisations...", null, OnImportProfile);
            _menu.Items.Add("Réinitialiser TOUTES les optimisations (valeurs Windows)", null, OnResetAll);
            _menu.Items.Add("Ouvrir le dossier des sauvegardes", null, (s, e) => OnOpenClicked(s, e));
            _menu.Items.Add("Ouvrir le journal (fichier)", null, (s, e) =>
            {
                try
                {
                    string p = System.IO.Path.Combine(Application.StartupPath, "bt-optimizer-log.txt");
                    if (System.IO.File.Exists(p)) Process.Start("notepad.exe", "\"" + p + "\"");
                    else MessageBox.Show(this, "Aucun journal fichier pour l'instant.", "DesTinGOOD", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            _btnLatMin = MakeButton("⚡ LATENCE MIN", 748, 104, 136, 28, false);
            _btnLatMin.BackColor = Accent;
            _btnLatMin.ForeColor = Color.White;
            _btnLatMin.Font = new Font("Segoe UI Semibold", 9f);
            _btnLatMin.FlatAppearance.BorderSize = 0;
            _btnLatMin.Click += OnLatencyMinimal;
            _btnFps500 = MakeButton("🎯 500 FPS", 636, 104, 106, 28, false);
            _btnFps500.BackColor = Color.FromArgb(200, 80, 0);
            _btnFps500.ForeColor = Color.White;
            _btnFps500.Font = new Font("Segoe UI Semibold", 9f);
            _btnFps500.FlatAppearance.BorderSize = 0;
            _btnFps500.Click += OnFps500Open;
            _search = new TextBox();
            _search.SetBounds(360, 105, 270, 26);
            _search.PlaceholderText = "Rechercher une optimisation (nom, catégorie, description)...";
            _search.TextChanged += (s, e) => FilterTweaks(_search.Text);

            // ⚡ LE bouton : tout optimiser en 1 clic (niveau choisi selon le matériel,
            // sauvegarde forcée, réglages à risque jeux/boutiques écartés d'office).
            _btnOneClick = new Button();
            _btnOneClick.Text = "⚡ TOUT OPTIMISER MON PC  —  1 clic : détection du matériel, sauvegarde automatique, 100 % réversible";
            _btnOneClick.SetBounds(16, 138, 868, 34);
            _btnOneClick.FlatStyle = FlatStyle.Flat;
            _btnOneClick.FlatAppearance.BorderSize = 0;
            _btnOneClick.BackColor = Accent;
            _btnOneClick.ForeColor = Color.White;
            _btnOneClick.Font = new Font("Segoe UI Semibold", 10f);
            _btnOneClick.Click += OnOneClickOptimize;

            // Zone déroulante des optimisations
            var panel = new Panel();
            panel.SetBounds(16, 178, 868, 358);
            panel.AutoScroll = true;
            panel.BackColor = Color.White;

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
            _chkBackup.SetBounds(16, 544, 240, 22);
            _chkBackup.Checked = true;

            _chkPoint = new CheckBox();
            _chkPoint.Text = "Point de restauration système";
            _chkPoint.SetBounds(262, 544, 210, 22);
            _chkPoint.Checked = true;

            _chkGuard = new CheckBox();
            _chkGuard.Text = "GARDIEN : ré-appliquer mon profil à chaque démarrage";
            _chkGuard.SetBounds(478, 544, 406, 22);
            _chkGuard.Checked = Sys.GuardExists();
            _chkGuard.CheckedChanged += OnGuardToggled;

            _chkTimer = new CheckBox();
            _chkTimer.Text = "Timer Windows 1 ms tant que l'app est ouverte (actif aussi réduite en zone de notification)";
            _chkTimer.SetBounds(16, 568, 550, 22);
            _chkTimer.CheckedChanged += OnTimerToggled;

            _chkAutoTimer = new CheckBox();
            _chkAutoTimer.Text = "Timer 1 ms AUTO en jeu plein écran";
            _chkAutoTimer.SetBounds(572, 568, 312, 22);
            _chkAutoTimer.Checked = true;   // par défaut : le timer 1 ms suit les jeux tout seul
            _chkAutoTimer.CheckedChanged += OnTimerToggled;

            _chkAutoBoost = new CheckBox();
            _chkAutoBoost.Text = "MODE JEU AUTO : active/coupe le mode jeu tout seul dès qu'un jeu est lancé (détection par jeu + plein écran)";
            _chkAutoBoost.SetBounds(16, 592, 868, 22);
            _chkAutoBoost.Checked = false;   // opt-in : il suspend des services de fond

            // Boutons d'action
            _btnApply = MakeButton("APPLIQUER LA SÉLECTION", 16, 624, 268, 44, true);
            _btnApply.Font = new Font("Segoe UI Semibold", 10.5f);
            _btnRevert = MakeButton("Rétablir (sélection)", 292, 624, 176, 44, false);
            _btnOpen = MakeButton("Sauvegardes", 476, 624, 104, 44, false);
            _btnReport = MakeButton("Rapport", 588, 624, 96, 44, false);
            _btnLatency = MakeButton("Analyse latence", 692, 624, 192, 44, false);
            _btnLatency.ForeColor = Accent;

            // Ligne outils
            _btnMonitor = MakeButton("Moniteur matériel", 16, 676, 210, 32, false);
            _btnMonitor.ForeColor = Accent;
            _btnOverclock = MakeButton("Overclock auto", 234, 676, 150, 32, false);
            _btnOverclock.ForeColor = Color.FromArgb(180, 70, 20);
            _btnDns = MakeButton("DNS rapide", 392, 676, 130, 32, false);
            _btnDns.ForeColor = Accent;
            _btnAutoCompare = MakeButton("Comparer les 2 dernières mesures", 530, 676, 354, 32, false);

            // Journal : console posée dans une carte arrondie.
            var logCard = new Panel();
            logCard.SetBounds(16, 716, 868, 138);
            logCard.Padding = new Padding(8, 6, 8, 6);
            logCard.Paint += OnPaintLogCard;
            logCard.Resize += (s, e) => logCard.Invalidate();
            _log = new RichTextBox();
            _log.Dock = DockStyle.Fill;
            _log.ReadOnly = true;
            _log.BackColor = Color.White;
            _log.Font = new Font("Consolas", 8.5f);
            _log.BorderStyle = BorderStyle.None;
            logCard.Controls.Add(_log);

            _btnReco.Click += (s, e) => ApplyPreset(t => t.Recommended);
            _btnEsport.Click += (s, e) => { if (RequirePro("Preset eSport")) ApplyPreset(t => t.Esport); };
            _btnAll.Click += (s, e) => ApplyPreset(t => true);
            _btnNone.Click += (s, e) => ApplyPreset(t => false);
            _btnAuto.Click += (s, e) =>
            {
                if (!RequirePro("Auto-tune")) return;
                int last = Sys.LoadAutoLevel();
                var m = new ContextMenuStrip();
                string[] labels =
                {
                    "Prudent — sûr, sans redémarrage",
                    "Équilibré — latence & perf (recommandé)",
                    "Agressif — maximum sûr (réversible)"
                };
                for (int lvl = 0; lvl <= 2; lvl++)
                {
                    int captured = lvl;
                    var item = new ToolStripMenuItem(labels[lvl] + (lvl == last ? "   ← dernier choix" : ""),
                        null, (a, b) => OnAutoTune(captured));
                    if (lvl == last) item.Font = new Font(Font, FontStyle.Bold);
                    m.Items.Add(item);
                }
                m.Show(_btnAuto, new Point(0, _btnAuto.Height));
            };
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
            _tray.Text = "DesTinGOOD";
            _tray.Visible = false;
            _tray.DoubleClick += (s, e) => RestoreFromTray();
            var trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Ouvrir DesTinGOOD", null, (s, e) => RestoreFromTray());
            trayMenu.Items.Add("▶ MODE JEU on/off   (Ctrl+Alt+G)", null, (s, e) => OnBoostToggle(s, e));
            trayMenu.Items.Add("Timer 1 ms on/off", null, (s, e) => _chkTimer.Checked = !_chkTimer.Checked);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("Quitter", null, (s, e) => { _tray.Visible = false; Close(); });
            _tray.ContextMenuStrip = trayMenu;

            // Toutes les 2 s : résolution timer réelle + détection jeu plein écran (mode AUTO).
            _uiTimer = new Timer();
            _uiTimer.Interval = 2000;
            _uiTimer.Tick += (s, e) => { UpdateTimerState(); UpdateTimerLabel(); UpdateAutoBoost(); };
            _uiTimer.Start();
            UpdateTimerLabel();

            Controls.AddRange(new Control[]
            {
                header, _btnReco, _btnEsport, _btnAll, _btnNone, _btnMeasure, _btnRestore,
                _btnAuto, _btnBench, _btnLatMin, _btnFps500, _search, _btnOneClick,
                panel, _chkBackup, _chkPoint, _chkGuard, _chkTimer, _chkAutoTimer, _chkAutoBoost,
                _btnApply, _btnRevert, _btnOpen, _btnReport, _btnLatency,
                _btnMonitor, _btnOverclock, _btnDns, _btnAutoCompare, logCard
            });
        }

        private void OnFormClosingCleanup(object sender, FormClosingEventArgs e)
        {
            try { UnregisterHotKey(Handle, HotkeyBoostId); } catch { }
            if (GameBoost.IsActive) GameBoost.Deactivate(delegate (string m, int l) { });
            Native.SetTimer1ms(false);
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        }

        // ------------------------------------------------------------------
        //  Raccourci clavier GLOBAL : Ctrl+Alt+G bascule le MODE JEU, même
        //  minimisé ou en pleine partie (le jeu garde le focus).
        // ------------------------------------------------------------------
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int HotkeyBoostId = 0xB70;
        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                if (RegisterHotKey(Handle, HotkeyBoostId, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, (uint)'G'))
                    Log("Raccourci global actif : Ctrl+Alt+G = MODE JEU (fonctionne en pleine partie).", 0);
            }
            catch { }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HotkeyBoostId)
            {
                OnBoostToggle(this, EventArgs.Empty);
                if (!Visible && _tray != null && _tray.Visible)
                    _tray.ShowBalloonTip(1500, "DesTinGOOD",
                        GameBoost.IsActive ? "MODE JEU activé (Ctrl+Alt+G)" : "MODE JEU désactivé (Ctrl+Alt+G)",
                        ToolTipIcon.Info);
            }
            base.WndProc(ref m);
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
                _tray.ShowBalloonTip(2500, "DesTinGOOD", tip, ToolTipIcon.Info);
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
        //  En-tête : wordmark « DesTin » blanc + « GOOD » dégradé, puces
        //  version/édition, tagline. Le liseré dégradé du bas est posé par le
        //  thème (signature commune à toutes les fenêtres).
        // ------------------------------------------------------------------
        private void OnPaintHeader(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            int x = 16, y = 7;

            float wm = Theme.DrawWordmark(g, _fontBrand, x, y);

            // Puces : version, puis édition (PRO / essai) si active.
            int cx = x + (int)wm + 14;
            cx += DrawChip(g, cx, 13, "v" + AppVer, Color.FromArgb(0, 210, 130), false) + 6;
            if (License.IsPro)
                DrawChip(g, cx, 13, "PRO", Color.FromArgb(0, 190, 120), true);
            else if (License.TrialActive)
                DrawChip(g, cx, 13, "ESSAI " + License.TrialDaysLeft + " J", Color.FromArgb(235, 180, 60), false);

            // Tagline bornée : la zone à droite appartient aux compteurs.
            TextRenderer.DrawText(g,
                "500 FPS · latence minimale · " + _tweaks.Count + " optimisations réversibles",
                _fontTag, new Rectangle(17, 38, 376, 17), Color.FromArgb(150, 156, 166),
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }

        /// <summary>Puce arrondie (pleine ou contour) ; renvoie sa largeur.</summary>
        private int DrawChip(Graphics g, int x, int y, string text, Color tone, bool filled)
        {
            Size ts = TextRenderer.MeasureText(g, text, _fontChip);
            int w = ts.Width + 10, h = 17;
            var rf = new RectangleF(x, y, w, h);
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.RoundPath(rf, h / 2f))
            {
                using (var br = new SolidBrush(filled ? tone : Color.FromArgb(28, tone)))
                    g.FillPath(br, path);
                using (var pen = new Pen(Color.FromArgb(filled ? 255 : 165, tone)))
                    g.DrawPath(pen, path);
            }
            g.SmoothingMode = old;
            TextRenderer.DrawText(g, text, _fontChip, new Rectangle(x, y, w, h),
                filled ? Color.White : tone,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            return w;
        }

        /// <summary>Carte arrondie derrière le journal.</summary>
        private void OnPaintLogCard(object sender, PaintEventArgs e)
        {
            var p = (Panel)sender;
            Rectangle r = p.ClientRectangle;
            if (r.Width < 8 || r.Height < 8) return;
            using (var br = new SolidBrush(p.Parent != null ? p.Parent.BackColor : BackColor))
                e.Graphics.FillRectangle(br, r);
            var rf = new RectangleF(0.5f, 0.5f, r.Width - 1f, r.Height - 1f);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.RoundPath(rf, 8f))
            {
                using (var br = new SolidBrush(Theme.FieldColor)) e.Graphics.FillPath(br, path);
                using (var pen = new Pen(Theme.LineColor)) e.Graphics.DrawPath(pen, path);
            }
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
                    cb.ForeColor = Theme.OkColor;
                    cb.Text = _baseText[t.Id] + "   [déjà actif]";
                }
                else
                {
                    cb.ForeColor = Theme.InkColor;
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

        /// <summary>Pack phare : coche l'ensemble latence/perf (eSport), force le timer 1 ms et applique.</summary>
        private void OnLatencyMinimal(object sender, EventArgs e)
        {
            ApplyPreset(t => t.Esport);
            if (!_chkTimer.Checked) _chkTimer.Checked = true;   // déclenche le timer 1 ms immédiat
            List<Tweak> sel = Selection();
            if (sel.Count == 0) return;
            int reboot = sel.Count(t => t.Reboot);
            string msg = "Appliquer le pack LATENCE MINIMALE ?\n\n"
                + sel.Count + " optimisations orientées input lag + performances :\n"
                + "• souris/clavier en prise directe (accélération off, file d'attente)\n"
                + "• timer 1 ms + tick fixe, priorité planificateur aux jeux\n"
                + "• alimentation maximale (CPU 100 %, pas de veille/throttling)\n"
                + "• GPU en mode MSI, HAGS, Game Mode, MPO off\n"
                + "• réseau réactif (Nagle off, throttling off, RSC/QoS)\n\n"
                + "Tout est réversible (sauvegarde .reg automatique)."
                + (reboot > 0 ? "\n" + reboot + " réglage(s) nécessitent un redémarrage." : "");
            if (MessageBox.Show(this, msg, "⚡ Latence minimale",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;
            Log("Application du pack LATENCE MINIMALE (" + sel.Count + " optimisations)...", 0);
            RunOperation(sel, true);
        }

        /// <summary>Ouvre le panneau « Objectif 500 FPS » ; applique le pack si l'utilisateur l'a demandé depuis le panneau.</summary>
        private void OnFps500Open(object sender, EventArgs e)
        {
            bool applyPack;
            using (var f = new Fps500Form(Log))
            {
                f.ShowDialog(this);
                applyPack = f.ApplyPackRequested;
            }
            if (applyPack) ApplyFps500Pack();
        }

        /// <summary>Pack 500 FPS : lève tous les freins Windows aux très hauts FPS (sélection eSport + timer 1 ms) et applique.</summary>
        private void ApplyFps500Pack()
        {
            ApplyPreset(t => t.Esport);
            if (!_chkTimer.Checked) _chkTimer.Checked = true;   // timer 1 ms immédiat
            List<Tweak> sel = Selection();
            if (sel.Count == 0) return;
            int reboot = sel.Count(t => t.Reboot);
            string msg = "Appliquer le pack 500 FPS ?\n\n"
                + sel.Count + " optimisations qui lèvent les freins Windows aux très hauts FPS :\n"
                + "• GPU : HAGS, vrai plein écran, MPO off, Mode Jeu, Game DVR off\n"
                + "• CPU : Performances ultimes, turbo agressif, zéro throttling\n"
                + "• planificateur : priorité au jeu, timer 1 ms, MMCSS réactif\n\n"
                + "Rappel honnête : les 500 FPS se DÉBLOQUENT ensuite dans chaque jeu\n"
                + "(limite de FPS → 500/illimitée, V-Sync off) — le panneau 🎯 te guide jeu par jeu.\n\n"
                + "Tout est réversible (sauvegarde .reg automatique)."
                + (reboot > 0 ? "\n" + reboot + " réglage(s) nécessitent un redémarrage." : "");
            if (MessageBox.Show(this, msg, "🎯 Objectif 500 FPS",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;
            Log("Application du pack 500 FPS (" + sel.Count + " optimisations)...", 0);
            RunOperation(sel, true);
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
            if (_header != null) _header.Invalidate();   // puce PRO / ESSAI de l'en-tête
            if (_miPro == null) return;
            if (License.IsPro) _miPro.Text = "Édition Pro active (" + License.Licensee + ")";
            else if (License.TrialActive) _miPro.Text = "Essai Pro — " + License.TrialDaysLeft + " j restants · entrer une clé";
            else _miPro.Text = "Activer la version Pro / essai gratuit";
        }

        private void OnAutoTune(int level)
        {
            Sys.SaveAutoLevel(level);
            if (_hw == null) _hw = Hardware.Detect();
            var ids = Hardware.AutoTuneIds(_tweaks, _hw, level);
            ApplyPreset(t => ids.Contains(t.Id));

            string niveau = level == Hardware.LevelPrudent ? "Prudent (sûr, sans redémarrage)"
                          : level == Hardware.LevelAggressive ? "Agressif (max sûr)"
                          : "Équilibré (latence/perf)";
            Log("Auto-tune — niveau " + niveau, 1);
            Log("Adapté à ton matériel : " + _hw.Summary(), 0);
            // Explique les décisions prises d'après le matériel détecté.
            Log("  • Disque : " + (_hw.AllSsd
                ? "SSD → SysMain/Prefetch désactivés (inutiles sur SSD)."
                : "HDD présent → préchargement conservé, disque laissé libre de se garer."), 0);
            Log("  • RAM : " + _hw.RamGB + " Go → " + (_hw.RamGB >= 16
                ? "combinaison de pages mémoire désactivée (moins de CPU)."
                : "réglages mémoire prudents (RAM limitée)."), 0);
            bool nvidia = _hw.GpuVendor != null && _hw.GpuVendor.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0;
            Log("  • GPU : " + _hw.GpuName + (nvidia ? " → télémétrie NVIDIA coupée." : " → réglages GPU génériques."), 0);
            Log("  • Châssis : " + (_hw.IsLaptop
                ? "portable → économie préservée (pas de CPU 100% permanent, veille USB/PCIe gardées)."
                : "PC fixe → perfs à fond" + (_hw.HasTouch ? "." : ", services capteurs/luminosité coupés.")), 0);
            Log("  • OS : " + (_hw.IsWin11 ? "Windows 11 → Widgets/Chat/Copilot retirés." : "Windows 10 → tweaks Win11 écartés."), 0);
            Log("  • Périphériques : " + (_hw.HasPrinter ? "imprimante détectée → spouleur conservé" : "aucune imprimante → spouleur coupé")
                + (_hw.HasBluetooth ? " · Bluetooth présent." : " · pas de Bluetooth."), 0);
            Log("  • Réseau : MSI carte réseau activé (latence).", 0);
            if (_hw.MaxHz >= 240 && level == Hardware.LevelPrudent)
                Log("  • Écran : " + _hw.MaxHz + " Hz — le pack très hauts FPS s'active aux niveaux Équilibré/Agressif.", 0);
            else if (_hw.MaxHz >= 240)
                Log("  • Écran : " + _hw.MaxHz + " Hz → pack très hauts FPS (files d'entrée courtes"
                    + (ids.Contains("dynamic_tick") ? ", tick noyau fixe" : "")
                    + (ids.Contains("cpu_idle_disable") ? ", CPU sans veille (C-States off)" : "") + ").", 0);
            else if (_hw.MaxHz > 0)
                Log("  • Écran : " + _hw.MaxHz + " Hz.", 0);
            try
            {
                var games = GameScan.Known();
                GameScan.Detect(games);
                int found = games.Count(g => g.Detected);
                if (found > 0)
                    Log("  • Jeux : " + found + " jeu(x) compétitif(s) détecté(s) → priorité CPU « Haute » "
                        + (ids.Contains("games_cpu_priority_high") ? "incluse." : "disponible dès le niveau Équilibré."), 0);
            }
            catch { }
            if (_hw.RamRatedMTs > _hw.RamRunningMTs + 66 && _hw.RamRunningMTs > 0)
                Log("  ! RAM à " + _hw.RamRunningMTs + " MT/s alors que tes barrettes gèrent " + _hw.RamRatedMTs
                    + " : active le profil XMP/EXPO dans le BIOS — gain de FPS gratuit.", 2);
            if (_hw.ScreensBelowMax > 0)
                Log("  ! " + _hw.ScreensBelowMax + " écran(s) SOUS leur fréquence max — ouvre 🎯 Objectif 500 FPS (bouton ⬆ Passer à la fréquence max).", 2);
            if (_hw.HypervisorActive)
                Log("  • Hyperviseur/VBS actif : coûte quelques % de CPU en jeu — désactivable via Composants & diagnostic (compromis sécurité, ton choix).", 0);
            bool idleIncluded = ids.Contains("cpu_idle_disable");
            Log("  • Écartés (choix explicite) : sécurité (Spectre/VBS), recherche Windows, MSI stockage"
                + (idleIncluded ? "." : ", CPU sans veille."), 2);
            Log("Sélection auto : " + ids.Count + " optimisation(s) cochée(s).", 1);

            List<Tweak> sel = Selection();
            if (sel.Count > 0 && MessageBox.Show(this,
                    "Appliquer maintenant les " + sel.Count + " optimisations cochées ?\n\n"
                    + "Oui : application immédiate (sauvegarde .reg automatique).\n"
                    + "Non : elles restent cochées, tu vérifies et appliques quand tu veux.",
                    "Auto — niveau " + niveau, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                Log("Application de la sélection Auto (" + sel.Count + " optimisations)...", 0);
                RunOperation(sel, true);
            }
        }

        /// <summary>
        /// ⚡ TOUT OPTIMISER : un seul clic. Niveau choisi selon le châssis (fixe → Agressif,
        /// portable → Équilibré), sélection auto-tune adaptée au matériel MOINS les réglages
        /// à risque jeux/boutiques (HAGS, UWP fond, tunnels IPv6), sauvegarde .reg forcée,
        /// timer 1 ms activé et RAM libérée. Tout reste réversible.
        /// </summary>
        private void OnOneClickOptimize(object sender, EventArgs e)
        {
            if (_hw == null) _hw = Hardware.Detect();
            int level = _hw.IsLaptop ? Hardware.LevelBalanced : Hardware.LevelAggressive;
            Sys.SaveAutoLevel(level);
            var ids = Hardware.OneClickIds(_tweaks, _hw, level);
            ApplyPreset(t => ids.Contains(t.Id));

            string niveau = _hw.IsLaptop ? "Équilibré (portable : batterie/chaleur préservées)"
                                         : "Agressif (PC fixe : max sûr)";
            Log("⚡ TOUT OPTIMISER — niveau " + niveau + " choisi automatiquement.", 1);
            Log("Matériel : " + _hw.Summary(), 0);
            Log("Écartés d'office (retours crashs/boutiques) : HAGS, applis UWP en fond, tunnels IPv6"
                + " — cochables à la main. Sécurité (Spectre/VBS) et OC jamais inclus.", 2);

            List<Tweak> sel = Selection();
            if (sel.Count == 0)
            {
                Log("Tout est déjà optimisé : rien à appliquer.", 1);
                return;
            }
            string msg = "Optimiser tout le PC maintenant ?\n\n"
                       + "• " + sel.Count + " optimisations adaptées à ton matériel (" + niveau + ")\n"
                       + "• Sauvegarde .reg automatique" + (_chkPoint.Checked ? " + point de restauration" : "") + "\n"
                       + "• Timer Windows 1 ms activé, RAM libérée\n"
                       + "• 100 % réversible (« Rétablir (sélection) » ou menu ☰ → Réinitialiser TOUT)";
            if (MessageBox.Show(this, msg, "⚡ TOUT OPTIMISER",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            _chkBackup.Checked = true;   // le 1 clic garde toujours un filet de sécurité
            _chkTimer.Checked = true;    // timer 1 ms immédiat (case existante)
            RunOperation(sel, true);
            Task.Run(() => Sys.CleanMemory(Log));
        }

        // ------------------------------------------------------------------
        //  Profil : anti-régression Windows Update + export / import
        // ------------------------------------------------------------------

        /// <summary>Optimisations du profil enregistré dont l'état réel a dérivé (annulées).</summary>
        private List<Tweak> DriftedTweaks()
        {
            var drifted = new List<Tweak>();
            List<string> ids = Sys.LoadProfile();
            if (ids.Count == 0) return drifted;
            foreach (Tweak t in _tweaks)
            {
                if (!ids.Contains(t.Id)) continue;
                // Dérive seulement si le Check est franchement FAUX (null = état illisible : on ne touche pas).
                try { if (t.Check != null && t.Check() == false) drifted.Add(t); } catch { }
            }
            return drifted;
        }

        /// <summary>Au démarrage : signale (sans bloquer) si une MAJ Windows a annulé le profil.</summary>
        private void CheckProfileDriftAtStartup()
        {
            Task.Run(() =>
            {
                List<Tweak> drifted = DriftedTweaks();
                if (drifted.Count == 0) return;
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        Log("🛡 " + drifted.Count + " optimisation(s) de ton profil ne sont PLUS actives "
                            + "(mise à jour Windows ?) — menu ☰ → « Mon profil a-t-il été annulé ? » pour les ré-appliquer.", 2);
                        if (_tray != null)
                            _tray.ShowBalloonTip(4000, "DesTinGOOD",
                                drifted.Count + " optimisation(s) annulée(s) par Windows — ré-application possible (menu ☰).",
                                ToolTipIcon.Warning);
                    }));
                }
                catch { }
            });
        }

        private void OnCheckDrift(object sender, EventArgs e)
        {
            if (Sys.LoadProfile().Count == 0)
            {
                MessageBox.Show(this,
                    "Aucun profil enregistré pour l'instant.\n\nLe profil se crée automatiquement à chaque "
                    + "« APPLIQUER LA SÉLECTION » (ou ⚡ TOUT OPTIMISER).",
                    "Profil", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Cursor = Cursors.WaitCursor;
            List<Tweak> drifted = DriftedTweaks();
            Cursor = Cursors.Default;
            if (drifted.Count == 0)
            {
                MessageBox.Show(this, "Ton profil est intact : aucune optimisation annulée. ✔",
                    "Profil", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string names = string.Join("\n", drifted.Take(8).Select(t => "  • " + t.Name).ToArray())
                         + (drifted.Count > 8 ? "\n  • … et " + (drifted.Count - 8) + " autre(s)" : "");
            if (MessageBox.Show(this,
                    drifted.Count + " optimisation(s) de ton profil ont été ANNULÉES (mise à jour Windows, "
                    + "pilote, autre outil ?) :\n\n" + names + "\n\nLes ré-appliquer maintenant ? (sauvegarde .reg automatique)",
                    "🛡 Profil annulé en partie", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            ApplyPreset(t => drifted.Contains(t));
            _chkBackup.Checked = true;
            RunOperation(drifted, true);
        }

        private void OnExportProfile(object sender, EventArgs e)
        {
            List<Tweak> sel = Selection();
            if (sel.Count == 0)
            {
                MessageBox.Show(this, "Coche d'abord les optimisations à exporter (ou utilise un preset).",
                    "Exporter", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (var d = new SaveFileDialog
            {
                Filter = "Profil DesTinGOOD (*.txt)|*.txt", FileName = "destingood-profil.txt",
                Title = "Exporter le profil (" + sel.Count + " optimisations cochées)"
            })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    System.IO.File.WriteAllLines(d.FileName, sel.Select(t => t.Id).ToArray());
                    Log("Profil exporté (" + sel.Count + " optimisation(s)) : " + d.FileName, 1);
                }
                catch (Exception ex) { Log("Export impossible : " + ex.Message, 3); }
            }
        }

        private void OnImportProfile(object sender, EventArgs e)
        {
            using (var d = new OpenFileDialog
            {
                Filter = "Profil DesTinGOOD (*.txt)|*.txt|Tous les fichiers (*.*)|*.*",
                Title = "Importer un profil d'optimisations"
            })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var ids = new HashSet<string>(
                        System.IO.File.ReadAllLines(d.FileName)
                            .Select(l => l.Trim())
                            .Where(l => l.Length > 0 && !l.StartsWith("#")),
                        StringComparer.OrdinalIgnoreCase);
                    int known = _tweaks.Count(t => ids.Contains(t.Id));
                    ApplyPreset(t => ids.Contains(t.Id));
                    Log("Profil importé : " + known + " optimisation(s) cochée(s)"
                        + (ids.Count > known ? " (" + (ids.Count - known) + " inconnue(s) ignorée(s))" : "")
                        + " — vérifie la sélection puis APPLIQUER.", 1);
                }
                catch (Exception ex) { Log("Import impossible : " + ex.Message, 3); }
            }
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
                MessageBox.Show(this, "Aucune optimisation cochée.", "DesTinGOOD",
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
                MessageBox.Show(this, "Cochez les optimisations à rétablir.", "DesTinGOOD",
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
                    "DesTinGOOD", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            Button[] buttons = { _btnApply, _btnRevert, _btnRestore, _btnReco, _btnEsport, _btnAll, _btnNone, _btnMeasure, _btnReport, _btnLatency, _btnMonitor, _btnAutoCompare, _btnOverclock, _btnDns, _btnAuto, _btnBench, _btnBoost, _btnLatMin, _btnFps500, _btnOneClick };
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
                    "DesTinGOOD", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var files = new System.IO.DirectoryInfo(tools).GetFiles("dpcisr-*.txt");
            if (files.Length < 2)
            {
                MessageBox.Show(this, "Il faut au moins 2 rapports DPC/ISR dans « tools » pour comparer.\n" +
                    "Fais deux captures (Mesurer latence → Oui à l'ETW), avant et après tes changements.",
                    "DesTinGOOD", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            // Bascule MANUELLE : l'auto ne doit plus « posséder » cet état (ne pas le couper tout seul).
            _autoBoostEngaged = false;
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
                        "DesTinGOOD", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }));
            });
        }

        private void OnReportClicked(object sender, EventArgs e)
        {
            // La génération inclut désormais un audit (journaux, WMI) : en arrière-plan pour ne pas figer.
            Cursor = Cursors.WaitCursor;
            Log("Génération du rapport (audit santé en cours)...", 0);
            HwProfile hw = _hw ?? Hardware.Detect();
            Task.Run(() =>
            {
                string result;
                try
                {
                    string html = Report.BuildHtml(_tweaks, hw);
                    string path = System.IO.Path.Combine(Sys.BackupDesktop, "bt-optimizer-rapport.html");
                    System.IO.File.WriteAllText(path, html, new System.Text.UTF8Encoding(false));
                    result = path;
                }
                catch (Exception ex) { result = "ERR:" + ex.Message; }
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        Cursor = Cursors.Default;
                        if (result.StartsWith("ERR:")) { Log("Rapport impossible : " + result.Substring(4), 3); return; }
                        Log("Rapport HTML enregistré : " + result, 1);
                        try { Process.Start(new ProcessStartInfo(result) { UseShellExecute = true }); } catch { }
                    }));
                }
                catch { }
            });
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

        private int _fsStableTicks;   // ticks consécutifs avec jeu plein écran (anti-clignotement)

        /// <summary>
        /// MODE JEU AUTO : enclenche le mode jeu quand un jeu tient le plein écran depuis 2 ticks
        /// (≈4 s) et le coupe seul au retour au bureau. Ne touche jamais à une activation MANUELLE.
        /// </summary>
        private void UpdateAutoBoost()
        {
            if (_chkAutoBoost == null || !_chkAutoBoost.Checked || _boostBusy) return;

            // Détection PRÉCISE : un jeu connu qui tourne (immédiat, pas de faux positif sur une vidéo)
            // OU, en repli, une appli plein écran stable (couvre les jeux non listés).
            string game = GameScan.RunningKnownGame();
            bool fullscreen = Native.IsGameFullscreen();
            _fsStableTicks = fullscreen ? Math.Min(_fsStableTicks + 1, 10) : 0;
            bool engage = game != null || _fsStableTicks >= 2;

            if (engage && !GameBoost.IsActive)
            {
                string reason = game != null ? "jeu détecté (" + game + ")" : "jeu plein écran détecté";
                _boostBusy = true;
                Task.Run(() =>
                {
                    GameBoost.Activate(Log);
                    try { BeginInvoke((Action)(() => { _autoBoostEngaged = true; _boostBusy = false; SyncBoostButton();
                        Log("MODE JEU AUTO : " + reason + " → mode jeu activé.", 1); })); }
                    catch { _boostBusy = false; }
                });
            }
            // Coupe : plus aucun jeu (ni process connu, ni plein écran) ET c'est NOUS qui l'avions activé.
            else if (game == null && !fullscreen && GameBoost.IsActive && _autoBoostEngaged)
            {
                _boostBusy = true;
                Task.Run(() =>
                {
                    GameBoost.Deactivate(Log);
                    try { BeginInvoke((Action)(() => { _autoBoostEngaged = false; _boostBusy = false; SyncBoostButton();
                        Log("MODE JEU AUTO : retour au bureau → mode jeu coupé.", 0); })); }
                    catch { _boostBusy = false; }
                });
            }
        }

        /// <summary>Aligne le bouton MODE JEU sur l'état réel (après une bascule automatique).</summary>
        private void SyncBoostButton()
        {
            if (_btnBoost == null) return;
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
        }

        private void OnOptimizeDrives(object sender, EventArgs e)
        {
            if (MessageBox.Show(this,
                    "Optimiser tous les lecteurs fixes ?\n\n"
                    + "Windows choisit automatiquement : RE-TRIM sur les SSD (préserve les performances), "
                    + "défragmentation sur les disques durs mécaniques (chargements plus rapides).\n\n"
                    + "• Réactive aussi la maintenance planifiée si un « optimiseur » l'avait coupée.\n"
                    + "• Quelques minutes ; peut être plus long sur un HDD. Suis le journal.",
                    "Optimiser les lecteurs", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;
            Log("Optimisation des lecteurs démarrée...", 0);
            Task.Run(() => Sys.OptimizeDrives(Log));
        }

        private void OnRepairWindows(object sender, EventArgs e)
        {
            if (MessageBox.Show(this,
                    "Réparer l'intégrité de Windows ?\n\n"
                    + "Lance DISM /RestoreHealth puis SFC /scannow : répare les fichiers système corrompus, "
                    + "cause fréquente de crashs qui persistent malgré tout le reste.\n\n"
                    + "• Dure 10 à 20 minutes (connexion internet conseillée pour DISM).\n"
                    + "• Tu peux continuer à utiliser le PC ; suis l'avancement dans le journal.\n"
                    + "• Un redémarrage peut être nécessaire si des réparations ont lieu.",
                    "Réparer Windows", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;
            Log("Réparation d'intégrité Windows démarrée (10-20 min)...", 0);
            Task.Run(() => Sys.RepairWindows(Log));
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
                        "DesTinGOOD", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
