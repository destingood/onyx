using System;
using System.Threading;
using System.Threading.Tasks;
using System.Drawing;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Test de stress / stabilité CPU INTÉGRÉ (comme la partie CPU d'OCCT ou Prime95, mais
    /// natif — aucun pilote noyau) : sature tous les cœurs logiques, surveille la charge et la
    /// température en direct, et signale un bridage thermique. Sert à révéler une instabilité
    /// (freeze/redémarrage) ou une surchauffe avant qu'elle ne gâche une partie.
    /// Sécurité : arrêt automatique si le CPU atteint 100 °C.
    /// </summary>
    internal class StressForm : Form
    {
        private readonly Action<string, int> _log;
        private HwMonitor _mon;
        private System.Windows.Forms.Timer _timer;
        private Thread[] _workers;
        private volatile bool _running;
        private long _iterations;
        private double _sink;                 // empêche l'élimination du calcul par le JIT
        private DateTime _start;
        private double _maxTemp, _maxLoad;
        private bool _busy, _stopped;

        private Label _big, _status;
        private Button _btnStart, _btnClose;

        private static readonly Color Accent = Color.FromArgb(0, 150, 90);
        private static readonly Color Warn   = Color.FromArgb(205, 133, 0);
        private static readonly Color Bad    = Color.FromArgb(200, 45, 45);

        public StressForm(Action<string, int> log)
        {
            _log = log;
            Build();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _mon = new HwMonitor();
            _timer = new System.Windows.Forms.Timer { Interval = 1000 };
            _timer.Tick += (s, ev) => Tick();
        }

        private void Build()
        {
            Text = "DesTinGOOD — Test de stress CPU (intégré)";
            ClientSize = new Size(560, 320);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = Color.FromArgb(245, 246, 248);
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  Test de stress CPU — révèle surchauffe & instabilité",
                Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 12.5f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            var intro = new Label
            {
                Text = "Sature tous les cœurs du CPU et surveille charge + température en direct. Si le PC "
                     + "freeze, redémarre ou grimpe très haut en température, c'est un vrai problème "
                     + "(refroidissement, alim, OC instable). Natif : aucun pilote, aucune injection.",
                Location = new Point(18, 60), Size = new Size(524, 60), ForeColor = Color.FromArgb(60, 64, 72)
            };
            Controls.Add(intro);

            _big = new Label
            {
                Location = new Point(18, 128), Size = new Size(524, 40),
                Font = new Font("Segoe UI Semibold", 15f), ForeColor = Color.FromArgb(40, 44, 52),
                Text = "Prêt. Clique « Démarrer » pour lancer le test."
            };
            Controls.Add(_big);

            _status = new Label
            {
                Location = new Point(18, 172), Size = new Size(524, 26),
                Font = new Font("Consolas", 10f), ForeColor = Color.FromArgb(90, 96, 104)
            };
            Controls.Add(_status);

            var safety = new Label
            {
                Location = new Point(18, 206), Size = new Size(524, 40), ForeColor = Color.FromArgb(150, 90, 20),
                Text = "Sécurité : le test s'arrête tout seul à 100 °C. Tu peux l'arrêter à tout moment. "
                     + "Garde un œil sur la température — vise < 90 °C sous charge."
            };
            Controls.Add(safety);

            _btnStart = MakeBtn("▶ Démarrer le test", 18, 262, 260, 40, true);
            _btnStart.Click += (s, e) => Toggle();
            _btnClose = MakeBtn("Fermer", 452, 262, 90, 40, false);
            _btnClose.Click += (s, e) => Close();
            Controls.Add(_btnStart); Controls.Add(_btnClose);
        }

        private static Button MakeBtn(string text, int x, int y, int w, int h, bool primary)
        {
            var b = new Button
            {
                Text = text, Location = new Point(x, y), Size = new Size(w, h), FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Color.FromArgb(40, 44, 52),
                Font = primary ? new Font("Segoe UI Semibold", 10f) : new Font("Segoe UI", 9f)
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            if (primary) b.FlatAppearance.BorderSize = 0;
            return b;
        }

        private void Toggle()
        {
            if (_running) StopStress("Test arrêté.");
            else StartStress();
        }

        private void StartStress()
        {
            if (_running) return;
            _running = true;
            _iterations = 0; _maxTemp = 0; _maxLoad = 0;
            _start = DateTime.Now;
            int n = Math.Max(1, Environment.ProcessorCount);
            _workers = new Thread[n];
            for (int i = 0; i < n; i++)
            {
                var t = new Thread(Worker) { IsBackground = true, Priority = ThreadPriority.Normal };
                t.Start();
                _workers[i] = t;
            }
            if (_timer != null) _timer.Start();
            _btnStart.Text = "⏹ Arrêter le test";
            _big.Text = "Test en cours — sature " + n + " cœurs...";
            _big.ForeColor = Accent;
            if (_log != null) _log("Stress CPU démarré (" + n + " cœurs).", 0);
        }

        // Boucle de calcul soutenue ; _sink empêche le JIT d'éliminer le travail.
        private void Worker()
        {
            double x = 0.1 + (Environment.TickCount & 15) * 0.017;
            while (_running)
            {
                for (int k = 0; k < 1000000; k++)
                    x = x * 1.0000001 + Math.Sqrt(x + 2.0);
                Interlocked.Add(ref _iterations, 1000000);
                _sink = x;
            }
        }

        private void Tick()
        {
            if (_busy || _mon == null) return;
            _busy = true;
            Task.Run(() =>
            {
                HwSample s = null;
                try { s = _mon.Sample(); } catch { }
                try { BeginInvoke((Action)(() => Apply(s))); } catch { }
                _busy = false;
            });
        }

        private void Apply(HwSample s)
        {
            if (!_running || s == null) return;
            if (s.CpuLoad > _maxLoad) _maxLoad = s.CpuLoad;
            bool hasTemp = !double.IsNaN(s.CpuTempC);
            if (hasTemp && s.CpuTempC > _maxTemp) _maxTemp = s.CpuTempC;

            // Sécurité : coupe à 100 °C.
            if (hasTemp && s.CpuTempC >= 100)
            {
                StopStress("⚠ ARRÊT DE SÉCURITÉ à " + s.CpuTempC.ToString("0") + " °C — ton refroidissement ne suit pas.");
                _big.ForeColor = Bad;
                return;
            }

            double secs = (DateTime.Now - _start).TotalSeconds;
            string temp = hasTemp ? s.CpuTempC.ToString("0") + " °C (max " + _maxTemp.ToString("0") + ")"
                                  : "temp n/d (capteur ACPI non exposé)";
            _big.Text = "Charge CPU " + s.CpuLoad.ToString("0") + " %   ·   " + temp;
            _big.ForeColor = hasTemp ? (s.CpuTempC >= 90 ? Bad : s.CpuTempC >= 80 ? Warn : Accent) : Accent;
            _status.Text = "Durée " + secs.ToString("0") + " s   ·   "
                + (Interlocked.Read(ref _iterations) / 1e9).ToString("0.0") + " milliards d'opérations   ·   "
                + Environment.ProcessorCount + " cœurs";
        }

        private void StopStress(string message)
        {
            if (!_running) return;
            _running = false;
            if (_timer != null) _timer.Stop();
            // Laisse les threads se terminer (ils sortent sur _running = false).
            try { if (_workers != null) foreach (Thread t in _workers) { try { t.Join(500); } catch { } } } catch { }
            _btnStart.Text = "▶ Démarrer le test";
            if (message != null) _big.Text = message;
            if (_log != null) _log("Stress CPU terminé (charge max " + _maxLoad.ToString("0") + " %"
                + (_maxTemp > 0 ? ", temp max " + _maxTemp.ToString("0") + " °C" : "") + ").",
                _maxTemp >= 95 ? 2 : 1);
        }

        private void StopAll()
        {
            if (_stopped) return;
            _stopped = true;
            _running = false;
            try { if (_timer != null) { _timer.Stop(); _timer.Dispose(); } } catch { }
            try { if (_workers != null) foreach (Thread t in _workers) { try { t.Join(500); } catch { } } } catch { }
            int waited = 0;
            while (_busy && waited < 2000) { Thread.Sleep(20); waited += 20; }
            try { if (_mon != null) _mon.Dispose(); } catch { }
        }

        protected override void OnFormClosed(FormClosedEventArgs e) { StopAll(); base.OnFormClosed(e); }
        protected override void Dispose(bool disposing) { if (disposing) StopAll(); base.Dispose(disposing); }
    }
}
