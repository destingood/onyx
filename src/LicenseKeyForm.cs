using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>Fenêtre d'activation : présente la fonction Pro et permet de coller une clé de licence.</summary>
    internal class LicenseKeyForm : Form
    {
        /// <summary>Boutique (les deux offres : abonnement 49 €/an et licence à vie 127 €) —
        /// cohérent avec marketing/landing.html et marketing/PLAN-LANCEMENT.md.</summary>
        internal const string BuyUrl = "https://fluide.gumroad.com";

        private TextBox _key;
        private Label _status;

        public LicenseKeyForm(string feature)
        {
            Text = "Fluide — Version Pro";
            ClientSize = new Size(520, 400);
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
                     + "L'édition Pro ajoute : ⚡ TOUT OPTIMISER (auto-tune matériel), presets eSport/Benchmark, "
                     + "MODE JEU auto, boosters dynamiques (affinité CPU, nettoyeur RAM), overclock GPU, "
                     + "gardien en fond et réglages réseau avancés.",
                Location = new Point(18, 72), Size = new Size(484, 120), ForeColor = Color.FromArgb(50, 54, 62)
            });

            Controls.Add(new Label { Text = "Clé de licence :", Location = new Point(18, 198), AutoSize = true, Font = new Font("Segoe UI Semibold", 9f) });
            _key = new TextBox { Location = new Point(18, 220), Size = new Size(484, 24) };
            Controls.Add(_key);

            _status = new Label { Location = new Point(18, 250), Size = new Size(360, 40), ForeColor = Color.FromArgb(200, 45, 45) };
            Controls.Add(_status);

            var activate = new Button
            {
                Text = "Activer", Width = 110, Location = new Point(392, 250),
                FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(79, 70, 229), ForeColor = Color.White,
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
                trial.FlatAppearance.BorderColor = Color.FromArgb(79, 70, 229);
                trial.ForeColor = Color.FromArgb(67, 56, 202);
                trial.Location = new Point(18, 250);
                _status.Location = new Point(18, 250);
                _status.Visible = false;
                activate.Location = new Point(392, 250);
                trial.Click += (s, e) =>
                {
                    if (License.StartTrial())
                    {
                        MessageBox.Show(this, "Essai Pro activé : " + License.TrialDaysLeft + " jours. Toutes les fonctions sont débloquées.",
                            "Fluide", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        DialogResult = DialogResult.OK; Close();
                    }
                };
                Controls.Add(trial);
                trial.BringToFront();
            }
            else if (License.TrialActive)
            {
                _status.ForeColor = Color.FromArgb(67, 56, 202);
                _status.Text = "Essai en cours : " + License.TrialDaysLeft + " jour(s) restant(s).";
            }
            else if (License.TrialUsed)
            {
                _status.Text = "Essai expiré — une clé est nécessaire pour la version Pro.";
            }
            // ID de CET ordinateur : chaque clé n'est activable que sur un seul PC, le client
            // doit donc pouvoir communiquer cet identifiant pour qu'on lui émette sa clé.
            Controls.Add(new Label
            {
                Text = "ID de cet ordinateur (à fournir pour obtenir ta clé) :",
                Location = new Point(18, 330), AutoSize = true
            });
            var midBox = new TextBox
            {
                Location = new Point(18, 350), Size = new Size(360, 24), ReadOnly = true,
                Font = new Font("Consolas", 10f), TextAlign = HorizontalAlignment.Center
            };
            try { midBox.Text = MachineId.Current; } catch { midBox.Text = "—"; }
            Controls.Add(midBox);
            var midCopy = new Button
            {
                Text = "Copier", Location = new Point(392, 350), Size = new Size(110, 24),
                FlatStyle = FlatStyle.Flat
            };
            midCopy.Click += (s2, e2) => { try { Clipboard.SetText(midBox.Text); midCopy.Text = "Copié ✓"; } catch { } };
            Controls.Add(midCopy);

            Theme.Apply(this);

            // Chemin d'achat : cette fenêtre est le passage obligé de toutes les fonctions Pro —
            // sans ce lien, un utilisateur décidé n'a aucun moyen de payer depuis l'app.
            // (Ajouté après Theme.Apply pour garder ses couleurs sur les deux thèmes.)
            var buy = new LinkLabel
            {
                Text = "🛒 Pas encore de clé ? Passer Pro — 49 €/an, ou 127 € une seule fois.",
                Location = new Point(18, 306), AutoSize = true,
                LinkColor = Color.FromArgb(79, 70, 229), ActiveLinkColor = Color.FromArgb(67, 56, 202),
                LinkBehavior = LinkBehavior.HoverUnderline, BackColor = Color.Transparent
            };
            buy.LinkClicked += (s, e) =>
            {
                try { Process.Start(new ProcessStartInfo(BuyUrl) { UseShellExecute = true }); }
                catch
                {
                    MessageBox.Show(this, "Ouvre cette adresse dans ton navigateur :\r\n" + BuyUrl,
                        "Fluide Pro", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            };
            Controls.Add(buy);
        }

        private void OnActivate(object sender, EventArgs e)
        {
            if (License.Activate(_key.Text, true))
            {
                string until = License.Expiry.HasValue
                    ? "\r\nAbonnement valable jusqu'au " + License.Expiry.Value.ToString("dd/MM/yyyy") + "."
                    : "\r\nLicence à vie — merci !";
                MessageBox.Show(this, "Édition Pro activée pour : " + License.Licensee + until,
                    "Fluide Pro", MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                string err = string.IsNullOrEmpty(License.ActivateError)
                    ? "Clé invalide. Vérifie qu'elle est collée en entier."
                    : License.ActivateError;
                // Le label est masqué quand le bouton d'essai occupe sa place → boîte de dialogue
                // (avant, l'erreur était tout simplement invisible dans ce cas).
                if (_status.Visible) _status.Text = err;
                else MessageBox.Show(this, err, "Fluide Pro", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
