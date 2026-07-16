using System;
using System.Drawing;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Panneau Overclock : OC GPU réel via l'outillage officiel NVIDIA
    /// (power limit + verrouillage de fréquences), persistance au démarrage,
    /// et diagnostic honnête RAM (XMP) / CPU (BIOS) — là où Windows ne peut pas agir.
    /// </summary>
    internal class OcForm : Form
    {
        private readonly Action<string, int> _log;
        private Sys.GpuOcInfo _gpu;

        private NumericUpDown _numPl, _numLockMin;
        private CheckBox _chkPersist;
        private Label _lblGpuState;

        private static readonly Color Bg     = Color.FromArgb(20, 22, 28);
        private static readonly Color TileBg = Color.FromArgb(32, 35, 44);
        private static readonly Color Green  = Color.FromArgb(120, 230, 150);
        private static readonly Color Warn   = Color.FromArgb(240, 190, 90);
        private static readonly Color Accent = Color.FromArgb(0, 150, 90);

        public OcForm(Action<string, int> log)
        {
            _log = log;
            Build();
            Refresh_();
        }

        private void Build()
        {
            Text = "BT Optimizer — Overclock & diagnostic";
            ClientSize = new Size(720, 560);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BackColor = Bg;
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            int y = 14;

            // ---------- GPU ----------
            AddSection(ref y, "GPU — overclock réel (outillage officiel NVIDIA)");
            _lblGpuState = AddText(ref y, "Lecture...", Color.White, 40);

            var lblPl = AddInline(y, "Power limit (W) :", 16, 150);
            _numPl = new NumericUpDown();
            _numPl.SetBounds(170, y, 90, 24);
            _numPl.Maximum = 1000; _numPl.Minimum = 0;
            _numPl.BackColor = TileBg; _numPl.ForeColor = Color.White;

            var lblLock = AddInline(y, "Verrou fréquence min (MHz) :", 290, 190);
            _numLockMin = new NumericUpDown();
            _numLockMin.SetBounds(484, y, 90, 24);
            _numLockMin.Maximum = 4000; _numLockMin.Minimum = 0; _numLockMin.Increment = 15;
            _numLockMin.BackColor = TileBg; _numLockMin.ForeColor = Color.White;
            Controls.Add(lblPl); Controls.Add(_numPl); Controls.Add(lblLock); Controls.Add(_numLockMin);
            y += 34;

            AddText(ref y,
                "Le verrou épingle le GPU entre « min » et sa fréquence max : plus AUCUNE latence de montée en fréquence.\n" +
                "0 = pas de verrou. Contrepartie : chauffe/