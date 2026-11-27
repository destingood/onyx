using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Réglages du compteur en jeu : activation, mesures affichées, position, taille, couleur.
    /// Chaque changement est enregistré et appliqué tout de suite au compteur s'il est affiché.
    /// </summary>
    internal class StatsOverlayForm : Form
    {
        private readonly Action<string, int> _log;
        private StatsOverlaySettings _s;
        private CheckBox _chk;
        private ComboBox _corner, _taille, _couleur, _style;
        private readonly Dictionary<string, CheckBox> _lignes = new Dictionary<string, CheckBox>();
        private bool _pret;

        public StatsOverlayForm(Action<string, int> log)
        {
            _log = log;
            _s = StatsOverlaySettings.Load();
            Build();
            Theme.Apply(this);
            _pret = true;
        }

        /// <summary>Enregistre, puis redessine le compteur s'il est à l'écran.</summary>
        private void Applique()
        {
            if (!_pret) return;
            _s.Corner = Math.Max(0, _corner.SelectedIndex);
            _s.O.Taille = Math.Max(0, _taille.SelectedIndex);
            _s.O.Couleur = Math.Max(0, _couleur.SelectedIndex);
            _s.O.Style = Math.Max(0, _style.SelectedIndex);
            _s.O.Lignes.Clear();
            foreach (var kv in _lignes) if (kv.Value.Checked) _s.O.Lignes.Add(kv.Key);
            _s.Save();
            if (StatsOverlayManager.IsVisible) StatsOverlayManager.Show(_s);
        }

        private ComboBox Liste(string titre, int y, string[] choix, int choisi)
        {
            Controls.Add(new Label { Text = titre, Location = new Point(18, y + 4), Size = new Size(110, 24) });
            var c = new ComboBox { Location = new Point(130, y), Size = new Size(330, 26), DropDownStyle = ComboBoxStyle.DropDownList };
            c.Items.AddRange(choix);
            c.SelectedIndex = Math.Max(0, Math.Min(choix.Length - 1, choisi));
            c.SelectedIndexChanged += (s, e) => Applique();
            Controls.Add(c);
            return c;
        }

        private void Build()
        {
            Text = "ONYX — Compteur en jeu";
            ClientSize = new Size(560, 640);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(12, 14, 13) };
            banner.Controls.Add(new Label
            {
                Text = "  Compteur en jeu — FPS et mesures par-dessus le jeu",
                Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 12.5f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            Controls.Add(new Label
            {
                Text = "Affiche les FPS du jeu et les mesures de ton choix, en surimpression (fenêtré ou sans "
                     + "bordure). Il n'intercepte jamais tes clics.",
                Location = new Point(18, 62), Size = new Size(524, 40), ForeColor = Color.FromArgb(60, 64, 72)
            });

            _chk = new CheckBox
            {
                Text = "Afficher le compteur en jeu", Location = new Point(18, 108), Size = new Size(300, 26),
                Checked = _s.Enabled, Font = new Font("Segoe UI Semibold", 10f)
            };
            _chk.CheckedChanged += (s, e) =>
            {
                _s.Enabled = _chk.Checked; _s.Save();
                if (_chk.Checked) StatsOverlayManager.Show(_s); else StatsOverlayManager.Hide();
                if (_log != null) _log("Compteur en jeu : " + (_chk.Checked ? "activé" : "désactivé") + ".", 0);
            };
            Controls.Add(_chk);

            _corner = Liste("Position :", 144, CompteurJeu.Positions, _s.Corner);
            _taille = Liste("Taille :", 176, CompteurJeu.Tailles, _s.O.Taille);
            _couleur = Liste("Couleur :", 208, CompteurJeu.Couleurs, _s.O.Couleur);
            _style = Liste("Style :", 240, CompteurJeu.Styles, _s.O.Style);

            Controls.Add(new Label
            {
                Text = "Mesures affichées avec les FPS", Location = new Point(18, 280), Size = new Size(400, 22),
                Font = new Font("Segoe UI Semibold", 9.5f)
            });
            // Deux colonnes, dans l'ordre d'affichage du compteur.
            int moitie = (CompteurJeu.Toutes.Length + 1) / 2;
            for (int i = 0; i < CompteurJeu.Toutes.Length; i++)
            {
                string id = CompteurJeu.Toutes[i];
                var c = new CheckBox
                {
                    Text = CompteurJeu.Libelle(id), Checked = _s.O.A(id),
                    Location = new Point(i < moitie ? 18 : 286, 306 + (i % moitie) * 26), Size = new Size(262, 24)
                };
                c.CheckedChanged += (s, e) => Applique();
                _lignes[id] = c;
                Controls.Add(c);
            }

            Controls.Add(new Label
            {
                Text = "Le compteur réapparaît au prochain démarrage s'il est activé. En plein écran EXCLUSIF, Windows "
                     + "masque toute surimpression : passe le jeu en « plein écran fenêtré » pour le voir. Les "
                     + "ventilateurs ne sont pas proposés : les lire demande un pilote noyau, qu'ONYX n'installe pas.",
                Location = new Point(18, 306 + moitie * 26 + 10), Size = new Size(524, 62), ForeColor = Color.FromArgb(110, 115, 125)
            });
            int bas = 306 + moitie * 26 + 80;

            // Aperçu : valeurs de démonstration pendant 5 secondes, pour juger la place et la taille.
            var apercu = new Button
            {
                Text = "Aperçu (5 s)", Location = new Point(18, bas), Size = new Size(130, 34), FlatStyle = FlatStyle.Flat,
                BackColor = Color.White, ForeColor = Color.FromArgb(40, 44, 52)
            };
            apercu.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            apercu.Click += (s, e) => { Applique(); StatsOverlayManager.Apercu(_s); };
            Controls.Add(apercu);

            var close = new Button
            {
                Text = "Fermer", Location = new Point(452, bas), Size = new Size(90, 34), FlatStyle = FlatStyle.Flat,
                BackColor = Color.White, ForeColor = Color.FromArgb(40, 44, 52)
            };
            close.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            close.Click += (s, e) => Close();
            Controls.Add(close);
            ClientSize = new Size(560, bas + 48);
        }
    }
}
