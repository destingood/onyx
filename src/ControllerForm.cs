using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Test manette : détecte les manettes XInput (Xbox/compatibles) et mesure leur taux de rapport
    /// réel (polling rate) en comptant les changements d'état pendant que l'utilisateur bouge les
    /// sticks. Lecture seule, aucun pilote, aucune injection — juste l'API XInput de Windows.
    /// </summary>
    internal class ControllerForm : Form
    {
        private readonly Action<string, int> _log;
        private static readonly Color Accent = Color.FromArgb(79, 70, 229);

        [DllImport("xinput1_4.dll")] private static extern int XInputGetState(int dwUserIndex, out XINPUT_STATE state);
        [StructLayout(LayoutKind.Sequential)] private struct XINPUT_STATE { public uint dwPacketNumber; public XINPUT_GAMEPAD Gamepad; }
        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_GAMEPAD
        {
            public ushort wButtons; public byte bLeftTrigger; public byte bRightTrigger;
            public short sThumbLX; public short sThumbLY; public short sThumbRX; public short sThumbRY;
        }

        private Panel _canvas;
        private Button _btnRun, _btnClose;
        private string _status = "Branche/allume ta manette, puis lance la mesure.";
        private int _measured = -1, _measuredSlot = -1;
        private bool _running;

        public ControllerForm(Action<string, int> log)
        {
            _log = log;
            Build();
            Theme.Apply(this);
        }

        private void Build()
        {
            Text = "Fluide — Test manette";
            ClientSize = new Size(560, 430);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(12, 14, 13) };
            banner.Controls.Add(new Label
            {
                Text = "  Test manette — détection · polling rate",
                Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 12.5f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            _canvas = new Panel { Location = new Point(0, 50), Size = new Size(560, 322), BackColor = Color.Transparent };
            _canvas.Paint += PaintBody;
            Controls.Add(_canvas);

            _btnRun = MakeBtn("Mesurer (bouge les sticks 3 s)", 18, 382, 240, 36, true);
            _btnRun.Click += (s, e) => Run();
            _btnClose = MakeBtn("Fermer", 452, 382, 90, 36, false);
            _btnClose.Click += (s, e) => Close();
            Controls.Add(_btnRun); Controls.Add(_btnClose);
        }

        private static Button MakeBtn(string text, int x, int y, int w, int h, bool primary)
        {
            var b = new Button
            {
                Text = text, Location = new Point(x, y), Size = new Size(w, h), FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Color.FromArgb(40, 44, 52),
                Font = primary ? new Font("Segoe UI Semibold", 9.5f) : new Font("Segoe UI", 9f)
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            return b;
        }

        private static bool Connected(int i)
        {
            try { XINPUT_STATE s; return XInputGetState(i, out s) == 0; } catch { return false; }
        }

        private int FirstConnected()
        {
            for (int i = 0; i < 4; i++) if (Connected(i)) return i;
            return -1;
        }

        private void Run()
        {
            if (_running) return;
            int slot = FirstConnected();
            if (slot < 0) { _status = "Aucune manette XInput détectée. Branche une manette Xbox/compatible (ou allume-la)."; _canvas.Invalidate(); return; }
            _running = true; _btnRun.Enabled = false;
            _measured = -1; _measuredSlot = slot;
            _status = "Mesure en cours — BOUGE les sticks à fond pendant 3 secondes…";
            _canvas.Invalidate();
            Task.Run(() =>
            {
                int hz = MeasureHz(slot, 3000);
                Ui(() =>
                {
                    _measured = hz;
                    _status = hz <= 0 ? "Aucun mouvement capté : relance et bouge bien les sticks pendant la mesure." : null;
                    _running = false; _btnRun.Enabled = true; _canvas.Invalidate();
                    if (_log != null) _log("Test manette : ~" + hz + " Hz (slot " + slot + ").", 0);
                });
            });
        }

        private void Ui(Action a) { try { if (IsHandleCreated) BeginInvoke(a); } catch { } }

        // Compte les incréments de dwPacketNumber (1 par changement d'état) pendant la fenêtre :
        // en bougeant les sticks, ce taux ≈ le taux de rapport réel de la manette.
        private static int MeasureHz(int slot, int ms)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                uint first = 0, last = 0; bool have = false;
                XINPUT_STATE s;
                while (sw.ElapsedMilliseconds < ms)
                {
                    if (XInputGetState(slot, out s) == 0)
                    {
                        if (!have) { first = s.dwPacketNumber; have = true; }
                        last = s.dwPacketNumber;
                    }
                    Thread.SpinWait(80);   // poll bien plus vite que le taux de rapport
                }
                double sec = Math.Max(0.1, sw.Elapsed.TotalSeconds);
                long changes = have ? (long)(last - first) : 0;
                return (int)Math.Round(changes / sec);
            }
            catch { return -1; }
        }

        private void PaintBody(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            int w = _canvas.Width;
            Color ink = Theme.InkColor, dim = Theme.InkDimColor;

            // Ligne de détection des 4 slots XInput.
            TextRenderer.DrawText(g, "MANETTES DÉTECTÉES (XInput)", new Font("Segoe UI Semibold", 9f), new Point(30, 18), dim, TextFormatFlags.NoPadding);
            int any = 0;
            for (int i = 0; i < 4; i++)
            {
                bool on = Connected(i);
                if (on) any++;
                var r = new Rectangle(30 + i * 128, 42, 116, 40);
                using (var br = new SolidBrush(on ? Color.FromArgb(16, 26, 21) : Color.FromArgb(15, 16, 15)))
                using (var path = FpsUi.Round(r, 8f)) g.FillPath(br, path);
                using (var pen = new Pen(on ? Accent : Color.FromArgb(40, 43, 41)))
                using (var path = FpsUi.Round(r, 8f)) g.DrawPath(pen, path);
                TextRenderer.DrawText(g, "P" + (i + 1) + (on ? "  ✔" : "  —"), new Font("Segoe UI Semibold", 10f),
                    r, on ? Accent : dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            // Cadran du polling rate mesuré.
            TextRenderer.DrawText(g, "TAUX DE RAPPORT MESURÉ", new Font("Segoe UI Semibold", 9f), new Point(30, 104), dim, TextFormatFlags.NoPadding);
            string val = _measured > 0 ? _measured.ToString() : (_measured == 0 ? "0" : "—");
            Color vc = _measured <= 0 ? dim : _measured >= 500 ? Accent : _measured >= 200 ? Color.FromArgb(220, 170, 40) : Color.FromArgb(210, 130, 60);
            using (var vf = new Font("Segoe UI", 46f, FontStyle.Bold))
                TextRenderer.DrawText(g, val, vf, new Point(26, 126), vc, TextFormatFlags.NoPadding);
            if (_measured > 0)
                using (var uf = new Font("Segoe UI", 12f))
                    TextRenderer.DrawText(g, "Hz", uf, new Point(30, 214), ink, TextFormatFlags.NoPadding);

            // Verdict / statut.
            string verdict = _status;
            if (verdict == null && _measured > 0)
            {
                verdict = _measured >= 500
                    ? "✔ Excellente réactivité (≥ 500 Hz) : manette filaire ou dongle rapide. Idéale en compétitif."
                    : _measured >= 200
                        ? "Correct (~250 Hz). Pour descendre l'input lag : USB filaire plutôt que Bluetooth."
                        : "≈ 125 Hz — typique du Bluetooth. Branche la manette en USB (ou dongle propriétaire) pour ~4× plus réactif.";
            }
            if (verdict != null)
                using (var f = new Font("Segoe UI", 9.5f))
                    TextRenderer.DrawText(g, verdict, f, new Rectangle(30, 238, w - 60, 70), ink,
                        TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }
    }
}
