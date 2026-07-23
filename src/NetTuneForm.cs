using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// ⚙️ Réglages TCP/IP avancés : affiche l'état réel de la pile TCP et applique/rétablit les
    /// bons réglages pour le jeu ET les téléchargements. Point clé : le « réglage automatique de
    /// la fenêtre de réception » (autotuning) désactivé par de mauvais guides BRIDE les
    /// téléchargements et fait caler des connexions (Steam qui charge à l'infini). On le remet à
    /// « normal ». Commandes netsh — indépendantes de la langue. Réversible.
    /// </summary>
    internal class NetTuneForm : Form
    {
        private readonly Action<string, int> _log;
        private TextBox _state;
        private Button _btnApply, _btnRevert, _btnRefresh, _btnClose;

        private static readonly Color Accent = Color.FromArgb(0, 150, 90);

        // Réglages recommandés (jeu + téléchargements). Valeurs = tokens netsh, non traduits.
        private static readonly string[][] Recommended =
        {
            new[] { "autotuninglevel", "normal",   "réglage auto de la fenêtre TCP (débloque les téléchargements)" },
            new[] { "rss",            "enabled",  "mise à l'échelle côté réception (réseau sur plusieurs cœurs)" },
            new[] { "ecncapability",  "disabled", "ECN désactivé (routeurs qui le gèrent mal)" },
            new[] { "timestamps",     "disabled", "horodatages RFC 1323 désactivés (léger)" },
            new[] { "rsc",            "disabled", "coalescence RSC désactivée (latence plus basse)" },
        };

        // Valeurs par défaut de Windows (pour « Rétablir »).
        private static readonly string[][] Defaults =
        {
            new[] { "autotuninglevel", "normal" },
            new[] { "rss",            "enabled" },
            new[] { "ecncapability",  "disabled" },
            new[] { "timestamps",     "disabled" },
            new[] { "rsc",            "enabled" },
        };

        public NetTuneForm(Action<string, int> log)
        {
            _log = log;
            Build();
            Refresh2();
            Theme.Apply(this);
        }

        private void Build()
        {
            Text = "Fluide — Réglages TCP/IP";
            ClientSize = new Size(660, 470);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = Color.FromArgb(245, 246, 248);
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  ⚙️ Réglages TCP/IP — jeu fluide + téléchargements rapides",
                Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 12f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            var intro = new Label
            {
                Text = "État actuel de ta pile TCP ci-dessous. « Appliquer » corrige les réglages jeu + téléchargements — "
                     + "notamment le RÉGLAGE AUTO de la fenêtre TCP remis à « normal » : s'il avait été désactivé, "
                     + "tes téléchargements (Steam, MAJ…) étaient bridés et des connexions calaient.",
                Location = new Point(18, 58), Size = new Size(624, 48), ForeColor = Color.FromArgb(60, 64, 72)
            };
            Controls.Add(intro);

            _state = new TextBox
            {
                Location = new Point(18, 112), Size = new Size(624, 262), Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9f), BackColor = Color.White
            };
            Controls.Add(_state);

            _btnApply = MakeBtn("⚡ Appliquer (jeu + téléchargements)", 18, 386, 280, 38, true);
            _btnApply.Click += (s, e) => Apply(Recommended, true);
            _btnRevert = MakeBtn("Rétablir les valeurs Windows", 308, 386, 200, 38, false);
            _btnRevert.Click += (s, e) => Apply(Defaults, false);
            _btnRefresh = MakeBtn("Rafraîchir", 18, 430, 120, 30, false);
            _btnRefresh.Click += (s, e) => Refresh2();
            _btnClose = MakeBtn("Fermer", 552, 430, 90, 30, false);
            _btnClose.Click += (s, e) => Close();
            Controls.Add(_btnApply); Controls.Add(_btnRevert); Controls.Add(_btnRefresh); Controls.Add(_btnClose);
        }

        private static Button MakeBtn(string text, int x, int y, int w, int h, bool primary)
        {
            var b = new Button
            {
                Text = text, Location = new Point(x, y), Size = new Size(w, h), FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Color.FromArgb(40, 44, 52),
                Font = primary ? new Font("Segoe UI Semibold", 9.5f) : new Font("Segoe UI", 9f)
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            return b;
        }

        private void SetBusy(bool busy)
        {
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            _btnApply.Enabled = !busy; _btnRevert.Enabled = !busy; _btnRefresh.Enabled = !busy;
        }

        private void Refresh2()
        {
            SetBusy(true);
            _state.Text = "Lecture des réglages TCP...";
            Task.Run(() =>
            {
                NativeResult r = Sys.Run(Sys.Sys32("netsh.exe"), "int tcp show global");
                string text = (r.Output ?? "").Trim();
                try { BeginInvoke((Action)(() => { _state.Text = text.Length > 0 ? text : "Lecture impossible."; SetBusy(false); })); } catch { }
            });
        }

        private void Apply(string[][] settings, bool recommended)
        {
            if (recommended && MessageBox.Show(this,
                    "Appliquer les réglages TCP/IP recommandés (jeu + téléchargements) ?\n\n"
                    + "• Réglage auto de la fenêtre TCP → normal (débloque les téléchargements)\n"
                    + "• RSS activé, ECN/horodatages/RSC ajustés pour la latence\n\n"
                    + "Réversible via « Rétablir les valeurs Windows ».",
                    "Réglages TCP/IP", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            SetBusy(true);
            Task.Run(() =>
            {
                int ok = 0;
                foreach (string[] s in settings)
                {
                    NativeResult r = Sys.Run(Sys.Sys32("netsh.exe"), "int tcp set global " + s[0] + "=" + s[1]);
                    bool good = r.ExitCode == 0;
                    if (good) ok++;
                    if (_log != null) _log("TCP " + s[0] + "=" + s[1] + (good ? " ✔" : " (code " + r.ExitCode + ")"),
                        good ? 1 : 2);
                }
                if (_log != null) _log("Réglages TCP/IP : " + ok + "/" + settings.Length
                    + (recommended ? " appliqué(s) (jeu + téléchargements)." : " rétabli(s) (défaut Windows)."), 1);
                try { BeginInvoke((Action)Refresh2); } catch { }
            });
        }
    }
}
