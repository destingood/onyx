using System;
using System.Drawing;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>Panneau DNS : choix d'un résolveur rapide appliqué à toutes les cartes réseau actives.</summary>
    internal class DnsForm : Form
    {
        private readonly Action<string, int> _log;
        private ComboBox _combo;
        private Label _current;

        private static readonly Color Bg     = Color.FromArgb(245, 246, 248);
        private static readonly Color Header = Color.FromArgb(28, 30, 38);
        private static readonly Color Accent = Color.FromArgb(0, 150, 90);

        private class Provider
        {
            public string Name; public string[] Servers;
            public Provider(string n, string[] s) { Name = n; Servers = s; }
            public override string ToString() { return Name; }
        }

        private static readonly Provider[] Providers = new[]
        {
            new Provider("Automatique (DHCP / box) — par défaut", null),
            new Provider("Cloudflare — 1.1.1.1 / 1.0.0.1 (le plus rapide)", new[] { "1.1.1.1", "1.0.0.1" }),
            new Provider("Cloudflare anti-malware — 1.1.1.2 / 1.0.0.2", new[] { "1.1.1.2", "1.0.0.2" }),
            new Provider("Google — 8.8.8.8 / 8.8.4.4", new[] { "8.8.8.8", "8.8.4.4" }),
            new Provider("Quad9 (sécurisé) — 9.9.9.9 / 149.112.112.112", new[] { "9.9.9.9", "149.112.112.112" }),
            new Provider("AdGuard (anti-pub) — 94.140.14.14 / 94.140.15.15", new[] { "94.140.14.14", "94.140.15.15" }),
        };

        public DnsForm(Action<string, int> log)
        {
            _log = log;
            Build();
            RefreshCurrent();
        }

        private void Build()
        {
            Text = "BT Optimizer — DNS";
            ClientSize = new Size(560, 340);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = Bg;
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = Header };
            banner.Controls.Add(new Label
            {
                Text = "  DNS — résolution plus rapide", Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 13f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            var lblCur = new Label { Text = "DNS actuel :", Location = new Point(18, 66), AutoSize = true, Font = new Font("Segoe UI Semibold", 9f) };
            _current = new Label { Location = new Point(18, 88), Size = new Size(524, 88), ForeColor = Color.FromArgb(60, 64, 72) };
            Controls.Add(lblCur); Controls.Add(_current);

            var lblSel = new Label { Text = "Choisir un résolveur :", Location = new Point(18, 186), AutoSize = true, Font = new Font("Segoe UI Semibold", 9f) };
            _combo = new ComboBox { Location = new Point(18, 208), Size = new Size(524, 26), DropDownStyle = ComboBoxStyle.DropDownList };
            _combo.Items.AddRange(Providers);
            _combo.SelectedIndex = 1; // Cloudflare par défaut
            Controls.Add(lblSel); Controls.Add(_combo);

            var info = new Label
            {
                Text = "Appliqué à toutes les cartes réseau actives (IPv4). Le cache DNS est vidé automatiquement.",
                Location = new Point(18, 240), Size = new Size(524, 34), ForeColor = Color.Gray, Font = new Font("Segoe UI", 8.5f)
            };
            Controls.Add(info);

            var apply = MakeBtn("APPLIQUER", 18, 288, 200, 38, true);
            apply.Click += OnApply;
            var flush = MakeBtn("Vider le cache DNS", 228, 288, 170, 38, false);
            flush.Click += (s, e) => { Sys.FlushDns(); if (_log != null) _log("Cache DNS vidé.", 1); };
            var close = MakeBtn("Fermer", 452, 288, 90, 38, false);
            close.Click += (s, e) => Close();
            Controls.Add(apply); Controls.Add(flush); Controls.Add(close);
        }

        private static Button MakeBtn(string text, int x, int y, int w, int h, bool primary)
        {
            var b = new Button
            {
                Text = text, Location = new Point(x, y), Size = new Size(w, h),
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Accent : Color.White,
                ForeColor = primary ? Color.White : Color.FromArgb(40, 44, 52),
                Font = primary ? new Font("Segoe UI Semibold", 10f) : new Font("Segoe UI", 9f)
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            return b;
        }

        private void RefreshCurrent()
        {
            _current.Text = Sys.CurrentDnsSummary();
        }

        private void OnApply(object sender, EventArgs e)
        {
            var p = _combo.SelectedItem as Provider;
            if (p == null) return;
            string msg = (p.Servers == null)
                ? "Remettre le DNS en automatique (DHCP) sur toutes les cartes ?"
                : "Appliquer le DNS " + string.Join(" / ", p.Servers) + " sur toutes les cartes réseau actives ?";
            if (MessageBox.Show(this, msg, "DNS", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;
            Cursor = Cursors.WaitCursor;
            try { Sys.SetDns(p.Servers, _log); }
            catch (Exception ex) { if (_log != null) _log("DNS : " + ex.Message, 3); }
            finally { Cursor = Cursors.Default; }
            RefreshCurrent();
            MessageBox.Show(this, "DNS mis à jour.", "BT Optimizer", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
