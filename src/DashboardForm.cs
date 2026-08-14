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
        private FlowLayoutPanel _pageToolBar;
        private readonly System.Collections.Generic.List<NavCell> _nav = new System.Collections.Generic.List<NavCell>();
        private readonly FpsPage[] _pages = new FpsPage[8];
        private int _current = -1;

        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr h, int id);
        private const int HotkeyId = 0xB71, HotkeyIdShow = 0xB72, WM_HOTKEY = 0x0312;
        private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000;
        private NotifyIcon _tray;
        private Panel _bandeauMaj;   // rappel de mise à jour, en haut de la fenêtre
        private ContextMenuStrip _toolsMenu;
        private Timer _sysTimer;
        private bool _trayShown;

        public DashboardForm()
        {
            Text = "ONYX — QG";
            ClientSize = new Size(1200, 760);
            MinimumSize = new Size(1040, 680);
            StartPosition = FormStartPosition.CenterScreen;
            try { WindowBounds.Restore(this); } catch { }   // rouvre où l'utilisateur avait laissé la fenêtre
            BackColor = FpsUi.BgMain;
            Font = FpsUi.Body;
            DoubleBuffered = true;
            try { Icon = Logo.MakeIcon(32, FpsUi.Gold); } catch { try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { } }
            // Garantit le rendu sombre des menus (⋯, tray) dès le démarrage.
            try { Theme.Prime(); } catch { }
            try { AnimSettings.Recompute(); } catch { }   // état initial de l'interrupteur « Animations »

            _host = new Panel();
            _host.Dock = DockStyle.Fill;
            _host.BackColor = FpsUi.BgMain;
            Controls.Add(_host);

            // Rangée d'outils de page (chips), intégrée SOUS LE TITRE : overlay non docké dans _host,
            // amené au premier plan par-dessus la page. Les pages laissent la place via Host.ContentTop().
            _pageToolBar = new FlowLayoutPanel
            {
                BackColor = FpsUi.BgMain, WrapContents = false, AutoScroll = true, Visible = false,
                Padding = new Padding(0)
            };
            _host.Controls.Add(_pageToolBar);
            // Réserve la largeur de la barre REPLIÉE : le contenu démarre juste après elle.
            // Quand elle s'ouvre, elle passe PAR-DESSUS le contenu — rien n'est remis en page.
            Padding = new Padding(RailNarrow, 0, 0, 0);

            BuildTools();
            BuildRail();
            Controls.Add(_rail);
            _rail.BringToFront();   // la barre superposée doit rester au-dessus du contenu

            BuildTray();
            // Rappel VISIBLE d'une mise à jour en attente. La notification Windows est fugace (elle
            // passe pendant une partie, ou n'apparaît pas du tout si les notifications sont
            // coupées) : le titre de la fenêtre, lui, reste sous les yeux à chaque ouverture tant
            // que la version n'est pas installée.
            try
            {
                string maj = UpdateFlag.EnAttente();
                if (maj != null)
                {
                    // Rappel PERMANENT et discret : tant que la mise à jour n'est pas posée, le
                    // titre le dit. Une notification se manque, un titre reste sous les yeux.
                    Text = "ONYX — QG     •  mise à jour " + maj + " disponible";
                    MonteBandeauMaj(maj);   // et, DANS l'app, un bandeau qui ne dépend d'aucune notification
                    Shown += (s, e) =>
                    {
                        try { Log("Une mise à jour d'ONYX est disponible : version " + maj
                                + " (menu ⋯ → « Vérifier les mises à jour »).", 2); } catch { }
                        // ANNONCE FRANCHE, mais UNE SEULE FOIS par version : reposer la question à
                        // chaque lancement transformerait l'information en harcèlement, et
                        // l'utilisateur finirait par cliquer sans lire.
                        try { AnnonceMaj(maj); } catch { }
                    };
                }
            }
            catch { }
            StartGuardian();   // contrôle silencieux des signaux vitaux (1×/jour ; muet si tout va bien)
            // Démarrage minimisé (optionnel) : ONYX naît dans la zone de notification — le Gardien
            // surveille chaque jour, aucune fenêtre ne s'impose. Ctrl+Alt+O le fait apparaître.
            if (TrayStartEnabled && Environment.GetEnvironmentVariable("BT_UISHOT") == null
                && Environment.GetEnvironmentVariable("BT_UITEST") != "1")
                Shown += (s, e) => { try { Hide(); _tray.Visible = true; } catch { } };
            // « Quoi de neuf » : une fois après une mise à jour (jamais au 1er lancement, jamais en mode tray).
            if (!TrayStartEnabled)
                Shown += (s, e) => { try { BeginInvoke((Action)(() => WhatsNew.ShowIfUpdated(this))); } catch { } };
            Resize += OnResizeShell;
            BadgeStore.OnNewBadge += OnNewBadge;   // toast « nouveau badge débloqué ! »

            _sysTimer = new Timer(); _sysTimer.Interval = 2000; _sysTimer.Tick += (s, e) => AutoTimer(); _sysTimer.Start();
            try { DiscordPresence.StartIfEnabled(); } catch { }   // présence Discord (parité FPSDoctor)
            try { Audience.PingSiActive(); } catch { }            // comptage des installations (voir Audience)

            Shown += (s, e) =>
            {
                SetDark(); ShowPage(0);
                if (_rail != null) { _rail.Height = ClientSize.Height; _rail.BringToFront(); }
                // Rétablit les overlays activés au dernier lancement (le shell remplace MainForm
                // qui portait ces appels — sans ça le viseur ne réapparaissait plus au démarrage).
                try { Crosshair.ShowOnStartupIfEnabled(Log); } catch { }
                try { StatsOverlayManager.ShowOnStartupIfEnabled(Log); } catch { }
                // Le filtre couleur avait été oublié lors de cette même migration. Une rampe gamma
                // ne survit pas à la fermeture de session : sans cet appel, « Appliquer &
                // enregistrer » ne tenait que jusqu'au redémarrage, et le réglage sauvegardé
                // n'était rétabli que si l'utilisateur ouvrait « Optimiseur complet ».
                try { ColorFilter.ReapplyOnStartup(Log); } catch { }
            };

#if !BTTEST
            // Cerveau IA LOCAL : amorçage au démarrage (gratuit, 100 % sur la machine). Bootstrap
            // décide seul — activation silencieuse si déjà prêt, sinon UNE question « Oui/Non » puis
            // installation du modèle ADAPTÉ à cette machine. Différé de 20 s (ne pas gêner le
            // démarrage) ; jamais dans le harnais de test ; « désactive l'ia » coupe et bloque.
            var iaTimer = new Timer { Interval = 20000 };
            iaTimer.Tick += (s, e) =>
            {
                iaTimer.Stop(); iaTimer.Dispose();
                System.Threading.Tasks.Task.Run(() => LocalBrain.Bootstrap(this, Log));
            };
            iaTimer.Start();
#endif
            FormClosing += (s, e) => { try { WindowBounds.Save(this); } catch { } Cleanup(); };
        }

        private void OnResizeShell(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized)
            {
                Hide(); _tray.Visible = true;
                if (!_trayShown) { _trayShown = true; _tray.ShowBalloonTip(2000, "ONYX", "Toujours actif. Double-clic pour rouvrir.", ToolTipIcon.Info); }
                return;
            }
            // Barre superposée (non dockée) : sa hauteur ne suit plus automatiquement la fenêtre.
            if (_rail != null && _rail.Height != ClientSize.Height) _rail.Height = ClientSize.Height;
            PositionPageTools();   // la rangée d'outils (overlay) suit la largeur
        }

        // ------------------------------------------------------------------
        //  Outils avancés (accès à TOUTES les fonctions de l'app).
        // ------------------------------------------------------------------
        private void BuildTools()
        {
            _toolsMenu = new ContextMenuStrip();
            var m = _toolsMenu.Items;

            // Les groupes de ce menu CALQUENT les catégories du rail : chaque outil avancé est
            // rangé sous la catégorie à laquelle il appartient (Optimisations, Jeux, Check Up+,
            // Laboratoire, Système) au lieu d'un fourre-tout « outils avancés ».

            var opti = new ToolStripMenuItem("🚀  Optimisations");
            opti.DropDownItems.Add("🛠 Optimiseur complet (presets, auto-tune, gardien, sauvegarde…)", null, (s, e) => OpenDialog(new MainForm()));
            opti.DropDownItems.Add("🎚 Mode SIMPLE (interrupteurs immédiats)", null, (s, e) => OpenDialog(new SimpleOptiForm(Catalog.All(), () => License.ProUnlocked, Log)));
            opti.DropDownItems.Add("⚡ Config auto adaptée à mon PC (+ preuve)", null, (s, e) => OpenDialog(new AutoConfigForm(Log)));
            opti.DropDownItems.Add(new ToolStripSeparator());
            opti.DropDownItems.Add("💾 Exporter mon profil…", null, (s, e) => ExportProfile());
            opti.DropDownItems.Add("💾 Importer un profil…", null, (s, e) => ImportProfile());
            opti.DropDownItems.Add("🔁 Restauration (points & sauvegardes)", null, (s, e) => OpenDialog(new RestoreForm(Log)));
            opti.DropDownItems.Add("📝 Problèmes rencontrés (registre local)", null, (s, e) => OpenDialog(new ProblemForm(Log)));
            opti.DropDownItems.Add("⏱ Latence DPC/ISR par pilote (mesure en direct)", null, (s, e) => MesurerDpc());
            m.Add(opti);

            var jeux = new ToolStripMenuItem("🎮  Jeux");
            jeux.DropDownItems.Add("Priorité CPU par jeu", null, (s, e) => OpenDialog(new GameProfileForm(Log)));
            jeux.DropDownItems.Add("🕹 Mes jeux (boost par jeu : léger / complet)", null, (s, e) => OpenDialog(new GamesForm(Log)));
            jeux.DropDownItems.Add("Réglages Mode Jeu (exclusions)", null, (s, e) => OpenDialog(new GameModeForm(Log)));
            jeux.DropDownItems.Add("Qualité réseau en jeu", null, (s, e) => OpenDialog(new NetworkForm(Log)));
            jeux.DropDownItems.Add("Jeux & disques", null, (s, e) => OpenDialog(new DiskForm(Log)));
            jeux.DropDownItems.Add("🧹 Jeux dormants (récupérer de l'espace)", null, (s, e) => OpenDialog(new DormantGamesForm(Log)));
            jeux.DropDownItems.Add("Boutiques & contenu en jeu", null, (s, e) => OpenDialog(new ShopFixForm(Log)));
            jeux.DropDownItems.Add("🛠 Réparer l'installation des jeux (EA/Steam/Epic/Battle.net)", null, (s, e) => OpenDialog(new LauncherFixForm(Log)));
            jeux.DropDownItems.Add("Bibliothèques & applis de jeu", null, (s, e) => OpenDialog(new LibsForm(Log)));
            jeux.DropDownItems.Add("Prérequis & installation automatique", null, (s, e) => OpenDialog(new AutoInstallForm(Log)));
            jeux.DropDownItems.Add("Exclusions antivirus (jeux)", null, (s, e) => OpenDialog(new DefenderForm(Log)));
            jeux.DropDownItems.Add("Prêt pour le match ?", null, (s, e) => OpenDialog(new TournamentForm(Log)));
            jeux.DropDownItems.Add("Réafficher les jeux masqués", null, (s, e) =>
            {
                int n = GameHidden.Count;
                if (n == 0)
                {
                    MessageBox.Show(this, "Aucun jeu n'est masqué.", "ONYX",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (MessageBox.Show(this, "Réafficher les " + n + " jeu(x) masqué(s) ?", "ONYX",
                        MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                GameHidden.ClearAll();
                ShowPage(2);   // page Jeux : elle se redessine a l'affichage
            });
            m.Add(jeux);

            // Sous-menus TRIÉS par sections (en-têtes grisés) : diagnostic → mesures → inventaire.
            var check = new ToolStripMenuItem("🩺  Check Up+");
            check.DropDownItems.Add(MenuHead("🔎 Diagnostic"));
            check.DropDownItems.Add("Santé de mon PC", null, (s, e) => OpenDialog(new HealthForm(Log)));
            check.DropDownItems.Add("Qui ralentit mon PC ?", null, (s, e) => OpenDialog(new BloatForm(Log)));
            check.DropDownItems.Add("Réglages néfastes", null, (s, e) => OpenDialog(new CheckupForm(Log)));
            check.DropDownItems.Add(new ToolStripSeparator());
            check.DropDownItems.Add(MenuHead("🌡 Mesures & stress"));
            check.DropDownItems.Add("Températures & throttling", null, (s, e) => OpenDialog(new ThermalForm(Log)));
            check.DropDownItems.Add("Moniteur matériel", null, (s, e) => OpenDialog(new MonitorForm()));
            check.DropDownItems.Add("Stabilité du PC", null, (s, e) => OpenDialog(new StabilityForm(Log)));
            check.DropDownItems.Add("🎯 Mon pilote GPU est-il instable ?", null, (s, e) => ShowGpuStability());
            check.DropDownItems.Add("🎮 Vérifier les fichiers d'un jeu (Steam)", null, (s, e) => ShowSteamValidate());
            check.DropDownItems.Add("🩺 Diagnostic des journaux Windows", null, (s, e) => ShowLogDoctor());
            check.DropDownItems.Add("Test de stress CPU", null, (s, e) => OpenDialog(new StressForm(Log)));
            check.DropDownItems.Add(new ToolStripSeparator());
            check.DropDownItems.Add(MenuHead("📋 Inventaire & entretien"));
            check.DropDownItems.Add("Composants & diagnostic", null, (s, e) => OpenDialog(new SystemInfoForm(Log)));
            check.DropDownItems.Add("Rapport de santé (HTML, à partager)", null, (s, e) => GenerateHealthReport());
            check.DropDownItems.Add("🧰 Entretien du PC (nettoyage, TRIM, caches, DNS — 6 routines)", null, (s, e) => OpenDialog(new MaintenanceForm(Log)));
            m.Add(check);

            var labo = new ToolStripMenuItem("🧪  Laboratoire");
            labo.DropDownItems.Add(MenuHead("🎯 FPS"));
            labo.DropDownItems.Add("Objectif 500 FPS", null, (s, e) => OpenDialog(new Fps500Form(Log)));
            labo.DropDownItems.Add("FPS en direct", null, (s, e) => OpenDialog(new FpsMonForm(Log)));
            labo.DropDownItems.Add("Benchmark FPS (avant/après)", null, (s, e) => OpenDialog(new BenchmarkFpsForm(Log)));
            labo.DropDownItems.Add("Benchmark rapide (CPU/GPU)", null, (s, e) => OpenDialog(new BenchForm(Log)));
            labo.DropDownItems.Add(new ToolStripSeparator());
            labo.DropDownItems.Add(MenuHead("🖥 Écran & bureau"));
            labo.DropDownItems.Add("Réglages d'écran", null, (s, e) => OpenDialog(new DisplayForm(Log)));
            labo.DropDownItems.Add("🪟 Fenêtres qui saccadent (bureau, DWM)", null, (s, e) => OpenDialog(new WindowLagForm(Log)));
            labo.DropDownItems.Add(new ToolStripSeparator());
            labo.DropDownItems.Add(MenuHead("⏱ Latence"));
            labo.DropDownItems.Add("⏱ Latence en direct (DPC/ISR)", null, (s, e) => OpenDialog(new LiveMonForm(Log)));
            labo.DropDownItems.Add("⏱ Guide latence & input lag", null, (s, e) => OpenDialog(new LatencyGuideForm(Log)));
            labo.DropDownItems.Add(new ToolStripSeparator());
            labo.DropDownItems.Add(MenuHead("📚 Guides"));
            labo.DropDownItems.Add("🎥 Streamer sans lag (RTSS/OBS/NVIDIA)", null, (s, e) => OpenDialog(new StreamGuideForm(Log)));
            labo.DropDownItems.Add("🧩 BIOS & manips manuelles (XMP, ReBAR…)", null, (s, e) => OpenDialog(new BiosGuideForm(Log)));
            m.Add(labo);

            var sys = new ToolStripMenuItem("⚙  Système");
            var net = new ToolStripMenuItem("🌐  Réseau");
            net.DropDownItems.Add("📶 Ma connexion & ma box (fibre, ADSL, 4G/5G : mesures + branchement)", null, (s, e) => OpenDialog(new MobileNetForm(Log)));
            net.DropDownItems.Add("DNS rapide", null, (s, e) => OpenDialog(new DnsForm(Log)));
            net.DropDownItems.Add("Réglages TCP/IP", null, (s, e) => OpenDialog(new NetTuneForm(Log)));
            net.DropDownItems.Add("Trajet réseau", null, (s, e) => OpenDialog(new NetRouteForm(Log)));
            net.DropDownItems.Add("📄 Assistant opérateur (journal, dossier support, réglages box)", null, (s, e) => OpenDialog(new OperatorHelpForm(Log)));
            sys.DropDownItems.Add(net);
            sys.DropDownItems.Add(new ToolStripSeparator());
            sys.DropDownItems.Add(MenuHead("🖱 Périphériques"));
            sys.DropDownItems.Add("Fréquence de la souris", null, (s, e) => OpenDialog(new MouseForm(Log)));
            sys.DropDownItems.Add("Audio & enceintes", null, (s, e) => OpenDialog(new AudioForm(Log)));
            sys.DropDownItems.Add("Périphériques (erreurs)", null, (s, e) => OpenDialog(new DeviceManagerForm(Log)));
            sys.DropDownItems.Add(new ToolStripSeparator());
            sys.DropDownItems.Add(MenuHead("🚀 Démarrage & fond"));
            sys.DropDownItems.Add("Programmes au démarrage", null, (s, e) => OpenDialog(new StartupForm(Log)));
            sys.DropDownItems.Add("Services Windows", null, (s, e) => OpenDialog(new ServicesForm(Log)));
            sys.DropDownItems.Add("Discord (ce qui pèse en jeu)", null, (s, e) => OpenDialog(new DiscordForm(Log)));
            sys.DropDownItems.Add("🗑 Retirer les applis Windows (dé-bloatware)", null, (s, e) => OpenDialog(new BloatRemoveForm(Log)));
            sys.DropDownItems.Add(new ToolStripSeparator());
            sys.DropDownItems.Add(MenuHead("🪟 Windows"));
            sys.DropDownItems.Add("🪪 État de la licence Windows (activation, clé OEM)", null, (s, e) => OpenDialog(new WindowsLicenseForm()));
            sys.DropDownItems.Add("🛡 Smart App Control (applications bloquées au lancement)", null, (s, e) => OpenDialog(new SmartAppControlForm(Log)));
            sys.DropDownItems.Add("Redémarrer l'explorateur Windows", null, (s, e) => RestartExplorerConfirm());
            sys.DropDownItems.Add(new ToolStripSeparator());
            sys.DropDownItems.Add(MenuHead("⭐ ONYX"));
            var autostart = new ToolStripMenuItem("Démarrer ONYX avec Windows") { Checked = AppAutostart.IsEnabled() };
            autostart.Click += (s, e) => { bool now = !AppAutostart.IsEnabled(); if (AppAutostart.SetEnabled(now)) autostart.Checked = now; };
            sys.DropDownItems.Add(autostart);
            var trayStart = new ToolStripMenuItem("Démarrer minimisé (zone de notification)") { Checked = TrayStartEnabled };
            trayStart.Click += (s, e) => { bool now = !TrayStartEnabled; TrayStartEnabled = now; trayStart.Checked = now; };
            sys.DropDownItems.Add(trayStart);
            var discord = new ToolStripMenuItem("Présence Discord (« optimise son PC avec ONYX »)") { Checked = DiscordPresence.Enabled };
            discord.Click += (s, e) => { bool now = !DiscordPresence.Enabled; DiscordPresence.Enabled = now; discord.Checked = now; if (now) DiscordPresence.Start(); else DiscordPresence.Stop(); };
            sys.DropDownItems.Add(discord);
            sys.DropDownItems.Add("Activer la présence Discord (coller l'App ID)…", null, (s, e) => ConfigureDiscordAppId());
            // Mesure d'audience : visible, cochée, et coupable en un clic. C'est la contrepartie
            // d'un envoi actif par défaut — voir Audience pour ce qui part exactement.
            var audience = new ToolStripMenuItem("Statistiques anonymes (compter les installations)") { Checked = Audience.Active };
            audience.Click += (s, e) =>
            {
                bool now = !Audience.Active;
                Audience.Active = now;
                audience.Checked = now;
                Log(now
                    ? "Statistiques : ONYX enverra une fois par jour un identifiant de machine haché et son numéro de version. Rien d'autre."
                    : "Statistiques : plus aucun envoi.", 0);
            };
            sys.DropDownItems.Add(audience);
            sys.DropDownItems.Add("Ce que les statistiques envoient…", null, (s, e) => ExpliqueAudience());
            var anim = new ToolStripMenuItem("Animations de l'interface") { Checked = AnimSettings.UserEnabled };
            anim.Click += (s, e) => { bool now = !AnimSettings.UserEnabled; AnimSettings.UserEnabled = now; anim.Checked = now; };
            sys.DropDownItems.Add(anim);
            m.Add(sys);

            m.Add(new ToolStripSeparator());
            m.Add("🎛  Réglages carte graphique (puissance, température, fréquences)…", null,
                (s, e) => OpenDialog(new GpuTuningForm(Log)));
            m.Add("💽  Quel disque est le plus rapide pour tes jeux ?", null, (s, e) => MesureDisques());
            var gel = new ToolStripMenuItem("📶  Geler la recherche de réseaux Wi-Fi (pendant la partie)")
            { Checked = false };
            gel.Click += (s, e) => BasculeGelWifi(gel);
            try { gel.Checked = WifiScan.GeleeParNous() != null; } catch { }
            m.Add(gel);
            m.Add(new ToolStripSeparator());
            m.Add("❓  J'ai un problème…", null, (s, e) => OpenDialog(new HelpNavForm(Log)));
            m.Add("🔄  Vérifier les mises à jour d'ONYX", null, (s, e) => ShowUpdateCheck());
            m.Add("ℹ  À propos de ONYX", null, (s, e) => OpenDialog(new AboutForm()));
            m.Add("🔑  Activer Pro / entrer une clé", null, (s, e) => OpenDialog(new LicenseKeyForm("")));
        }

        /// <summary>Vérification des mises à jour d'ONYX, à la demande. Si une version plus récente
        /// existe, propose de télécharger l'installateur officiel — jamais sans clic.</summary>
        private void ShowUpdateCheck()
        {
            string status = "";
            Updater.Release rel = null;
            try { rel = Updater.Check(out status); } catch (Exception ex) { status = "Échec : " + ex.Message; }
            var cur = Updater.CurrentVersion();
            string txt = Updater.Describe(cur, rel, status);
            bool canInstall = rel != null && Updater.IsNewer(cur, rel.Ver) && Updater.IsTrustedUrl(rel.AssetUrl, Updater.ManifestUrl);
            if (!canInstall)
            {
                MessageBox.Show(this, txt, "ONYX — mises à jour", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(this, txt + "\n\nTélécharger et installer maintenant ?", "ONYX — nouvelle version",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            System.Threading.Tasks.Task.Run(() =>
            {
                string file = null;
                try { file = Updater.Download(rel, Log); } catch { }
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        if (file == null)
                        {
                            MessageBox.Show(this, "Le téléchargement a échoué — rien n'a été installé, ta version actuelle est intacte.",
                                "ONYX — mise à jour", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }
                        try { Journal.Add("Mise à jour lancée vers la version " + rel.Tag); } catch { }
                        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = true }); }
                        catch (Exception ex)
                        {
                            MessageBox.Show(this, "Téléchargé, mais impossible de lancer l'installateur (" + ex.Message + ") :\n" + file,
                                "ONYX — mise à jour", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }
                        Close();   // l'installateur prend le relais
                    }));
                }
                catch { }
            });
        }

        /// <summary>Médecin des journaux Windows : traduit les erreurs enregistrées par Windows en
        /// diagnostic lisible (avec le bruit connu clairement identifié comme tel).</summary>
        private void ShowLogDoctor()
        {
            using (var f = new Form
            {
                Text = "ONYX — diagnostic des journaux Windows", Width = 820, Height = 620,
                StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.Sizable,
                MinimizeBox = false, BackColor = FpsUi.BgMain, ForeColor = FpsUi.Dim
            })
            {
                var box = new TextBox
                {
                    Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
                    BorderStyle = BorderStyle.None, BackColor = FpsUi.BgMain, ForeColor = FpsUi.Dim,
                    Font = FpsUi.Small, Text = "Analyse des journaux Windows en cours…"
                };
                var pad = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 12, 14, 6), BackColor = FpsUi.BgMain };
                pad.Controls.Add(box);
                var close = new Button
                {
                    Text = "Fermer", Dock = DockStyle.Bottom, Height = 34, FlatStyle = FlatStyle.Flat,
                    ForeColor = FpsUi.Gold, DialogResult = DialogResult.OK
                };
                f.Controls.Add(pad); f.Controls.Add(close);
                f.AcceptButton = close;
                f.Shown += (s, e) => System.Threading.Tasks.Task.Run(() =>
                {
                    string t;
                    try { t = LogDoctor.Run(14, Log); }
                    catch (Exception ex) { t = "L'analyse a échoué : " + ex.Message; }
                    try { f.BeginInvoke((Action)(() => { box.Text = t; box.SelectionStart = 0; box.SelectionLength = 0; })); } catch { }
                });
                f.ShowDialog(this);
            }
        }

        /// <summary>Liste les jeux Steam installés (toutes bibliothèques) et lance la vérification
        /// OFFICIELLE des fichiers du jeu choisi. Rien n'est supprimé : Steam re-télécharge.</summary>
        private void ShowSteamValidate()
        {
            System.Collections.Generic.List<SteamGames.Game> games = null;
            try { games = SteamGames.Installed(); } catch { }
            if (SteamGames.SteamPath() == null)
            {
                MessageBox.Show(this, "Steam n'est pas installé sur ce PC.\n\nPour les autres plateformes : Epic → « Vérifier » "
                    + "dans le menu ⋯ du jeu ; Battle.net → Options → « Analyser et réparer ».",
                    "Vérifier les fichiers d'un jeu", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (games == null || games.Count == 0)
            {
                MessageBox.Show(this, "Steam est bien installé, mais je n'ai trouvé aucun jeu (bibliothèque vide ou déplacée).",
                    "Vérifier les fichiers d'un jeu", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (var f = new Form
            {
                Text = "ONYX — vérifier les fichiers d'un jeu (Steam)", Width = 620, Height = 520,
                StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false, MinimizeBox = false, BackColor = FpsUi.BgMain, ForeColor = FpsUi.Dim
            })
            {
                var info = new Label
                {
                    Dock = DockStyle.Top, Height = 62, ForeColor = FpsUi.Dim, Font = FpsUi.Small, Padding = new Padding(14, 10, 14, 0),
                    Text = "Un jeu qui plante, refuse de démarrer ou a subi un disque plein / une coupure : la vérification "
                         + "OFFICIELLE de Steam répare les fichiers abîmés.\nRien n'est supprimé — Steam re-télécharge "
                         + "uniquement ce qui manque. Tes sauvegardes ne sont pas touchées."
                };
                var list = new ListBox
                {
                    Dock = DockStyle.Fill, BackColor = FpsUi.BgMain, ForeColor = FpsUi.Dim,
                    Font = FpsUi.Small, BorderStyle = BorderStyle.None, IntegralHeight = false
                };
                foreach (var g in games) list.Items.Add(g.Name + "   (" + SteamGames.Human(g.SizeBytes) + ")");
                var go = new Button
                {
                    Text = "▶  Vérifier les fichiers du jeu sélectionné", Dock = DockStyle.Bottom, Height = 38,
                    FlatStyle = FlatStyle.Flat, ForeColor = FpsUi.Gold
                };
                go.Click += (s, e) =>
                {
                    int i = list.SelectedIndex;
                    if (i < 0 || i >= games.Count) { go.Text = "Choisis d'abord un jeu dans la liste"; return; }
                    var g = games[i];
                    if (MessageBox.Show(this, "Lancer la vérification des fichiers de « " + g.Name + " » ?\n\n"
                        + "Steam va ouvrir et contrôler l'intégralité des fichiers du jeu (quelques minutes selon la taille). "
                        + "Rien n'est supprimé ; seuls les fichiers abîmés sont re-téléchargés.",
                        "Vérifier " + g.Name, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                    if (SteamGames.Validate(g.AppId))
                    {
                        try { Journal.Add("Vérification des fichiers Steam lancée : " + g.Name); } catch { }
                        Log("Vérification Steam lancée pour « " + g.Name + " ».", 0);
                        go.Text = "✓ Vérification lancée dans Steam";
                    }
                    else go.Text = "Steam n'a pas répondu — lance-le puis réessaie";
                };
                var close = new Button
                {
                    Text = "Fermer", Dock = DockStyle.Bottom, Height = 32, FlatStyle = FlatStyle.Flat,
                    ForeColor = FpsUi.Dim, DialogResult = DialogResult.Cancel
                };
                f.Controls.Add(list); f.Controls.Add(info); f.Controls.Add(go); f.Controls.Add(close);
                f.ShowDialog(this);
            }
        }

        /// <summary>Verdict « pilote GPU instable ? » : croise erreurs, âge du pilote et crashs,
        /// et donne la marche à suivre dans l'ordre. Lecture seule.</summary>
        private void ShowGpuStability()
        {
            string txt;
            try { txt = GpuStability.Text(); } catch (Exception ex) { txt = "Analyse impossible : " + ex.Message; }
            using (var f = new Form
            {
                Text = "ONYX — stabilité du pilote GPU", Width = 760, Height = 620,
                StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false, MinimizeBox = false, BackColor = FpsUi.BgMain, ForeColor = FpsUi.Dim
            })
            {
                var box = new TextBox
                {
                    Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
                    BorderStyle = BorderStyle.None, BackColor = FpsUi.BgMain, ForeColor = FpsUi.Dim,
                    Font = FpsUi.Small, Text = txt
                };
                var top = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 14, 6), BackColor = FpsUi.BgMain };
                top.Controls.Add(box);

                // Plan d'action COCHÉ : ce qui est fait est daté et ressort au prochain passage.
                var plan = new FlowLayoutPanel
                {
                    Dock = DockStyle.Bottom, Height = 152, FlowDirection = FlowDirection.TopDown,
                    WrapContents = false, AutoScroll = true, BackColor = FpsUi.BgMain, Padding = new Padding(14, 4, 14, 4)
                };
                plan.Controls.Add(new Label
                {
                    Text = "Coche ce que tu as DÉJÀ fait (daté, et rappelé au prochain passage) :",
                    AutoSize = true, ForeColor = FpsUi.Gold, Font = FpsUi.Small, Margin = new Padding(0, 0, 0, 6)
                });
                foreach (var stepName in GpuStability.Steps)
                {
                    string st = stepName;
                    string dt = null;
                    try { dt = GpuStability.DoneDate(st); } catch { }
                    var cb = new CheckBox
                    {
                        Text = st + (dt != null ? "   (fait le " + dt + ")" : ""), AutoSize = true,
                        Checked = dt != null, ForeColor = FpsUi.Dim, Font = FpsUi.Small, Margin = new Padding(0, 2, 0, 2)
                    };
                    cb.CheckedChanged += (s, e) =>
                    {
                        try
                        {
                            GpuStability.SetDone(st, cb.Checked);
                            string nd = GpuStability.DoneDate(st);
                            cb.Text = st + (nd != null ? "   (fait le " + nd + ")" : "");
                        }
                        catch { }
                    };
                    plan.Controls.Add(cb);
                }
                var close = new Button
                {
                    Text = "Fermer", Dock = DockStyle.Bottom, Height = 34, FlatStyle = FlatStyle.Flat,
                    ForeColor = FpsUi.Gold, DialogResult = DialogResult.OK
                };
                f.Controls.Add(top); f.Controls.Add(plan); f.Controls.Add(close);
                f.AcceptButton = close;
                f.ShowDialog(this);
            }
        }

        /// <summary>Item du tray : vérification du Gardien À LA DEMANDE — répond TOUJOURS,
        /// même quand tout va bien (contrairement au contrôle quotidien, muet si sain).</summary>
        private void GuardianCheckNow()
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                System.Collections.Generic.List<string> al = null;
                try { al = Guardian.Alerts(); } catch { }
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        try
                        {
                            _tray.Visible = true;
                            bool bad = al != null && al.Count > 0;
                            _tray.BalloonTipTitle = bad ? "🛡 Gardien — " + al.Count + " alerte(s)" : "🛡 Gardien — tout va bien";
                            _tray.BalloonTipText = bad
                                ? al[0] + (al.Count > 1 ? "  (+" + (al.Count - 1) + " autre(s) — dis « gardien » au Copilote)" : "")
                                : "Disque, santé SMART, redémarrage, uptime, crashs : rien à signaler.";
                            _tray.ShowBalloonTip(8000);
                        }
                        catch { }
                    }));
                }
                catch { }
            });
        }

        // Démarrage minimisé (optionnel) : ONYX naît directement dans la zone de notification.
        private static string TrayStartPath
        {
            get { return AppPaths.File("bt-traystart.txt"); }
        }
        private static bool TrayStartEnabled
        {
            get { try { return System.IO.File.Exists(TrayStartPath) && System.IO.File.ReadAllText(TrayStartPath).Trim() == "1"; } catch { return false; } }
            set { try { System.IO.File.WriteAllText(TrayStartPath, value ? "1" : "0"); } catch { } }
        }

        /// <summary>LE GARDIEN : au lancement, vérifie en arrière-plan disque / santé SMART /
        /// redémarrage en attente / uptime — 1 fois par jour. UNE notification discrète s'il y a
        /// des alertes ; silence TOTAL sinon. Un gardien, pas une alarme de voiture.</summary>
        private void StartGuardian()
        {
            if (Environment.GetEnvironmentVariable("BT_UISHOT") != null
                || Environment.GetEnvironmentVariable("BT_UITEST") == "1") return;   // pas pendant les tests UI
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    System.Threading.Thread.Sleep(4000);          // laisse l'app finir de démarrer
                    // 0) MÉNAGE : les installateurs des versions précédentes ne resservent plus une
                    //    fois la mise à jour posée (~45 Mo chacun). À CHAQUE lancement, pas une fois
                    //    par jour : juste après une mise à jour, l'installateur est périmé tout de
                    //    suite. Silencieux, jamais bloquant.
                    try { OldVersions.Sweep(Log); } catch { }
                    // 0 bis) AUTO-RÉPARATION : si ONYX a posé « Ultra faible latence » sur une
                    //    machine que la mesure montre limitée par le processeur, ce profil lui
                    //    coûte des images à chaque partie — et l'utilisateur ne soupçonne pas qu'il
                    //    existe. On le retire tout seul, en l'écrivant au journal.
                    try { NvProfile.SoigneSiNocif(Log); } catch { }
                    // 0 ter) Services qu'on ne doit jamais laisser DÉSACTIVÉS : une page de Windows
                    //    en meurt. Corriger le réglage ne suffisait pas — il faut réparer les
                    //    machines déjà touchées, quel que soit le chemin par lequel le service a
                    //    été coupé (preset, mode simple, config auto, fenêtre Services, autre outil).
                    try { ServiceGuard.Soigne(Log); } catch { }
                    // 0 quater) Réglage anti-Nagle : il vit dans UNE sous-clé PAR CARTE RÉSEAU, donc
                    //    toute carte apparue depuis (WSL, VPN, adaptateur USB, pilote réinstallé) ne
                    //    l'a pas. On recolle les manquantes — mais JAMAIS on n'active le réglage de
                    //    sa propre initiative : sans aucune carte déjà réglée, ce garde ne fait rien.
                    try { NagleGuard.Soigne(Log); } catch { }
                    // 0 quinquies) Recherche de reseaux Wi-Fi laissee GELEE par une session
                    //    precedente (fermeture brutale, plantage, extinction en pleine partie).
                    //    Dans cet etat la carte ne se reconnecte plus toute seule : un outil de
                    //    latence n'a pas le droit de laisser une machine comme ca.
                    try { WifiScan.Soigne(Log); } catch { }
                    // 1) SOS POST-CRASH : à CHAQUE lancement — si un jeu vient de planter (< 30 min),
                    //    on le remarque POUR l'utilisateur, c'est sûrement pour ça qu'il ouvre ONYX.
                    string sos = null;
                    try { sos = Guardian.FreshCrash(); } catch { }
                    // 2) Contrôle quotidien classique (silencieux si tout va bien).
                    var al = new System.Collections.Generic.List<string>();
                    if (sos == null)
                    {
                        if (!Guardian.DueToday()) return;
                        // la mesure santé du jour part en fond (historique = tendance dans le chat)
                        try { AppStats.Get(snap => { try { HealthTrend.RecordToday(snap.Health); } catch { } }); } catch { }
                        // photo quotidienne de l'état du système (« qu'est-ce qui a changé sur mon PC ? »)
                        try { StateDiff.SaveToday(); } catch { }
                        // Nouvelle version d'ONYX ? Vérification silencieuse, 1×/jour, jamais bloquante.
                        try
                        {
                            if (Updater.DueToday())
                            {
                                string st;
                                var nu = Updater.Check(out st);
                                if (nu != null && Updater.IsNewer(Updater.CurrentVersion(), nu.Ver))
                                {
                                    string dispo = nu.Ver.Major + "." + nu.Ver.Minor.ToString("00");
                                    // Mémorisée pour l'affichage DANS l'app : la notification Windows
                                    // peut être manquée (PC absent, notifications coupées), l'en-tête
                                    // de la fenêtre, lui, reste visible tant que la mise à jour est là.
                                    try { UpdateFlag.Set(dispo); } catch { }
                                    // La fenêtre est peut-être ouverte pendant que le Gardien
                                    // découvre la version : le bandeau apparaît sans attendre le
                                    // prochain lancement.
                                    try
                                    {
                                        BeginInvoke((Action)(() =>
                                        {
                                            try
                                            {
                                                Text = "ONYX — QG     •  mise à jour " + dispo + " disponible";
                                                MonteBandeauMaj(dispo);
                                            }
                                            catch { }
                                        }));
                                    }
                                    catch { }
                                    // Notification DÉDIÉE, pas noyée dans « Gardien — N alertes » :
                                    // une mise à jour n'est pas une alerte de santé, et un titre
                                    // générique se referme sans être lu. Envoyée ici plutôt qu'avec
                                    // le lot du Gardien, qui pourrait ne jamais partir s'il n'y a
                                    // aucune autre alerte à signaler.
                                    try
                                    {
                                        if (!WinToast.Show("🔄 Mise à jour d'ONYX disponible",
                                                "Version " + dispo + " — ouvre ONYX, puis menu ⋯ → « Vérifier les mises à jour »."))
                                        {
                                            BeginInvoke((Action)(() =>
                                            {
                                                try
                                                {
                                                    _tray.Visible = true;
                                                    _tray.BalloonTipTitle = "🔄 Mise à jour d'ONYX disponible";
                                                    _tray.BalloonTipText = "Version " + dispo + " — menu ⋯ → « Vérifier les mises à jour ».";
                                                    _tray.ShowBalloonTip(10000);
                                                }
                                                catch { }
                                            }));
                                        }
                                    }
                                    catch { }
                                }
                                else { try { UpdateFlag.Clear(); } catch { } }
                            }
                        }
                        catch { }
                        // Mesures faites : en VEILLE, on s'arrête là — on mesure, on ne dérange pas.
                        if (Guardian.AlertsMuted()) return;
                        // AddRange, PAS d'affectation : « al = Guardian.Alerts() » ÉCRASAIT la liste
                        // et jetait l'avis de mise à jour qu'on venait d'y mettre — la notification
                        // n'apparaissait donc jamais, sauf par hasard s'il y avait une autre alerte.
                        al.AddRange(Guardian.Alerts());
                        if (al.Count == 0) return;
                    }
                    BeginInvoke((Action)(() =>
                    {
                        try
                        {
                            _tray.Visible = true;
                            string titre, corps;
                            if (sos != null)
                            {
                                titre = "🆘 ONYX a remarqué un crash";
                                corps = sos + " — clique : je te dis POURQUOI (enquête sur la cause exacte).";
                            }
                            else
                            {
                                titre = "🛡 Gardien ONYX — " + al.Count + " alerte(s)";
                                corps = al[0] + (al.Count > 1 ? "  (+" + (al.Count - 1) + " autre(s) — dis « gardien » au Copilote)" : "");
                            }
                            TrayAlertBadge(sos != null ? 1 : al.Count);   // la pastille reste après la notification

                            // VRAIE notification Windows d'abord : elle s'enregistre dans le centre
                            // de notifications, donc elle est retrouvable si l'utilisateur était
                            // absent ou en jeu. La bulle de la zone de notification, elle, disparaît
                            // sans laisser de trace — on ne s'en sert que si le toast échoue.
                            if (!WinToast.Show(titre, corps))
                            {
                                _tray.BalloonTipTitle = titre;
                                _tray.BalloonTipText = corps;
                                _tray.BalloonTipClicked += (s, e) => { try { RestoreFromTray(); ShowPage(6); } catch { } };
                                _tray.ShowBalloonTip(10000);
                            }
                        }
                        catch { }
                    }));
                }
                catch { }
            });
        }

        /// <summary>
        /// Annonce la mise à jour au lancement — une seule fois par version. Les fois suivantes,
        /// seuls le titre de la fenêtre et le journal la rappellent : l'utilisateur a été informé,
        /// il a le droit de reporter sans qu'on le relance à chaque démarrage.
        /// </summary>
        private void AnnonceMaj(string version)
        {
            if (UpdateFlag.AAnnoncer() == null) return;   // déjà montrée pour cette version
            UpdateFlag.MarqueAnnonce();
            if (MessageBox.Show(this,
                    "Une nouvelle version d'ONYX est disponible : " + version + "\n\n"
                    + "Tes réglages, ta mémoire du Copilote et ton journal sont CONSERVÉS par la mise à jour.\n\n"
                    + "L'ouvrir maintenant ? (Sinon, le bandeau en haut de la fenêtre te le rappellera "
                    + "tant qu'elle n'est pas installée — cette question ne reviendra pas pour cette version.)",
                    "ONYX — mise à jour disponible",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                try { ShowUpdateCheck(); } catch { }
            }
        }

        /// <summary>
        /// Mesure le temps de réponse de chaque disque sur des accès ALÉATOIRES — le seul motif qui
        /// compte quand un jeu charge ses textures, et celui où l'étiquette « NVMe » ne garantit
        /// rien. Strictement en lecture.
        /// </summary>
        private void MesureDisques()
        {
            if (MessageBox.Show(this,
                    "Mesurer le temps de réponse de tes disques ?\n\n"
                    + "• On lit des petits blocs à des endroits au hasard : c'est ce que fait un jeu qui "
                    + "charge ses textures, et ce n'est PAS ce que mesurent les tests de débit.\n"
                    + "• Lecture seule : aucun octet n'est écrit.\n"
                    + "• Quelques secondes par disque.",
                    "Temps de réponse des disques",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
                return;

            Log("Mesure du temps de réponse des disques…", 0);
            System.Threading.Tasks.Task.Run(() =>
            {
                string texte;
                try
                {
                    var l = DiskLatency.Scan(4000, 6000);
                    var sb = new System.Text.StringBuilder();
                    sb.Append("TEMPS DE RÉPONSE DES DISQUES — accès aléatoires de 4 Ko\n\n");
                    foreach (DiskLatency.Resultat r in l)
                    {
                        sb.Append("  ").Append(r.Lecteur.PadRight(4));
                        sb.Append((r.Disque ?? "").PadRight(32).Substring(0, 32)).Append("  ");
                        if (r.MsParAcces <= 0) { sb.Append("— ").Append(r.Motif).Append('\n'); continue; }
                        sb.Append(r.MsParAcces.ToString("0.00")).Append(" ms   ")
                          .Append(r.RemplissagePourcent).Append(" % plein   ")
                          .Append(DiskLatency.Verdict(r.MsParAcces)).Append('\n');
                    }
                    var cl = DiskLatency.Classement(l);
                    string conseil = cl.Count >= 2 ? DiskLatency.Conseil(cl[0], cl[cl.Count - 1]) : null;
                    sb.Append('\n').Append(conseil ?? "Aucun écart significatif entre tes disques : "
                        + "déplacer un jeu ne changerait rien.");
                    sb.Append("\n\nCe test mesure le TEMPS DE RÉPONSE, pas le débit. Un jeu qui empile "
                            + "plusieurs demandes à la fois exploite mieux un NVMe et l'écart se resserre — "
                            + "mais c'est bien ce temps-là qui produit les micro-saccades de chargement.");
                    texte = sb.ToString();
                }
                catch (Exception ex) { texte = "Mesure impossible : " + ex.Message; }

                string t = texte;
                try { BeginInvoke((Action)(() =>
                {
                    Log("Mesure du temps de réponse des disques terminée.", 1);
                    MessageBox.Show(this, t, "Temps de réponse des disques", MessageBoxButtons.OK, MessageBoxIcon.Information);
                })); } catch { }
            });
        }

        /// <summary>
        /// Gèle / dégèle la recherche de réseaux Wi-Fi. Le gel supprime le pic de ping périodique
        /// dû au balayage, au prix d'une carte qui ne voit plus rien d'autre — d'où la confirmation
        /// avant, et le rétablissement automatique à la fermeture.
        /// </summary>
        private void BasculeGelWifi(ToolStripMenuItem item)
        {
            try
            {
                if (WifiScan.GeleeParNous() != null)
                {
                    WifiScan.Degeler(Log);
                    item.Checked = false;
                    return;
                }
                if (MessageBox.Show(this,
                        "Geler la recherche de réseaux Wi-Fi pendant la partie ?\n\n"
                        + "• Supprime le pic de ping périodique dû au balayage des canaux.\n"
                        + "• PENDANT CE TEMPS, ta carte ne verra aucun autre réseau et ne se "
                        + "reconnectera PAS toute seule si le lien tombe.\n"
                        + "• ONYX rétablit tout seul à la fermeture de l'app — et au prochain "
                        + "lancement s'il se ferme mal.",
                        "Recherche de réseaux Wi-Fi",
                        MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                    return;
                item.Checked = WifiScan.Geler(Log);
            }
            catch (Exception ex) { Log("Gel Wi-Fi impossible : " + ex.Message, 3); }
        }

        /// <summary>
        /// LE RAPPEL QUI RESTE — un bandeau en haut de la fenêtre, tant que la mise à jour n'est
        /// pas posée.
        ///
        /// Les deux autres avis ne durent pas : la notification Windows passe (souvent pendant une
        /// partie, ou pas du tout si les notifications sont coupées), et l'annonce au lancement ne
        /// se montre qu'UNE fois par version — délibérément, pour ne pas harceler. Passé ces deux
        /// instants, il ne restait que le titre de la fenêtre, que personne ne lit.
        ///
        /// Le bandeau, lui, reste sous les yeux et porte le bouton qui lance la mise à jour : plus
        /// besoin de savoir qu'elle se cache dans le menu « ⋯ ».
        ///
        /// « Plus tard » le referme — un rappel ne doit pas devenir un mur — mais il REVIENT au
        /// lancement suivant : reporter est un choix légitime, oublier n'en est pas un.
        /// </summary>
        private void MonteBandeauMaj(string version)
        {
            if (_bandeauMaj != null) return;   // déjà là (détection au lancement puis par le Gardien)

            var bandeau = new Panel
            {
                Dock = DockStyle.Top,
                Height = 44,
                BackColor = FpsUi.Card,
                Padding = new Padding(18, 8, 12, 9)
            };
            bandeau.Paint += (s, e) =>
            {
                using (var pen = new Pen(FpsUi.GoldDim))
                    e.Graphics.DrawLine(pen, 0, bandeau.Height - 1, bandeau.Width, bandeau.Height - 1);
            };

            // Ordre d'ajout = ordre d'ancrage inverse : le DERNIER ajouté se colle le plus au bord.
            // Le contenu extensible (le texte) doit donc être ajouté EN PREMIER.
            var texte = new Label
            {
                Dock = DockStyle.Fill,
                Text = "Mise à jour " + version + " disponible — tes réglages et ton journal sont conservés.",
                Font = FpsUi.Body,
                ForeColor = FpsUi.Ink,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = false,
                UseMnemonic = false
            };
            bandeau.Controls.Add(texte);

            // Le pictogramme a SA police : les polices de texte de l'app (Inter, Segoe UI) n'ont pas
            // le glyphe et l'afficheraient en carré vide — vérifié au rendu.
            bandeau.Controls.Add(new Label
            {
                Dock = DockStyle.Left,
                Width = 30,
                Text = "🔄",
                Font = new Font("Segoe UI Emoji", 11f),
                ForeColor = FpsUi.Gold,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = false
            });

            var installer = new PillButton("Mettre à jour maintenant") { Dock = DockStyle.Right };
            installer.FitWidth();
            installer.Click += (s, e) => { try { ShowUpdateCheck(); } catch { } };
            bandeau.Controls.Add(installer);

            bandeau.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 8, BackColor = FpsUi.Card });

            var plusTard = new PillButton("Plus tard") { Dock = DockStyle.Right };
            plusTard.FitWidth();
            plusTard.Click += (s, e) =>
            {
                try
                {
                    bandeau.Visible = false;
                    Log("Rappel de mise à jour masqué. Il reviendra au prochain lancement, "
                        + "ou par le menu ⋯ → « Vérifier les mises à jour ».", 0);
                }
                catch { }
            };
            bandeau.Controls.Add(plusTard);

            _bandeauMaj = bandeau;
            Controls.Add(bandeau);
            if (_rail != null) _rail.BringToFront();   // la barre latérale reste au-dessus quand elle s'ouvre
        }

        /// <summary>
        /// Mesure la latence DPC/ISR par pilote, en direct. Le chiffre qui compte n'est pas la
        /// moyenne mais le PIRE temps d'exécution : pendant qu'un DPC tourne, son cœur ne fait rien
        /// d'autre — c'est lui qui fait la saccade.
        /// </summary>
        private void MesurerDpc()
        {
            if (MessageBox.Show(this,
                    "Mesurer la latence DPC/ISR pendant 20 secondes ?\n\n"
                    + "• Un DPC est un travail que différent les pilotes. Tant qu'il s'exécute, il monopolise son cœur.\n"
                    + "• Pour un résultat utile, LANCE TON JEU et joue pendant la mesure : c'est en charge que les "
                    + "pilotes fautifs se révèlent.\n"
                    + "• Lecture seule : rien n'est modifié sur ton système.\n\n"
                    + "La mesure est comparée à ton relevé de référence, s'il existe — et la comparaison "
                    + "est REFUSÉE si les deux n'ont pas été prises sous la même charge.",
                    "Latence DPC / ISR", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
                return;

            Log("Mesure de latence DPC/ISR démarrée (20 s)…", 0);
            System.Threading.Tasks.Task.Run(() =>
            {
                string texte;
                var d = new DpcLive();
                try
                {
                    if (!d.Demarre())
                        texte = "La session noyau a été refusée : " + (d.DerniereErreur ?? "raison inconnue")
                              + ".\n\nRelance ONYX en administrateur.";
                    else
                    {
                        System.Threading.Thread.Sleep(20000);
                        // On transmet les événements JETÉS : un rapport bâti sur une mesure trouée
                        // doit le dire, sinon il rassure à tort — l'erreur va toujours vers le bas.
                        var classement = d.Instantane();
                        texte = DpcLive.Texte(classement, 12, 20, d.EvenementsPerdus);

                        // AVANT / APRÈS. Le pire temps est un maximum sur un échantillon : une
                        // machine moins occupée rend de meilleurs chiffres sans qu'aucun réglage
                        // n'ait change. On compare donc, mais on REFUSE de conclure quand les
                        // charges different — un verdict flatteur sur une comparaison invalide
                        // ferait garder un reglage inutile en croyant l'avoir mesure.
                        DpcCompare.Releve maintenant = DpcCompare.Depuis(classement, 20);
                        DpcCompare.Releve reference = DpcCompare.Reference();
                        if (maintenant != null)
                        {
                            if (reference != null)
                                texte += "\n\n" + new string('-', 60) + "\nCOMPARAISON AVEC TON RELEVÉ DE RÉFÉRENCE\n\n"
                                       + DpcCompare.Verdict(reference, maintenant);
                            else
                                texte += "\n\nAucun relevé de référence : celui-ci en devient un. "
                                       + "Change UN réglage, puis relance cette mesure DANS LE MÊME ÉTAT "
                                       + "(même jeu, même scène) — ONYX comparera les deux et te dira "
                                       + "franchement si l'écart veut dire quelque chose.";
                            DpcCompare.EnregistreReference(reference == null ? maintenant : reference);
                        }
                    }
                }
                catch (Exception ex) { texte = "Mesure impossible : " + ex.Message; }
                finally { try { d.Dispose(); } catch { } }

                string t = texte;
                try { BeginInvoke((Action)(() =>
                {
                    Log("Mesure de latence DPC/ISR terminée.", 1);
                    MessageBox.Show(this, t, "Latence DPC / ISR par pilote", MessageBoxButtons.OK, MessageBoxIcon.Information);
                })); } catch { }
            });
        }

        // En-tête de section dans un menu déroulant : item grisé, non cliquable — juste un titre.
        private static ToolStripMenuItem MenuHead(string text)
        {
            return new ToolStripMenuItem(text) { Enabled = false };
        }

        /// <summary>Colle l'Application ID Discord (créé sur discord.com/developers) et démarre la
        /// présence. Sans App ID, la présence Discord ne PEUT pas s'afficher — honnêteté oblige.</summary>
        private void ConfigureDiscordAppId()
        {
            using (var f = new Form
            {
                Text = "Présence Discord — Application ID", Width = 560, Height = 210,
                FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false, MinimizeBox = false, BackColor = FpsUi.BgMain, ForeColor = FpsUi.Dim
            })
            {
                var lab = new Label
                {
                    Left = 16, Top = 14, Width = 515, Height = 66, ForeColor = FpsUi.Dim,
                    Text = "La présence Discord marche déjà : ONYX a sa propre application, rien à faire.\n"
                         + "Ce champ ne sert qu'à la REMPLACER par la tienne (18-20 chiffres) — pour tester,\n"
                         + "ou pour afficher ton propre nom. Vide-le pour revenir à celle d'ONYX."
                };
                var tb = new TextBox { Left = 16, Top = 88, Width = 400, Text = DiscordPresence.AppId };
                var ok = new Button
                {
                    Text = "Enregistrer", Left = 428, Top = 86, Width = 104, DialogResult = DialogResult.OK,
                    FlatStyle = FlatStyle.Flat, ForeColor = FpsUi.Gold
                };
                var hint = new Label
                {
                    Left = 16, Top = 122, Width = 515, Height = 40, ForeColor = FpsUi.Dim2,
                    Text = "La présence n'envoie RIEN sur internet : elle parle au Discord installé sur CE PC (canal local)."
                };
                f.Controls.Add(lab); f.Controls.Add(tb); f.Controls.Add(ok); f.Controls.Add(hint);
                f.AcceptButton = ok;
                if (f.ShowDialog(this) != DialogResult.OK) return;
                string saisi = (tb.Text ?? "").Trim();
                // Un identifiant saisi mais invalide ne doit PAS écraser silencieusement celui qui
                // marche : on refuse, on le dit, et la présence continue de tourner.
                if (saisi.Length > 0 && !DiscordPresence.EstValide(saisi))
                {
                    Log("Application ID ignoré (attendu : 18 à 20 chiffres). ONYX garde le sien.", 1);
                    return;
                }
                DiscordPresence.AppId = saisi;
                DiscordPresence.Stop();
                DiscordPresence.Enabled = true;
                DiscordPresence.Start();
                Log(DiscordPresence.UtiliseCeluiDOnyx
                        ? "Présence Discord démarrée avec l'application ONYX (visible si Discord tourne)."
                        : "Présence Discord démarrée avec ton application (" + DiscordPresence.AppId + ").", 0);
            }
        }

        private void BuildTray()
        {
            _tray = new NotifyIcon();
            try { _tray.Icon = Icon; } catch { }
            _tray.Text = "ONYX"; _tray.Visible = false;
            _tray.DoubleClick += (s, e) => RestoreFromTray();
            var m = new ContextMenuStrip();
            m.Items.Add("Ouvrir ONYX  (Ctrl+Alt+O)", null, (s, e) => RestoreFromTray());
            m.Items.Add("▶ MODE JEU on/off  (Ctrl+Alt+G)", null, (s, e) => ToggleBoost());
            m.Items.Add("Overlay stats on/off", null, (s, e) => ToggleOverlay());
            m.Items.Add("🛡 Gardien : vérifier maintenant", null, (s, e) => GuardianCheckNow());
            var snooze = new ToolStripMenuItem("🔕 Gardien : ne plus me prévenir 7 jours");
            snooze.Click += (s, e) =>
            {
                try
                {
                    if (Guardian.AlertsMuted()) { Guardian.Wake(); Log("Gardien réveillé — alertes réactivées.", 0); }
                    else { Guardian.Snooze(7); Log("Gardien en veille 7 jours (il continue de mesurer, sans prévenir).", 0); }
                    var until = Guardian.SnoozedUntil();
                    snooze.Text = until != null
                        ? "🔔 Gardien en veille jusqu'au " + until.Value.ToString("dd/MM") + " — réveiller"
                        : "🔕 Gardien : ne plus me prévenir 7 jours";
                }
                catch { }
            };
            try
            {
                var until0 = Guardian.SnoozedUntil();
                if (until0 != null) snooze.Text = "🔔 Gardien en veille jusqu'au " + until0.Value.ToString("dd/MM") + " — réveiller";
            }
            catch { }
            m.Items.Add(snooze);
            m.Items.Add("Rapport de santé (HTML)", null, (s, e) => GenerateHealthReport());
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Quitter", null, (s, e) => { _tray.Visible = false; Close(); });
            _tray.ContextMenuStrip = m;
        }

        private void RestoreFromTray()
        {
            Show(); WindowState = FormWindowState.Normal; Activate(); _tray.Visible = false;
            try { _tray.Icon = Icon; _tray.Text = "ONYX"; } catch { }   // efface la pastille d'alerte
        }

        // Pastille ROUGE sur l'icône de zone de notification quand le Gardien a des alertes —
        // indispensable en mode « démarrage minimisé » : l'icône raconte l'état sans bulle.
        private Icon _badgeIcon;
        private void TrayAlertBadge(int count)
        {
            try
            {
                if (_badgeIcon == null)
                {
                    var bmp = Icon.ToBitmap();
                    using (var g = Graphics.FromImage(bmp))
                    {
                        int d = Math.Max(6, bmp.Width * 7 / 16);
                        g.FillEllipse(Brushes.Red, bmp.Width - d, bmp.Height - d, d, d);
                    }
                    _badgeIcon = Icon.FromHandle(bmp.GetHicon());
                }
                _tray.Icon = _badgeIcon;
                _tray.Text = "ONYX — " + count + " alerte(s) du Gardien";
            }
            catch { }
        }

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
                "ONYX", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
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
            try { RegisterHotKey(Handle, HotkeyIdShow, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, (uint)'O'); } catch { }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HotkeyId) ToggleBoost();
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HotkeyIdShow) ToggleShell();
            base.WndProc(ref m);
        }

        /// <summary>Ctrl+Alt+O — global : ramène ONYX au premier plan, ou le range dans la zone de
        /// notification s'il est déjà visible. Utilisable même pendant un jeu en fenêtré.</summary>
        private void ToggleShell()
        {
            try
            {
                if (Visible && WindowState != FormWindowState.Minimized) { Hide(); _tray.Visible = true; }
                else RestoreFromTray();
            }
            catch { }
        }

        private void Cleanup()
        {
            try { BadgeStore.OnNewBadge -= OnNewBadge; } catch { }
            try { UnregisterHotKey(Handle, HotkeyId); } catch { }
            try { UnregisterHotKey(Handle, HotkeyIdShow); } catch { }
            try { if (GameBoost.IsActive) GameBoost.Deactivate(delegate (string a, int b) { }); } catch { }
            // La recherche de réseaux ne doit JAMAIS survivre à l'app : sans elle, la carte ne se
            // reconnecte pas toute seule. Rétabli ici, et de nouveau au lancement si on meurt avant.
            try { if (WifiScan.GeleeParNous() != null) WifiScan.Degeler(null); } catch { }
            try { Native.SetTimer1ms(false); } catch { }
            try { Crosshair.Hide(); } catch { }
            try { StatsOverlayManager.Hide(); } catch { }
            try { if (_sysTimer != null) _sysTimer.Stop(); } catch { }
            try { if (_tray != null) { _tray.Visible = false; _tray.Dispose(); } } catch { }
        }

        /// <summary>Y maximal utilisable par une page (plus de mascotte : plein cadre).</summary>
        public int ContentBottom(int margin) { return ClientSize.Height - margin; }

        private int _headerToolsH;   // hauteur de la rangée d'outils sous le titre (0 si la page n'en a pas)

        /// <summary>Y de départ du contenu d'une page : sous la bande de titre ET sous la rangée
        /// d'outils (chips) quand elle existe. Miroir de ContentBottom, pour intégrer ces outils
        /// dans l'en-tête sans chevaucher le contenu.</summary>
        public int ContentTop(int baseTop) { return baseTop + _headerToolsH; }

        /// <summary>X maximal utilisable par du contenu (plus de mascotte : plein cadre).</summary>
        public int ContentRight(int margin) { return ClientSize.Width - margin; }

        private void SetDark() { Dwm.Darken(this); }

        public void Log(string m, int l) { }

        // ------------------------------------------------------------------
        //  Barre laterale
        // ------------------------------------------------------------------
        // Barre latérale repliable : étroite (icônes seules) ou large (icônes + libellés).
        private const int RailNarrow = 66, RailWide = 232, BrandH = 64, ProfileH = 64;
        private static readonly Font BrandFont = Fonts.Make(Fonts.Marcellus, 14.5f, FontStyle.Regular, "Georgia");
        private bool _railOpen;
        private Panel _railBrand, _railProfile;
        private NavCell _tools;

        private void BuildRail()
        {
            _rail = new BufferedPanel();
            // NON docké, volontairement : la barre se SUPERPOSE au contenu quand elle s'ouvre au
            // lieu de le pousser. Avec Dock=Left, animer la largeur forçait Windows à recalculer la
            // mise en page de toute la fenêtre — donc de la page Jeux et de ses dizaines de tuiles —
            // à CHAQUE image : c'était l'origine du lag. En superposition, rien d'autre ne bouge.
            _rail.Dock = DockStyle.None;
            _rail.SetBounds(0, 0, RailNarrow, Math.Max(200, ClientSize.Height));
            _rail.BackColor = FpsUi.RailBg;
            _rail.Paint += (s, e) => { using (var pen = new Pen(FpsUi.Border)) e.Graphics.DrawLine(pen, _rail.Width - 1, 0, _rail.Width - 1, _rail.Height); };

            // --- Haut : marque + avatar. Un clic replie/déplie tout le menu. ---
            _railBrand = new BufferedPanel { Dock = DockStyle.Top, Height = BrandH, BackColor = FpsUi.RailBg, Cursor = Cursors.Hand };
            _railBrand.Paint += (s, e) =>
            {
                var gr = e.Graphics;
                gr.SmoothingMode = SmoothingMode.AntiAlias;
                gr.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                int mw = 34, mx = _railOpen ? 16 : (_railBrand.Width - mw) / 2;
                Logo.Draw(gr, new RectangleF(mx, (BrandH - mw) / 2f, mw, mw), FpsUi.Gold, false);
                if (_railOpen)
                {
                    // Wordmark gravé : capitales Marcellus or, interlettrées — l'écrin de la marque.
                    FpsUi.DrawTracked(gr, "ONYX", BrandFont, FpsUi.Gold, mx + mw + 12, BrandH / 2f, 3f);
                    // Pastille discrète = menu épinglé (il ne se referme plus quand on s'éloigne).
                    if (_railPinned)
                        using (var br = new SolidBrush(FpsUi.Gold))
                            gr.FillEllipse(br, _railBrand.Width - 22, BrandH / 2f - 3.5f, 7f, 7f);
                }
            };
            // La barre s'ouvre au survol ; le clic sur la marque l'ÉPINGLE (elle reste ouverte).
            _railBrand.Click += (s, e) =>
            {
                _railPinned = !_railPinned;
                if (_railPinned) { SetRail(true); if (_railHover != null) _railHover.Stop(); }
                else RailHoverIn();
                _railBrand.Invalidate();
            };
            var brandTip = new ToolTip();
            brandTip.SetToolTip(_railBrand, "Le menu s'ouvre au survol — clic pour l'épingler / le libérer");
            _rail.Controls.Add(_railBrand);

            // --- Bas : bloc profil (mène à « Mon compte »). ---
            _railProfile = new BufferedPanel { Dock = DockStyle.Bottom, Height = ProfileH, BackColor = FpsUi.RailBg, Cursor = Cursors.Hand };
            _railProfile.Paint += (s, e) => PaintProfile(e.Graphics);
            _railProfile.Click += (s, e) => OpenDialog(new AccountForm());   // Mon compte
            var profTip = new ToolTip(); profTip.SetToolTip(_railProfile, "Mon compte");
            _rail.Controls.Add(_railProfile);

            string[] glyphs = { "🏠", "🚀", "🎮", "💉", "🧪", "🏆", "🩺", "⚙" };
            string[] tips = { "Dashboard", "Optimisations", "Jeux", "Check Up+", "Laboratoire", "Collection", "Consultation", "Système" };
            for (int i = 0; i < glyphs.Length; i++)
            {
                var cell = new NavCell(glyphs[i], tips[i]);
                cell.IconId = i;              // icône vectorielle (NavIcons), dans l'ordre des pages
                int idx = i;
                cell.Click += (s, e) => ShowPage(idx);
                _nav.Add(cell);
                _rail.Controls.Add(cell);
            }

            _tools = new NavCell("⋯", "Outils avancés (optimiseur complet, latence, DNS…)");
            _tools.Label = "Outils avancés";
            _tools.IconId = NavIcons.Outils;
            _tools.Click += (s, e) => _toolsMenu.Show(_tools, new Point(_tools.Width, 0));
            _rail.Controls.Add(_tools);

            LayoutRail();
            HookRailHover(_rail);   // ouverture automatique dès que le curseur arrive
        }

        private Timer _railAnim;
        private int _railFrom, _railTo, _railElapsed;
        private const int RailAnimMs = 180, RailFrameMs = 15;

        /// <summary>
        /// Replie / déplie la barre avec une transition fluide.
        /// </summary>
        /// <remarks>
        /// Animation DÉDIÉE, volontairement indépendante du système Anim global : celui-ci se coupe
        /// dès qu'un jeu tourne, que le Mode Jeu est actif ou que Windows est réglé sur « meilleures
        /// performances » — or déplier un menu est une interaction DIRECTE de l'utilisateur, pas un
        /// effet décoratif : elle doit répondre dans tous les cas. Seul le harnais de test la coupe,
        /// pour garder des captures déterministes.
        /// </remarks>
        private void ToggleRail() { SetRail(!_railOpen); }

        /// <summary>Ouvre/ferme la barre (sans effet si elle est déjà dans l'état demandé).</summary>
        private void SetRail(bool open)
        {
            if (_railOpen == open) return;
            _railOpen = open;
            foreach (NavCell c in _nav) c.Expanded = _railOpen;
            if (_tools != null) _tools.Expanded = _railOpen;

            _railFrom = _rail.Width;
            _railTo = _railOpen ? RailWide : RailNarrow;

            if (Anim.ForceOff) { _rail.Width = _railTo; LayoutRail(); return; }

            _railElapsed = 0;
            if (_railAnim == null)
            {
                _railAnim = new Timer();
                _railAnim.Interval = RailFrameMs;
                _railAnim.Tick += RailAnimTick;
            }
            _railAnim.Start();
        }

        private void RailAnimTick(object sender, EventArgs e)
        {
            _railElapsed += RailFrameMs;
            float p = Math.Min(1f, (float)_railElapsed / RailAnimMs);
            float eased = 1f - (float)Math.Pow(1f - p, 3);      // OutCubic : départ franc, arrivée douce

            _rail.SuspendLayout();
            _rail.Width = (int)(_railFrom + (_railTo - _railFrom) * eased);
            LayoutRail();
            _rail.ResumeLayout(true);

            if (p >= 1f)
            {
                _railAnim.Stop();
                _rail.Width = _railTo;
                LayoutRail();
            }
        }

        // ------------------------------------------------------------------
        //  Ouverture au survol (avec épinglage possible)
        // ------------------------------------------------------------------
        private Timer _railHover;
        private bool _railPinned;

        /// <summary>Le curseur arrive sur la barre : on l'ouvre.</summary>
        /// <remarks>
        /// La FERMETURE est détectée par un timer léger, pas par MouseLeave : ce dernier se
        /// déclenche aussi quand on passe simplement d'un item à l'autre (chaque NavCell est un
        /// contrôle distinct), ce qui refermerait la barre en permanence. Le timer ne tourne que
        /// pendant que la barre est ouverte.
        /// </remarks>
        private void RailHoverIn()
        {
            if (_railPinned) return;
            SetRail(true);
            if (_railHover == null)
            {
                _railHover = new Timer();
                _railHover.Interval = 130;
                _railHover.Tick += RailHoverTick;
            }
            _railHover.Start();
        }

        private void RailHoverTick(object sender, EventArgs e)
        {
            if (_railPinned) { _railHover.Stop(); return; }
            Point p;
            try { p = _rail.PointToClient(Cursor.Position); }
            catch { return; }
            if (_rail.ClientRectangle.Contains(p)) return;   // curseur toujours sur la barre
            _railHover.Stop();
            SetRail(false);
        }

        /// <summary>Branche le survol sur la barre ET tous ses enfants (ils masquent le parent).</summary>
        private void HookRailHover(Control c)
        {
            c.MouseEnter += (s, e) => RailHoverIn();
            foreach (Control k in c.Controls) HookRailHover(k);
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
            using (var br = new SolidBrush(FpsUi.Accent(30))) gr.FillEllipse(br, circ);
            using (var pen = new Pen(Color.FromArgb(120, FpsUi.Gold.R, FpsUi.Gold.G, FpsUi.Gold.B), 1.4f)) gr.DrawEllipse(pen, circ);
            Logo.Draw(gr, new RectangleF(x + 7, cy + 7, d - 14, d - 14), FpsUi.Gold, false);

            if (!_railOpen) return;

            int tx = x + d + 12, tw = _railProfile.Width - tx - 26;
            TextRenderer.DrawText(gr, "Joueur ONYX", FpsUi.Small, new Rectangle(tx, cy - 1, tw, 18), FpsUi.Ink,
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
                        "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                RenderPageTools(page.Tools);                              // barre d'outils du bas selon la page
                if (_pageToolBar != null) _pageToolBar.BringToFront();    // garde son edge en bas (la page Fill rétrécit au-dessus)
                _host.ResumeLayout();
                _current = idx;
                try { page.OnShown(); } catch { }
            };

            commit();   // échange instantané (le cross-fade par capture flashait en noir sur certains GPU)
        }

        // Rebuild la barre d'outils partagée (bas de _host) pour la page courante ; masquée si
        // la page n'a pas d'outils (Dashboard, Collection, Copilote, Système).
        private void RenderPageTools(FpsPage.ToolItem[] tools)
        {
            if (_pageToolBar == null) return;
            _pageToolBar.SuspendLayout();
            var old = new System.Collections.Generic.List<Control>();
            foreach (Control c in _pageToolBar.Controls) old.Add(c);
            _pageToolBar.Controls.Clear();
            foreach (Control c in old) { try { c.Dispose(); } catch { } }

            if (tools == null || tools.Length == 0)
            {
                _headerToolsH = 0;
                _pageToolBar.Visible = false;
                _pageToolBar.ResumeLayout();
                return;
            }
            foreach (FpsPage.ToolItem t in tools)
            {
                var b = new PillButton(t.Label);
                b.Height = 30; b.FitWidth(); b.Margin = new Padding(0, 0, 8, 0);
                Action act = t.Act;
                b.Click += (s, e) => { try { if (act != null) act(); } catch { } };
                _pageToolBar.Controls.Add(b);
            }
            _headerToolsH = 44;                 // réserve la bande sous le titre (Host.ContentTop)
            PositionPageTools();
            _pageToolBar.Visible = true;
            _pageToolBar.BringToFront();
            _pageToolBar.ResumeLayout();
        }

        // Place la rangée d'outils juste SOUS la bande de titre (sous-titre à ~y64), pleine largeur.
        private void PositionPageTools()
        {
            if (_pageToolBar == null || _host == null) return;
            _pageToolBar.SetBounds(34, 84, Math.Max(200, _host.ClientSize.Width - 54), 34);
        }

        private FpsPage CreatePage(int idx)
        {
            FpsPage p;
            switch (idx)
            {
                case 0: p = new PageDashboard(this); break;
                case 1: p = new PageOptimisations(this); break;
                case 2: p = new PageGames(this); break;
                case 3: p = new PageCheckup(this); break;
                case 4: p = new PageLab(this); break;
                case 5: p = new PageCollection(this); break;
                case 6: p = new PageConsultation(this); break;
                case 7: p = new PageSystem(this); break;
                default: p = new PageDashboard(this); break;
            }
            try { AttachPageTools(p, idx); } catch { }
            return p;
        }

        // Barre d'outils EN BAS des pages de contenu : les outils avancés de la catégorie
        // correspondante, directement sur la page (en plus du menu ⋯, qui reste exhaustif).
        // Le rangement colle au rail : Optimisations / Jeux / Check Up+ / Laboratoire.
        private void AttachPageTools(FpsPage p, int idx)
        {
            switch (idx)
            {
                case 1: // Optimisations
                    p.SetTools(
                        Tool("🛠 Optimiseur complet", () => OpenDialog(new MainForm())),
                        Tool("🎚 Mode SIMPLE", () => OpenDialog(new SimpleOptiForm(Catalog.All(), () => License.ProUnlocked, Log))),
                        Tool("⚡ Config auto", () => OpenDialog(new AutoConfigForm(Log))),
                        Tool("🔁 Restauration", () => OpenDialog(new RestoreForm(Log))));
                    break;
                case 2: // Jeux
                    p.SetTools(
                        Tool("🕹 Mes jeux", () => OpenDialog(new GamesForm(Log))),
                        Tool("Mode Jeu", () => OpenDialog(new GameModeForm(Log))),
                        Tool("Priorité CPU", () => OpenDialog(new GameProfileForm(Log))),
                        Tool("🛠 Réparer launchers", () => OpenDialog(new LauncherFixForm(Log))),
                        Tool("Bibliothèques", () => OpenDialog(new LibsForm(Log))),
                        Tool("Prérequis auto", () => OpenDialog(new AutoInstallForm(Log))),
                        Tool("Boutiques en jeu", () => OpenDialog(new ShopFixForm(Log))));
                    break;
                case 3: // Check Up+
                    p.SetTools(
                        Tool("Santé du PC", () => OpenDialog(new HealthForm(Log))),
                        Tool("Qui ralentit ?", () => OpenDialog(new BloatForm(Log))),
                        Tool("Réglages néfastes", () => OpenDialog(new CheckupForm(Log))),
                        Tool("Stabilité", () => OpenDialog(new StabilityForm(Log))),
                        Tool("Températures", () => OpenDialog(new ThermalForm(Log))),
                        Tool("🧰 Entretien", () => OpenDialog(new MaintenanceForm(Log))),
                        Tool("Rapport HTML", () => GenerateHealthReport()));
                    break;
                // Laboratoire : ses outils sont désormais INTÉGRÉS EN CARTES dans la page
                // (PageLab.Build) — donc pas de barre en bas ici (sinon doublon).
                // Page Système : elle a déjà ses propres contrôles en bas (nettoyeur RAM) et ses
                // boutons en haut ; ses outils restent dans le menu ⋯ → Système pour ne rien chevaucher.
            }
        }

        private static FpsPage.ToolItem Tool(string label, Action act) { return new FpsPage.ToolItem(label, act); }

        /// <summary>
        /// Dit exactement ce qui part, sans enrobage. Une case à cocher ne vaut consentement que
        /// si la personne peut savoir ce qu'elle coche — et le mot « anonyme » est ici abusif :
        /// l'identifiant est stable, donc pseudonyme. On l'écrit.
        /// </summary>
        private void ExpliqueAudience()
        {
            string url = Audience.PointDeCollecte;
            MessageBox.Show(this,
                "Une fois par jour au maximum, ONYX envoie TROIS informations :\n\n"
                + "  · un identifiant de machine, haché — impossible de remonter jusqu'à toi,\n"
                + "    mais stable, donc deux envois de ce PC se ressemblent ;\n"
                + "  · la version d'ONYX installée ;\n"
                + "  · le numéro de version de Windows.\n\n"
                + "C'est tout. Ni ton nom, ni celui de ta machine, ni tes jeux, ni ton matériel, "
                + "ni ce que tu fais dans l'application.\n\n"
                + "À quoi ça sert : savoir combien de personnes utilisent ONYX et quelles versions "
                + "sont installées, pour ne pas casser celles qui servent encore.\n\n"
                + "Tu peux couper l'envoi à tout moment : Système → « Statistiques anonymes ».\n\n"
                + (url.Length == 0
                    ? "État actuel : aucun point de collecte configuré — ONYX n'envoie RIEN."
                    : "Destination : " + url),
                "Ce que les statistiques envoient", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public void OpenDialog(Form f)
        {
            try { AnimFx.HookDialog(f); using (f) f.ShowDialog(this); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Error); }
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
                    path = System.IO.Path.Combine(dir, "ONYX-rapport-sante.html");
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
                            catch { MessageBox.Show(this, "Rapport enregistré sur le Bureau :\n" + path, "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Information); }
                        }
                        else MessageBox.Show(this, "Impossible de générer le rapport :\n\n" + err, "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            using (var dlg = new SaveFileDialog { Filter = "Profil ONYX (*.dtg)|*.dtg", FileName = "mon-profil-onyx.dtg", Title = "Exporter mon profil d'optimisations" })
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
                        else MessageBox.Show(this, "Échec de l'export :\n\n" + err, "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            using (var dlg = new OpenFileDialog { Filter = "Profil ONYX (*.dtg)|*.dtg|Tous les fichiers|*.*", Title = "Importer un profil d'optimisations" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                file = dlg.FileName;
            }
            var wanted = new System.Collections.Generic.HashSet<string>();
            try { foreach (string line in System.IO.File.ReadAllLines(file)) { string id = line.Trim(); if (id.Length > 0) wanted.Add(id); } }
            catch (Exception ex) { MessageBox.Show(this, "Lecture impossible :\n\n" + ex.Message, "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }

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

        /// <summary>Un bouton de la barre d'outils du bas (libellé + action).</summary>
        public sealed class ToolItem
        {
            public readonly string Label;
            public readonly Action Act;
            public ToolItem(string label, Action act) { Label = label; Act = act; }
        }

        /// <summary>Outils avancés de la catégorie de cette page. Rendus par le shell dans une
        /// barre partagée EN BAS de _host : comme les pages sont Dock=Fill, la barre (Dock=Bottom)
        /// les fait rétrécir → aucun chevauchement, sur toutes les pages.</summary>
        public ToolItem[] Tools { get; private set; }
        public void SetTools(params ToolItem[] tools) { Tools = tools; }

        public virtual void OnShown() { }

        /// <summary>Titre de page souligne neon.</summary>
        protected void PaintTitle(Graphics g, string title, string subtitle)
        {
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            TextRenderer.DrawText(g, title, FpsUi.H3, new Point(34, 26), FpsUi.Ink, TextFormatFlags.NoPadding);
            int w = TextRenderer.MeasureText(g, title, FpsUi.H3).Width;
            using (var pen = new Pen(FpsUi.Gold, 2f)) g.DrawLine(pen, 34, 50, 34 + w, 50);
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
    /// <summary>
    /// Panneau double-bufferisé et repeint intégralement au redimensionnement.
    /// Indispensable pour animer une largeur sans scintillement ni traînées.
    /// </summary>
    internal class BufferedPanel : Panel
    {
        public BufferedPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;   // sans ça, l'ancien contenu reste affiché après un resize
        }
    }

    /// <summary>
    /// Pastille de la barre d'outils du bas de page. Dessinée en propre (coins pleins, contour
    /// discret, teinte néon au survol) pour s'intégrer au thème au lieu du bouton gris « plaqué ».
    /// </summary>
    internal class PillButton : Button
    {
        private bool _hover;

        public PillButton(string text)
        {
            Text = text;
            Font = FpsUi.Small;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = FpsUi.BgMain;
            ForeColor = FpsUi.Dim;
            Cursor = Cursors.Hand;
            AutoSize = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            MouseEnter += (s, e) => { _hover = true; Invalidate(); };
            MouseLeave += (s, e) => { _hover = false; Invalidate(); };
        }

        /// <summary>Ajuste la largeur au texte (pastille compacte).</summary>
        public void FitWidth() { Width = TextRenderer.MeasureText(Text, Font).Width + 30; }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : FpsUi.BgMain);

            var rf = new RectangleF(0.5f, 1.5f, Width - 1.5f, Height - 3f);
            using (var path = FpsUi.Round(rf, (Height - 3f) / 2f))
            {
                using (var fill = new SolidBrush(_hover ? FpsUi.Accent(32) : FpsUi.Card))
                    g.FillPath(fill, path);
                using (var pen = new Pen(_hover ? FpsUi.Gold : FpsUi.Border, 1f))
                    g.DrawPath(pen, path);
            }
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, _hover ? FpsUi.Ink : FpsUi.Dim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

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

        /// <summary>Icône vectorielle à dessiner (voir NavIcons) ; -1 = garder le glyphe emoji.</summary>
        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int IconId { get; set; }

        /// <summary>Pastille chiffrée (0 = aucune).</summary>
        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int Badge { get { return _badge; } set { _badge = value; Invalidate(); } }

        public NavCell(string glyph, string tip)
        {
            _glyph = glyph;
            Label = tip;
            IconId = -1;                 // par défaut : ancien rendu emoji
            DoubleBuffered = true;
            ResizeRedraw = true;
            // Fond OPAQUE (couleur du rail) et non Transparent : un fond transparent ne fait pas
            // repeindre la zone du parent quand la cellule est redimensionnée → l'ancien contour
            // arrondi reste à l'écran et on obtient des traînées pendant l'animation.
            BackColor = FpsUi.RailBg;
            Cursor = Cursors.Hand;
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
                    using (var br = new SolidBrush(FpsUi.Accent(26))) g.FillPath(br, path);
                    using (var pen = new Pen(FpsUi.Gold, 1.4f)) g.DrawPath(pen, path);
                }
            }
            else if (_hover)
            {
                using (var path = FpsUi.Round(rf, 12f))
                using (var br = new SolidBrush(Color.FromArgb(16, 255, 255, 255))) g.FillPath(br, path);
            }

            Color fg = _active ? FpsUi.Gold : (_hover ? FpsUi.Ink : FpsUi.Dim);

            const int IcoSz = 24;
            if (_expanded)
            {
                DrawIcon(g, new Rectangle(14, (Height - IcoSz) / 2, IcoSz, IcoSz), fg);
                var textR = new Rectangle(54, 0, Math.Max(10, Width - 54 - 30), Height);
                TextRenderer.DrawText(g, (Label ?? "").ToUpperInvariant(), FpsUi.Small, textR, fg,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
            else
            {
                DrawIcon(g, new Rectangle((Width - IcoSz) / 2, (Height - IcoSz) / 2, IcoSz, IcoSz), fg);
            }

            if (_badge > 0) PaintBadge(g);
        }

        /// <summary>Icône vectorielle si un id est fourni, sinon repli sur le glyphe d'origine.</summary>
        private void DrawIcon(Graphics g, Rectangle r, Color fg)
        {
            if (IconId >= 0) NavIcons.Draw(g, IconId, new RectangleF(r.X, r.Y, r.Width, r.Height), fg);
            else TextRenderer.DrawText(g, _glyph, FpsUi.Glyph, r, fg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private void PaintBadge(Graphics g)
        {
            string txt = _badge > 99 ? "99+" : _badge.ToString();
            int d = 17;
            // Déplié : à droite de la ligne. Replié : en pastille sur le coin de l'icône.
            int bx = _expanded ? Width - d - 12 : Width / 2 + 6;
            int by = _expanded ? (Height - d) / 2 : 5;
            var circ = new Rectangle(bx, by, d, d);
            using (var br = new SolidBrush(FpsUi.Gold)) g.FillEllipse(br, circ);
            TextRenderer.DrawText(g, txt, FpsUi.Tiny, circ, FpsUi.RailBg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }
}
