using System;
using System.Drawing;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>Fenêtre d'activation : présente la fonction Pro et permet de coller une clé de licence.</summary>
    internal class LicenseKeyForm : Form
    {
        private TextBox _key;
        private Label _status;

        public LicenseKeyForm(string feature)
        {
            Text = "BT Optimizer — Version Pro";
            ClientSize = new Size(520, 300);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = Color.White;
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  Fonction Pro", Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 14f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            string feat = string.IsNullOrEmpty(feature) ? "Cette fonction" : "« " + feature + " »";
            Controls.Add(new Label
            {
                Text = feat + " fait partie de l'édition Pro.\r\n\r\n"
                     + "L'édition gratuite inclut toutes les optimisations manuelles, la sauvegarde, "
                     + "le point de restauration, la mesure de latence et le moniteur matériel.\r\n\r\n"
                     + "L'édition Pro ajoute : auto-tune matériel, presets eSport/Benchmark, overclock GPU, "
                     + "profil pilote NVIDIA, gardien de démarrage, outils DNS et analyse avancée.",
                Location = new Point(18, 72), Size = new Size(484, 120), ForeColor = Color.FromArgb(50, 54, 62)
            });

            Controls.Add(new Label { Text = "Clé de licence :", Location = new Point(18, 198), AutoSize = true, Font = new Font("Segoe UI Semibold", 9f) });
            _key = new TextBox { Location = new Point(18, 220), Size = new Size(484, 24) };
            Controls.Add(_key);

            _status = new Label { Location = new Point(18, 250), Size = new Size(240, 40), ForeColor = Color.FromArgb(200, 45, 45) };
            Controls.Add(_status);

            var activate = new Button
            {
                Text = "Activer", Width = 110, Location = new Point(392, 250),
                FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 150, 90), ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 10f)
            };
            activate.FlatAppearance.BorderSize = 0;
            activate.Click += OnActivate;
            Controls.Add(activate);
            AcceptButton = activate;

            // Essai gratuit 7 jours (si pas déjà utilisé et pas déjà Pro).
            if (License.CanStartTrial)
            {
                var trial = new Button
                {
                    Text = "Démarrer l'essai Pro gratuit de 7 jours", Width = 254, Location = new Point(266, 250),
                    FlatStyle = FlatStyle.Flat, BackColor = Color.White, Font = new Font("Segoe UI", 9f)
                };
                trial.FlatAppearance.BorderColor = Color.FromArgb(0, 150, 90);
                trial.ForeColor = Color.FromArgb(0, 120, 60);
                trial.Location = new Point(18, 250);
                _status.Location = new Point(18, 250);
                _status.Visible = false;
                activate.Location = new Point(392, 250);
                trial.Click += (s, e) =>
                {
                    if (License.StartTrial())
                    {
                        MessageBox.Show(this, "Essai Pro activé : " + License.TrialDaysLeft + " jours. Toutes les fonctions sont débloquées.",
                            "BT Optimizer", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        DialogResult = DialogResult.OK; Close();
                    }
                };
                Controls.Add(trial);
                trial.BringToFront();
            }
            else if (License.TrialActive)
            {
                _status.ForeColor = Color.FromArgb(0, 120, 60);
                _status.Text = "Essai en cours : " + License.TrialDaysLeft + " jour(s) restant(s).";
            }
            else if (License.TrialUsed)
            {
                _status.Text = "Essai expiré — une clé est nécessaire pour la version Pro.";
            }
        }

        private void OnActivate(object sender, EventArgs e)
        {
            if (License.Activate(_key.Text, true))
            {
                MessageBox.Show(this, "Merci ! Édition Pro activée pour : " + License.Licensee,
                    "BT Optimizer Pro", MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                _status.Text = "Clé invalide. Vérifie qu'elle est collée en entier.";
            }
        }
    }
}
