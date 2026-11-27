using System;
using System.Drawing;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Petite fenêtre qui demande UNE ligne de texte (renommer un clip, ajouter un lien…).
    /// <c>valide</c> renvoie la raison d'un refus, ou null quand la saisie convient : la fenêtre
    /// reste ouverte tant que la saisie est refusée.
    /// </summary>
    internal sealed class SaisieTexte : Form
    {
        private readonly TextBox _champ;
        private readonly Label _erreur;
        private readonly Func<string, string> _valide;

        public string Valeur { get { return _champ.Text; } }

        public SaisieTexte(string titre, string consigne, string valeur, Func<string, string> valide, int longueurMax)
        {
            _valide = valide;
            Text = titre;
            ClientSize = new Size(520, 178);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            Font = FpsUi.Body;

            Controls.Add(new Label { Text = consigne, Location = new Point(16, 14), Size = new Size(488, 38) });
            _champ = new TextBox { Text = valeur ?? "", Location = new Point(16, 56), Width = 488, MaxLength = longueurMax };
            _champ.AccessibleName = consigne;
            Controls.Add(_champ);
            _erreur = new Label { Location = new Point(16, 88), Size = new Size(488, 36), ForeColor = Color.FromArgb(232, 84, 74) };
            Controls.Add(_erreur);

            var ok = OnyxDesign.AccentButton("OK");
            ok.SetBounds(ClientSize.Width - 16 - 110 - 10 - 110, ClientSize.Height - 46, 110, 32);
            ok.Click += (s, e) =>
            {
                string raison = _valide != null ? _valide(_champ.Text) : null;
                if (raison != null) { _erreur.Text = raison; _champ.Focus(); return; }
                DialogResult = DialogResult.OK; Close();
            };
            var annuler = OnyxDesign.Button("Annuler");
            annuler.SetBounds(ClientSize.Width - 16 - 110, ClientSize.Height - 46, 110, 32);
            annuler.DialogResult = DialogResult.Cancel;
            Controls.Add(ok); Controls.Add(annuler);
            AcceptButton = ok; CancelButton = annuler;
            try { Theme.Apply(this); } catch { }
            _erreur.ForeColor = Color.FromArgb(232, 84, 74);   // après le thème, qui recolore les libellés
            Shown += (s, e) => { _champ.Focus(); _champ.SelectAll(); };
        }

        /// <summary>La saisie, ou null si l'utilisateur annule.</summary>
        public static string Demande(IWin32Window parent, string titre, string consigne, string valeur, Func<string, string> valide, int longueurMax = 200)
        {
            using (var f = new SaisieTexte(titre, consigne, valeur, valide, longueurMax))
                return f.ShowDialog(parent) == DialogResult.OK ? f.Valeur : null;
        }
    }
}
