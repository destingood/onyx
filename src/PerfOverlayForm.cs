using System;
using System.Drawing;
using System.Windows.Forms;

namespace BTOptimizer
{
    // ----------------------------------------------------------------------
    //  Réglage de l'overlay de performances en jeu, avec aperçu en direct.
    // ----------------------------------------------------------------------
    internal class PerfOverlayForm : Form
    {
        private readonly Action<string, int> _log;
        private PerfOverlaySettings _s;

        private ComboBox _corner;
        private TrackBar _size, _opacity;
        private Label _lSize, _lOpacity;
        private CheckBox _fps, _cpu, _gpu, _ram, _gameOnly, _enabled;
        private bool _loading;

        public PerfOverlayForm(Action<string, int> log)
        {
            _log = log;
            _s = PerfOverlaySettings.Load();
            BuildUi();
            LoadState();
            Theme.Apply(this);
        }

        private void BuildUi()
        {
            Text = "Overlay perfs en jeu — DesTinGOOD";
            ClientSize = new Size(470, 470);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            Font = new Font("Segoe UI", 9f);

            var intro = new Label();
            intro.SetBounds(16, 12, 438, 54);
            intro.Text = "FPS + CPU / GPU / RAM affichés dans un coin de l'écran, par-dessus tes jeux "
                       + "(fenêtré / sans bordure). 100 % natif : AUCUNE injection dans le jeu "
                       + "(compatible anticheat), zéro impact. L'aperçu est en direct.";
            intro.ForeColor = Theme.InkDimColor;

            var lCorner = new Label(); lCorner.SetBounds(16, 76, 90, 22); lCorner.Text = "Position :";
            _corner = new ComboBox();
            _corner.SetBounds(110, 73, 240, 26);
            _corner.DropDownStyle = ComboBoxStyle.DropDownList;
            _corner.Items.AddRange(new object[] { "Haut gauche", "Haut droite", "Bas gauche", "Bas droite" });
            _corner.SelectedIndexChanged += (s, e) => Apply();

            _size    = MakeSlider(8, 20, out _lSize, 112);
            _opacity = MakeSlider(30, 100, out _lOpacity, 158);

            var gb = new GroupBox();
            gb.Text = "Éléments affichés";
            gb.SetBounds(16, 204, 438, 122);

            _fps = MakeCheck(gb, "FPS + frametime du jeu (façon PresentMon)", 14, 22);
            _cpu = MakeCheck(gb, "CPU : charge + température", 14, 46);
            _gpu = MakeCheck(gb, "GPU : charge + température", 14, 70);
            _ram = MakeCheck(gb, "RAM utilisée / totale", 14, 94);

            _gameOnly = new CheckBox();
            _gameOnly.SetBounds(16, 336, 438, 22);
            _gameOnly.Text = "Seulement EN JEU (masqué sur le bureau, apparaît en plein écran)";
            _gameOnly.CheckedChanged += (s, e) => Apply();

            _enabled = new CheckBox();
            _enabled.SetBounds(16, 362, 438, 22);
            _enabled.Text = "Afficher l'overlay (raccourci global : Ctrl+Alt+O)";
            _enabled.CheckedChanged += OnToggleEnabled;

            var btnSave = MakeButton("Enregistrer", 16, 408, 180, 44, true);
            btnSave.Click += OnSave;
            var btnClose = MakeButton("Fermer", 206, 408, 120, 44, false);
            btnClose.Click += (s, e) => Close();
            var hint = new Label();
            hint.SetBounds(336, 408, 120, 44);
            hint.Text = "Ctrl+Alt+O : on/off même en pleine partie.";
            hint.ForeColor = Theme.InkDimColor;
            hint.Font = new Font("Segoe UI", 8f);

            Controls.AddRange(new Control[]
            {
                intro, lCorner, _corner,
                _lSize, _size, _lOpacity, _opacity,
                gb, _gameOnly, _enabled, btnSave, btnClose, hint
            });
        }

        private CheckBox MakeCheck(GroupBox parent, string text, int x, int y)
        {
            var cb = new CheckBox();
            cb.SetBounds(x, y, 408, 22);
            cb.Text = text;
            cb.CheckedChanged += (s, e) => Apply();
            parent.Controls.Add(cb);
            return cb;
        }

        private TrackBar MakeSlider(int min, int max, out Label label, int y)
        {
            label = new Label();
            label.SetBounds(16, y, 320, 20);
            var tb = new TrackBar();
            tb.SetBounds(330, y - 4, 128, 40);
            tb.Minimum = min; tb.Maximum = max;
            tb.TickStyle = TickStyle.None;
            tb.Scroll += (s, e) => Apply();
            return tb;
        }

        private void LoadState()
        {
            _loading = true;
            _corner.SelectedIndex = Math.Max(0, Math.Min(3, _s.Corner));
            _size.Value = Clamp(_s.TextPt, _size);
            _opacity.Value = Clamp(_s.Opacity, _opacity);
            _fps.Checked = _s.ShowFps;
            _cpu.Checked = _s.ShowCpu;
            _gpu.Checked = _s.ShowGpu;
            _ram.Checked = _s.ShowRam;
            _gameOnly.Checked = _s.GameOnly;
            _enabled.Checked = _s.Enabled;
            _loading = false;
            UpdateLabels();
            if (_s.Enabled) PerfOverlay.Show(Collect());
        }

        private static int Clamp(int v, TrackBar tb) { return Math.Max(tb.Minimum, Math.Min(tb.Maximum, v)); }

        private PerfOverlaySettings Collect()
        {
            _s.Corner = _corner.SelectedIndex;
            _s.TextPt = _size.Value;
            _s.Opacity = _opacity.Value;
            _s.ShowFps = _fps.Checked;
            _s.ShowCpu = _cpu.Checked;
            _s.ShowGpu = _gpu.Checked;
            _s.ShowRam = _ram.Checked;
            _s.GameOnly = _gameOnly.Checked;
            _s.Enabled = _enabled.Checked;
            return _s;
        }

        private void UpdateLabels()
        {
            _lSize.Text = "Taille du texte : " + _size.Value + " pt";
            _lOpacity.Text = "Opacité : " + _opacity.Value + " %";
        }

        /// <summary>Applique en direct dans l'overlay si affiché.</summary>
        private void Apply()
        {
            if (_loading) return;
            UpdateLabels();
            if (PerfOverlay.IsVisible) PerfOverlay.Update(Collect());
            else Collect();
        }

        private void OnToggleEnabled(object sender, EventArgs e)
        {
            if (_loading) return;
            if (_enabled.Checked) PerfOverlay.Show(Collect());
            else { PerfOverlay.Hide(); Collect(); }
        }

        private void OnSave(object sender, EventArgs e)
        {
            PerfOverlaySettings s = Collect();
            s.Save();
            if (s.Enabled) PerfOverlay.Show(s);
            if (_log != null)
                _log(s.Enabled ? "Overlay perfs enregistré et affiché (Ctrl+Alt+O pour masquer)."
                               : "Overlay perfs enregistré (masqué).", 1);
            Close();
        }

        private static Button MakeButton(string text, int x, int y, int w, int h, bool primary)
        {
            var b = new Button();
            b.Text = text;
            b.SetBounds(x, y, w, h);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = primary ? 0 : 1;
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            b.BackColor = primary ? Color.FromArgb(0, 150, 90) : Color.White;
            b.ForeColor = primary ? Color.White : Color.FromArgb(40, 44, 52);
            b.UseVisualStyleBackColor = false;
            return b;
        }
    }
}
