using System;
using System.Drawing;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Panneau overclock : OC GPU réel (power limit + verrou de fréquences via nvidia-smi,
    /// bornés par le pilote, persistant en option), et diagnostic RAM/CPU — l'OC de ces
    /// derniers se fait au BIOS (XMP/EXPO, PBO), pas depuis Windows.
    /// </summary>
    internal class OverclockForm : Form
    {
        private readonly Action<string, int> _log;
        private Sys.GpuOcInfo _gpu;
        private Label _gpuState;
        private TrackBar _plBar;
        private Label _plVal;
        private CheckBox _chkPersist;

        private static readonly Color Bg     = Color.FromArgb(245, 246, 248);
        private static readonly Color Header = Color.FromArgb(28, 30, 38);
        private static readonly Color Accent = Color.FromArgb(0, 150, 90);
        private static readonly Color Warn   = Color.FromArgb(205, 90, 40);

        public OverclockForm(Action<string, int> log)
        {
            _log = log;
            _gpu = Sys.QueryGpuOc();
            Build();
            LoadSaved();
            Theme.Apply(this);
        }

        private void Build()
        {
            Text = "DesTinGOOD — Overclock automatique";
            ClientSize = new Size(660, 690);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = Bg;
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = Header };
            var bt = new Label
            {
                Text = "  Overclock automatique",
                Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 14f), TextAlign = ContentAlignment.MiddleLeft
            };
            banner.Controls.Add(bt);
            Controls.Add(banner);

            int y = 70;

            // ---------------- GPU ----------------
            AddSection("GPU — overclock en direct (nvidia-smi)", ref y);

            _gpuState = new Label
            {
                Location = new Point(20, y), Size = new Size(620, 40),
                ForeColor = Color.FromArgb(60, 64, 72)
            };
            if (_gpu.Ok)
                _gpuState.Text = _gpu.Name + "\nPower limit : " + _gpu.PowerCur.ToString("0") + " W  (défaut "
                    + _gpu.PowerDefault.ToString("0") + " / max " + _gpu.PowerMax.ToString("0") + " W)   ·   Boost max "
                    + _gpu.MaxCoreMhz.ToString("0") + " MHz";
            else
                _gpuState.Text = "Aucun GPU NVIDIA détecté via nvidia-smi. L'overclock GPU est indisponible sur cette machine.";
            Controls.Add(_gpuState);
            y += 48;

            // Presets GPU — power limit uniquement (aucun verrou de fréquence : sûr par conception)
            var pSafe = MakePreset("Défaut", "Power limit constructeur (100 %). État d'origine.", 20, y);
            pSafe.Click += (s, e) => ApplyPreset(100);
            var pBal = MakePreset("Équilibré", "Power limit relevé à mi-chemin. Boost plus soutenu, sans risque.", 220, y);
            pBal.Click += (s, e) => ApplyBalanced();
            var pMax = MakePreset("Performance", "Power limit au maximum autorisé par le pilote. Le GPU booste seul, aucun verrou.", 420, y);
            pMax.Click += (s, e) => ApplyMax();
            Controls.Add(pSafe); Controls.Add(pBal); Controls.Add(pMax);
            y += 78;

            // Power limit manuel
            var plLbl = new Label { Text = "Power limit (%) :", Location = new Point(20, y + 4), AutoSize = true };
            Controls.Add(plLbl);
            _plBar = new TrackBar
            {
                Location = new Point(150, y), Size = new Size(360, 40),
                Minimum = 50, Maximum = 200, TickFrequency = 10, SmallChange = 5, LargeChange = 10
            };
            if (_gpu.Ok && _gpu.PowerDefault > 0)
            {
                int maxPct = (int)Math.Round(100.0 * _gpu.PowerMax / _gpu.PowerDefault);
                _plBar.Maximum = Math.Max(120, maxPct);
                _plBar.Value = Math.Min(_plBar.Maximum, (int)Math.Round(100.0 * _gpu.PowerCur / _gpu.PowerDefault));
            }
            else { _plBar.Value = 100; _plBar.Enabled = false; }
            _plBar.ValueChanged += (s, e) => UpdatePlLabel();
            Controls.Add(_plBar);
            _plVal = new Label { Location = new Point(520, y + 4), AutoSize = true, Font = new Font("Segoe UI Semibold", 10f) };
            Controls.Add(_plVal);
            UpdatePlLabel();
            y += 46;

            // Note sécurité : le verrou de fréquence a été retiré (un plancher verrouillé trop
            // haut peut geler la machine). L'OC ici = power limit uniquement.
            Controls.Add(new Label
            {
                Text = "OC sûr : seul le power limit est réglé — jamais de verrou de fréquence figé "
                     + "(cause de plantages). Pour un OC avancé par offset/courbe, utilise MSI Afterburner.",
                Location = new Point(20, y), Size = new Size(620, 34), ForeColor = Color.FromArgb(110, 115, 125)
            });
            y += 40;

            _chkPersist = new CheckBox
            {
                Text = "Ré-appliquer ce power limit à chaque démarrage (tâche planifiée)",
                Location = new Point(20, y), Size = new Size(500, 24),
                Checked = Sys.OcGuardExists()
            };
            Controls.Add(_chkPersist);
            y += 34;

            var btnApply = MakeButton("APPLIQUER L'OC GPU", 20, y, 300, 40, true);
            btnApply.Enabled = _gpu.Ok;
            btnApply.Click += OnApplyGpu;
            var btnReset = MakeButton("Réinitialiser (défaut constructeur)", 330, y, 310, 40, false);
            btnReset.Enabled = _gpu.Ok;
            btnReset.Click += OnResetGpu;
            Controls.Add(btnApply); Controls.Add(btnReset);
            y += 48;

            var btnNv = MakeButton("Appliquer le profil pilote NVIDIA « faible latence » (Ultra Low Latency)", 20, y, 620, 34, false);
            btnNv.ForeColor = Color.FromArgb(0, 120, 60);
            btnNv.Enabled = Sys.NvpiAvailable();
            if (!btnNv.Enabled) btnNv.Text = "Profil pilote NVIDIA — nvidiaProfileInspector introuvable (tools\\npi\\)";
            btnNv.Click += OnApplyNvidia;
            Controls.Add(btnNv);
            y += 46;

            // ---------------- RAM / CPU ----------------
            AddSection("RAM & CPU — diagnostic (overclock au BIOS)", ref y);

            Sys.RamInfo ram = Sys.QueryRam();
            Sys.CpuInfo cpu = Sys.QueryCpu();

            string ramMsg;
            Color ramColor = Color.FromArgb(60, 64, 72);
            if (ram.Modules == 0)
                ramMsg = "RAM : informations indisponibles.";
            else if (ram.SpeedRated > 0 && ram.SpeedRunning > 0 && ram.SpeedRunning < ram.SpeedRated - 50)
            {
                ramMsg = "RAM : " + (ram.TotalMB / 1024) + " Go, tourne à " + ram.SpeedRunning + " MT/s alors que les modules sont notés "
                    + ram.SpeedRated + " MT/s.\n→ Active le profil XMP/EXPO dans le BIOS pour gagner la vitesse annoncée (grosse influence sur les FPS/latence).";
                ramColor = Warn;
            }
            else
                ramMsg = "RAM : " + (ram.TotalMB / 1024) + " Go à " + ram.SpeedRunning + " MT/s. Profil mémoire déjà au maximum SPD/XMP détecté — rien à faire.";

            Controls.Add(new Label
            {
                Text = ramMsg, Location = new Point(20, y), Size = new Size(620, 44), ForeColor = ramColor
            });
            y += 50;

            string cpuMsg = "CPU : " + cpu.Name + "  (" + cpu.Cores + " cœurs / " + cpu.Threads + " threads, base "
                + (cpu.MaxMhz / 1000.0).ToString("0.0") + " GHz).\n";
            bool k = cpu.Name.IndexOf("K", StringComparison.OrdinalIgnoreCase) >= 0
                     || cpu.Name.IndexOf("X", StringComparison.OrdinalIgnoreCase) >= 0
                     || cpu.Name.IndexOf("Ryzen", StringComparison.OrdinalIgnoreCase) >= 0;
            cpuMsg += k
                ? "→ CPU débloqué : l'overclock (multiplicateur / PBO / Curve Optimizer) se fait dans le BIOS. Windows ne peut pas le faire."
                : "→ CPU non débloqué : pas d'overclock possible. Vérifie surtout le refroidissement pour tenir le turbo.";
            Controls.Add(new Label
            {
                Text = cpuMsg, Location = new Point(20, y), Size = new Size(620, 44), ForeColor = Color.FromArgb(60, 64, 72)
            });
            y += 52;

            var close = MakeButton("Fermer", 470, y, 170, 34, false);
            close.Click += (s, e) => Close();
            Controls.Add(close);
        }

        private void AddSection(string title, ref int y)
        {
            var l = new Label
            {
                Text = title, Location = new Point(16, y), Size = new Size(628, 24),
                Font = new Font("Segoe UI Semibold", 10.5f), ForeColor = Color.FromArgb(50, 70, 130)
            };
            Controls.Add(l);
            var line = new Panel { Location = new Point(16, y + 24), Size = new Size(628, 1), BackColor = Color.FromArgb(210, 214, 220) };
            Controls.Add(line);
            y += 34;
        }

        private Button MakePreset(string title, string desc, int x, int y)
        {
            var b = new Button
            {
                Location = new Point(x, y), Size = new Size(190, 68),
                TextAlign = ContentAlignment.TopCenter,
                Text = title + "\n\n" + desc,
                FlatStyle = FlatStyle.Flat, BackColor = Color.White
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            b.Font = new Font("Segoe UI", 8f);
            b.Enabled = _gpu.Ok;
            return b;
        }

        private static Button MakeButton(string text, int x, int y, int w, int h, bool primary)
        {
            var b = new Button
            {
                Text = text, Location = new Point(x, y), Size = new Size(w, h),
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Accent : Color.White,
                ForeColor = primary ? Color.White : Color.FromArgb(40, 44, 52),
                Font = primary ? new Font("Segoe UI Semibold", 10f) : new Font("Segoe UI", 9f)
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            return b;
        }

        private void UpdatePlLabel()
        {
            int pct = _plBar.Value;
            double watts = _gpu.Ok && _gpu.PowerDefault > 0 ? _gpu.PowerDefault * pct / 100.0 : 0;
            _plVal.Text = pct + " %" + (watts > 0 ? "  (" + watts.ToString("0") + " W)" : "");
        }

        private void ApplyPreset(int pct)
        {
            _plBar.Value = Math.Min(_plBar.Maximum, Math.Max(_plBar.Minimum, pct));
        }

        private void ApplyBalanced()
        {
            // Mi-chemin entre le défaut (100 %) et le maximum autorisé par le pilote.
            ApplyPreset((100 + _plBar.Maximum) / 2);
        }

        private void ApplyMax()
        {
            ApplyPreset(_plBar.Maximum);
        }

        private void LoadSaved()
        {
            int pl;
            if (Sys.LoadGpuOcConfig(out pl) && _gpu.Ok && _gpu.PowerDefault > 0 && pl > 0)
                _plBar.Value = Math.Min(_plBar.Maximum, (int)Math.Round(100.0 * pl / _gpu.PowerDefault));
        }

        private void OnApplyGpu(object sender, EventArgs e)
        {
            int watts = _gpu.PowerDefault > 0 ? (int)Math.Round(_gpu.PowerDefault * _plBar.Value / 100.0) : 0;
            if (MessageBox.Show(this,
                    "Appliquer un power limit de " + watts + " W (" + _plBar.Value + " %) ?\n\n"
                    + "· Aucune fréquence n'est verrouillée : le GPU gère son boost de façon stable.\n"
                    + "· Valeur bornée par le pilote NVIDIA.\n"
                    + "En cas de souci, clique « Réinitialiser » pour revenir au défaut constructeur.",
                    "Appliquer le power limit GPU", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            Sys.ApplyGpuOc(watts, _log);
            Sys.SaveGpuOcConfig(watts);
            if (_chkPersist.Checked) Sys.SetOcGuard(true, Application.ExecutablePath, _log);
            else if (Sys.OcGuardExists()) Sys.SetOcGuard(false, Application.ExecutablePath, _log);
            _gpu = Sys.QueryGpuOc();
            MessageBox.Show(this, "Power limit GPU appliqué (fréquences gérées par le pilote).",
                "DesTinGOOD", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnApplyNvidia(object sender, EventArgs e)
        {
            if (MessageBox.Show(this,
                    "Appliquer le profil pilote NVIDIA « faible latence » ?\n\n"
                    + "• Ultra Low Latency = Ultra\n• Frames pré-rendues max = 1\n• Mode de gestion = Performances maximales\n\n"
                    + "Via nvidiaProfileInspector (ton propre outil). Réversible dans le panneau NVIDIA ou en remettant les réglages par défaut du pilote.",
                    "Profil NVIDIA faible latence", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;
            var btn = sender as Button;
            if (btn != null) btn.Enabled = false;
            Cursor = Cursors.WaitCursor;
            System.Threading.Tasks.Task.Run(() =>
            {
                Sys.ApplyNvidiaLowLatency(_log);
                BeginInvoke((Action)(() =>
                {
                    Cursor = Cursors.Default;
                    if (btn != null) btn.Enabled = true;
                    MessageBox.Show(this, "Profil NVIDIA appliqué. Certains réglages prennent effet au prochain lancement du jeu.",
                        "DesTinGOOD", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }));
            });
        }

        private void OnResetGpu(object sender, EventArgs e)
        {
            Sys.ResetGpuLocks(_log);
            if (Sys.OcGuardExists()) Sys.SetOcGuard(false, Application.ExecutablePath, _log);
            try { if (System.IO.File.Exists(Sys.GpuOcConfigPath)) System.IO.File.Delete(Sys.GpuOcConfigPath); } catch { }
            _chkPersist.Checked = false;
            _gpu = Sys.QueryGpuOc();
            UpdatePlLabel();
            MessageBox.Show(this, "GPU remis aux réglages par défaut du constructeur.",
                "DesTinGOOD", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
