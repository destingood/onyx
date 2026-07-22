using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// 🌡️ Surveillance thermique & throttling EN DIRECT : température/fréquence/puissance GPU
    /// (nvidia-smi), charge/température CPU, et surtout les RAISONS de bridage signalées par le
    /// pilote (ralentissement thermique, frein d'alimentation). Un « dispositif de rendu perdu »
    /// ou des chutes de FPS soudaines viennent très souvent d'ici.
    /// </summary>
    internal class ThermalForm : Form
    {
        private readonly Action<string, int> _log;
        private HwMonitor _mon;
        private Timer _timer;
        private Label _gpu, _cpu, _throttle, _verdict;
        private Button _btnClose;
        private double _gpuMax = 0, _cpuMax = 0;
        private bool _sawThermal, _sawPower;
        private int _ticks;
        private volatile bool _thBusy;

        private static readonly Color Accent = Color.FromArgb(0, 150, 90);
        private static readonly Color Warn = Color.FromArgb(200, 110, 0);
        private static readonly Color Bad = Color.FromArgb(200, 60, 40);

        public ThermalForm(Action<string, int> log)
        {
            _log = log;
            Build();
            Theme.Apply(this);
        }

        private void Build()
        {
            Text = "DesTinGOOD — Températures & throttling";
            ClientSize = new Size(600, 400);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = Color.FromArgb(245, 246, 248);
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  🌡️ Températures & throttling — ta carte bride-t-elle ?",
                Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 12.5f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            var hint = new Label
            {
                Text = "Pour un vrai test, lance un jeu ou un benchmark en parallèle et regarde ici : "
                     + "le pilote signale s'il ralentit à cause de la chaleur ou de l'alimentation.",
                Location = new Point(18, 58), Size = new Size(564, 40), ForeColor = Color.FromArgb(60, 64, 72)
            };
            Controls.Add(hint);

            _gpu = Card(18, 104, "GPU");
            _cpu = Card(18, 168, "CPU");
            _throttle = Card(18, 232, "Bridage (pilote)");
            Controls.Add(_gpu); Controls.Add(_cpu); Controls.Add(_throttle);

            _verdict = new Label
            {
                Location = new Point(18, 300), Size = new Size(564, 50), ForeColor = Color.FromArgb(60, 64, 72),
                Font = new Font("Segoe UI", 9.5f)
            };
            Controls.Add(_verdict);

            // Lien fonction → outil : la température des GPU AMD/Intel n'est pas exposée par Windows
            // (cf. le capteur PDH), et un stress-test aide à révéler le throttling. On propose donc
            // les bons outils, avec leur état d'installation, en un clic.
            var btnTools = new Button
            {
                Text = "🌡️ Outils de température & stress", Location = new Point(18, 358), Size = new Size(300, 32),
                FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Color.FromArgb(40, 44, 52),
                Font = new Font("Segoe UI", 9f)
            };
            btnTools.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            LibScan.WireToolButton(btnTools, this, _log, "🌡️ Outils température & stress",
                new[] { "REALiX.HWiNFO", "TechPowerUp.GPU-Z", "CPUID.HWMonitor", "TechPowerUp.ThrottleStop", "OCBase.OCCT.Personal" });
            Controls.Add(btnTools);

            _btnClose = new Button
            {
                Text = "Fermer", Location = new Point(482, 358), Size = new Size(100, 32), FlatStyle = FlatStyle.Flat,
                BackColor = Accent, ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 9.5f)
            };
            _btnClose.FlatAppearance.BorderSize = 0;
            _btnClose.Click += (s, e) => Close();
            Controls.Add(_btnClose);
        }

        private static Label Card(int x, int y, string title)
        {
            return new Label
            {
                Location = new Point(x, y), Size = new Size(564, 58),
                Font = new Font("Consolas", 10.5f), ForeColor = Color.FromArgb(40, 44, 52),
                BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(10, 6, 6, 6),
                TextAlign = ContentAlignment.MiddleLeft, Text = title + " : lecture..."
            };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _mon = new HwMonitor();
            _timer = new Timer { Interval = 1000 };
            _timer.Tick += (s, ev) => Refresh2();
            _timer.Start();
            Refresh2();
        }

        // ---- Raisons de bridage via nvidia-smi (best-effort, tolère les vieux pilotes) ----
        private static bool ReasonActive(string field, string nvsmi)
        {
            try
            {
                NativeResult r = Sys.Run(nvsmi, "--query-gpu=" + field + " --format=csv,noheader");
                if (r.ExitCode != 0) return false;
                string o = (r.Output ?? "").Trim().ToLowerInvariant();
                return o.Contains("active") && !o.Contains("not active");
            }
            catch { return false; }
        }

        private static string NvSmi()
        {
            string p = System.IO.Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe");
            return System.IO.File.Exists(p) ? p : "nvidia-smi.exe";
        }

        // Chaque tick : les mesures nvidia-smi (jusqu'à 6 lancements de process) tournent EN
        // ARRIÈRE-PLAN — jamais sur le thread UI, sinon le panneau se fige toutes les 2 s.
        private void Refresh2()
        {
            if (_thBusy) return;
            _thBusy = true;
            _ticks++;
            int tick = _ticks;
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    HwSample s = _mon.Sample();
                    // Raisons de bridage = nvidia-smi : seulement pour NVIDIA (température connue).
                    bool doReasons = s.Gpu != null && s.Gpu.Ok && !double.IsNaN(s.Gpu.TempC) && tick % 2 == 1;
                    bool thermal = false, powerBrake = false;
                    if (doReasons)
                    {
                        string nv = NvSmi();
                        bool hwTherm = ReasonActive("clocks_event_reasons.hw_thermal_slowdown", nv)
                                    || ReasonActive("clocks_throttle_reasons.hw_thermal_slowdown", nv);
                        bool swTherm = ReasonActive("clocks_event_reasons.sw_thermal_slowdown", nv)
                                    || ReasonActive("clocks_throttle_reasons.sw_thermal_slowdown", nv);
                        thermal = hwTherm || swTherm;
                        powerBrake = ReasonActive("clocks_event_reasons.hw_power_brake_slowdown", nv)
                                  || ReasonActive("clocks_throttle_reasons.hw_power_brake_slowdown", nv);
                    }
                    UiSafe.Post(this, () => ApplySample(s, doReasons, thermal, powerBrake));
                }
                catch { }
                finally { _thBusy = false; }   // libère APRÈS Sample() : la garde PDH de StopMonitoring reste correcte
            });
        }

        // Sur le thread UI : applique la mesure aux libellés (toutes les mutations de champs ici).
        private void ApplySample(HwSample s, bool didReasons, bool thermal, bool powerBrake)
        {
            Color normal = Color.FromArgb(40, 44, 52);
            if (s.Gpu != null && s.Gpu.Ok && !double.IsNaN(s.Gpu.TempC) && s.Gpu.CoreMhz > 0)
            {
                // Chemin NVIDIA (nvidia-smi) : température + fréquences + puissance.
                if (s.Gpu.TempC > _gpuMax) _gpuMax = s.Gpu.TempC;
                _gpu.Text = string.Format("GPU  {0}\n {1:0}°C (max {2:0})   {3:0} MHz   {4:0} W   charge {5:0} %",
                    s.Gpu.Name, s.Gpu.TempC, _gpuMax, s.Gpu.CoreMhz, s.Gpu.PowerW, s.Gpu.Util);
                _gpu.ForeColor = s.Gpu.TempC >= 83 ? Bad : (s.Gpu.TempC >= 75 ? Warn : normal);
            }
            else if (s.Gpu != null && s.Gpu.Ok)
            {
                // Chemin AMD / Intel : charge + VRAM (PDH) + température NATIVE (LibreHardwareMonitor,
                // API constructeur user-mode, SANS pilote noyau) — plus besoin d'un outil externe.
                bool hasT = !double.IsNaN(s.Gpu.TempC);
                if (hasT && s.Gpu.TempC > _gpuMax) _gpuMax = s.Gpu.TempC;
                string tempStr = hasT ? string.Format("{0:0}°C (max {1:0})", s.Gpu.TempC, _gpuMax) : "température n/d";
                _gpu.Text = string.Format("GPU  {0}\n charge {1:0} %   VRAM {2:N0} Mo   ·   {3}",
                    s.Gpu.Name, s.Gpu.Util, s.Gpu.VramUsedMB, tempStr);
                _gpu.ForeColor = hasT ? (s.Gpu.TempC >= 83 ? Bad : s.Gpu.TempC >= 75 ? Warn : normal) : normal;
            }
            else _gpu.Text = "GPU : capteurs indisponibles sur ce système.";

            if (s.CpuLoad >= 0)
            {
                if (!double.IsNaN(s.CpuTempC) && s.CpuTempC > _cpuMax) _cpuMax = s.CpuTempC;
                string temp = double.IsNaN(s.CpuTempC) ? "temp n/d (capteur non exposé)" :
                    string.Format("{0:0}°C (max {1:0})", s.CpuTempC, _cpuMax);
                _cpu.Text = string.Format("CPU\n charge {0:0} %   {1}", s.CpuLoad, temp);
                _cpu.ForeColor = (!double.IsNaN(s.CpuTempC) && s.CpuTempC >= 90) ? Bad :
                                 (!double.IsNaN(s.CpuTempC) && s.CpuTempC >= 80) ? Warn : Color.FromArgb(40, 44, 52);
            }

            if (didReasons)
            {
                if (thermal) _sawThermal = true;
                if (powerBrake) _sawPower = true;
                _throttle.Text = "Bridage (pilote)\n "
                    + (thermal ? "⚠ RALENTISSEMENT THERMIQUE actif" : "thermique : non")
                    + "   ·   " + (powerBrake ? "⚠ FREIN D'ALIMENTATION actif" : "alim : non");
                _throttle.ForeColor = (thermal || powerBrake) ? Bad : normal;
            }

            // GPU non-NVIDIA (AMD/Intel) : les RAISONS détaillées de throttling sont réservées à
            // nvidia-smi ; la température, elle, est désormais lue nativement ci-dessus.
            _noGpuTemp = s.Gpu != null && s.Gpu.Ok && !(s.Gpu.CoreMhz > 0);
            if (_noGpuTemp)
            {
                _throttle.Text = "Bridage (pilote)\n raisons détaillées réservées à NVIDIA — surveille la température GPU ci-dessus.";
                _throttle.ForeColor = normal;
            }
            UpdateVerdict();
        }
        private bool _noGpuTemp;

        private void UpdateVerdict()
        {
            if (_sawThermal)
            {
                _verdict.ForeColor = Bad;
                _verdict.Text = "→ THERMIQUE : le GPU a réduit ses fréquences à cause de la chaleur. "
                    + "Nettoie les poussières/ventilos, améliore le flux d'air, ou baisse le power limit "
                    + "(Overclock auto). C'est une cause fréquente de crashs et de chutes de FPS.";
            }
            else if (_sawPower)
            {
                _verdict.ForeColor = Bad;
                _verdict.Text = "→ ALIMENTATION : frein d'alim signalé — alimentation (PSU) ou connecteur PCIe insuffisant/"
                    + "mal branché. Vérifie les câbles GPU (12VHPWR bien enfoncé) et la puissance du PSU.";
            }
            else if (_gpuMax >= 83 || _cpuMax >= 90)
            {
                _verdict.ForeColor = Warn;
                _verdict.Text = "→ Températures ÉLEVÉES (GPU " + _gpuMax.ToString("0") + "°C / CPU "
                    + (_cpuMax > 0 ? _cpuMax.ToString("0") + "°C" : "n/d") + ") mais pas encore de bridage. "
                    + "Surveille en charge ; améliore le refroidissement si ça grimpe encore.";
            }
            else if (_noGpuTemp)
            {
                _verdict.ForeColor = Accent;
                _verdict.Text = "✔ GPU AMD/Intel : charge, VRAM et température lues nativement dans l'app (sans outil "
                    + "externe). Les RAISONS détaillées de throttling restent réservées aux cartes NVIDIA.";
            }
            else
            {
                _verdict.ForeColor = Accent;
                _verdict.Text = "✔ Aucun bridage thermique ni d'alimentation détecté. Températures sous contrôle. "
                    + "(Teste en charge — jeu/benchmark — pour confirmer sous stress réel.)";
            }
        }

        private bool _stopped;

        // Arrête le timer PUIS attend qu'une lecture de fond (Task.Run) libère le HwMonitor
        // avant de fermer son handle PDH : sinon PdhCloseQuery court-circuiterait un
        // PdhCollectQueryData encore en vol sur le même handle natif → crash (non rattrapable
        // sur CoreCLR). Idempotent : sûr d'être appelé par OnFormClosed ET par Dispose().
        private void StopMonitoring()
        {
            if (_stopped) return;
            _stopped = true;
            try { if (_timer != null) { _timer.Stop(); _timer.Dispose(); } } catch { }
            int waited = 0;
            while (_thBusy && waited < 2000) { System.Threading.Thread.Sleep(20); waited += 20; }
            try { if (_mon != null) _mon.Dispose(); } catch { }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            StopMonitoring();
            if (_log != null && _gpuMax > 0)
                _log("Thermique : GPU max " + _gpuMax.ToString("0") + "°C"
                     + (_sawThermal ? " — throttling thermique VU" : "")
                     + (_sawPower ? " — frein d'alim VU" : "") + ".", (_sawThermal || _sawPower) ? 2 : 0);
            base.OnFormClosed(e);
        }

        // Filet : une fermeture par Dispose() direct (ex. le harnais BTTEST : using(...))
        // ne déclenche pas OnFormClosed. Sans ça, le timer continuerait de tourner (fenêtre
        // native propre) et relancerait des lectures/nvidia-smi contre une fenêtre détruite.
        protected override void Dispose(bool disposing)
        {
            if (disposing) StopMonitoring();
            base.Dispose(disposing);
        }
    }
}
