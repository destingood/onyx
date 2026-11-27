using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Appréciation sur 5, note personnelle et liens web d'un jeu (bouton « Notes et liens… » de la fiche). Tout reste
    /// sur ce PC ; un lien ne s'ouvre que sur un clic. <see cref="Change"/> dit à la fiche de
    /// mettre son bouton à jour.
    /// </summary>
    internal sealed class NotesJeuForm : Form
    {
        private readonly GameScan.GameInfo _g;
        private readonly TextBox _note;
        private readonly ListBox _liens;
        private readonly ComboBox _etoiles;
        private readonly Button _ajouter, _retirer, _ouvrir;
        private readonly Label _etat, _compte;
        private readonly List<NotesJeu.Lien> _liste;

        public bool Change { get; private set; }

        public NotesJeuForm(GameScan.GameInfo g)
        {
            _g = g;
            Text = "ONYX — Notes et liens de " + OptionsJeu.Nom(g.Name);
            ClientSize = new Size(600, 568);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false;
            Font = FpsUi.Body;

            NotesJeu.Fiche f = NotesJeu.De(g.Name);
            _liste = f.Liens;

            Controls.Add(new Label { Text = "Mon appréciation :", Location = new Point(16, 17), Size = new Size(130, 18) });
            _etoiles = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(150, 13), Size = new Size(150, 24) };
            _etoiles.AccessibleName = "Mon appréciation du jeu, sur 5";
            _etoiles.Items.Add("Pas noté");
            for (int n = 1; n <= NotesJeu.MaxEtoiles; n++) _etoiles.Items.Add(new string('★', n) + new string('☆', NotesJeu.MaxEtoiles - n) + "   " + NotesJeu.TexteEtoiles(n));
            _etoiles.SelectedIndex = NotesJeu.EtoilesPropres(f.Etoiles);
            Controls.Add(_etoiles);

            Controls.Add(new Label { Text = "Note personnelle (réglages à retenir, objectif, où tu en es…). Elle reste sur ce PC.", Location = new Point(16, 50), Size = new Size(568, 18) });
            _note = new TextBox
            {
                Text = f.Note.Replace("\n", "\r\n"), Location = new Point(16, 72), Size = new Size(568, 170),
                Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, MaxLength = NotesJeu.MaxNote
            };
            _note.AccessibleName = "Note personnelle";
            _note.TextChanged += (s, e) => MajCompte();
            Controls.Add(_note);
            _compte = new Label { Location = new Point(384, 244), Size = new Size(200, 16), ForeColor = FpsUi.Dim, Font = FpsUi.Small, TextAlign = ContentAlignment.TopRight };
            Controls.Add(_compte);

            Controls.Add(new Label { Text = "Liens (wiki, carte interactive, guide, serveur de la communauté…) : http ou https seulement.", Location = new Point(16, 268), Size = new Size(568, 18) });
            _liens = new ListBox { Location = new Point(16, 290), Size = new Size(568, 150), IntegralHeight = false, HorizontalScrollbar = true };
            _liens.AccessibleName = "Liens du jeu";
            _liens.SelectedIndexChanged += (s, e) => MajBoutons();
            _liens.DoubleClick += (s, e) => Ouvrir();
            Controls.Add(_liens);

            _ajouter = OnyxDesign.Button("Ajouter un lien…");
            _ajouter.SetBounds(16, 448, 160, 32);
            _ajouter.Click += (s, e) => Ajouter();
            _ouvrir = OnyxDesign.Button("Ouvrir");
            _ouvrir.SetBounds(184, 448, 100, 32);
            _ouvrir.Click += (s, e) => Ouvrir();
            _retirer = OnyxDesign.Button("Retirer");
            _retirer.SetBounds(292, 448, 100, 32);
            _retirer.Click += (s, e) => Retirer();
            Controls.Add(_ajouter); Controls.Add(_ouvrir); Controls.Add(_retirer);

            _etat = new Label { Location = new Point(16, 488), Size = new Size(568, 20), ForeColor = FpsUi.Dim, AutoEllipsis = true };
            Controls.Add(_etat);

            var ok = OnyxDesign.AccentButton("Enregistrer");
            ok.SetBounds(ClientSize.Width - 16 - 130 - 10 - 110, ClientSize.Height - 48, 130, 34);
            ok.Click += (s, e) => Enregistrer();
            var ca = OnyxDesign.Button("Annuler");
            ca.SetBounds(ClientSize.Width - 16 - 110, ClientSize.Height - 48, 110, 34);
            ca.DialogResult = DialogResult.Cancel;
            Controls.Add(ok); Controls.Add(ca);
            CancelButton = ca;   // pas de bouton par défaut : Entrée sert à aller à la ligne dans la note
            try { Theme.Apply(this); } catch { }
            Remplit(-1);
            MajCompte();
        }

        private void MajCompte()
        {
            _compte.Text = _note.TextLength + " / " + NotesJeu.MaxNote;
        }

        private void Remplit(int choisi)
        {
            _liens.BeginUpdate();
            _liens.Items.Clear();
            foreach (NotesJeu.Lien k in _liste) _liens.Items.Add(k.Titre + "  —  " + k.Url);
            _liens.EndUpdate();
            if (choisi >= 0 && choisi < _liste.Count) _liens.SelectedIndex = choisi;
            MajBoutons();
        }

        private void MajBoutons()
        {
            bool un = _liens.SelectedIndex >= 0 && _liens.SelectedIndex < _liste.Count;
            _ouvrir.Enabled = un; _retirer.Enabled = un;
            _ajouter.Enabled = _liste.Count < NotesJeu.MaxLiens;
        }

        private void Ajouter()
        {
            string saisi = SaisieTexte.Demande(this, "ONYX — Ajouter un lien", "Adresse de la page (ex. https://fortnite.gg/map).", "",
                t =>
                {
                    string raison, u = NotesJeu.UrlPropre(t, out raison);
                    if (u == null) return raison;
                    return _liste.Exists(x => string.Equals(x.Url, u, StringComparison.OrdinalIgnoreCase)) ? "Ce lien est déjà dans la liste." : null;
                }, NotesJeu.MaxUrl);
            if (saisi == null) return;
            string erreur, url = NotesJeu.UrlPropre(saisi, out erreur);
            if (url == null) { _etat.Text = erreur; return; }
            string titre = SaisieTexte.Demande(this, "ONYX — Ajouter un lien", "Nom du lien, tel qu'il s'affichera dans la liste.", NotesJeu.TitreParDefaut(url),
                t => NotesJeu.TitrePropre(t).Length == 0 ? "Le nom est vide." : null, NotesJeu.MaxTitre);
            if (titre == null) return;
            _liste.Add(new NotesJeu.Lien { Url = url, Titre = NotesJeu.TitrePropre(titre) });
            Remplit(_liste.Count - 1);
            _etat.Text = "Lien ajouté : pense à « Enregistrer ».";
        }

        private void Retirer()
        {
            int i = _liens.SelectedIndex;
            if (i < 0 || i >= _liste.Count) return;
            _liste.RemoveAt(i);
            Remplit(Math.Min(i, _liste.Count - 1));
            _etat.Text = "Lien retiré de la liste : pense à « Enregistrer ».";
        }

        private void Ouvrir()
        {
            int i = _liens.SelectedIndex;
            if (i < 0 || i >= _liste.Count) return;
            string erreur;
            _etat.Text = NotesJeu.Ouvre(_liste[i].Url, out erreur) ? "Ouvert dans ton navigateur." : "Ouverture impossible : " + erreur;
        }

        private void Enregistrer()
        {
            if (!NotesJeu.Enregistre(_g.Name, _etoiles.SelectedIndex, _note.Text, _liste))
            {
                _etat.Text = "Enregistrement impossible (fichier en lecture seule, ou " + NotesJeu.MaxJeux + " jeux annotés au plus).";
                return;
            }
            Change = true;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
