using System;
using System.Drawing;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>Conditions d'utilisation + avertissement, à accepter au premier lancement (protection juridique).</summary>
    internal class LicenseForm : Form
    {
        public const int Version = 1;

        private static readonly Color Header = Color.FromArgb(28, 30, 38);

        private const string Eula =
"CONDITIONS D'UTILISATION ET AVERTISSEMENT\r\n" +
"\r\n" +
"DesTinGOOD (« le logiciel ») modifie des paramètres système de Windows : registre, " +
"plan d'alimentation, services, réglages réseau, DNS et pilote graphique.\r\n" +
"\r\n" +
"1. RISQUES. Certaines options réduisent volontairement des protections de sécurité " +
"(mitigations Spectre/Meltdown, Intégrité de la mémoire / VBS) ou modifient le comportement " +
"matériel (power limit et fréquences du GPU). Elles ne sont ni cochées par défaut ni incluses " +
"dans les presets automatiques, et doivent être activées en connaissance de cause.\r\n" +
"\r\n" +
"2. RÉVERSIBILITÉ. Chaque modification est réversible et une sauvegarde du registre (.reg) " +
"ainsi qu'un point de restauration système peuvent être créés avant application. Il vous " +
"appartient de les créer et de les conserver.\r\n" +
"\r\n" +
"3. AUCUNE GARANTIE. Le logiciel est fourni « en l'état », sans garantie d'aucune sorte. " +
"Vous l'utilisez à vos seuls risques. L'éditeur ne peut être tenu responsable d'une perte " +
"de données, d'une instabilité, d'une baisse de performances, d'une perte de garantie " +
"matérielle ou de tout dommage direct ou indirect.\r\n" +
"\r\n" +
"4. MARQUES. Le logiciel n'est ni affilié ni approuvé par Microsoft, NVIDIA, AMD ou Intel. " +
"Toutes les marques citées appartiennent à leurs propriétaires respectifs.\r\n" +
"\r\n" +
"5. USAGE. Vous êtes responsable de la conformité de l'usage sur les machines que vous " +
"administrez. N'appliquez ces réglages que sur du matériel dont vous êtes propriétaire ou " +
"pour lequel vous disposez d'une autorisation.\r\n" +
"\r\n" +
"En cliquant « J'accepte », vous reconnaissez avoir lu, compris et accepté ces conditions.";

        public LicenseForm()
        {
            Text = "DesTinGOOD — Conditions d'utilisation";
            ClientSize = new Size(620, 480);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = Color.FromArgb(245, 246, 248);
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 54, BackColor = Header };
            banner.Controls.Add(new Label
            {
                Text = "  Avant de commencer", Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 14f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            var txt = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill, BackColor = Color.White, Text = Eula,
                Font = new Font("Segoe UI", 9f), BorderStyle = BorderStyle.None
            };
            var pad = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 10, 16, 10), BackColor = Color.FromArgb(245, 246, 248) };
            pad.Controls.Add(txt);
            Controls.Add(pad);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(16, 10, 16, 10) };
            var accept = new Button
            {
                Text = "J'accepte", Width = 160, Dock = DockStyle.Right,
                FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 150, 90), ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 10f), DialogResult = DialogResult.OK
            };
            var decline = new Button
            {
                Text = "Refuser et quitter", Width = 160, Dock = DockStyle.Right,
                FlatStyle = FlatStyle.Flat, BackColor = Color.White, DialogResult = DialogResult.Cancel
            };
            decline.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            accept.FlatAppearance.BorderSize = 0;
            var spacer = new Label { Width = 8, Dock = DockStyle.Right };
            bottom.Controls.Add(accept);
            bottom.Controls.Add(spacer);
            bottom.Controls.Add(decline);
            Controls.Add(bottom);

            AcceptButton = accept;
            CancelButton = decline;

            // Ordre d'empilement : banner (haut), bottom (bas), pad (remplit).
            Controls.SetChildIndex(banner, 0);
            Theme.Apply(this);
        }

        /// <summary>Retourne true si les conditions sont acceptées (affiche le dialogue si nécessaire).</summary>
        public static bool EnsureAccepted()
        {
            if (Sys.EulaAcceptedVersion() >= Version) return true;
            using (var f = new LicenseForm())
            {
                if (f.ShowDialog() == DialogResult.OK)
                {
                    Sys.SetEulaAccepted(Version);
                    return true;
                }
            }
            return false;
        }
    }
}
