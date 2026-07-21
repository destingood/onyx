using System;
using System.Drawing;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>Panneau de contrôle de l'overlay de stats in-game (activation + coin d'écran).</summary>
    internal class StatsOverlayForm : Form
    {
        private readonly Action<string, int> _log;
        private StatsOverlaySettings _s;
        private CheckBox _chk;
        private ComboBox _corner;
        private static readonly Color Accent = Color.FromArgb(0, 150, 90);

        public StatsOverlayForm(Action<string, int> log)
        {
            _log = log;
            _s = StatsOverlaySettings.Load();
            Build();
            Theme.Apply(this);
        }

        private void Build()
        {
            Text = "DesTinGOOD — Overlay de stats";
            ClientSize = new Size(480, 300);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(12, 14, 13) };
            banner.Controls.Add(new Label
            {
                Text = "  Overlay de stats — CPU/GPU/temps par-dessus le jeu",
                Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 12.5f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            var intro = new Label
            {
                Text = "Affiche les FPS du jeu + CPU / GPU / RAM / températures en direct, en surimpression "
                     + "(fenêtré ou sans bordure). « Click-through » : il n'intercepte jamais tes clics.",
                Location = new Point(18, 62), Size = new Size(444, 46), ForeColor = Color.FromArgb(60, 64, 72)
            };
            Controls.Add(intro);

            _chk = new CheckBox
            {
                Text = "Afficher l'overlay en jeu", Location = new Point(18, 118), Size = new Size(300, 26),
                Checked = _s.Enabled, Font = new Font("Segoe UI Semibold", 10f)
            };
            _chk.CheckedChanged += (s, e) =>
            {
                _s.Enabled = _chk.Checked; _s.Save();
                if (_chk.Checked) StatsOverlayManager.Show(_s); else StatsOverlayManager.Hide();
                if (_log != null) _log("Overlay de stats : " + (_chk.Checked ? "activé" : "désactivé") + ".", 0);
            };
            Controls.Add(_chk);

            var lc = new Label { Text = "Coin de l'écran :", Location = new Point(18, 158), Size = new Size(110, 24) };
            Controls.Add(lc);
            _corner = new ComboBox { Location = new Point(130, 154), Size = new Size(200, 26), DropDownStyle = ComboBoxStyle.DropDownList };
            _corner.Items.AddRange(new object[] { "Haut gauche", "Haut droit", "Bas gauche", "Bas droit" });
            _corner.SelectedIndex = Math.Max(0, Math.Min(3, _s.Corner));
            _corner.SelectedIndexChanged += (s, e) =>
            {
                _s.Corner = _corner.SelectedIndex; _s.Save();
                if (StatsOverlayManager.IsVisible) StatsOverlayManager.Show(_s);
            };
            Controls.Add(_corner);

            var note = new Label
            {
                Text = "Astuce : garde l'overlay activé, il réapparaît au prochain démarrage. En plein écran EXCLUSIF, "
                     + "Windows masque tout overlay — passe le jeu en « plein écran fenêtré » pour le voir.",
                Location = new Point(18, 196), Size = new Size(444, 50), ForeColor = Color.FromArgb(110, 115, 125)
            };
            Controls.Add(note);

            var close = new Button
            {
                Text = "Fermer", Location = new Point(372, 252), Size = new Size(90, 34), FlatStyle = FlatStyle.Flat,
                BackColor = Color.White, ForeColor = Color.FromArgb(40, 44, 52)
            };
            close.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            close.Click += (s, e) => Close();
            Controls.Add(close);
        }
    }
}
