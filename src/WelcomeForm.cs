using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace DTGOptimizer
{
    /// <summary>Écran de démarrage rapide au premier lancement : oriente et propose une action immédiate.</summary>
    internal class WelcomeForm : Form
    {
        public enum StartAction { Open, ApplyRecommended, StartTrial }
        public StartAction Choice { get; private set; }

        private static string FlagPath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dtg-welcome.txt"); } }
        public static bool AlreadyShown { get { return File.Exists(FlagPath); } }
        public static void MarkShown() { try { File.WriteAllText(FlagPath, "1"); } catch { } }

        public WelcomeForm()
        {
            Choice = StartAction.Open;

            Text = "Bienvenue dans DesTinGOOD PC Optimizer";
            ClientSize = new Size(560, 420);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = Color.White;
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 96, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "DesTinGOOD PC Optimizer", Location = new Point(22, 20), AutoSize = true,
                ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 20f), BackColor = Color.Transparent
            });
            banner.Controls.Add(new Label
            {
                Text = "Réduire la latence et l'input lag, en 1 clic ou à la carte.",
                Location = new Point(24, 60), Size = new Size(500, 20),
                ForeColor = Color.FromArgb(170, 175, 185), BackColor = Color.Transparent, Font = new Font("Segoe UI", 9.5f)
            });
            Controls.Add(banner);

            Controls.Add(new Label
            {
                Text = "86 optimisations réversibles, mesure de latence, overclock GPU, DNS et bien plus. "
                     + "Toutes les modifications sont sauvegardées et annulables en un clic.",
                Location = new Point(24, 112), Size = new Size(512, 44), ForeColor = Color.FromArgb(60, 64, 72)
            });

            var reco = BigButton("⚡  Optimiser automatiquement",
                "Applique les réglages recommandés, adaptés et sûrs (avec sauvegarde).", 24, 168, true);
            reco.Click += (s, e) => { Choice = StartAction.ApplyRecommended; Close(); };

            var open = BigButton("🛠  Ouvrir l'application",
                "Choisir moi-même les optimisations à appliquer.", 24, 246, false);
            open.Click += (s, e) => { Choice = StartAction.Open; Close(); };

            Controls.Add(reco);
            Controls.Add(open);

            if (License.CanStartTrial)
            {
                var trial = BigButton("★  Essai Pro gratuit de 7 jours",
                    "Débloquer overclock, auto-tune, presets avancés et outils DNS.", 24, 324, false);
                trial.Click += (s, e) => { Choice = StartAction.StartTrial; Close(); };
                Controls.Add(trial);
            }
            else
            {
                var later = BigButton("Continuer", "", 24, 324, false);
                later.Height = 40;
                later.Click += (s, e) => { Choice = StartAction.Open; Close(); };
                Controls.Add(later);
            }

            Theme.Apply(this);
        }

        private static Button BigButton(string title, string desc, int x, int y, bool primary)
        {
            var b = new Button
            {
                Location = new Point(x, y), Size = new Size(512, 66), FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(16, 0, 0, 0),
                BackColor = primary ? Color.FromArgb(0, 150, 90) : Color.White,
                ForeColor = primary ? Color.White : Color.FromArgb(40, 44, 52),
                Font = new Font("Segoe UI Semibold", 11f),
                Text = string.IsNullOrEmpty(desc) ? title : (title + "\n" + desc)
            };
            b.FlatAppearance.BorderColor = primary ? Color.FromArgb(0, 150, 90) : Color.FromArgb(200, 204, 210);
            b.FlatAppearance.BorderSize = primary ? 0 : 1;
            if (!string.IsNullOrEmpty(desc))
            {
                // Deuxième ligne en plus petit via un label superposé.
                var sub = new Label
                {
                    Text = desc, AutoSize = false, Size = new Size(470, 20), Location = new Point(16, 36),
                    BackColor = Color.Transparent, Font = new Font("Segoe UI", 8.5f),
                    ForeColor = primary ? Color.FromArgb(220, 245, 235) : Color.FromArgb(110, 115, 125)
                };
                b.Text = title;
                b.TextAlign = ContentAlignment.TopLeft;
                b.Padding = new Padding(16, 12, 0, 0);
                b.Controls.Add(sub);
            }
            return b;
        }
    }
}
