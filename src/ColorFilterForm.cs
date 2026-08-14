using System;
using System.Drawing;
using System.Windows.Forms;

namespace BTOptimizer
{
    // ----------------------------------------------------------------------
    //  Réglage du filtre couleur (vibrance / chaleur / contraste). Aperçu en
    //  direct via la rampe gamma ; enregistrement dans bt-colorfilter.txt.
    // ----------------------------------------------------------------------
    internal class ColorFilterForm : Form
    {
        private readonly Action<string, int> _log;
        private ComboBox _preset;
        private TrackBar _intensity;
        private CheckBox _vivid;
        private Label _lblIntensity;
        private bool _loading;

        // État tel qu'il était à l'ouverture, et mémoire d'un enregistrement explicite.
        // L'aperçu ÉCRIT la rampe gamma de l'écran en direct ; sans ces deux champs, fermer la
        // fenêtre sans enregistrer laissait les couleurs de l'aperçu en place — « Fermer » se
        // comprend comme « annuler », il ne doit rien valider.
        private int _ouvertIndex, _ouvertValeur;
        private bool _ouvertVivid, _enregistre;

        public ColorFilterForm(Action<string, int> log)
        {
            _log = log;
            BuildUi();
            LoadState();
            Theme.Apply(this);
        }

        private void BuildUi()
        {
            Text = "Filtre couleur / vibrance — ONYX";
            ClientSize = new Size(460, 280);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            Font = new Font("Segoe UI", 9f);

            var intro = new Label();
            intro.SetBounds(16, 14, 428, 40);
            intro.Text = "Booste les couleurs de l'écran (vibrance) façon panneau NVIDIA/AMD, "
                       + "sans pilote. 100 % réversible. Idéal pour faire ressortir les ennemis.";
            intro.ForeColor = Theme.InkDimColor;

            var lblP = new Label();
            lblP.SetBounds(16, 66, 110, 22);
            lblP.Text = "Preset :";
            _preset = new ComboBox();
            _preset.SetBounds(130, 63, 300, 26);
            _preset.DropDownStyle = ComboBoxStyle.DropDownList;
            _preset.Items.AddRange(ColorFilter.PresetNames);
            _preset.SelectedIndexChanged += (s, e) => Preview();

            _lblIntensity = new Label();
            _lblIntensity.SetBounds(16, 104, 414, 22);
            _lblIntensity.Text = "Intensité : 50 %";
            _intensity = new TrackBar();
            _intensity.SetBounds(14, 126, 432, 40);
            _intensity.Minimum = 0; _intensity.Maximum = 100;
            _intensity.TickFrequency = 10; _intensity.Value = 50;
            _intensity.Scroll += (s, e) => Preview();

            _vivid = new CheckBox();
            _vivid.SetBounds(16, 172, 414, 22);
            _vivid.Text = "Mode « vivid » (contraste renforcé)";
            _vivid.CheckedChanged += (s, e) => Preview();

            var btnSave = MakeButton("Appliquer & enregistrer", 16, 214, 200, 44, true);
            btnSave.Click += OnSave;
            var btnOff = MakeButton("Désactiver", 226, 214, 120, 44, false);
            btnOff.Click += OnDisable;
            var btnClose = MakeButton("Fermer", 356, 214, 88, 44, false);
            btnClose.Click += (s, e) => Close();

            Controls.AddRange(new Control[]
            {
                intro, lblP, _preset, _lblIntensity, _intensity, _vivid, btnSave, btnOff, btnClose
            });
        }

        private void LoadState()
        {
            _loading = true;
            int index, value; bool vivid;
            ColorFilter.Load(out index, out value, out vivid);
            if (index < 0 || index >= _preset.Items.Count) index = 0;
            _preset.SelectedIndex = index;
            _intensity.Value = Math.Max(0, Math.Min(100, value));
            _vivid.Checked = vivid;
            _lblIntensity.Text = "Intensité : " + _intensity.Value + " %";
            _loading = false;

            // On retient l'état d'ouverture pour pouvoir y revenir si l'utilisateur ferme
            // sans enregistrer.
            _ouvertIndex = _preset.SelectedIndex;
            _ouvertValeur = _intensity.Value;
            _ouvertVivid = _vivid.Checked;
        }

        /// <summary>
        /// Fermeture sans enregistrement = annulation. On remet la rampe gamma dans l'état où on
        /// l'a trouvée : l'aperçu écrit sur l'écran pour de vrai, il ne doit rien laisser derrière
        /// lui que l'utilisateur n'ait pas validé.
        /// </summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_enregistre)
            {
                try
                {
                    if (_ouvertIndex <= 0) ColorFilter.Disable();
                    else ColorFilter.Apply(_ouvertIndex, _ouvertValeur, _ouvertVivid);
                }
                catch { }
            }
            base.OnFormClosing(e);
        }

        /// <summary>Aperçu en direct (n'enregistre pas).</summary>
        private void Preview()
        {
            if (_loading) return;
            _lblIntensity.Text = "Intensité : " + _intensity.Value + " %";
            try { ColorFilter.Apply(_preset.SelectedIndex, _intensity.Value, _vivid.Checked); }
            catch { }
        }

        private void OnSave(object sender, EventArgs e)
        {
            int index = _preset.SelectedIndex;
            int value = _intensity.Value;
            bool vivid = _vivid.Checked;
            bool ok = false;
            try { ok = ColorFilter.Apply(index, value, vivid); } catch { }
            ColorFilter.Save(index, value, vivid);
            _enregistre = true;   // choix validé : la fermeture ne doit plus rien annuler
            if (_log != null)
            {
                if (index <= 0) _log("Filtre couleur désactivé.", 0);
                else if (ok) _log("Filtre couleur appliqué : " + ColorFilter.PresetNames[index] + " (" + value + " %"
                    + (vivid ? ", vivid" : "") + ").", 1);
                else _log("Filtre couleur : impossible d'écrire la rampe gamma (écran/pilote refusé).", 2);
            }
            Close();
        }

        private void OnDisable(object sender, EventArgs e)
        {
            _loading = true;
            _preset.SelectedIndex = 0;
            _loading = false;
            try { ColorFilter.Disable(); } catch { }
            ColorFilter.Save(0, _intensity.Value, _vivid.Checked);
            // Désactivation explicite : elle devient l'état de référence, sans quoi fermer la
            // fenêtre juste après aurait remis le filtre que l'utilisateur vient de retirer.
            _enregistre = true; _ouvertIndex = 0;
            if (_log != null) _log("Filtre couleur désactivé (rampe gamma rétablie).", 0);
        }

        private static Button MakeButton(string text, int x, int y, int w, int h, bool primary)
        {
            var b = new Button();
            b.Text = text;
            b.SetBounds(x, y, w, h);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = primary ? 0 : 1;
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            b.BackColor = primary ? Theme.AccentColor : Color.White;
            b.ForeColor = primary ? Color.FromArgb(16, 13, 9) : Color.FromArgb(40, 44, 52);
            b.UseVisualStyleBackColor = false;
            return b;
        }
    }
}
