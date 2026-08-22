using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Registre des problèmes rencontrés : noter sur le moment, retrouver plus tard.
    /// Le fichier reste LOCAL — aucun envoi, l'export est une action manuelle.
    /// </summary>
    internal class ProblemForm : Form
    {
        private readonly Action<string, int> _log;
        private ListBox _list;
        private TextBox _saisie;
        private Label _compteur;
        private Button _btnAjouter, _btnBasculer, _btnSupprimer, _btnCopier, _btnFermer;
        private List<ProblemLog.Entree> _entrees = new List<ProblemLog.Entree>();

        public ProblemForm(Action<string, int> log)
        {
            _log = log;
            Build();
            Recharger();
            Theme.Apply(this);
        }

        private void Build()
        {
            Text = "ONYX — Problèmes rencontrés";
            ClientSize = new Size(640, 460);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = Color.FromArgb(245, 246, 248);
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  Problèmes rencontrés", Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 13f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            Controls.Add(new Label
            {
                Text = "Note ce qui cloche AU MOMENT où ça arrive : dans trois semaines, personne ne saura plus\n"
                     + "quand ça a commencé. La date et ta version d'ONYX sont enregistrées avec.",
                Location = new Point(18, 60), Size = new Size(604, 34),
                ForeColor = Color.FromArgb(90, 94, 104)
            });

            _saisie = new TextBox
            {
                Location = new Point(18, 100), Size = new Size(492, 26),
                Font = new Font("Segoe UI", 9.5f)
            };
            _saisie.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Ajouter(); } };
            Controls.Add(_saisie);

            _btnAjouter = new Button { Text = "Ajouter", Location = new Point(518, 99), Size = new Size(104, 28) };
            _btnAjouter.Click += (s, e) => Ajouter();
            Controls.Add(_btnAjouter);

            _list = new ListBox
            {
                Location = new Point(18, 140), Size = new Size(604, 240),
                BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 9f),
                IntegralHeight = false, HorizontalScrollbar = true
            };
            Controls.Add(_list);

            _compteur = new Label
            {
                Location = new Point(18, 388), Size = new Size(300, 22),
                Font = new Font("Segoe UI Semibold", 9.5f), ForeColor = Color.FromArgb(60, 64, 72)
            };
            Controls.Add(_compteur);

            _btnBasculer = new Button { Text = "Marquer réglé / à régler", Location = new Point(18, 416), Size = new Size(180, 28) };
            _btnBasculer.Click += (s, e) => { if (Selection() >= 0 && ProblemLog.Basculer(Selection())) Recharger(); };
            Controls.Add(_btnBasculer);

            _btnSupprimer = new Button { Text = "Supprimer", Location = new Point(206, 416), Size = new Size(100, 28) };
            _btnSupprimer.Click += (s, e) => Supprimer();
            Controls.Add(_btnSupprimer);

            _btnCopier = new Button { Text = "Copier le rapport", Location = new Point(314, 416), Size = new Size(140, 28) };
            _btnCopier.Click += (s, e) => Copier();
            Controls.Add(_btnCopier);

            _btnFermer = new Button { Text = "Fermer", Location = new Point(522, 416), Size = new Size(100, 28), DialogResult = DialogResult.OK };
            Controls.Add(_btnFermer);
            AcceptButton = _btnAjouter;
            CancelButton = _btnFermer;
        }

        private int Selection() { return _list.SelectedIndex; }

        private void Recharger()
        {
            _entrees = ProblemLog.Charger();
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var e in _entrees)
                _list.Items.Add((e.Etat == ProblemLog.Etat.Regle ? "[réglé]    " : "[à régler] ")
                    + e.Date.ToString("dd/MM/yy HH:mm") + "  v" + e.Version + "  " + e.Description);
            _list.EndUpdate();
            int ouverts = ProblemLog.Ouverts(_entrees);
            _compteur.Text = _entrees.Count == 0
                ? "Aucun problème enregistré."
                : ouverts + " à régler sur " + _entrees.Count + " au total.";
        }

        private void Ajouter()
        {
            string t = _saisie.Text;
            if (ProblemLog.Nettoie(t).Length == 0)
            {
                MessageBox.Show(this, "Décris le problème en quelques mots avant d'ajouter.",
                    "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!ProblemLog.Ajouter(t, _log))
            {
                MessageBox.Show(this, "Impossible d'écrire le registre (dossier en lecture seule ?).",
                    "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _saisie.Clear();
            Recharger();
        }

        private void Supprimer()
        {
            int i = Selection();
            if (i < 0) return;
            if (MessageBox.Show(this, "Supprimer définitivement cette entrée du registre ?",
                    "ONYX", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            if (ProblemLog.Supprimer(i)) Recharger();
        }

        private void Copier()
        {
            try
            {
                Clipboard.SetText(ProblemLog.Export(_entrees));
                MessageBox.Show(this, "Rapport copié : tu peux le coller où tu veux.\n\n"
                    + "Rien n'a été envoyé — ce registre reste sur ta machine.",
                    "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Copie impossible : " + ex.Message, "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
