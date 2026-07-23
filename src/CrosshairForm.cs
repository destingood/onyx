using System;
using System.Drawing;
using System.Windows.Forms;

namespace BTOptimizer
{
    // ----------------------------------------------------------------------
    //  Réglage du viseur (crosshair) avec aperçu en direct par-dessus l'écran.
    // ----------------------------------------------------------------------
    internal class CrosshairForm : Form
    {
        private readonly Action<string, int> _log;
        private CrosshairSettings _s;

        private ComboBox _shape;
        private Button _color;
        private TrackBar _size, _thick, _gap, _dot, _opacity;
        private Label _lSize, _lThick, _lGap, _lDot, _lOpacity;
        private CheckBox _outline, _enabled;
        private bool _loading;

        public CrosshairForm(Action<string, int> log)
        {
            _log = log;
            _s = CrosshairSettings.Load();
            BuildUi();
            LoadState();
            Theme.Apply(this);
        }

        private void BuildUi()
        {
            Text = "Viseur (crosshair) — Fluide";
            ClientSize = new Size(470, 486);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            Font = new Font("Segoe UI", 9f);

            var intro = new Label();
            intro.SetBounds(16, 12, 438, 38);
            intro.Text = "Réticule au centre de l'écran par-dessus tes jeux (fenêtré / sans bordure). "
                       + "Idéal quand le jeu n'a pas de viseur. L'aperçu est en direct.";
            intro.ForeColor = Theme.InkDimColor;

            var lShape = new Label(); lShape.SetBounds(16, 60, 90, 22); lShape.Text = "Forme :";
            _shape = new ComboBox();
            _shape.SetBounds(110, 57, 240, 26);
            _shape.DropDownStyle = ComboBoxStyle.DropDownList;
            _shape.Items.AddRange(new object[] { "Croix", "Croix + point", "Point seul", "Cercle", "Cercle + point" });
            _shape.SelectedIndexChanged += (s, e) => Apply();

            var lColor = new Label(); lColor.SetBounds(16, 94, 90, 22); lColor.Text = "Couleur :";
            _color = new Button();
            _color.SetBounds(110, 91, 90, 26);
            _color.FlatStyle = FlatStyle.Flat;
            _color.Click += OnPickColor;

            _size    = MakeSlider(1, 40, out _lSize, 130);
            _thick   = MakeSlider(1, 8,  out _lThick, 176);
            _gap     = MakeSlider(0, 30, out _lGap, 222);
            _dot     = MakeSlider(0, 12, out _lDot, 268);
            _opacity = MakeSlider(10, 100, out _lOpacity, 314);

            _outline = new CheckBox();
            _outline.SetBounds(16, 360, 220, 22);
            _outline.Text = "Liseré noir (lisibilité)";
            _outline.CheckedChanged += (s, e) => Apply();

            _enabled = new CheckBox();
            _enabled.SetBounds(250, 360, 204, 22);
            _enabled.Text = "Afficher le viseur";
            _enabled.CheckedChanged += OnToggleEnabled;

            var btnSave = MakeButton("Enregistrer", 16, 424, 180, 44, true);
            btnSave.Click += OnSave;
            var btnClose = MakeButton("Fermer", 206, 424, 120, 44, false);
            btnClose.Click += (s, e) => Close();
            var hint = new Label();
            hint.SetBounds(336, 424, 120, 44);
            hint.Text = "Astuce : jeu en mode fenêtré sans bordure.";
            hint.ForeColor = Theme.InkDimColor;
            hint.Font = new Font("Segoe UI", 8f);

            Controls.AddRange(new Control[]
            {
                intro, lShape, _shape, lColor, _color,
                _lSize, _size, _lThick, _thick, _lGap, _gap, _lDot, _dot, _lOpacity, _opacity,
                _outline, _enabled, btnSave, btnClose, hint
            });
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
            _shape.SelectedIndex = Math.Max(0, Math.Min(4, _s.Shape));
            _color.BackColor = _s.Color;
            _size.Value = Clamp(_s.Size, _size);
            _thick.Value = Clamp(_s.Thickness, _thick);
            _gap.Value = Clamp(_s.Gap, _gap);
            _dot.Value = Clamp(_s.Dot, _dot);
            _opacity.Value = Clamp(_s.Opacity, _opacity);
            _outline.Checked = _s.Outline;
            _enabled.Checked = _s.Enabled;
            _loading = false;
            UpdateLabels();
            if (_s.Enabled) Crosshair.Show(Collect());
        }

        private static int Clamp(int v, TrackBar tb) { return Math.Max(tb.Minimum, Math.Min(tb.Maximum, v)); }

        private CrosshairSettings Collect()
        {
            _s.Shape = _shape.SelectedIndex;
            _s.Color = _color.BackColor;
            _s.Size = _size.Value;
            _s.Thickness = _thick.Value;
            _s.Gap = _gap.Value;
            _s.Dot = _dot.Value;
            _s.Opacity = _opacity.Value;
            _s.Outline = _outline.Checked;
            _s.Enabled = _enabled.Checked;
            return _s;
        }

        private void UpdateLabels()
        {
            _lSize.Text = "Longueur des branches : " + _size.Value + " px";
            _lThick.Text = "Épaisseur : " + _thick.Value + " px";
            _lGap.Text = "Écart central : " + _gap.Value + " px";
            _lDot.Text = "Point central : " + _dot.Value + " px";
            _lOpacity.Text = "Opacité : " + _opacity.Value + " %";
        }

        /// <summary>Applique en direct dans l'overlay si affiché.</summary>
        private void Apply()
        {
            if (_loading) return;
            UpdateLabels();
            if (Crosshair.IsVisible) Crosshair.Update(Collect());
            else Collect();
        }

        private void OnToggleEnabled(object sender, EventArgs e)
        {
            if (_loading) return;
            if (_enabled.Checked) Crosshair.Show(Collect());
            else { Crosshair.Hide(); Collect(); }
        }

        private void OnPickColor(object sender, EventArgs e)
        {
            using (var dlg = new ColorDialog())
            {
                dlg.Color = _color.BackColor;
                dlg.FullOpen = true;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _color.BackColor = dlg.Color;
                    Apply();
                }
            }
        }

        private void OnSave(object sender, EventArgs e)
        {
            CrosshairSettings s = Collect();
            s.Save();
            if (s.Enabled) Crosshair.Show(s);
            if (_log != null)
                _log(s.Enabled ? "Viseur enregistré et affiché." : "Viseur enregistré (masqué).", 1);
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
            b.BackColor = primary ? Color.FromArgb(79, 70, 229) : Color.White;
            b.ForeColor = primary ? Color.White : Color.FromArgb(40, 44, 52);
            b.UseVisualStyleBackColor = false;
            return b;
        }
    }
}
