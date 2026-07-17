using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Objectif 500 FPS / 500 Hz : vérifie que l'écran tourne à sa fréquence max (bouton
    /// pour l'y passer, avec retour automatique de sécurité), contrôle en direct les freins
    /// Windows qui coûtent des FPS, et donne pour chaque jeu détecté la manipulation exacte
    /// qui débloque la limite de FPS — car les 500 FPS se gagnent d'abord DANS les jeux.
    /// </summary>
    internal class Fps500Form : Form
    {
        private readonly Action<string, int> _log;
        private ListView _list;
        private Button _btnPack, _btnMaxHz;
        private List<Font> _ownedFonts;
        private Font _bold;
        private DisplayInfo.DisplayMode _below;   // premier écran sous sa fréquence max
        private bool _loggedGames;                // journalise la détection une seule fois

        /// <summary>Mis à true quand l'utilisateur clique « Pack 500 FPS » : la fenêtre principale applique alors le pack.</summary>
        public bool ApplyPackRequested { get; private set; }

        private const string HighPerf = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
        private const string Ultimate = "e9a42b02-d5df-448d-aa00-03f14749eb62";
        private const string MMKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";

        private Font Own(Font f) { _ownedFonts.Add(f); return f; }

        public Fps500Form(Action<string, int> log)
        {
            _log = log;
            Build();
            Load += (s, e) => Reload();
        }

        private void Build()
        {
            Text = "BT Optimizer — Objectif 500 FPS (écran 500 Hz)";
            ClientSize = new Size(760, 640);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(640, 500);
            BackColor = Color.FromArgb(245, 246, 248);
            _ownedFonts = new List<Font>();
            Font = Own(new Font("Segoe UI", 9f));
            _bold = Own(new Font("Segoe UI Semibold", 9.5f));
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  🎯 Objectif 500 FPS — écran 500 Hz", Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = Own(new Font("Segoe UI Semibold", 13f)), TextAlign = ContentAlignment.MiddleLeft
            });

            var intro = new Label
            {
                Dock = DockStyle.Top, Height = 46, Padding = new Padding(14, 6, 12, 2),
                ForeColor = Color.FromArgb(90, 95, 105),
                Text = "Windows ne « fabrique » pas des FPS : il enlève les freins. Les 500 FPS se gagnent DANS chaque jeu\n"
                     + "(limite de FPS, V-Sync, qualité). Ce panneau vérifie l'écran, les freins Windows et te guide jeu par jeu."
            };

            _list = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, ShowGroups = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable, ShowItemToolTips = true,
                Font = Own(new Font("Segoe UI", 9.5f))
            };
            _list.Columns.Add("Point", 262);
            _list.Columns.Add("État / réglage exact", 452);
            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 4, 12, 6) };
            host.Controls.Add(_list);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(12, 7, 12, 7) };

            _btnPack = MakeBtn("⚡ Appliquer le pack 500 FPS", 200, DockStyle.Left);
            _btnPack.BackColor = Color.FromArgb(0, 120, 215);
            _btnPack.ForeColor = Color.White;
            _btnPack.FlatAppearance.BorderSize = 0;
            _btnPack.Click += (s, e) => { ApplyPackRequested = true; Close(); };

            _btnMaxHz = MakeBtn("⬆ Écran → fréquence max", 180, DockStyle.Left);
            _btnMaxHz.Click += OnForceMaxHz;

            var scr = MakeBtn("Réglages écran...", 130, DockStyle.Left);
            scr.Click += (s, e) => Shell("ms-settings:display-advanced", "ms-settings:display");
            var gpu = MakeBtn("Panneau NVIDIA...", 130, DockStyle.Left);
            gpu.Click += (s, e) => Shell("nvcpl.cpl", null);
            var refresh = MakeBtn("Rafraîchir", 90, DockStyle.Left);
            refresh.Click += (s, e) => Reload();
            var close = MakeBtn("Fermer", 80, DockStyle.Right);
            close.Click += (s, e) => Close();

            bottom.Controls.Add(new Label { Dock = DockStyle.Fill });
            bottom.Controls.Add(refresh); bottom.Controls.Add(gpu); bottom.Controls.Add(scr);
            bottom.Controls.Add(_btnMaxHz); bottom.Controls.Add(_btnPack);
            bottom.Controls.Add(close);

            Controls.Add(banner);
            Controls.Add(intro);
            Controls.Add(host);
            Controls.Add(bottom);
            Controls.SetChildIndex(banner, 3);
            Controls.SetChildIndex(intro, 2);
            Controls.SetChildIndex(host, 0);
            Controls.SetChildIndex(bottom, 1);

            Theme.Apply(this);
        }

        private static Button MakeBtn(string text, int w, DockStyle dock)
        {
            var b = new Button { Text = text, Width = w, Dock = dock, FlatStyle = FlatStyle.Flat, BackColor = Color.White, Margin = new Padding(4, 0, 4, 0) };
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            return b;
        }

        private void Shell(string file, string fallback)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = true }); }
            catch { if (fallback != null) try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(fallback) { UseShellExecute = true }); } catch { } }
        }

        // level : 0 OK (vert), 1 à régler (orange), 2 info (gris)
        private void Add(ListViewGroup g, int level, string name, string advice)
        {
            string icon = level == 0 ? "✓ " : (level == 1 ? "! " : "• ");
            Color c = level == 0 ? Color.FromArgb(0, 140, 80)
                    : (level == 1 ? Color.FromArgb(190, 120, 0) : Color.FromArgb(90, 95, 105));
            var it = new ListViewItem(icon + name) { Group = g, UseItemStyleForSubItems = false, ForeColor = c, ToolTipText = advice };
            if (level != 2) it.Font = _bold;
            var sub = it.SubItems.Add(advice);
            sub.ForeColor = c;
            _list.Items.Add(it);
        }

        private void Reload()
        {
            Cursor = Cursors.WaitCursor;
            _list.BeginUpdate();
            _list.Items.Clear();
            _list.Groups.Clear();
            _below = null;

            var gScreen = new ListViewGroup("1. L'écran — 500 FPS ne servent à rien si l'écran n'est pas à 500 Hz") { HeaderAlignment = HorizontalAlignment.Left };
            var gWin = new ListViewGroup("2. Freins Windows (vérifiés en direct — le pack 500 FPS règle tout)") { HeaderAlignment = HorizontalAlignment.Left };
            var gGames = new ListViewGroup("3. Dans chaque jeu — c'est ICI que se gagnent les 500 FPS") { HeaderAlignment = HorizontalAlignment.Left };
            var gDriver = new ListViewGroup("4. Pilote GPU (panneau NVIDIA / AMD)") { HeaderAlignment = HorizontalAlignment.Left };
            _list.Groups.Add(gScreen);
            _list.Groups.Add(gWin);
            _list.Groups.Add(gGames);
            _list.Groups.Add(gDriver);

            // ---- 1. Écran(s) ----
            try
            {
                var modes = DisplayInfo.Query();
                if (modes.Count == 0)
                    Add(gScreen, 2, "Écran", "Impossible de lire les modes d'affichage.");
                foreach (var d in modes)
                {
                    string label = (d.Primary ? "Écran principal : " : "Écran : ") + d.Name;
                    if (d.BelowMax)
                    {
                        if (_below == null || d.Primary) _below = d;
                        Add(gScreen, 1, label,
                            d.CurrentHz + " Hz alors qu'il gère " + d.MaxHz + " Hz en " + d.Width + "×" + d.Height
                            + " — clique « ⬆ Écran → fréquence max » ci-dessous.");
                    }
                    else
                        Add(gScreen, 0, label, "À sa fréquence max : " + d.CurrentHz + " Hz (" + d.Width + "×" + d.Height + ").");
                }
                Add(gScreen, 2, "Câble & OSD",
                    "Un vrai 500 Hz exige le câble DisplayPort fourni (DP 1.4+ / DSC) et parfois un mode à activer dans le menu de l'écran (OSD).");
                Add(gScreen, 2, "Fréquence FIXE, pas « Dynamique »",
                    "Dans Paramètres → Affichage → Fréquence : choisis la valeur fixe la plus haute (ex. 500 Hz), pas « Dynamique » (DRR).");
            }
            catch { }

            // ---- 2. Freins Windows ----
            AddCheck(gWin, PlanPerf(), "Plan d'alimentation performance",
                "Actif.", "Plan équilibré — coche « Performances ultimes » ou clique Pack 500 FPS.");
            AddCheck(gWin, Sys.IntEquals(Sys.GetMachine(@"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff"), 1),
                "Power Throttling désactivé", "Aucun bridage énergétique des processus.",
                "Windows peut brider le jeu — coche « Désactiver le Power Throttling ».");
            AddCheck(gWin, Sys.IntEquals(Sys.GetMachine(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode"), 2),
                "Planification GPU matérielle (HAGS)", "Active.",
                "Inactive — coche « HAGS » (redémarrage requis).");
            AddCheck(gWin, Sys.IntEquals(Sys.GetUser(@"Software\Microsoft\GameBar", "AutoGameModeEnabled"), 1),
                "Mode Jeu de Windows", "Actif : priorité CPU/GPU au jeu.",
                "Inactif — coche « Activer le Mode Jeu de Windows ».");
            AddCheck(gWin, Sys.IntEquals(Sys.GetUser(@"System\GameConfigStore", "GameDVR_Enabled"), 0),
                "Game DVR / captures désactivés", "Aucun enregistrement d'écran en fond (gros mangeur de FPS).",
                "L'enregistrement de fond coûte des FPS — coche « Désactiver Game DVR ».");
            AddCheck(gWin, Sys.IntEquals(Sys.GetUser(@"System\GameConfigStore", "GameDVR_FSEBehaviorMode"), 2),
                "Optimisations plein écran désactivées", "Vrai plein écran exclusif possible.",
                "Coche « Désactiver les optimisations plein écran ».");
            AddCheck(gWin, Sys.IntEquals(Sys.GetMachine(@"SOFTWARE\Microsoft\Windows\Dwm", "OverlayTestMode"), 5),
                "MultiPlane Overlay (MPO) désactivé", "Frame pacing plus régulier sur écrans très rapides.",
                "Coche « Désactiver le MultiPlane Overlay » (redémarrage).");
            AddCheck(gWin, Sys.DxTokenEquals("SwapEffectUpgradeEnable", "1"),
                "Flip fenêtré (Windows 11)", "Latence de présentation réduite en fenêtré/borderless.",
                "Coche « Optimisations pour les jeux en fenêtré ».");
            AddCheck(gWin, Sys.IntEquals(Sys.GetMachine(MMKey, "SystemResponsiveness"), 10),
                "SystemResponsiveness = 10", "Le multimédia de fond laisse le CPU au jeu.",
                "Coche « SystemResponsiveness = 10 ».");
            AddCheck(gWin, Sys.IntEquals(Sys.GetMachine(MMKey, "NoLazyMode"), 1),
                "MMCSS : mode paresseux désactivé", "Le planificateur multimédia reste réactif en continu.",
                "Coche « MMCSS : désactiver le mode paresseux (NoLazyMode) ».");
            AddCheck(gWin, GamesPriorityHigh(),
                "Priorité CPU « Haute » pour les jeux", "Les jeux compétitifs passent devant les tâches de fond quand le CPU sature.",
                "Coche « Priorité CPU Haute pour les jeux compétitifs » — le levier qui aide quand c'est le CPU qui limite (ex. OW2).");
            try
            {
                double ms = Native.CurrentTimerMs();
                AddCheck(gWin, ms <= 1.2, "Timer système à 1 ms",
                    "Actuel : " + ms.ToString("0.0") + " ms.",
                    "Actuel : " + ms.ToString("0.0") + " ms — active MODE JEU ou la case Timer 1 ms.");
            }
            catch { }

            // ---- 3. Jeux ----
            try
            {
                var games = GameScan.Known();
                GameScan.Detect(games);
                int found = 0;
                foreach (var g in games) if (g.Detected) found++;

                foreach (var g in games)
                    if (g.Detected) { Add(gGames, 1, g.Name + " — installé", g.Uncap); }
                foreach (var g in games)
                    if (!g.Detected) Add(gGames, 2, g.Name, g.Uncap);

                if (_log != null && found > 0 && !_loggedGames)
                {
                    _loggedGames = true;
                    _log("Objectif 500 FPS : " + found + " jeu(x) détecté(s) — réglage de la limite de FPS affiché pour chacun.", 0);
                }
            }
            catch { }
            Add(gGames, 2, "Règles d'or (tous les jeux)",
                "1) V-Sync : OFF dans le jeu. 2) Limite de FPS : 500 ou illimitée (beaucoup de jeux sortent plafonnés à 60/144/237). 3) NVIDIA Reflex : Activé quand il existe.");
            Add(gGames, 2, "Pour TENIR 500 FPS",
                "À 500 FPS on est surtout limité par le CPU : baisse les détails « CPU » (ombres, foule, distance d'affichage, effets), pas seulement la résolution. Presets Bas/Compétitif.");
            Add(gGames, 2, "Jeu absent de la liste ?",
                "Cherche « limite de FPS / frame rate cap » et « V-Sync » dans ses options vidéo — le déblocage est toujours à cet endroit.");

            // ---- 4. Pilote GPU ----
            Add(gDriver, 2, "NVIDIA — Synchronisation verticale",
                "Panneau NVIDIA → Gérer les paramètres 3D : « Synchronisation verticale : Désactivée » (sinon FPS bloqués sur le Hz).");
            Add(gDriver, 2, "NVIDIA — Fréquence d'images max",
                "« Fréquence d'images max : Désactivée » — aucun limiteur côté pilote. « Mode de gestion de l'alimentation : Performances maximales ». « Latence faible : Ultra » si le jeu n'a pas Reflex.");
            Add(gDriver, 2, "AMD Radeon",
                "Anti-Lag : Activé. Radeon Chill, FRTC et « cible de fréquence » : DÉSACTIVÉS (ce sont des limiteurs de FPS).");
            Add(gDriver, 2, "Overlays",
                "GeForce Experience / Discord / Steam : désactive les superpositions inutiles, chacune coûte quelques FPS.");

            _list.EndUpdate();

            _btnMaxHz.Enabled = _below != null;
            _btnMaxHz.Text = _below != null ? ("⬆ Passer à " + _below.MaxHz + " Hz") : "⬆ Écran déjà au max";

            Cursor = Cursors.Default;
        }

        private void AddCheck(ListViewGroup g, bool ok, string name, string okText, string fixText)
        {
            try { Add(g, ok ? 0 : 1, name, ok ? okText : fixText); } catch { }
        }

        private static bool GamesPriorityHigh()
        {
            try
            {
                foreach (string exe in GameScan.PriorityExes())
                    if (!Sys.IntEquals(Sys.GetMachine(GameScan.IfeoKey + "\\" + exe + "\\PerfOptions", "CpuPriorityClass"), 3))
                        return false;
                return true;
            }
            catch { return false; }
        }

        private static bool PlanPerf()
        {
            try
            {
                string sch = Convert.ToString(Sys.GetMachine(@"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes", "ActivePowerScheme"));
                return sch != null && (sch.IndexOf(HighPerf, StringComparison.OrdinalIgnoreCase) >= 0
                                    || sch.IndexOf(Ultimate, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            catch { return false; }
        }

        // ------------------------------------------------------------------
        //  Forcer l'écran à sa fréquence max (avec retour automatique de sécurité)
        // ------------------------------------------------------------------
        private void OnForceMaxHz(object sender, EventArgs e)
        {
            var target = _below;
            if (target == null) return;

            string msg = "Passer « " + target.Name + " » de " + target.CurrentHz + " Hz à " + target.MaxHz + " Hz ("
                       + target.Width + "×" + target.Height + ") ?\n\n"
                       + "L'écran peut clignoter un court instant.\n"
                       + "Sécurité : si l'image disparaît, NE TOUCHE À RIEN — retour automatique à "
                       + target.CurrentHz + " Hz au bout de 12 secondes.";
            if (MessageBox.Show(this, msg, "Fréquence de rafraîchissement",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            int oldHz = target.CurrentHz;
            if (!DisplayInfo.SetHz(target.Device, target.MaxHz))
            {
                MessageBox.Show(this, "Le mode " + target.MaxHz + " Hz a été refusé par le pilote.\n"
                    + "Vérifie le câble (DisplayPort) et le menu de l'écran (OSD), puis passe par « Réglages écran... ».",
                    "BT Optimizer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            bool keep;
            using (var dlg = new KeepModeDialog(target.MaxHz, 12))
                keep = dlg.ShowDialog(this) == DialogResult.Yes;

            if (!keep)
            {
                DisplayInfo.SetHz(target.Device, oldHz);
                if (_log != null) _log("Écran rétabli à " + oldHz + " Hz.", 0);
            }
            else if (_log != null)
                _log("Écran « " + target.Name + " » passé à " + target.MaxHz + " Hz — profite de tes "
                     + target.MaxHz + " images par seconde !", 1);

            Reload();
        }

        /// <summary>Petite boîte « Garder ce mode ? » avec compte à rebours : sans réponse, on rétablit.</summary>
        private class KeepModeDialog : Form
        {
            private readonly Timer _timer;
            private readonly Button _keep, _revert;
            private int _left;

            public KeepModeDialog(int hz, int seconds)
            {
                _left = seconds;
                Text = "Fréquence changée";
                ClientSize = new Size(380, 120);
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false; MinimizeBox = false;
                StartPosition = FormStartPosition.CenterParent;

                var lbl = new Label { Text = "L'écran est maintenant à " + hz + " Hz.\nGarder ce mode ?" };
                lbl.SetBounds(16, 12, 350, 40);

                _keep = new Button { Text = "Garder ✔", DialogResult = DialogResult.Yes };
                _keep.SetBounds(70, 66, 110, 32);
                _revert = new Button { Text = "Rétablir (" + _left + ")", DialogResult = DialogResult.No };
                _revert.SetBounds(200, 66, 110, 32);

                Controls.Add(lbl); Controls.Add(_keep); Controls.Add(_revert);
                AcceptButton = _keep; CancelButton = _revert;

                _timer = new Timer { Interval = 1000 };
                _timer.Tick += (s, e) =>
                {
                    _left--;
                    if (_left <= 0) { DialogResult = DialogResult.No; Close(); return; }
                    _revert.Text = "Rétablir (" + _left + ")";
                };
                _timer.Start();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing && _timer != null) { _timer.Stop(); _timer.Dispose(); }
                base.Dispose(disposing);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _ownedFonts != null)
            {
                foreach (Font f in _ownedFonts) { try { f.Dispose(); } catch { } }
                _ownedFonts.Clear();
            }
            base.Dispose(disposing);
        }
    }
}
