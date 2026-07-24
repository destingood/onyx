using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Fréquence réelle de la souris : mesure le taux de rapport (polling rate) EN DIRECT
    /// via l'entrée brute Windows (raw input WM_INPUT) — vérifie que ton « 1000 Hz » est bien
    /// à 1000 Hz. Aucune injection, aucun pilote : on écoute les rapports HID de la souris.
    /// </summary>
    internal class MouseForm : Form
    {
        private readonly Action<string, int> _log;
        private Label _live, _best, _verdict, _hint;
        private Timer _refresh;

        // Intervalles récents entre rapports (ms), fenêtre glissante.
        private readonly Queue<double> _intervals = new Queue<double>();
        private readonly object _lock = new object();
        private double _lastTick = -1;
        private double _bestHz;
        private readonly System.Diagnostics.Stopwatch _sw = System.Diagnostics.Stopwatch.StartNew();

        private const int WM_INPUT = 0x00FF;
        private const int RIDEV_INPUTSINK = 0x00000100;
        private const int RIM_TYPEMOUSE = 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTDEVICE { public ushort UsagePage; public ushort Usage; public int Flags; public IntPtr Target; }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

        private static readonly Color Accent = Theme.AccentColor;

        public MouseForm(Action<string, int> log)
        {
            _log = log;
            Build();
            Theme.Apply(this);
        }

        private void Build()
        {
            Text = "ONYX — Fréquence de la souris";
            ClientSize = new Size(560, 340);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = Color.FromArgb(245, 246, 248);
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  Fréquence de la souris — ton 1000 Hz est-il vrai ?",
                Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 12.5f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            _hint = new Label
            {
                Text = "Bouge la souris en CERCLES, sans t'arrêter, pendant ~5 secondes. La mesure se lit toute seule.",
                Location = new Point(20, 66), Size = new Size(520, 24), Font = new Font("Segoe UI Semibold", 9.5f),
                ForeColor = Color.FromArgb(60, 64, 72)
            };
            Controls.Add(_hint);

            _live = new Label
            {
                Text = "—", Location = new Point(20, 98), Size = new Size(520, 70),
                Font = new Font("Segoe UI", 40f, FontStyle.Bold), ForeColor = Accent, TextAlign = ContentAlignment.MiddleCenter
            };
            Controls.Add(_live);

            _best = new Label
            {
                Text = "Meilleur stable : —", Location = new Point(20, 176), Size = new Size(520, 24),
                Font = new Font("Segoe UI", 11f), ForeColor = Color.FromArgb(60, 64, 72), TextAlign = ContentAlignment.MiddleCenter
            };
            Controls.Add(_best);

            _verdict = new Label
            {
                Location = new Point(20, 210), Size = new Size(520, 72), ForeColor = Color.FromArgb(60, 64, 72),
                Font = new Font("Segoe UI", 9.5f), TextAlign = ContentAlignment.TopCenter
            };
            Controls.Add(_verdict);

            var btnClose = new Button
            {
                Text = "Fermer", Location = new Point(230, 292), Size = new Size(100, 34), FlatStyle = FlatStyle.Flat,
                BackColor = Accent, ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 9.5f)
            };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s, e) => Close();
            Controls.Add(btnClose);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                var dev = new RAWINPUTDEVICE[1];
                dev[0].UsagePage = 0x01;   // Generic Desktop
                dev[0].Usage = 0x02;       // Mouse
                dev[0].Flags = RIDEV_INPUTSINK;   // reçoit même sans focus clavier
                dev[0].Target = Handle;
                if (!RegisterRawInputDevices(dev, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE))))
                    _verdict.Text = "Impossible d'écouter l'entrée brute de la souris sur ce système.";
            }
            catch { }

            _refresh = new Timer { Interval = 200 };
            _refresh.Tick += (s, ev) => UpdateLabels();
            _refresh.Start();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_INPUT)
            {
                double now = _sw.Elapsed.TotalMilliseconds;
                lock (_lock)
                {
                    if (_lastTick >= 0)
                    {
                        double delta = now - _lastTick;
                        // Garde-fou : ignore le bruit (< 0,05 ms) et les pauses (> 40 ms = souris à l'arrêt).
                        if (delta >= 0.05 && delta <= 40)
                        {
                            _intervals.Enqueue(delta);
                            while (_intervals.Count > 250) _intervals.Dequeue();
                        }
                    }
                    _lastTick = now;
                }
            }
            base.WndProc(ref m);
        }

        private void UpdateLabels()
        {
            double[] arr;
            lock (_lock) { arr = _intervals.ToArray(); }
            if (arr.Length < 8)
            {
                _live.Text = "…";
                _best.Text = "Meilleur stable : —";
                _verdict.Text = "En attente de mouvement continu…";
                return;
            }
            Array.Sort(arr);
            double median = arr[arr.Length / 2];
            // Cluster « rapide » (10e centile) : reflète le vrai palier de polling hors ralentissements.
            double fast = arr[Math.Max(0, arr.Length / 10)];
            int hz = (int)Math.Round(1000.0 / median);
            int hzFast = (int)Math.Round(1000.0 / Math.Max(0.05, fast));
            if (hzFast > _bestHz) _bestHz = hzFast;

            _live.Text = hz + " Hz";
            _best.Text = "Meilleur stable : " + (int)_bestHz + " Hz";
            _verdict.Text = Verdict(hz, (int)_bestHz);
        }

        private static readonly int[] Tiers = { 125, 250, 500, 1000, 2000, 4000, 8000 };

        private static string Verdict(int hz, int best)
        {
            int nearest = Tiers[0]; int bd = int.MaxValue;
            foreach (int t in Tiers) { int d = Math.Abs(t - best); if (d < bd) { bd = d; nearest = t; } }

            if (best >= 950)
                return "✔ Excellent : ta souris rapporte à ~" + nearest + " Hz — palier gaming atteint. "
                     + "En dessous de 1 ms entre deux rapports, l'input est ultra-réactif.";
            if (best >= 450)
                return "Correct : ~" + nearest + " Hz. Beaucoup de souris montent à 1000 Hz — vérifie le logiciel "
                     + "du constructeur (Logitech G HUB, Razer Synapse…) ou un interrupteur sous la souris.";
            if (best >= 200)
                return "⚠ ~" + nearest + " Hz seulement. Passe le polling à 1000 Hz dans le logiciel de ta souris : "
                     + "gain d'input net, surtout en visée.";
            return "⚠ ~" + nearest + " Hz — très bas (réglage par défaut ou USB lent). Monte à 500-1000 Hz dans le "
                 + "logiciel constructeur et branche la souris sur un port USB direct (pas un hub).";
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { if (_refresh != null) { _refresh.Stop(); _refresh.Dispose(); } } catch { }
            if (_log != null && _bestHz > 0) _log("Souris : fréquence réelle mesurée ~" + (int)_bestHz + " Hz.", 0);
            base.OnFormClosed(e);
        }
    }
}
