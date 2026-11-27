using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>Notifications par catégorie (⋯ → Système → Notifications…) : une case par catégorie.</summary>
    internal sealed class NotificationsForm : Form
    {
        private readonly Dictionary<string, CheckBox> _cases = new Dictionary<string, CheckBox>();
        private readonly Label _etat;

        public NotificationsForm()
        {
            Text = "ONYX — Notifications";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false;
            Font = FpsUi.Body;

            Controls.Add(new Label
            {
                Text = "Choisis ce qu'ONYX a le droit de t'annoncer par une notification Windows. Une catégorie coupée ne sonne plus ; "
                     + "ce qu'elle signale reste visible dans ONYX.",
                Location = new Point(16, 14), Size = new Size(568, 40)
            });
            int y = 66;
            foreach (string id in NotifCategories.Toutes)
            {
                var c = new CheckBox { Text = NotifCategories.Libelle(id), Checked = NotifReglages.Active(id), Location = new Point(16, y), Size = new Size(568, 24) };
                c.AccessibleName = NotifCategories.Libelle(id);
                _cases[id] = c;
                Controls.Add(c);
                Controls.Add(new Label { Text = NotifCategories.Detail(id), Location = new Point(36, y + 25), Size = new Size(548, 20), ForeColor = FpsUi.Dim, Font = FpsUi.Small });
                y += 58;
            }

            _etat = new Label { Location = new Point(16, y + 4), Size = new Size(568, 22), ForeColor = FpsUi.Dim };
            Controls.Add(_etat);
            int bas = y + 36;
            ClientSize = new Size(600, bas + 48);

            var ok = OnyxDesign.AccentButton("Enregistrer");
            ok.SetBounds(ClientSize.Width - 16 - 110 - 10 - 130, bas, 130, 34);
            ok.Click += (s, e) => Enregistre();
            var ca = OnyxDesign.Button("Annuler");
            ca.SetBounds(ClientSize.Width - 16 - 110, bas, 110, 34);
            ca.DialogResult = DialogResult.Cancel;
            Controls.Add(ok); Controls.Add(ca);
            CancelButton = ca;
            try { Theme.Apply(this); } catch { }
        }

        private void Enregistre()
        {
            var etats = new Dictionary<string, bool>();
            foreach (var kv in _cases) etats[kv.Key] = kv.Value.Checked;
            if (NotifReglages.Enregistre(etats)) { DialogResult = DialogResult.OK; Close(); }
            else { _etat.ForeColor = FpsUi.Warn; _etat.Text = "Réglage non enregistré : le dossier de données d'ONYX est inaccessible."; }
        }
    }
}
