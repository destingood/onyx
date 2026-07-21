using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BTOptimizer
{
    // ----------------------------------------------------------------------
    //  Shell facon FPSDoctor : fenetre unique, barre laterale a icones qui
    //  echange le contenu (pages), mascotte docteur toujours visible.
    // ----------------------------------------------------------------------
    internal class DashboardForm : Form
    {
        private Panel _rail, _host;
        private PictureBox _mascot;
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
            Text = "DesTinGOOD — Bloc opératoire";
            ClientSize = new Size(1200, 760);
            MinimumSize = new Size(1040, 680);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = FpsUi.BgMain;
            Font = FpsUi.Body;
            DoubleBuffered = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            // Garantit le rendu sombre des menus (⋯, tray) dès le démarrage.
            try { Theme.Prime(); } catch { }

            _host = new Panel();
            _host.Dock = DockStyle.Fill;
            _host.BackColor = FpsUi.BgMain;
            Controls.Add(_host);

            BuildTools();
            BuildRail();
            Controls.Add(_rail);

            _mascot = new PictureBox();
            _mascot.SizeMode = PictureBoxSizeMode.Zoom;
            _mascot.BackColor = Color.Transparent;
            try { _mascot.Image = Assets.DoctorFinger; } catch { }
            _mascot.Size = new Size(180, 200);
            _mascot.Enabled = false;
            Controls.Add(_mascot);
            _mascot.BringToFront();

            BuildTray();
            Resize += OnResizeShell;

            _sysTimer = new Timer(); _sysTimer.Interval = 2000; _sysTimer.Tick += (s, e) => AutoTimer(); _sysTimer.Start();

            Shown += (s, e) => { SetDark(); ShowPage(0); PlaceMascot(); };
            FormClosing += (s, e) => Cleanup();
        }

        private void OnResizeShell(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized)
            {
                Hide(); _tray.Visible = true;
                if (!_trayShown) { _trayShown = true; _tray.ShowBalloonTip(2000, "DesTinGOOD", "Toujours actif. Double-clic pour rouvrir.", ToolTipIcon.Info); }
                return;
            }
            PlaceMascot();
        }

        // ------------------------------------------------------------------
        //  Outils avancés (accès à TOUTES les fonctions de l'app).
        // ------------------------------------------------------------------
        private void BuildTools()
        {
            _toolsMenu = new ContextMenuStrip();
            var m = _toolsMenu.Items;

            m.Add("🛠  Optimiseur complet (presets, auto-tune, gardien, sauvegarde…)", null, (s, e) => OpenDialog(new MainForm()));
            m.Add(new ToolStripSeparator());

            var jeux = new ToolStripMenuItem("🎮  Jeux");
            jeux.DropDownItems.Add("Priorité CPU par jeu", null, (s, e) => OpenDialog(new GameProfileForm(Log)));
            jeux.DropDownItems.Add("Qualité réseau en jeu", null, (s, e) => OpenDialog(new NetworkForm(Log)));
            jeux.DropDownItems.Add("Jeux & disques", null, (s, e) => OpenDialog(new DiskForm(Log)));
            jeux.DropDownItems.Add("Boutiques & contenu en jeu", null, (s, e) => OpenDialog(new ShopFixForm(Log)));
            jeux.DropDownItems.Add("Bibliothèques & applis de jeu", null, (s, e) => OpenDialog(new LibsForm(Log)));
            jeux.DropDownItems.Add("Exclusions antivirus (jeux)", null, (s, e) => OpenDialog(new DefenderForm(Log)));
            jeux.DropDownItems.Add("Prêt pour le match ?", null, (s, e) => OpenDialog(new TournamentForm(Log)));
            m.Add(jeux);

            var perf = new ToolStripMenuItem("📈  Performances & FPS");
            perf.DropDownItems.Add("Objectif 500 FPS", null, (s, e) => OpenDialog(new Fps500Form(Log)));
            perf.DropDownItems.Add("FPS en direct", null, (s, e) => OpenDialog(new FpsMonForm(Log)));
            perf.DropDownItems.Add("Benchmark rapide", null, (s, e) => OpenDialog(new BenchForm(Log)));
            perf.DropDownItems.Add("Réglages d'écran", null, (s, e) => OpenDialog(new DisplayForm(Log)));
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
            reg.DropDownItems.Add("Périphériques (erreurs)", null, (s, e) => OpenDialog(new DeviceManagerForm(Log)));
            reg.DropDownItems.Add("Programmes au démarrage", null, (s, e) => OpenDialog(new StartupForm(Log)));
            reg.DropDownItems.Add("Services Windows", null, (s, e) => OpenDialog(new ServicesForm(Log)));
            m.Add(reg);

            m.Add("🔁  Restauration (points & sauvegardes)", null, (s, e) => OpenDialog(new RestoreForm(Log)));
            m.Add(new ToolStripSeparator());
            m.Add("❓  J'ai un problème…", null, (s, e) => OpenDialog(new HelpNavForm(Log)));
            m.Add("ℹ  À propos de DesTinGOOD", null, (s, e) => OpenDialog(new AboutForm()));
            m.Add("🔑  Activer Pro / entrer une clé", null, (s, e) => OpenDialog(new LicenseKeyForm("")));
        }

        private void BuildTray()
        {
            _tray = new NotifyIcon();
            try { _tray.Icon = Icon; } catch { }
            _tray.Text = "DesTinGOOD"; _tray.Visible = false;
            _tray.DoubleClick += (s, e) => RestoreFromTray();
            var m = new ContextMenuStrip();
            m.Items.Add("Ouvrir DesTinGOOD", null, (s, e) => RestoreFromTray());
            m.Items.Add("▶ MODE JEU on/off  (Ctrl+Alt+G)", null, (s, e) => ToggleBoost());
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

        private void AutoTimer()
        {
            try
            {
                bool wanted = Native.IsGameFullscreen();
                if (wanted != Native.TimerActive) Native.SetTimer1ms(wanted);
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
            try { UnregisterHotKey(Handle, HotkeyId); } catch { }
            try { if (GameBoost.IsActive) GameBoost.Deactivate(delegate (string a, int b) { }); } catch { }
            try { Native.SetTimer1ms(false); } catch { }
            try { if (_sysTimer != null) _sysTimer.Stop(); } catch { }
            try { if (_tray != null) { _tray.Visible = false; _tray.Dispose(); } } catch { }
        }

        private void PlaceMascot()
        {
            if (_mascot == null) return;
            _mascot.Location = new Point(ClientSize.Width - _mascot.Width - 8, ClientSize.Height - _mascot.Height - 4);
            _mascot.BringToFront();
        }

        /// <summary>Y maximal utilisable par une page avant d'atteindre la mascotte (coin bas-droit).</summary>
        public int ContentBottom(int margin)
        {
            int b = ClientSize.Height - margin;
            if (_mascot != null && _mascot.Visible) b = Math.Min(b, _mascot.Top - 10);
            return b;
        }

        /// <summary>X maximal utilisable par du contenu bas-droit avant d'atteindre la mascotte.</summary>
        public int ContentRight(int margin)
        {
            int r = ClientSize.Width - margin;
            if (_mascot != null && _mascot.Visible) r = Math.Min(r, _mascot.Left - 12);
            return r;
        }

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

            var brand = new Label();
            brand.Text = "DTG"; brand.Font = new Font("Segoe UI Black", 9f);
            brand.ForeColor = FpsUi.Neon; brand.TextAlign = ContentAlignment.MiddleCenter;
            brand.Dock = DockStyle.Bottom; brand.Height = 42;
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
                        "DesTinGOOD", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            // Mascotte seulement sur les pages aérées (évite de recouvrir des contrôles).
            // Visibilité fixée AVANT OnShown pour que la page réserve la bonne zone au layout.
            _mascot.Visible = (idx == 0 || idx == 4 || idx == 5 || idx == 6);
            PlaceMascot();
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
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "DesTinGOOD", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        public void Goto(int idx) { ShowPage(idx); }

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
                    path = System.IO.Path.Combine(dir, "DesTinGOOD-rapport-sante.html");
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
                            catch { MessageBox.Show(this, "Rapport enregistré sur le Bureau :\n" + path, "DesTinGOOD", MessageBoxButtons.OK, MessageBoxIcon.Information); }
                        }
                        else MessageBox.Show(this, "Impossible de générer le rapport :\n\n" + err, "DesTinGOOD", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }));
                }
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
            var rf = new RectangleF(1.5f, 1.5f, Width - 3f, Height - 3f);
            if (_active || _hover)
            {
                using (var path = FpsUi.Round(rf, 12f))
                {
                    using (var br = new SolidBrush(_active ? Color.FromArgb(20, 40, 30) : FpsUi.Card)) g.FillPath(br, path);
                    if (_active) using (var pen = new Pen(FpsUi.Neon)) g.DrawPath(pen, path);
                }
            }
            TextRenderer.DrawText(g, _glyph, FpsUi.Glyph, ClientRectangle, _active ? FpsUi.Neon : FpsUi.Dim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
