using System;
using System.Drawing;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Fenêtre « Mon compte » (ouverte par le bloc profil en bas du rail) : état de la licence
    /// (Gratuit / Pro / essai), bouton pour activer une clé ou passer Pro, et quelques stats
    /// de la machine (optimisations actives, jeux détectés, santé). Volontairement sobre —
    /// pas de grille de prix (le modèle est achat unique / à vie ; l'achat passe par LicenseKeyForm).
    /// </summary>
    internal class AccountForm : Form
    {
        private Label _big, _sub, _stats;
        private Button _activate;

        public AccountForm()
        {
            Text = "ONYX — Mon compte";
            ClientSize = new Size(560, 490);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = Color.White;   // Theme.Apply recolore ensuite selon le thème
            Font = new Font("Segoe UI", 9.5f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            BuildUi();
            Theme.Apply(this);
            RefreshLicense();
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

            // --- Licence -------------------------------------------------
            var gLic = new GroupBox { Text = "Licence", Location = new Point(18, 82), Size = new Size(524, 138) };
            _big = new Label { Location = new Point(16, 30), Size = new Size(492, 28), Font = new Font("Segoe UI Semibold", 14f) };
            _sub = new Label { Location = new Point(16, 62), Size = new Size(492, 24), ForeColor = Theme.InkDimColor };
            _activate = new Button
            {
                Location = new Point(16, 92), Size = new Size(260, 32), FlatStyle = FlatStyle.Flat,
                BackColor = Theme.AccentColor, ForeColor = Color.FromArgb(16, 13, 9),
                Font = new Font("Segoe UI Semibold", 9.5f), Cursor = Cursors.Hand
            };
            _activate.FlatAppearance.BorderSize = 0;
            _activate.Click += (s, e) =>
            {
                try { using (var f = new LicenseKeyForm("Mon compte")) f.ShowDialog(this); } catch { }
                RefreshLicense();
            };
            gLic.Controls.AddRange(new Control[] { _big, _sub, _activate });
            Controls.Add(gLic);

            // --- Statistiques + identifiant de CET ordinateur -------------
            var gStats = new GroupBox { Text = "Mon PC", Location = new Point(18, 232), Size = new Size(524, 178) };
            _stats = new Label { Location = new Point(16, 26), Size = new Size(492, 62), Text = "Calcul en cours…", ForeColor = Theme.InkColor };

            var midCap = new Label
            {
                Location = new Point(16, 94), Size = new Size(492, 18), ForeColor = Theme.InkDimColor,
                Text = "ID de cet ordinateur — à communiquer pour recevoir ta clé :"
            };
            var mid = new TextBox
            {
                Location = new Point(16, 114), Size = new Size(340, 26), ReadOnly = true,
                Font = new Font("Consolas", 11f), TextAlign = HorizontalAlignment.Center
            };
            try { mid.Text = MachineId.Current; } catch { mid.Text = "—"; }
            var copyMid = new Button { Text = "Copier l'ID", Location = new Point(366, 114), Size = new Size(142, 26), FlatStyle = FlatStyle.Flat };
            copyMid.Click += (s, e) => { try { Clipboard.SetText(mid.Text); copyMid.Text = "Copié ✓"; } catch { } };
            var midNote = new Label
            {
                Location = new Point(16, 146), Size = new Size(492, 18), ForeColor = Theme.InkDimColor,
                Text = "Chaque clé n'est activable que sur l'ordinateur pour lequel elle a été émise."
            };

            gStats.Controls.AddRange(new Control[] { _stats, midCap, mid, copyMid, midNote });
            Controls.Add(gStats);

            var close = new Button { Text = "Fermer", Location = new Point(442, 422), Size = new Size(100, 32), FlatStyle = FlatStyle.Flat };
            close.Click += (s, e) => Close();
            Controls.Add(close);
        }

        private void RefreshLicense()
        {
            bool pro = License.ProUnlocked;
            _big.Text = pro ? "★ Édition Pro" : "Édition gratuite";
            _big.ForeColor = pro ? Theme.OkColor : Theme.InkColor;
            _sub.Text = License.Status();
            _activate.Text = pro ? "Gérer / changer de clé" : "Activer une clé  ·  Passer Pro";
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
