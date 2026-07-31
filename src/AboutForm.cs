using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>Fenêtre « À propos » : identité produit, version, mentions légales.</summary>
    internal class AboutForm : Form
    {
        public AboutForm()
        {
            string ver = "5.9";
            try { ver = Assembly.GetExecutingAssembly().GetName().Version.ToString(3); } catch { }

            Text = "À propos de ONYX";
            ClientSize = new Size(480, 340);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = Color.White;
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 78, BackColor = Color.FromArgb(28, 30, 38) };
            var name = new Label
            {
                Text = "ONYX", Location = new Point(18, 12), AutoSize = true,
                ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 18f), BackColor = Color.Transparent
            };
            var tag = new Label
            {
                Text = "Optimiseur & diagnostic gaming pour Windows 10/11",
                Location = new Point(20, 48), Size = new Size(440, 18),
                ForeColor = Color.FromArgb(170, 175, 185), BackColor = Color.Transparent, Font = new Font("Segoe UI", 8.5f)
            };
            banner.Controls.Add(name); banner.Controls.Add(tag);
            Controls.Add(banner);

            int count = 173;
            try { count = Catalog.All().Count; } catch { }

            var body = new Label
            {
                Location = new Point(20, 92), Size = new Size(440, 176), ForeColor = Color.FromArgb(50, 54, 62),
                Text =
                    "Version " + ver + "  ·  " + count + " optimisations réversibles + suite de diagnostic\r\n\r\n" +
                    "Optimisation en 1 clic (adaptée au matériel), bilan Santé /100, réparation des crashs " +
                    "(thermique, pilote GPU, réglages néfastes), suite réseau (qualité, traceroute, TCP/IP, DNS), " +
                    "mesure FPS et latence DPC/ISR, benchmark, bibliothèques de jeu, points de restauration.\r\n\r\n" +
                    "Aucune injection (compatible anticheat). Tout est réversible et sauvegardé ; les options " +
                    "de sécurité ne sont jamais appliquées automatiquement.\r\n\r\n" +
                    "Logiciel fourni « en l'état », sans garantie. Non affilié à Microsoft, NVIDIA, " +
                    "AMD ou Intel ; les marques appartiennent à leurs propriétaires."
            };
            Controls.Add(body);

            var eula = new LinkLabel { Text = "Conditions d'utilisation", Location = new Point(20, 336), AutoSize = true };
            eula.LinkClicked += (s, e) => { using (var f = new LicenseForm()) f.ShowDialog(this); };
            Controls.Add(eula);

            // Auto-diagnostic d'ONYX : l'app vérifie sa propre installation (droits, WMI, données…)
            var diag = new Button
            {
                Text = "🩹 Vérifier mon installation", Width = 190, Location = new Point(20, 296), Height = 26,
                FlatStyle = FlatStyle.Flat, BackColor = Color.White
            };
            diag.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            diag.Click += (s, e) =>
            {
                string txt;
                try { txt = SelfCheck.Text(); } catch (Exception ex) { txt = "Le diagnostic a échoué : " + ex.Message; }
                MessageBox.Show(this, txt, "ONYX — auto-diagnostic", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            Controls.Add(diag);

            // Infos de support : tout pour dépanner, dans le presse-papiers, sans donnée perso.
            var supp = new Button
            {
                Text = "📋 Copier les infos de support", Width = 200, Location = new Point(218, 296), Height = 26,
                FlatStyle = FlatStyle.Flat, BackColor = Color.White
            };
            supp.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            supp.Click += (s, e) =>
            {
                try
                {
                    Clipboard.SetText(SelfCheck.SupportInfo());
                    supp.Text = "✓ Copié !";
                }
                catch { supp.Text = "Échec de la copie"; }
            };
            Controls.Add(supp);

            var close = new Button
            {
                Text = "Fermer", Width = 100, Location = new Point(360, 328),
                FlatStyle = FlatStyle.Flat, BackColor = Color.White, DialogResult = DialogResult.OK
            };
            close.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            Controls.Add(close);
            AcceptButton = close;
            ClientSize = new Size(480, 368);   // place pour les deux nouveaux boutons
            Theme.Apply(this);
        }
    }
}
