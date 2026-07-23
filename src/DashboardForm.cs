using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BTOptimizer
{
    // ----------------------------------------------------------------------
    //  Shell style DTG : fenetre unique, barre laterale a icones qui
    //  echange le contenu (pages), mascotte docteur toujours visible.
    // ----------------------------------------------------------------------
    internal class DashboardForm : Form
    {
        private Panel _rail, _host;
        private readonly System.Collections.Generic.List<NavCell> _nav = new System.Collections.Generic.List<NavCell>();
        private readonly FpsPage[] _pages = new FpsPage[8];
        private int _current = -1;

        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int sz);
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr h, int id);
        private const int HotkeyId = 0xB71, WM_HOTKEY = 0x0312;
        private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000;
        private NotifyIcon _tray;
        private ContextMenuStrip _toolsMenu;
        private Timer _sysTimer;
        private bool _trayShown;

        public DashboardForm()
        {
            Text = "Fluide — QG";
            ClientSize = new Size(1200, 760);
            MinimumSize = new Size(1040, 680);
            StartPosition = FormStartPosition.CenterScreen;
            try { WindowBounds.Restore(this); } catch { }   // rouvre où l'utilisateur avait laissé la fenêtre
            BackColor = FpsUi.BgMain;
            Font = FpsUi.Body;
            DoubleBuffered = true;
            try { Icon = Logo.MakeIcon(32, FpsUi.Neon); } catch { try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { } }
            // Garantit le rendu sombre des menus (⋯, tray) dès le démarrage.
            try { Theme.Prime(); } catch { }

            _host = new Panel();
            _host.Dock = DockStyle.Fill;
            _host.BackColor = FpsUi.BgMain;
            Controls.Add(_host);

            BuildTools();
            BuildRail();
            Controls.Add(_rail);

            BuildTray();
            Resize += OnResizeShell;
            BadgeStore.OnNewBadge += OnNewBadge;   // toast « nouveau badge débloqué ! »

            _sysTimer = new Timer(); _sysTimer.Interval = 2000; _sysTimer.Tick += (s, e) => AutoTimer(); _sysTimer.Start();
            try { DiscordPresence.StartIfEnabled(); } catch { }   // présence Discord (parité FPSDoctor)

            Shown += (s, e) =>
            {
                SetDark(); ShowPage(0);
                // Rétablit les overlays activés au dernier lancement (le shell remplace MainForm
                // qui portait ces appels — sans ça le viseur ne réapparaissait plus au démarrage).
                try { Crosshair.ShowOnStartupIfEnabled(Log); } catch { }
                try { StatsOverlayManager.ShowOnStartupIfEnabled(Log); } catch { }
            };
            FormClosing += (s, e) => { try { WindowBounds.Save(this); } catch { } Cleanup(); };
        }

        private void OnResizeShell(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized)
            {
                Hide(); _tray.Visible = true;
                if (!_trayShown) { _trayShown = true; _tray.ShowBalloonTip(2000, "Fluide", "Toujours actif. Double-clic pour rouvrir.", ToolTipIcon.Info); }
                return;
            }
        }

        // ------------------------------------------------------------------
        //  Outils avancés (accès à TOUTES les fonctions de l'app).
        // ------------------------------------------------------------------
        private void BuildTools()
        {
            _toolsMenu = new ContextMenuStrip();
            var m = _toolsMenu.Items;

            m.Add("🛠  Optimiseur complet (presets, auto-tune, gardien, sauvegarde…)", null, (s, e) => OpenDialog(new MainForm()));
            m.Add("🎚  Mode SIMPLE (interrupteurs immédiats)", null, (s, e) => OpenDialog(new SimpleOptiForm(Catalog.All(), () => License.ProUnlocked, Log)));
            m.Add(new ToolStripSeparator());

            var jeux = new ToolStripMenuItem("🎮  Jeux");
            jeux.DropDownItems.Add("Priorité CPU par jeu", null, (s, e) => OpenDialog(new GameProfileForm(Log)));
            jeux.DropDownItems.Add("🕹 Mes jeux (boost par jeu : léger / complet)", null, (s, e) => OpenDialog(new GamesForm(Log)));
            jeux.DropDownItems.Add("Réglages Mode Jeu (exclusions)", null, (s, e) => OpenDialog(new GameModeForm(Log)));
            jeux.DropDownItems.Add("Qualité réseau en jeu", null, (s, e) => OpenDialog(new NetworkForm(Log)));
            jeux.DropDownItems.Add("Jeux & disques", null, (s, e) => OpenDialog(new DiskForm(Log)));
            jeux.DropDownItems.Add("Boutiques & contenu en jeu", null, (s, e) => OpenDialog(new ShopFixForm(Log)));
            jeux.DropDownItems.Add("🛠 Réparer l'installation des jeux (EA/Steam/Epic/Battle.net)", null, (s, e) => OpenDialog(new LauncherFixForm(Log)));
            jeux.DropDownItems.Add("Bibliothèques & applis de jeu", null, (s, e) => OpenDialog(new LibsForm(Log)));
            jeux.DropDownItems.Add("Prérequis & installation automatique", null, (s, e) => OpenDialog(new AutoInstallForm(Log)));
            jeux.DropDownItems.Add("Exclusions antivirus (jeux)", null, (s, e) => OpenDialog(new DefenderForm(Log)));
            jeux.DropDownItems.Add("Prêt pour le match ?", null, (s, e) => OpenDialog(new TournamentForm(Log)));
            m.Add(jeux);

            var perf = new ToolStripMenuItem("📈  Performances & FPS");
            perf.DropDownItems.Add("⚡ Config auto adaptée à mon PC (+ preuve)", null, (s, e) => OpenDialog(new AutoConfigForm(Log)));
            perf.DropDownItems.Add("Objectif 500 FPS", null, (s, e) => OpenDialog(new Fps500Form(Log)));
            perf.DropDownItems.Add("FPS en direct", null, (s, e) => OpenDialog(new FpsMonForm(Log)));
            perf.DropDownItems.Add("Benchmark FPS (avant/après)", null, (s, e) => OpenDialog(new BenchmarkFpsForm(Log)));
            perf.DropDownItems.Add("Benchmark rapide (CPU/GPU)", null, (s, e) => OpenDialog(new BenchForm(Log)));
            perf.DropDownItems.Add("Réglages d'écran", null, (s, e) => OpenDialog(new DisplayForm(Log)));
            perf.DropDownItems.Add("🎥 Streamer sans lag (RTSS/OBS/NVIDIA)", null, (s, e) => OpenDialog(new StreamGuideForm(Log)));
            perf.DropDownItems.Add("🧩 BIOS & manips manuelles (XMP, ReBAR…)", null, (s, e) => OpenDialog(new BiosGuideForm(Log)));
            m.Add(perf);

            var lat = new ToolStripMenuItem("⏱  Latence");
            lat.DropDownItems.Add("Latence en direct (DPC/ISR)", null, (s, e) => OpenDialog(new LiveMonForm(Log)));
            lat.DropDownItems.Add("Guide latence & input lag", null, (s, e) => OpenDialog(new LatencyGuideForm(Log)));
            m.Add(lat);

            var diag = new ToolStripMenuItem("🩺  Diagnostic & santé");
            diag.DropDownItems.Add("Santé de mon PC", null, (s, e) => OpenDialog(new HealthForm(Log)));
            diag.DropDownItems.Add("Qui ralentit mon PC ?", null, (s, e) => OpenDialog(new BloatForm(Log)));
            diag.DropDownItems.Add("Réglages néfastes", null, (s, e) => OpenDialog(new CheckupForm(Log)));
            diag.DropDownItems.Add("Stabilité du PC", null, (s, e) => OpenDialog(new StabilityForm(Log)));
            diag.DropDownItems.Add("Test de stress CPU", null, (s, e) => OpenDialog(new StressForm(Log)));
            diag.DropDownItems.Add("Températures & throttling", null, (s, e) => OpenDialog(new ThermalForm(Log)));
            diag.DropDownItems.Add("Moniteur matériel", null, (s, e) => OpenDialog(new MonitorForm()));
            diag.DropDownItems.Add("Composants & diagnostic", null, (s, e) => OpenDialog(new SystemInfoForm(Log)));
            diag.DropDownItems.Add("Rapport de santé (HTML, à partager)", null, (s, e) => GenerateHealthReport());
            m.Add(diag);

            var net = new ToolStripMenuItem("🌐  Réseau");
            net.DropDownItems.Add("DNS rapide", null, (s, e) => OpenDialog(new DnsForm(Log)));
            net.DropDownItems.Add("Réglages TCP/IP", null, (s, e) => OpenDialog(new NetTuneForm(Log)));
            net.DropDownItems.Add("Trajet réseau", null, (s, e) => OpenDialog(new NetRouteForm(Log)));
            m.Add(net);

            var reg = new ToolStripMenuItem("⚙  Réglages système");
            reg.DropDownItems.Add("Fréquence de la souris", null, (s, e) => OpenDialog(new MouseForm(Log)));
            reg.DropDownItems.Add("Audio & enceintes", null, (s, e) => OpenDialog(new AudioForm(Log)));
            reg.DropDownItems.Add("Discord (ce qui pèse en jeu)", null, (s, e) => OpenDialog(new DiscordForm(Log)));
            reg.DropDownItems.Add("Périphériques (erreurs)", null, (s, e) => OpenDialog(new DeviceManagerForm(Log)));
            reg.DropDownItems.Add("Programmes au démarrage", null, (s, e) => OpenDialog(new StartupForm(Log)));
            reg.DropDownItems.Add("Services Windows", null, (s, e) => OpenDialog(new ServicesForm(Log)));
            reg.DropDownItems.Add("🗑 Retirer les applis Windows (dé-bloatware)", null, (s, e) => OpenDialog(new BloatRemoveForm(Log)));
            reg.DropDownItems.Add(new ToolStripSeparator());
            var autostart = new ToolStripMenuItem("Démarrer Fluide avec Windows") { Checked = AppAutostart.IsEnabled() };
            autostart.Click += (s, e) => { bool now = !AppAutostart.IsEnabled(); if (AppAutostart.SetEnabled(now)) autostart.Checked = now; };
            reg.DropDownItems.Add(autostart);
            var discord = new ToolStripMenuItem("Présence Discord (« optimise son PC avec Fluide »)") { Checked = DiscordPresence.Enabled };
            discord.Click += (s, e) => { bool now = !DiscordPresence.Enabled; DiscordPresence.Enabled = now; discord.Checked = now; if (now) DiscordPresence.Start(); else DiscordPresence.Stop(); };
            reg.DropDownItems.Add(discord);
            reg.DropDownItems.Add("Redémarrer l'explorateur Windows", null, (s, e) => RestartExplorerConfirm());
            m.Add(reg);

            m.Add("🧰  Entretien du PC (nettoyage, TRIM, caches, DNS — 6 routines)", null, (s, e) => OpenDialog(new MaintenanceForm(Log)));
            m.Add("🔁  Restauration (points & sauvegardes)", null, (s, e) => OpenDialog(new RestoreForm(Log)));

            var prof = new ToolStripMenuItem("💾  Profil d'optimisations");
            prof.DropDownItems.Add("Exporter mon profil…", null, (s, e) => ExportProfile());
            prof.DropDownItems.Add("Importer un profil…", null, (s, e) => ImportProfile());
            m.Add(prof);
            m.Add(new ToolStripSeparator());
            m.Add("❓  J'ai un problème…", null, (s, e) => OpenDialog(new HelpNavForm(Log)));
            m.Add("ℹ  À propos de Fluide", null, (s, e) => OpenDialog(new AboutForm()));
            m.Add("🔑  Activer Pro / entrer une clé", null, (s, e) => OpenDialog(new LicenseKeyForm("")));
        }

        private void BuildTray()
        {
            _tray = new NotifyIcon();
            try { _tray.Icon = Icon; } catch { }
            _tray.Text = "Fluide"; _tray.Visible = false;
            _tray.DoubleClick += (s, e) => RestoreFromTray();
            var m = new ContextMenuStrip();
            m.Items.Add("Ouvrir Fluide", null, (s, e) => RestoreFromTray());
            m.Items.Add("▶ MODE JEU on/off  (Ctrl+Alt+G)", null, (s, e) => ToggleBoost());
            m.Items.Add("Overlay stats on/off", null, (s, e) => ToggleOverlay());
            m.Items.Add("Rapport de santé (HTML)", null, (s, e) => GenerateHealthReport());
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Quitter", null, (s, e) => { _tray.Visible = false; Close(); });
            _tray.ContextMenuStrip = m;
        }

        private void RestoreFromTray() { Show(); WindowState = FormWindowState.Normal; Activate(); _tray.Visible = false; }

        private void ToggleBoost()
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                try { if (GameBoost.IsActive) GameBoost.Deactivate(Log); else GameBoost.Activate(Log); } catch { }
            });
        }

        private void RestartExplorerConfirm()
        {
            if (MessageBox.Show(this,
                "Redémarrer l'explorateur Windows ?\n\nLa barre des tâches et le bureau disparaissent ~1 seconde puis reviennent. "
                + "Utile pour rafraîchir le shell après des réglages, ou débloquer une barre des tâches figée.",
                "Fluide", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            System.Threading.Tasks.Task.Run(() => AppAutostart.RestartExplorer());
        }

        // Notification (thread de fond possible) : marshale vers l'UI et affiche le toast.
        private void OnNewBadge(string id)
        {
            try { BeginInvoke((Action)(() => { try { BadgeToastManager.Show(BadgeCatalog.ById(id)); } catch { } })); } catch { }
        }

        private void ToggleOverlay()
        {
            try
            {
                var s = StatsOverlaySettings.Load();
                StatsOverlayManager.Toggle(s);
                s.Enabled = StatsOverlayManager.IsVisible; s.Save();   // persiste l'état pour le prochain lancement
            }
            catch { }
        }

        private void AutoTimer()
        {
            try
            {
                bool wanted = Native.IsGameFullscreen();
                if (wanted != Native.TimerActive) Native.SetTimer1ms(wanted);

                // Viseur AUTO en jeu : affiché dès qu'un jeu tourne (jeu connu ou plein écran), retiré au bureau.
                if (Crosshair.AutoGameEnabled)
                {
                    bool game = false;
                    try { game = GameScan.RunningKnownGame() != null; } catch { }
                    Crosshair.AutoTick(game || wanted, game || wanted);
                }
            }
            catch { }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { RegisterHotKey(Handle, HotkeyId, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, (uint)'G'); } catch { }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HotkeyId) ToggleBoost();
            base.WndProc(ref m);
        }

        private void Cleanup()
        {
            try { BadgeStore.OnNewBadge -= OnNewBadge; } catch { }
            try { UnregisterHotKey(Handle, HotkeyId); } catch { }
            try { if (GameBoost.IsActive) GameBoost.Deactivate(delegate (string a, int b) { }); } catch { }
            try { Native.SetTimer1ms(false); } catch { }
            try { Crosshair.Hide(); } catch { }
            try { StatsOverlayManager.Hide(); } catch { }
            try { if (_sysTimer != null) _sysTimer.Stop(); } catch { }
            try { if (_tray != null) { _tray.Visible = false; _tray.Dispose(); } } catch { }
        }

        /// <summary>Y maximal utilisable par une page (plus de mascotte : plein cadre).</summary>
        public int ContentBottom(int margin) { return ClientSize.Height - margin; }

        /// <summary>X maximal utilisable par du contenu (plus de mascotte : plein cadre).</summary>
        public int ContentRight(int margin) { return ClientSize.Width - margin; }

        private void SetDark() { try { int v = 1; DwmSetWindowAttribute(Handle, 20, ref v, 4); } catch { } }

        public void Log(string m, int l) { }

        // ------------------------------------------------------------------
        //  Barre laterale
        // ------------------------------------------------------------------
        private void BuildRail()
        {
            _rail = new Panel();
            _rail.Dock = DockStyle.Left;
            _rail.Width = 66;
            _rail.BackColor = FpsUi.RailBg;
            _rail.Paint += (s, e) => { using (var pen = new Pen(FpsUi.Border)) e.Graphics.DrawLine(pen, _rail.Width - 1, 0, _rail.Width - 1, _rail.Height); };

            var brand = new Panel();
            brand.Dock = DockStyle.Bottom; brand.Height = 62; brand.BackColor = Color.Transparent;
            brand.Paint += (s, e) =>
            {
                var gr = e.Graphics; gr.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                int mw = 34, mx = (brand.Width - mw) / 2;
                Logo.Draw(gr, new RectangleF(mx, 6, mw, mw), FpsUi.Neon, false);
                TextRenderer.DrawText(gr, "Fluide", FpsUi.Tiny, new Rectangle(0, 42, brand.Width, 16), FpsUi.Neon,
                    TextFormatFlags.HorizontalCenter);
            };
            var brandTip = new ToolTip(); brandTip.SetToolTip(brand, "Fluide — QG");
            _rail.Controls.Add(brand);

            string[] glyphs = { "🏠", "🚀", "🎮", "💉", "🧪", "🏆", "🩺", "⚙" };
            string[] tips = { "Dashboard", "Optimisations", "Jeux", "Check Up+", "Laboratoire", "Collection", "Consultation", "Système" };
            int y = 58;
            for (int i = 0; i < glyphs.Length; i++)
            {
                var cell = new NavCell(glyphs[i], tips[i]);
                cell.SetBounds(9, y, 48, 48);
                int idx = i;
                cell.Click += (s, e) => ShowPage(idx);
                _nav.Add(cell);
                _rail.Controls.Add(cell);
                y += 56;
            }

            var tools = new Label();
            tools.Text = "⋯"; tools.Font = new Font("Segoe UI", 15f);
            tools.ForeColor = FpsUi.Dim; tools.TextAlign = ContentAlignment.MiddleCenter;
            tools.Cursor = Cursors.Hand; tools.BackColor = Color.Transparent;
            tools.SetBounds(9, y + 4, 48, 40);
            var ttip = new ToolTip(); ttip.SetToolTip(tools, "Outils avancés (optimiseur complet, latence, DNS…)");
            tools.Click += (s, e) => _toolsMenu.Show(tools, new Point(tools.Width, 0));
            _rail.Controls.Add(tools);
        }

        // ------------------------------------------------------------------
        //  Navigation par pages
        // ------------------------------------------------------------------
        public void ShowPage(int idx)
        {
            if (idx < 0 || idx >= _pages.Length) return;

            if (_pages[idx] == null)
            {
                try { _pages[idx] = CreatePage(idx); }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Impossible d'ouvrir cette page :\n\n" + ex.Message,
                        "Fluide", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }
            for (int i = 0; i < _nav.Count; i++) _nav[i].Active = (i == idx);
            FpsPage page = _pages[idx];

            _host.SuspendLayout();
            if (_current >= 0 && _pages[_current] != null) _pages[_current].Visible = false;
            if (!_host.Controls.Contains(page)) _host.Controls.Add(page);
            page.Visible = true;
            page.BringToFront();
            _host.ResumeLayout();
            _current = idx;
            try { page.OnShown(); } catch { }
        }

        private FpsPage CreatePage(int idx)
        {
            switch (idx)
            {
                case 0: return new PageDashboard(this);
                case 1: return new PageOptimisations(this);
                case 2: return new PageGames(this);
                case 3: return new PageCheckup(this);
                case 4: return new PageLab(this);
                case 5: return new PageCollection(this);
                case 6: return new PageConsultation(this);
                case 7: return new PageSystem(this);
                default: return new PageDashboard(this);
            }
        }

        public void OpenDialog(Form f)
        {
            try { using (f) f.ShowDialog(this); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Fluide", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        public void Goto(int idx) { ShowPage(idx); }

        /// <summary>Page déjà créée à cet index (ou null) — pour le harnais de test visuel.</summary>
        internal FpsPage PageAt(int idx) { return idx >= 0 && idx < _pages.Length ? _pages[idx] : null; }

        /// <summary>Génère un rapport de santé HTML (état + optimisations actives + matériel),
        /// l'enregistre sur le Bureau et l'ouvre dans le navigateur. Lecture seule, partageable.</summary>
        public void GenerateHealthReport()
        {
            Cursor = Cursors.WaitCursor;
            System.Threading.Tasks.Task.Run(() =>
            {
                string path = null, err = null;
                try
                {
                    string html = Report.BuildHtml(Catalog.All(), Hardware.Detect());
                    string dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    path = System.IO.Path.Combine(dir, "Fluide-rapport-sante.html");
                    System.IO.File.WriteAllText(path, html, new System.Text.UTF8Encoding(false));
                }
                catch (Exception ex) { err = ex.Message; }
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        Cursor = Cursors.Default;
                        if (path != null)
                        {
                            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
                            catch { MessageBox.Show(this, "Rapport enregistré sur le Bureau :\n" + path, "Fluide", MessageBoxButtons.OK, MessageBoxIcon.Information); }
                        }
                        else MessageBox.Show(this, "Impossible de générer le rapport :\n\n" + err, "Fluide", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }));
                }
                catch { }
            });
        }

        /// <summary>Exporte le profil : les IDs des optimisations ACTUELLEMENT actives, dans un
        /// fichier choisi (.dtg). Lecture seule — pour sauvegarder ou partager sa config.</summary>
        public void ExportProfile()
        {
            string file;
            using (var dlg = new SaveFileDialog { Filter = "Profil Fluide (*.dtg)|*.dtg", FileName = "mon-profil-fluide.dtg", Title = "Exporter mon profil d'optimisations" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                file = dlg.FileName;
            }
            Cursor = Cursors.WaitCursor;
            System.Threading.Tasks.Task.Run(() =>
            {
                var ids = new System.Collections.Generic.List<string>();
                try { foreach (Tweak t in Catalog.All()) { bool? c = null; try { if (t.Check != null) c = t.Check(); } catch { } if (c == true) ids.Add(t.Id); } }
                catch { }
                string err = null;
                try { System.IO.File.WriteAllLines(file, ids); } catch (Exception ex) { err = ex.Message; }
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        Cursor = Cursors.Default;
                        if (err == null) MessageBox.Show(this, ids.Count + " optimisation(s) active(s) exportée(s) :\n" + file, "Profil exporté", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        else MessageBox.Show(this, "Échec de l'export :\n\n" + err, "Fluide", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }));
                }
                catch { }
            });
        }

        /// <summary>Importe un profil (.dtg) et applique les optimisations qu'il liste, après
        /// confirmation. Une sauvegarde .reg automatique est faite avant application.</summary>
        public void ImportProfile()
        {
            string file;
            using (var dlg = new OpenFileDialog { Filter = "Profil Fluide (*.dtg)|*.dtg|Tous les fichiers|*.*", Title = "Importer un profil d'optimisations" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                file = dlg.FileName;
            }
            var wanted = new System.Collections.Generic.HashSet<string>();
            try { foreach (string line in System.IO.File.ReadAllLines(file)) { string id = line.Trim(); if (id.Length > 0) wanted.Add(id); } }
            catch (Exception ex) { MessageBox.Show(this, "Lecture impossible :\n\n" + ex.Message, "Fluide", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }

            var list = new System.Collections.Generic.List<Tweak>();
            try { foreach (Tweak t in Catalog.All()) if (wanted.Contains(t.Id)) list.Add(t); } catch { }
            if (list.Count == 0) { MessageBox.Show(this, "Aucune optimisation reconnue dans ce fichier.", "Profil", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            if (MessageBox.Show(this, "Appliquer " + list.Count + " optimisation(s) de ce profil ?\n\nUne sauvegarde .reg automatique est réalisée avant.",
                "Importer un profil", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            Cursor = Cursors.WaitCursor;
            System.Threading.Tasks.Task.Run(() =>
            {
                try { Engine.Run(list, true, true, false, Log); } catch { }   // apply=true, backup .reg=true
                try { BeginInvoke((Action)(() => { Cursor = Cursors.Default;
                    MessageBox.Show(this, list.Count + " optimisation(s) du profil appliquée(s).", "Profil importé", MessageBoxButtons.OK, MessageBoxIcon.Information); })); }
                catch { }
            });
        }
    }

    // ----------------------------------------------------------------------
    //  Page de base.
    // ----------------------------------------------------------------------
    internal class FpsPage : UserControl
    {
        protected readonly DashboardForm Host;

        public FpsPage(DashboardForm host)
        {
            Host = host;
            Dock = DockStyle.Fill;
            BackColor = FpsUi.BgMain;
            DoubleBuffered = true;
        }

        public virtual void OnShown() { }

        /// <summary>Titre de page souligne neon.</summary>
        protected void PaintTitle(Graphics g, string title, string subtitle)
        {
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            TextRenderer.DrawText(g, title, FpsUi.H3, new Point(34, 26), FpsUi.Ink, TextFormatFlags.NoPadding);
            int w = TextRenderer.MeasureText(g, title, FpsUi.H3).Width;
            using (var pen = new Pen(FpsUi.Neon, 2f)) g.DrawLine(pen, 34, 50, 34 + w, 50);
            using (var pen = new Pen(FpsUi.Border)) g.DrawLine(pen, 34, 51, Width - 34, 51);
            if (!string.IsNullOrEmpty(subtitle))
                TextRenderer.DrawText(g, subtitle, FpsUi.Body, new Point(34, 64), FpsUi.Dim, TextFormatFlags.NoPadding);
        }
    }

    // ----------------------------------------------------------------------
    //  Icone de navigation.
    // ----------------------------------------------------------------------
    internal class NavCell : Panel
    {
        private readonly string _glyph;
        private bool _active, _hover;

        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool Active { get { return _active; } set { _active = value; Invalidate(); } }

        public NavCell(string glyph, string tip)
        {
            _glyph = glyph;
            DoubleBuffered = true; BackColor = Color.Transparent; Cursor = Cursors.Hand;
            var tt = new ToolTip(); tt.SetToolTip(this, tip);
            MouseEnter += (s, e) => { _hover = true; Invalidate(); };
            MouseLeave += (s, e) => { _hover = false; Invalidate(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rf = new RectangleF(5f, 2f, Width - 8f, Height - 4f);
            if (_active)
            {
                // Pastille néon-teintée + BARRE néon à gauche (indicateur d'onglet actif, façon FPS Doctor).
                using (var path = FpsUi.Round(rf, 12f))
                using (var br = new SolidBrush(Color.FromArgb(26, 129, 140, 248))) g.FillPath(br, path);
                using (var bar = FpsUi.Round(new RectangleF(0f, Height / 2f - 13f, 3.5f, 26f), 1.75f))
                using (var br = new SolidBrush(FpsUi.Neon)) g.FillPath(br, bar);
            }
            else if (_hover)
            {
                using (var path = FpsUi.Round(rf, 12f))
                using (var br = new SolidBrush(Color.FromArgb(16, 255, 255, 255))) g.FillPath(br, path);
            }
            TextRenderer.DrawText(g, _glyph, FpsUi.Glyph, ClientRectangle,
                _active ? FpsUi.Neon : (_hover ? FpsUi.Ink : FpsUi.Dim),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
