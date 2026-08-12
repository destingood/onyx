using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Panneau overclock : OC GPU réel (power limit via nvidia-smi, borné par le pilote,
    /// persistant en option), boost CPU maximal (tous les leviers que Windows possède :
    /// plan ultimes, turbo Agressif, état min 100 %, déparcage des cœurs, plafond de
    /// fréquence levé — avec fréquence effective EN DIRECT), et diagnostic RAM — l'OC
    /// multiplicateur/tensions (PBO, XMP/EXPO) se fait au BIOS, pas depuis Windows.
    /// </summary>
    internal class OverclockForm : Form
    {
        private readonly Action<string, int> _log;
        private Sys.GpuOcInfo _gpu;
        private Label _gpuState;
        private TrackBar _plBar;
        private Label _plVal;
        private CheckBox _chkPersist;

        // ---- CPU : leviers Windows + fréquence effective en direct ----
        private const string SubProc = "54533251-82be-4824-96c1-47b60b740d00";
        private const string PerfBoostMode = "be337238-0d82-4146-a960-4f3749d470c7";
        private const string CpMinCores = "0cc5b647-c1df-4637-891a-dec35c318583";
        private const string ProcFreqMax = "75b0ae3f-bce0-45a7-8c89-c9611c25e100";
        private const string ProcMinState = "893dee8e-2bef-41e0-89c6-b55d0929964c";

        [DllImport("pdh.dll")] private static extern uint PdhOpenQuery(IntPtr src, IntPtr user, out IntPtr q);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounter(IntPtr q, string path, IntPtr user, out IntPtr c);
        [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr q);
        [DllImport("pdh.dll")] private static extern uint PdhGetFormattedCounterValue(IntPtr c, uint fmt, IntPtr res, out PdhValue v);
        [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr q);
        [StructLayout(LayoutKind.Sequential)] private struct PdhValue { public uint CStatus; public double Value; }
        private const uint PDH_FMT_DOUBLE = 0x00000200;

        private IntPtr _pdhQuery, _pdhPerf;
        private bool _pdhReady;
        private Timer _cpuTimer;
        private double _baseMhz;
        private Label _cpuLive, _cpuLevers;
        private Button _btnBoostCpu, _btnRevertCpu;

        private static readonly Color Bg     = Color.FromArgb(245, 246, 248);
        private static readonly Color Header = Color.FromArgb(28, 30, 38);
        private static readonly Color Accent = Theme.AccentColor;
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
            Text = "ONYX — Overclock automatique (GPU & CPU)";
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
                Text = "  Overclock automatique — GPU & CPU", UseMnemonic = false,
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
            y += 38;

            _chkPersist = new CheckBox
            {
                Text = "Ré-appliquer ce power limit à chaque démarrage (tâche planifiée)",
                Location = new Point(20, y), Size = new Size(500, 24),
                Checked = Sys.OcGuardExists()
            };
            Controls.Add(_chkPersist);
            y += 32;

            var btnApply = MakeButton("APPLIQUER L'OC GPU", 20, y, 300, 38, true);
            btnApply.Enabled = _gpu.Ok;
            btnApply.Click += OnApplyGpu;
            var btnReset = MakeButton("Réinitialiser (défaut constructeur)", 330, y, 310, 38, false);
            btnReset.Enabled = _gpu.Ok;
            btnReset.Click += OnResetGpu;
            Controls.Add(btnApply); Controls.Add(btnReset);
            y += 46;

            var btnNv = MakeButton("Appliquer le profil pilote NVIDIA « faible latence » (Ultra Low Latency)", 20, y, 620, 32, false);
            btnNv.ForeColor = Color.FromArgb(67, 56, 202);
            btnNv.Enabled = Sys.NvpiAvailable();
            if (!btnNv.Enabled) btnNv.Text = "Profil pilote NVIDIA — nvidiaProfileInspector introuvable (tools\\npi\\)";
            btnNv.Click += OnApplyNvidia;
            Controls.Add(btnNv);
            y += 44;

            // ---------------- CPU ----------------
            AddSection("CPU — boost maximal (tout ce que Windows peut donner)", ref y);

            Sys.CpuInfo cpu = Sys.QueryCpu();
            _baseMhz = cpu.MaxMhz;
            bool unlocked = cpu.Name.IndexOf("K", StringComparison.OrdinalIgnoreCase) >= 0
                         || cpu.Name.IndexOf("X", StringComparison.OrdinalIgnoreCase) >= 0
                         || cpu.Name.IndexOf("Ryzen", StringComparison.OrdinalIgnoreCase) >= 0;
            Controls.Add(new Label
            {
                Text = "CPU : " + cpu.Name + "  (" + cpu.Cores + " cœurs / " + cpu.Threads + " threads, base "
                    + (cpu.MaxMhz / 1000.0).ToString("0.0") + " GHz).\n"
                    + (unlocked
                        ? "CPU débloqué : le vrai overclock (multiplicateur, PBO, Curve Optimizer) se règle au BIOS — Windows ne peut pas le faire."
                        : "CPU non débloqué : pas d'overclock multiplicateur possible. Soigne le refroidissement pour tenir le turbo."),
                Location = new Point(20, y), Size = new Size(620, 42), ForeColor = Color.FromArgb(60, 64, 72)
            });
            y += 46;

            _cpuLive = new Label
            {
                Text = "Fréquence effective : mesure...",
                Location = new Point(20, y), Size = new Size(620, 22),
                Font = new Font("Segoe UI Semibold", 10.5f), ForeColor = Color.FromArgb(60, 64, 72)
            };
            Controls.Add(_cpuLive);
            y += 26;

            _cpuLevers = new Label
            {
                Location = new Point(20, y), Size = new Size(620, 88),
                ForeColor = Color.FromArgb(60, 64, 72)
            };
            Controls.Add(_cpuLevers);
            y += 92;

            _btnBoostCpu = MakeButton("⚡ BOOST CPU MAXIMAL", 20, y, 300, 38, true);
            _btnBoostCpu.Click += OnBoostCpu;
            _btnRevertCpu = MakeButton("Rétablir le boost CPU (défauts Windows)", 330, y, 310, 38, false);
            _btnRevertCpu.Click += OnRevertCpu;
            Controls.Add(_btnBoostCpu); Controls.Add(_btnRevertCpu);
            y += 46;

            Controls.Add(new Label
            {
                Text = "Le pack applique : plan Performances ultimes, turbo boost Agressif, état minimal 100 %, "
                     + "déparcage des cœurs, et lève tout plafond de fréquence du plan. Réversible d'un clic — "
                     + "aucune tension ni multiplicateur touchés (ça, c'est le BIOS).",
                Location = new Point(20, y), Size = new Size(620, 50), ForeColor = Color.FromArgb(110, 115, 125)
            });
            y += 54;

            // ---------------- RAM ----------------
            AddSection("RAM — diagnostic (XMP/EXPO au BIOS)", ref y);

            Sys.RamInfo ram = Sys.QueryRam();
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

            // Lien fonction → outil : overlay OC/FPS (Afterburner+RTSS) et capteurs GPU (GPU-Z).
            var btnTools = MakeButton("Afterburner / GPU-Z", 20, y, 300, 32, false);
            LibScan.WireToolButton(btnTools, this, _log, "Afterburner / GPU-Z",
                new[] { "Guru3D.Afterburner", "TechPowerUp.GPU-Z" });
            Controls.Add(btnTools);
            var close = MakeButton("Fermer", 470, y, 170, 32, false);
            close.Click += (s, e) => Close();
            Controls.Add(close);
            y += 42;

            ClientSize = new Size(660, y);

            RefreshLevers();
            StartCpuLive();
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

        // ------------------------------------------------------------------
        //  CPU : fréquence effective en direct (PDH % Processor Performance)
        // ------------------------------------------------------------------
        private void StartCpuLive()
        {
            if (PdhOpenQuery(IntPtr.Zero, IntPtr.Zero, out _pdhQuery) == 0)
            {
                if (PdhAddEnglishCounter(_pdhQuery, @"\Processor Information(_Total)\% Processor Performance", IntPtr.Zero, out _pdhPerf) == 0)
                {
                    PdhCollectQueryData(_pdhQuery); // amorçage
                    _pdhReady = true;
                }
            }
            if (!_pdhReady || _baseMhz <= 0)
            {
                _cpuLive.Text = "Fréquence effective : compteur indisponible sur ce système.";
                return;
            }
            _cpuTimer = new Timer { Interval = 1000 };
            _cpuTimer.Tick += (s, e) => UpdateCpuLive();
            _cpuTimer.Start();
        }

        private void UpdateCpuLive()
        {
            if (!_pdhReady || PdhCollectQueryData(_pdhQuery) != 0) return;
            PdhValue v;
            if (PdhGetFormattedCounterValue(_pdhPerf, PDH_FMT_DOUBLE, IntPtr.Zero, out v) != 0 || v.CStatus != 0) return;
            double eff = _baseMhz * v.Value / 100.0;
            _cpuLive.Text = "Fréquence effective : " + (eff / 1000.0).ToString("0.00") + " GHz   ("
                + v.Value.ToString("0") + " % de la base " + (_baseMhz / 1000.0).ToString("0.0") + " GHz)";
            _cpuLive.ForeColor = v.Value >= 100 ? Color.FromArgb(0, 130, 80) : Color.FromArgb(60, 64, 72);
        }

        // ------------------------------------------------------------------
        //  CPU : état des leviers + pack boost / rétablissement
        // ------------------------------------------------------------------
        private void RefreshLevers()
        {
            bool ult = false;
            try { ult = Sys.UltimateActive(); } catch { }
            bool? boost = Sys.PowerAcEquals(SubProc, PerfBoostMode, 2);
            bool? min = Sys.PowerAcEquals(SubProc, ProcMinState, 100);
            bool? unpark = Sys.PowerAcEquals(SubProc, CpMinCores, 100);
            int? cap = Sys.GetPowerAcIndex(SubProc, ProcFreqMax);

            var sb = new StringBuilder();
            sb.AppendLine(Mark(ult) + "  Plan d'alimentation Performances ultimes");
            sb.AppendLine(Mark(boost == true) + "  Turbo boost en mode Agressif");
            sb.AppendLine(Mark(min == true) + "  État minimal du processeur à 100 %");
            sb.AppendLine(Mark(unpark == true) + "  Tous les cœurs déparqués (core parking off)");
            if (cap.HasValue && cap.Value > 0)
                sb.Append("⚠  Plafond Windows détecté : " + cap.Value + " MHz max — le pack le lève.");
            else
                sb.Append(Mark(true) + "  Aucun plafond de fréquence Windows");
            _cpuLevers.Text = sb.ToString();
        }

        private static string Mark(bool on) { return on ? "✓" : "○"; }

        private List<Tweak> BoostTweaks()
        {
            string[] ids = { "power_ultimate", "proc_min_100", "cpu_boost_aggressive", "cpu_unpark_cores" };
            List<Tweak> all = Catalog.All();
            var sel = new List<Tweak>();
            foreach (string id in ids)
                foreach (Tweak t in all)
                    if (t.Id == id) { sel.Add(t); break; }
            return sel;
        }

        private void OnBoostCpu(object sender, EventArgs e)
        {
            if (MessageBox.Show(this,
                    "Appliquer le BOOST CPU MAXIMAL ?\n\n"
                    + "• Plan d'alimentation Performances ultimes\n"
                    + "• Turbo boost en mode Agressif (fréquence max immédiate)\n"
                    + "• État minimal du processeur à 100 % (pas de sous-cadençage)\n"
                    + "• Tous les cœurs déparqués (core parking off)\n"
                    + "• Plafond de fréquence du plan levé (aucune limite)\n\n"
                    + "Aucune tension ni multiplicateur modifiés : c'est le maximum que Windows\n"
                    + "autorise, dans les limites du refroidissement. Consommation en hausse au repos.\n"
                    + "Réversible avec « Rétablir le boost CPU ».",
                    "⚡ Boost CPU maximal", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;
            RunCpuPack(true);
        }

        private void OnRevertCpu(object sender, EventArgs e)
        {
            if (MessageBox.Show(this,
                    "Rétablir les valeurs par défaut de Windows pour le boost CPU ?\n\n"
                    + "Turbo boost efficace, état minimal 5 %, parcage des cœurs rétabli (10 %),\n"
                    + "et retour au plan d'alimentation Équilibré.",
                    "Rétablir le boost CPU", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;
            RunCpuPack(false);
        }

        private void RunCpuPack(bool apply)
        {
            List<Tweak> sel = BoostTweaks();
            _btnBoostCpu.Enabled = _btnRevertCpu.Enabled = false;
            Cursor = Cursors.WaitCursor;
            System.Threading.Tasks.Task.Run(() =>
            {
                Engine.Run(sel, apply, false, false, _log);
                if (apply)
                {
                    // Lève tout plafond de fréquence posé dans le plan (0 = illimité).
                    try { Sys.SetPowerValue(SubProc, ProcFreqMax, 0, 0); } catch { }
                }
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        Cursor = Cursors.Default;
                        _btnBoostCpu.Enabled = _btnRevertCpu.Enabled = true;
                        RefreshLevers();
                        MessageBox.Show(this,
                            apply ? "Boost CPU maximal appliqué. La fréquence effective ci-dessus doit tenir le turbo."
                                  : "Boost CPU rétabli aux valeurs par défaut de Windows.",
                            "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }));
                }
                catch { }
            });
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
                "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnApplyNvidia(object sender, EventArgs e)
        {
            // Le profil est choisi d'après la DERNIÈRE MESURE en jeu, pas appliqué à l'aveugle :
            // « Ultra » n'est bénéfique que si la carte graphique travaille déjà à fond.
            double cpuAvg, gpuAvg; DateTime quandMes;
            bool mesure = Bottleneck.LastMeasure(out cpuAvg, out gpuAvg, out quandMes);
            if (!mesure) { cpuAvg = -1; gpuAvg = -1; }
            NvProfile.Kind choix = NvProfile.Recommande(cpuAvg, gpuAvg);
            string diag = mesure
                ? "Ta dernière mesure en jeu : CPU " + Math.Round(cpuAvg) + " %, GPU " + Math.Round(gpuAvg) + " %.\n"
                  + (NvProfile.UltraNocif(cpuAvg, gpuAvg)
                        ? "→ Ton PROCESSEUR limite : le mode « Ultra » te ferait PERDRE des images.\n\n"
                        : "→ Profil retenu : " + NvProfile.Libelle(choix) + ".\n\n")
                : "Aucune mesure en jeu n'a encore été faite : on applique le profil SÛR, jamais « Ultra » à l'aveugle.\n\n";
            if (MessageBox.Show(this,
                    diag
                    + "Appliquer « " + NvProfile.Libelle(choix) + " » ?\n\n"
                    + "• Le mode « Ultra » supprime la file d'attente de rendu (1 image pré-rendue). Ce tampon est ce qui "
                    + "amortit les à-coups du processeur : sans lui, chaque pic CPU devient une image perdue. Il ne vaut "
                    + "que sur une machine limitée par le GPU.\n"
                    + "• Le profil sûr garde une latence réduite ET la stabilité.\n"
                    + "• Via nvidiaProfileInspector. Retirable depuis ONYX (le diagnostic propose le retour au profil sûr).",
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
                        "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_cpuTimer != null) { _cpuTimer.Stop(); _cpuTimer.Dispose(); _cpuTimer = null; }
                if (_pdhQuery != IntPtr.Zero) { try { PdhCloseQuery(_pdhQuery); } catch { } _pdhQuery = IntPtr.Zero; }
            }
            base.Dispose(disposing);
        }
    }
}
