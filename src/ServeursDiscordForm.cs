using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Mes serveurs Discord (⋯ → Système → Démarrage &amp; fond) : liens d'invitation collés par
    /// l'utilisateur, ouverts dans le navigateur ou l'application Discord. Aucun compte.
    /// </summary>
    internal sealed class ServeursDiscordForm : Form
    {
        private readonly List<ServeursDiscord.Serveur> _liste;
        private readonly ListView _vue;
        private readonly TextBox _nom, _lien;
        private readonly Button _ouvre, _retire;
        private readonly Label _etat;

        public ServeursDiscordForm()
        {
            Text = "ONYX — Mes serveurs Discord";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false;
            Font = FpsUi.Body;
            ClientSize = new Size(640, 470);
            _liste = ServeursDiscordReglages.Lire();

            Controls.Add(new Label
            {
                Text = "Garde ici les serveurs Discord de tes jeux et de tes amis. Colle un lien d'invitation : ONYX l'ouvre d'un double-clic. "
                     + "Aucun compte, rien n'est envoyé.",
                Location = new Point(16, 14), Size = new Size(608, 40)
            });

            _vue = new ListView
            {
                View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable, Location = new Point(16, 60), Size = new Size(608, 230)
            };
            _vue.Columns.Add("Serveur", 330);
            _vue.Columns.Add("Lien", 250);
            _vue.AccessibleName = "Serveurs Discord enregistrés";
            _vue.SelectedIndexChanged += (s, e) => Boutons();
            _vue.DoubleClick += (s, e) => Ouvre();
            _vue.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) Ouvre(); else if (e.KeyCode == Keys.Delete) Retire(); };
            Controls.Add(_vue);

            _ouvre = OnyxDesign.AccentButton("Ouvrir");
            _ouvre.SetBounds(16, 298, 120, 34);
            _ouvre.Click += (s, e) => Ouvre();
            _retire = OnyxDesign.Button("Retirer");
            _retire.SetBounds(146, 298, 120, 34);
            _retire.Click += (s, e) => Retire();
            Controls.Add(_ouvre); Controls.Add(_retire);

            Controls.Add(new Label { Text = "AJOUTER UN SERVEUR", Location = new Point(16, 348), Size = new Size(300, 18), ForeColor = FpsUi.Dim, Font = FpsUi.Small });
            _nom = new TextBox { Location = new Point(16, 370), Size = new Size(200, 28), PlaceholderText = "Nom (facultatif)" };
            _nom.AccessibleName = "Nom du serveur";
            _lien = new TextBox { Location = new Point(224, 370), Size = new Size(290, 28), PlaceholderText = "discord.gg/…" };
            _lien.AccessibleName = "Lien d'invitation Discord";
            var ajoute = OnyxDesign.Button("Ajouter");
            ajoute.SetBounds(522, 367, 102, 34);
            ajoute.Click += (s, e) => Ajoute();
            _lien.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Ajoute(); } };
            Controls.Add(_nom); Controls.Add(_lien); Controls.Add(ajoute);

            _etat = new Label { Location = new Point(16, 406), Size = new Size(490, 44), ForeColor = FpsUi.Dim };
            Controls.Add(_etat);
            var ferme = OnyxDesign.Button("Fermer");
            ferme.SetBounds(524, 420, 100, 34);
            ferme.DialogResult = DialogResult.Cancel;
            Controls.Add(ferme);
            CancelButton = ferme;

            Remplit();
            try { Theme.Apply(this); } catch { }
        }

        private void Remplit(int choisi = -1)
        {
            _vue.BeginUpdate();
            _vue.Items.Clear();
            foreach (ServeursDiscord.Serveur s in _liste)
            {
                var it = new ListViewItem(s.Nom);
                it.SubItems.Add("discord.gg/" + s.Code);
                _vue.Items.Add(it);
            }
            _vue.EndUpdate();
            if (choisi >= 0 && choisi < _vue.Items.Count) { _vue.Items[choisi].Selected = true; _vue.Items[choisi].EnsureVisible(); }
            if (_liste.Count == 0) Etat("Aucun serveur pour l'instant. Sur Discord : clic droit sur le serveur → Inviter des gens → Copier le lien.", false);
            Boutons();
        }

        private void Boutons() { _ouvre.Enabled = _retire.Enabled = _vue.SelectedIndices.Count > 0; }

        private int Choisi() { return _vue.SelectedIndices.Count > 0 ? _vue.SelectedIndices[0] : -1; }

        private void Etat(string texte, bool alerte)
        {
            _etat.ForeColor = alerte ? FpsUi.Warn : FpsUi.Dim;
            _etat.Text = texte;
        }

        /// <summary>Ajout depuis les champs ; aussi appelé par le test d'interface.</summary>
        internal bool Ajoute(string nom, string lien)
        {
            _nom.Text = nom; _lien.Text = lien;
            return Ajoute();
        }

        private bool Ajoute()
        {
            string erreur;
            if (!ServeursDiscord.Ajoute(_liste, _nom.Text, _lien.Text, out erreur)) { Etat(erreur, true); return false; }
            if (!ServeursDiscordReglages.Enregistre(_liste))
            {
                _liste.RemoveAt(_liste.Count - 1);
                Etat("Serveur non enregistré : le dossier de données d'ONYX est inaccessible.", true);
                return false;
            }
            _nom.Text = _lien.Text = "";
            Remplit(_liste.Count - 1);
            Etat("« " + _liste[_liste.Count - 1].Nom + " » ajouté.", false);
            return true;
        }

        private void Ouvre()
        {
            int i = Choisi();
            if (i < 0) return;
            // Lien reconstruit à partir du code vérifié : rien d'autre ne peut être lancé.
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_liste[i].Lien) { UseShellExecute = true }); }
            catch (Exception ex) { Etat("Ouverture impossible : " + ex.Message, true); }
        }

        private void Retire()
        {
            int i = Choisi();
            if (i < 0) return;
            ServeursDiscord.Serveur s = _liste[i];
            if (MessageBox.Show(this, "Retirer « " + s.Nom + " » de la liste ?\nTu resteras membre du serveur sur Discord.", "Mes serveurs Discord",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _liste.RemoveAt(i);
            if (!ServeursDiscordReglages.Enregistre(_liste))
            {
                _liste.Insert(i, s);
                Etat("Liste non enregistrée : le dossier de données d'ONYX est inaccessible.", true);
                return;
            }
            Remplit(Math.Min(i, _liste.Count - 1));
            Etat("« " + s.Nom + " » retiré de la liste.", false);
        }
    }
}
