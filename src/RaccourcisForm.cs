using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Raccourcis clavier globaux (⋯ → Système → Raccourcis clavier…) : une case par action, on y
    /// tape la nouvelle combinaison. Pendant que la fenêtre est ouverte, les raccourcis d'ONYX sont
    /// suspendus, sinon Windows avalerait la combinaison qu'on veut taper.
    /// </summary>
    internal sealed class RaccourcisForm : Form
    {
        private readonly DashboardForm _hote;
        private readonly Raccourcis.Combo[] _t;
        private readonly TextBox[] _cases = new TextBox[Raccourcis.Nombre];
        private readonly Label _etat;

        public RaccourcisForm(DashboardForm hote)
        {
            _hote = hote;
            Raccourcis.Combo[] actuels = hote != null ? hote.RaccourcisActuels : DashboardForm.ChargeRaccourcis();
            _t = new Raccourcis.Combo[Raccourcis.Nombre];
            for (int i = 0; i < _t.Length; i++) _t[i] = new Raccourcis.Combo { Mods = actuels[i].Mods, Touche = actuels[i].Touche };

            Text = "ONYX — Raccourcis clavier";
            ClientSize = new Size(600, 160 + Raccourcis.Nombre * 50);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false;
            Font = FpsUi.Body;
            KeyPreview = false;

            Controls.Add(new Label
            {
                Text = "Clique dans une case puis tape la combinaison (Ctrl ou Alt + une lettre, un chiffre ou F1 à F12). "
                     + "Ils marchent partout, même en pleine partie.",
                Location = new Point(16, 14), Size = new Size(568, 40)
            });
            for (int i = 0; i < Raccourcis.Nombre; i++)
            {
                int k = i, y = 66 + i * 50;
                Controls.Add(new Label { Text = Raccourcis.Libelles[i], Location = new Point(16, y + 7), Size = new Size(230, 20) });
                var c = new TextBox { Location = new Point(250, y + 3), Size = new Size(190, 26), ReadOnly = true, ShortcutsEnabled = false, TabStop = true, Cursor = Cursors.Hand };
                c.AccessibleName = "Raccourci : " + Raccourcis.Libelles[i];
                c.KeyDown += (s, e) => Saisie(k, e);
                c.Enter += (s, e) => { _etat.Text = "Tape la combinaison pour « " + Raccourcis.Libelles[k] + " »."; };
                _cases[i] = c;
                Controls.Add(c);
                var off = OnyxDesign.Button("Désactiver");
                off.SetBounds(452, y, 132, 32);
                off.Click += (s, e) => { _t[k] = new Raccourcis.Combo(); Affiche(); _etat.Text = Raccourcis.Libelles[k] + " : désactivé."; };
                Controls.Add(off);
            }

            _etat = new Label { Location = new Point(16, 70 + Raccourcis.Nombre * 50), Size = new Size(568, 36), ForeColor = FpsUi.Dim };
            Controls.Add(_etat);

            var reset = OnyxDesign.Button("Tout réinitialiser");
            reset.SetBounds(16, ClientSize.Height - 48, 160, 34);
            reset.Click += (s, e) => { Raccourcis.Combo[] d = Raccourcis.TousParDefaut(); Array.Copy(d, _t, d.Length); Affiche(); _etat.Text = "Raccourcis d'origine remis : pense à « Enregistrer »."; };
            var ok = OnyxDesign.AccentButton("Enregistrer");
            ok.SetBounds(ClientSize.Width - 16 - 130 - 10 - 110, ClientSize.Height - 48, 130, 34);
            ok.Click += (s, e) => Enregistre();
            var ca = OnyxDesign.Button("Annuler");
            ca.SetBounds(ClientSize.Width - 16 - 110, ClientSize.Height - 48, 110, 34);
            ca.DialogResult = DialogResult.Cancel;
            Controls.Add(reset); Controls.Add(ok); Controls.Add(ca);
            CancelButton = ca;
            try { Theme.Apply(this); } catch { }
            Affiche();

            Shown += (s, e) => { if (_hote != null) _hote.SuspendRaccourcis(); };
            FormClosed += (s, e) => { if (_hote != null && DialogResult != DialogResult.OK) _hote.ReprendRaccourcis(); };
        }

        private void Affiche()
        {
            for (int i = 0; i < _cases.Length; i++) _cases[i].Text = Raccourcis.Texte(_t[i]);
        }

        // Alt+touche passe par ProcessDialogKey (mnémoniques) : on le laisse arriver à la case.
        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (ActiveControl is TextBox && Array.IndexOf(_cases, ActiveControl) >= 0 && (keyData & Keys.Alt) == Keys.Alt) return false;
            return base.ProcessDialogKey(keyData);
        }

        private void Saisie(int i, KeyEventArgs e)
        {
            e.SuppressKeyPress = true; e.Handled = true;
            Keys k = e.KeyCode;
            if (k == Keys.Tab) { SelectNextControl(_cases[i], !e.Shift, true, true, true); return; }
            if (k == Keys.Escape) { Close(); return; }
            if (k == Keys.ControlKey || k == Keys.Menu || k == Keys.ShiftKey || k == Keys.LWin || k == Keys.RWin)
            {
                var p = new List<string>();
                if (e.Control) p.Add("Ctrl"); if (e.Alt) p.Add("Alt"); if (e.Shift) p.Add("Maj");
                _cases[i].Text = string.Join("+", p) + "+…";
                return;
            }
            var c = new Raccourcis.Combo
            {
                Touche = (int)k,
                Mods = (e.Control ? Raccourcis.ModCtrl : 0) | (e.Alt ? Raccourcis.ModAlt : 0) | (e.Shift ? Raccourcis.ModMaj : 0)
            };
            string refus = Raccourcis.Refus(c);
            if (refus != null) { _etat.Text = refus; Affiche(); return; }
            int autre = Raccourcis.Conflit(_t, c, i);
            if (autre >= 0) { _etat.Text = Raccourcis.Texte(c) + " sert déjà à « " + Raccourcis.Libelles[autre] + " »."; Affiche(); return; }
            _t[i] = c;
            Affiche();
            _etat.Text = Raccourcis.Libelles[i] + " : " + Raccourcis.Texte(c) + ". Pense à « Enregistrer ».";
        }

        private void Enregistre()
        {
            if (_hote == null) { DialogResult = DialogResult.OK; Close(); return; }
            List<string> echecs = _hote.AppliqueRaccourcis(_t);
            if (echecs.Count > 0)
            {
                _etat.ForeColor = FpsUi.Warn;
                _etat.Text = "Déjà pris par une autre application : " + string.Join(", ", echecs) + ". Choisis une autre combinaison.";
                return;   // enregistré quand même ; la fenêtre reste ouverte pour corriger
            }
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
