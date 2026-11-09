using System;
using System.Drawing;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Fenêtre « Mon compte » (ouverte par le bloc profil en bas du rail) : quelques stats de
    /// la machine (optimisations actives, jeux détectés, santé). Volontairement sobre : ONYX est
    /// gratuit, il n'y a ni licence ni clé à gérer ici.
    /// </summary>
    internal class AccountForm : Form
    {
        private Label _stats;

        public AccountForm()
        {
            Text = "ONYX — Mon compte";
            ClientSize = new Size(560, 250);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = Color.White;   // Theme.Apply recolore ensuite selon le thème
            Font = new Font("Segoe UI", 9.5f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            BuildUi();
            Theme.Apply(this);
            LoadStats();
        }

        private void BuildUi()
        {
            var banner = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Color.FromArgb(12, 14, 13) };
            banner.Controls.Add(new Label
            {
                Text = "  Mon compte", Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 15f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            // --- Statistiques de la machine --------------------------------
            var gStats = new GroupBox { Text = "Mon PC", Location = new Point(18, 82), Size = new Size(524, 100) };
            _stats = new Label { Location = new Point(16, 26), Size = new Size(492, 62), Text = "Calcul en cours…", ForeColor = Theme.InkColor };
            gStats.Controls.Add(_stats);
            Controls.Add(gStats);

            var close = new Button { Text = "Fermer", Location = new Point(442, 198), Size = new Size(100, 32), FlatStyle = FlatStyle.Flat };
            close.Click += (s, e) => Close();
            Controls.Add(close);
        }

        private void LoadStats()
        {
            AppStats.Snap c = AppStats.Cached;
            if (c != null) ShowStats(c);
            AppStats.Get(s => { try { BeginInvoke((Action)(() => ShowStats(s))); } catch { } });
        }

        private void ShowStats(AppStats.Snap s)
        {
            if (s == null) return;
            _stats.Text =
                "Optimisations actives : " + s.OptiActive + " / " + s.OptiTotal + "\r\n" +
                "Jeux détectés : " + s.GamesDet + "\r\n" +
                "Santé estimée : " + s.Health + " %";
        }
    }
}
