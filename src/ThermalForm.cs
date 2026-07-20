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

            _btnClose = new Button
            {
                Text = "Fermer", Location = new Point(250, 358), Size = new Size(100, 32), FlatStyle = FlatStyle.Flat,
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
                    bool doReasons = s.Gpu != null && s.Gpu.Ok && tick % 2 == 1;
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
                    try { BeginInvoke((Action)(() => ApplySample(s, doReasons, thermal, powerBrake))); } catch { }
                }
                catch { }
                finally { _thBusy = false; }
            });
        }

        // Sur le thread UI : applique la mesure aux libellés (toutes les mutations de champs ici).
        private void ApplySample(HwSample s, bool didReasons, bool thermal, bool powerBrake)
        {
            if (s.Gpu != null && s.Gpu.Ok)
            {
                if (s.Gpu.TempC > _gpuMax) _gpuMax = s.Gpu.TempC;
                _gpu.Text = string.Format("GPU  {0}\n {1:0}°C (max {2:0})   {3:0} MHz   {4:0} W   charge {5:0} %",
                    s.Gpu.Name, s.Gpu.TempC, _gpuMax, s.Gpu.CoreMhz, s.Gpu.PowerW, s.Gpu.Util);
                _gpu.ForeColor = s.Gpu.TempC >= 83 ? Bad : (s.Gpu.TempC >= 75 ? Warn : Color.FromArgb(40, 44, 52));
            }
            else _gpu.Text = "GPU : non NVIDIA ou nvidia-smi indisponible (surveille la température via l'OSD du jeu).";

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
                _throttle.ForeColor = (thermal || powerBrake) ? Bad : Color.FromArgb(40, 44, 52);
            }

            UpdateVerdict();
        }

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
            else
            {
                _verdict.ForeColor = Accent;
                _verdict.Text = "✔ Aucun bridage thermique ni d'alimentation détecté. Températures sous contrôle. "
                    + "(Teste en charge — jeu/benchmark — pour confirmer sous stress réel.)";
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { if (_timer != null) { _timer.Stop(); _timer.Dispose(); } } catch { }
            try { if (_mon != null) _mon.Dispose(); } catch { }
            if (_log != null && _gpuMax > 0)
                _log("Thermique : GPU max " + _gpuMax.ToString("0") + "°C"
                     + (_sawThermal ? " — throttling thermique VU" : "")
                     + (_sawPower ? " — frein d'alim VU" : "") + ".", (_sawThermal || _sawPower) ? 2 : 0);
            base.OnFormClosed(e);
        }
    }
}
