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

            Text = "À propos de DesTinGOOD";
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
                Text = "DesTinGOOD", Location = new Point(18, 12), AutoSize = true,
                ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 18f), BackColor = Color.Transparent
            };
            var tag = new Label
            {
                Text = "Optimiseur de latence, d'input lag et de performances pour Windows 10/11",
                Location = new Point(20, 48), Size = new Size(440, 18),
                ForeColor = Color.FromArgb(170, 175, 185), BackColor = Color.Transparent, Font = new Font("Segoe UI", 8.5f)
            };
            banner.Controls.Add(name); banner.Controls.Add(tag);
            Controls.Add(banner);

            var body = new Label
            {
                Location = new Point(20, 92), Size = new Size(440, 170), ForeColor = Color.FromArgb(50, 54, 62),
                Text =
                    "Version " + ver + "  ·  62 optimisations réversibles\r\n\r\n" +
                    "Fonctions : optimisations input lag / rapidité, mesure et analyse de latence " +
                    "DPC/ISR, moniteur matériel, overclock GPU, DNS rapide, auto-tune matériel.\r\n\r\n" +
                    "Toutes les modifications sont réversibles et sauvegardées. Les options de sécurité " +
                    "ne sont jamais appliquées automatiquement.\r\n\r\n" +
                    "Logiciel fourni « en l'état », sans garantie. Non affilié à Microsoft, NVIDIA, " +
                    "AMD ou Intel ; les marques appartiennent à leurs propriétaires."
            };
            Controls.Add(body);

            var eula = new LinkLabel { Text = "Conditions d'utilisation", Location = new Point(20, 300), AutoSize = true };
            eula.LinkClicked += (s, e) => { using (var f = new LicenseForm()) f.ShowDialog(this); };
            Controls.Add(eula);

            var close = new Button
            {
                Text = "Fermer", Width = 100, Location = new Point(360, 296),
                FlatStyle = FlatStyle.Flat, BackColor = Color.White, DialogResult = DialogResult.OK
            };
            close.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            Controls.Add(close);
            AcceptButton = close;
            Theme.Apply(this);
        }
    }
}
