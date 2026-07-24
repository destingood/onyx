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
            try { AnimSettings.Recompute(); } catch { }   // état initial de l'interrupteur « Animations »

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
            var anim = new ToolStripMenuItem("Animations de l'interface") { Checked = AnimSettings.UserEnabled };
            anim.Click += (s, e) => { bool now = !AnimSettings.UserEnabled; AnimSettings.UserEnabled = now; anim.Checked = now; };
            reg.DropDownItems.Add(anim);
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

                bool knownGame = false;
                try { knownGame = GameScan.RunningKnownGame() != null; } catch { }

                // Animations coupées quand un jeu tourne (plein écran, jeu connu, ou Mode Jeu actif).
                bool anyGame = wanted || knownGame;
                try { anyGame = anyGame || GameBoost.IsActive; } catch { }
                try { AnimSettings.SetGameRunning(anyGame); } catch { }

                // Viseur AUTO en jeu : affiché dès qu'un jeu tourne (jeu connu ou plein écran), retiré au bureau.
                if (Crosshair.AutoGameEnabled)
                    Crosshair.AutoTick(knownGame || wanted, knownGame || wanted);
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
        // Barre latérale repliable : étroite (icônes seules) ou large (icônes + libellés).
        private const int RailNarrow = 66, RailWide = 232, BrandH = 64, ProfileH = 64;
        private bool _railOpen;
        private Panel _railBrand, _railProfile;
        private NavCell _tools;

        private void BuildRail()
        {
            _rail = new Panel();
            _rail.Dock = DockStyle.Left;
            _rail.Width = RailNarrow;
            _rail.BackColor = FpsUi.RailBg;
            _rail.Paint += (s, e) => { using (var pen = new Pen(FpsUi.Border)) e.Graphics.DrawLine(pen, _rail.Width - 1, 0, _rail.Width - 1, _rail.Height); };

            // --- Haut : marque + avatar. Un clic replie/déplie tout le menu. ---
            _railBrand = new Panel { Dock = DockStyle.Top, Height = BrandH, BackColor = Color.Transparent, Cursor = Cursors.Hand };
            _railBrand.Paint += (s, e) =>
            {
                var gr = e.Graphics;
                gr.SmoothingMode = SmoothingMode.AntiAlias;
                gr.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                int mw = 34, mx = _railOpen ? 16 : (_railBrand.Width - mw) / 2;
                Logo.Draw(gr, new RectangleF(mx, (BrandH - mw) / 2f, mw, mw), FpsUi.Neon, false);
                if (_railOpen)
                    TextRenderer.DrawText(gr, "Fluide", FpsUi.H3,
                        new Rectangle(mx + mw + 12, 0, _railBrand.Width - mx - mw - 20, BrandH), FpsUi.Ink,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            };
            _railBrand.Click += (s, e) => ToggleRail();
            var brandTip = new ToolTip(); brandTip.SetToolTip(_railBrand, "Replier / déplier le menu");
            _rail.Controls.Add(_railBrand);

            // --- Bas : bloc profil (mène à la Collection de badges). ---
            _railProfile = new Panel { Dock = DockStyle.Bottom, Height = ProfileH, BackColor = Color.Transparent, Cursor = Cursors.Hand };
            _railProfile.Paint += (s, e) => PaintProfile(e.Graphics);
            _railProfile.Click += (s, e) => ShowPage(5);   // page Collection
            var profTip = new ToolTip(); profTip.SetToolTip(_railProfile, "Ma collection de badges");
            _rail.Controls.Add(_railProfile);

            string[] glyphs = { "🏠", "🚀", "🎮", "💉", "🧪", "🏆", "🩺", "⚙" };
            string[] tips = { "Dashboard", "Optimisations", "Jeux", "Check Up+", "Laboratoire", "Collection", "Consultation", "Système" };
            for (int i = 0; i < glyphs.Length; i++)
            {
                var cell = new NavCell(glyphs[i], tips[i]);
                int idx = i;
                cell.Click += (s, e) => ShowPage(idx);
                _nav.Add(cell);
                _rail.Controls.Add(cell);
            }

            _tools = new NavCell("⋯", "Outils avancés (optimiseur complet, latence, DNS…)");
            _tools.Label = "Outils avancés";
            _tools.Click += (s, e) => _toolsMenu.Show(_tools, new Point(_tools.Width, 0));
            _rail.Controls.Add(_tools);

            LayoutRail();
        }

        /// <summary>Replie / déplie la barre, avec animation de largeur si les animations sont actives.</summary>
        private void ToggleRail()
        {
            _railOpen = !_railOpen;
            int from = _rail.Width, to = _railOpen ? RailWide : RailNarrow;

            foreach (NavCell c in _nav) c.Expanded = _railOpen;
            if (_tools != null) _tools.Expanded = _railOpen;

            if (Anim.On)
                Anim.Tween(160, p => { _rail.Width = (int)(from + (to - from) * p); LayoutRail(); },
                           () => { _rail.Width = to; LayoutRail(); });
            else { _rail.Width = to; LayoutRail(); }
        }

        /// <summary>Place les items selon la largeur courante (marque en haut, profil en bas).</summary>
        private void LayoutRail()
        {
            if (_rail == null) return;
            int cellW = Math.Max(48, _rail.Width - 18);
            int y = BrandH + 12;
            foreach (NavCell c in _nav) { c.SetBounds(9, y, cellW, 48); y += 56; }
            if (_tools != null) _tools.SetBounds(9, y + 4, cellW, 44);
            if (_railBrand != null) _railBrand.Invalidate();
            if (_railProfile != null) _railProfile.Invalidate();
        }

        // Bloc profil : pastille + nom + badge courant (façon « carte de membre »).
        private void PaintProfile(Graphics gr)
        {
            gr.SmoothingMode = SmoothingMode.AntiAlias;
            gr.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int d = 34, x = _railOpen ? 16 : (_railProfile.Width - d) / 2, cy = (ProfileH - d) / 2;
            var circ = new RectangleF(x, cy, d, d);
            using (var br = new SolidBrush(Color.FromArgb(30, 129, 140, 248))) gr.FillEllipse(br, circ);
            using (var pen = new Pen(Color.FromArgb(120, FpsUi.Neon.R, FpsUi.Neon.G, FpsUi.Neon.B), 1.4f)) gr.DrawEllipse(pen, circ);
            Logo.Draw(gr, new RectangleF(x + 7, cy + 7, d - 14, d - 14), FpsUi.Neon, false);

            if (!_railOpen) return;

            int tx = x + d + 12, tw = _railProfile.Width - tx - 26;
            TextRenderer.DrawText(gr, "DesTinGOOD", FpsUi.Small, new Rectangle(tx, cy - 1, tw, 18), FpsUi.Ink,
                TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(gr, "Ma collection", FpsUi.Tiny, new Rectangle(tx, cy + 16, tw, 16), FpsUi.Dim,
                TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(gr, "›", FpsUi.H3, new Rectangle(_railProfile.Width - 24, 0, 20, ProfileH), FpsUi.Dim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
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
            int prev = _current;

            // L'échange réel (masquer l'ancienne, montrer la nouvelle) est encapsulé pour pouvoir
            // le jouer SOUS un cross-fade quand on passe d'une page à une autre.
            Action commit = delegate
            {
                _host.SuspendLayout();
                if (prev >= 0 && _pages[prev] != null) _pages[prev].Visible = false;
                if (!_host.Controls.Contains(page)) _host.Controls.Add(page);
                page.Visible = true;
                page.BringToFront();
                _host.ResumeLayout();
                _current = idx;
                try { page.OnShown(); } catch { }
            };

            commit();   // échange instantané (le cross-fade par capture flashait en noir sur certains GPU)
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
            try { AnimFx.HookDialog(f); using (f) f.ShowDialog(this); }
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
    /// <summary>
    /// Item de la barre latérale. Deux rendus selon l'état du rail : icône seule (replié) ou
    /// icône + libellé (déplié). L'item actif est ENCADRÉ en néon (repère net, façon FPS Doctor),
    /// et un badge chiffré peut signaler du nouveau (jeux détectés, badges gagnés…).
    /// </summary>
    internal class NavCell : Panel
    {
        private readonly string _glyph;
        private bool _active, _hover, _expanded;
        private int _badge;

        /// <summary>Libellé affiché quand le rail est déplié (par défaut : l'info-bulle).</summary>
        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string Label { get; set; }

        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool Active { get { return _active; } set { _active = value; Invalidate(); } }

        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool Expanded { get { return _expanded; } set { _expanded = value; Invalidate(); } }

        /// <summary>Pastille chiffrée (0 = aucune).</summary>
        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int Badge { get { return _badge; } set { _badge = value; Invalidate(); } }

        public NavCell(string glyph, string tip)
        {
            _glyph = glyph;
            Label = tip;
            DoubleBuffered = true; BackColor = Color.Transparent; Cursor = Cursors.Hand;
            var tt = new ToolTip(); tt.SetToolTip(this, tip);
            MouseEnter += (s, e) => { _hover = true; Invalidate(); };
            MouseLeave += (s, e) => { _hover = false; Invalidate(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rf = new RectangleF(5f, 2f, Width - 10f, Height - 4f);

            if (_active)
            {
                // Fond teinté + CONTOUR néon : l'item courant se repère d'un coup d'œil, replié
                // comme déplié (la barre latérale d'avant disparaissait une fois le rail élargi).
                using (var path = FpsUi.Round(rf, 12f))
                {
                    using (var br = new SolidBrush(Color.FromArgb(26, 129, 140, 248))) g.FillPath(br, path);
                    using (var pen = new Pen(FpsUi.Neon, 1.4f)) g.DrawPath(pen, path);
                }
            }
            else if (_hover)
            {
                using (var path = FpsUi.Round(rf, 12f))
                using (var br = new SolidBrush(Color.FromArgb(16, 255, 255, 255))) g.FillPath(br, path);
            }

            Color fg = _active ? FpsUi.Neon : (_hover ? FpsUi.Ink : FpsUi.Dim);

            if (_expanded)
            {
                var iconR = new Rectangle(10, 0, 38, Height);
                TextRenderer.DrawText(g, _glyph, FpsUi.Glyph, iconR, fg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                var textR = new Rectangle(54, 0, Math.Max(10, Width - 54 - 30), Height);
                TextRenderer.DrawText(g, (Label ?? "").ToUpperInvariant(), FpsUi.Small, textR, fg,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
            else
            {
                TextRenderer.DrawText(g, _glyph, FpsUi.Glyph, ClientRectangle, fg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            if (_badge > 0) PaintBadge(g);
        }

        private void PaintBadge(Graphics g)
        {
            string txt = _badge > 99 ? "99+" : _badge.ToString();
            int d = 17;
            // Déplié : à droite de la ligne. Replié : en pastille sur le coin de l'icône.
            int bx = _expanded ? Width - d - 12 : Width / 2 + 6;
            int by = _expanded ? (Height - d) / 2 : 5;
            var circ = new Rectangle(bx, by, d, d);
            using (var br = new SolidBrush(FpsUi.Neon)) g.FillEllipse(br, circ);
            TextRenderer.DrawText(g, txt, FpsUi.Tiny, circ, FpsUi.RailBg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }
}
