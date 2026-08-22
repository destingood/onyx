using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Réglages du Mode Jeu (game_mode config / exclusions du concurrent) : choisir quels services
    /// de fond le Mode Jeu suspend pendant une partie. Décocher un service = le laisser tourner.
    /// </summary>
    internal class GameModeForm : Form
    {
        private readonly Action<string, int> _log;
        private static readonly Color Accent = Theme.AccentColor;
        private CheckedListBox _list;
        private readonly string[] _svcs;

        public GameModeForm(Action<string, int> log)
        {
            _log = log;
            _svcs = new List<string>(GameBoost.SuspendableServices).ToArray();
            Build();
            Theme.Apply(this);
            Populate();
        }

        private void Build()
        {
            Text = "ONYX — Réglages Mode Jeu";
            ClientSize = new Size(560, 430);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(12, 14, 13) };
            banner.Controls.Add(new Label
            {
                Text = "  Mode Jeu — que suspendre pendant une partie ?",
                Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 12.5f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            Controls.Add(new Label
            {
                Text = "Le Mode Jeu (Ctrl+Alt+G) libère la RAM, force le timer 1 ms, suspend TEMPORAIREMENT ces "
                     + "services de fond (relancés à la désactivation) et ferme les familles d'applications cochées "
                     + "(jamais de force : une fenêtre qui demande à enregistrer reste ouverte). Décoche ce que tu "
                     + "veux GARDER. Ton choix est mémorisé.",
                Location = new Point(18, 60), Size = new Size(524, 58), ForeColor = Color.FromArgb(60, 64, 72)
            });

            _list = new CheckedListBox
            {
                Location = new Point(18, 124), Size = new Size(524, 214),
                CheckOnClick = true, BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false
            };
            Controls.Add(_list);

            var save = MakeBtn("Enregistrer", 18, 350, 160, 38, true);
            save.Click += (s, e) => Save();
            var close = MakeBtn("Fermer", 452, 350, 90, 38, false);
            close.Click += (s, e) => Close();
            Controls.Add(save); Controls.Add(close);
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

        private void Populate()
        {
            var excl = GameBoost.LoadExclusions();
            var exclApps = ApplisDeFond.ExclusionsChargees();
            _list.Items.Clear();
            foreach (string svc in _svcs)
                _list.Items.Add(GameBoost.FriendlyName(svc), !excl.Contains(svc));   // coché = suspendu
            // Les familles d'applications, dans la même liste : un seul écran, une seule règle de
            // lecture (coché = ONYX s'en occupe).
            foreach (ApplisDeFond.Categorie c in ApplisDeFond.Catalogue)
                _list.Items.Add("Appli : " + c.Libelle, !exclApps.Contains(c.Cle));
        }

        private void Save()
        {
            var excluded = new List<string>();
            for (int i = 0; i < _svcs.Length; i++)
                if (!_list.GetItemChecked(i)) excluded.Add(_svcs[i]);   // décoché = exclu (gardé)
            GameBoost.SaveExclusions(excluded);

            var exclApps = new List<string>();
            for (int i = 0; i < ApplisDeFond.Catalogue.Length; i++)
                if (!_list.GetItemChecked(_svcs.Length + i)) exclApps.Add(ApplisDeFond.Catalogue[i].Cle);
            ApplisDeFond.EnregistrerExclusions(exclApps);

            if (_log != null) _log("Mode Jeu : " + excluded.Count + " service(s) et " + exclApps.Count
                                 + " famille(s) d'applications exclu(s) de la suspension.", 0);
            MessageBox.Show(this, "Réglages enregistrés.\n\n"
                + (_svcs.Length - excluded.Count) + " service(s) seront suspendus, " + excluded.Count + " gardé(s).\n"
                + (ApplisDeFond.Catalogue.Length - exclApps.Count) + " famille(s) d'applications seront fermées.",
                "Mode Jeu", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
