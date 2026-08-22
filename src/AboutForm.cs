using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
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
                Text = "Optimiseur & diagnostic gaming pour Windows 10/11  ·  par destingood",
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
                    "Créé et maintenu par destingood — github.com/destingood/onyx\r\n\r\n" +
                    "Logiciel fourni « en l'état », sans garantie. Non affilié à Microsoft, NVIDIA, " +
                    "AMD ou Intel ; les marques appartiennent à leurs propriétaires."
            };
            Controls.Add(body);

            var eula = new LinkLabel { Text = "Conditions d'utilisation", Location = new Point(20, 398), AutoSize = true };
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

            // Export du diagnostic COMPLET : un fichier texte à joindre à un forum / SAV.
            var expo = new Button
            {
                Text = "📄 Exporter le diagnostic complet (Bureau)", Width = 398, Location = new Point(20, 326), Height = 26,
                FlatStyle = FlatStyle.Flat, BackColor = Color.White
            };
            expo.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            expo.Click += (s, e) =>
            {
                string p = null;
                try { p = DiagExport.Save(); } catch { }
                expo.Text = p != null ? "✓ " + Path.GetFileName(p) + " (Bureau)" : "Échec de l'export";
                if (p != null) { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(p) { UseShellExecute = true }); } catch { } }
            };
            Controls.Add(expo);

            // RAPPORT DE DIAGNOSTIC : le dossier complet, MONTRÉ avant tout envoi. Le bouton
            // n'envoie rien lui-même — il ouvre l'écran où l'utilisateur lit ce qu'il transmet.
            var rap = new Button
            {
                Text = "🧾 Rapport de diagnostic (voir, enregistrer, envoyer)", Width = 398,
                Location = new Point(20, 356), Height = 26,
                FlatStyle = FlatStyle.Flat, BackColor = Color.White
            };
            rap.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            rap.Click += (s, e) => { using (var f = new RapportDiagnosticForm()) f.ShowDialog(this); };
            Controls.Add(rap);

            var close = new Button
            {
                Text = "Fermer", Width = 100, Location = new Point(360, 392),
                FlatStyle = FlatStyle.Flat, BackColor = Color.White, DialogResult = DialogResult.OK
            };
            close.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            Controls.Add(close);
            AcceptButton = close;
            ClientSize = new Size(480, 430);   // place pour diagnostic / support / export / rapport
            Theme.Apply(this);
        }
    }
}
