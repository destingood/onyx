using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Mode Compétition : tout ce qui aide en partie classée, en un clic, et retiré en un clic.
    /// Le travail est dans Competition (CompetitionMachine.cs) ; cette fenêtre ne fait qu'afficher.
    /// </summary>
    internal class CompetitionForm : Form
    {
        private readonly List<Tweak> _all;
        private readonly TextBox _journal;
        private readonly CheckBox _cProfils, _cModeJeu, _cEsport, _cPlafond;
        private readonly Button _go, _annule, _close;
        private volatile bool _stop;
        private bool _running;

        public CompetitionForm(List<Tweak> all)
        {
            _all = all;
            Text = "ONYX — Compétition";
            ClientSize = new Size(720, 600);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = FpsUi.BgMain; ForeColor = FpsUi.Ink; Font = FpsUi.Body;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var titre = new Label
            {
                Text = "🏁 Compétition", Font = FpsUi.H2, ForeColor = FpsUi.Gold,
                Location = new Point(20, 16), AutoSize = true
            };
            var expl = new Label
            {
                Text = "Pose d'un coup ce qui sert en partie classée, puis le retire exactement : « Annuler "
                     + "Compétition » ne touche qu'à ce qu'elle a changé, pas à ce que tu as réglé entre-temps.\r\n"
                     + "Conseil : Mesure en jeu — avant (fenêtre Optimiser et prouver), puis Compétition, "
                     + "puis Mesure en jeu — APRÈS.",
                ForeColor = FpsUi.Dim, Font = FpsUi.Small,
                Location = new Point(20, 46), Size = new Size(680, 58)
            };
            _cProfils = Case("Profil Auto pour les jeux installés sans profil (cœurs P ou V-Cache, priorité Haute)", 108, true);
            _cModeJeu = Case("Mode Jeu automatique (services de fond suspendus pendant la partie)", 132, true);
            _cEsport = Case("Réglages eSport, prouvés : appliqués par lots, annulés s'ils dégradent (2 à 5 min)", 156, true);
            _cPlafond = Case("Plafond FPS recommandé pour ton écran (utile avec G-Sync / FreeSync)", 180, false);

            _journal = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true,
                BackColor = FpsUi.Card, ForeColor = FpsUi.Ink, BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 9f), Location = new Point(20, 212), Size = new Size(680, 330)
            };
            _go = FpsUi.GoldButton("Activer"); _go.SetBounds(20, 554, 140, 32);
            _go.Click += (s, e) => { if (_running) DemandeArret(); else Activer(); };
            _annule = FpsUi.GhostButton("Annuler Compétition"); _annule.SetBounds(170, 554, 190, 32); _annule.ForeColor = FpsUi.Err;
            _annule.Click += (s, e) => Annuler();
            _close = FpsUi.GhostButton("Fermer"); _close.SetBounds(600, 554, 100, 32);
            _close.Click += (s, e) => Close();
            Controls.Add(titre); Controls.Add(expl);
            Controls.Add(_cProfils); Controls.Add(_cModeJeu); Controls.Add(_cEsport); Controls.Add(_cPlafond);
            Controls.Add(_journal); Controls.Add(_go); Controls.Add(_annule); Controls.Add(_close);

            FormClosing += (s, e) =>
            {
                if (!_running) return;
                // Fermer en route laisserait un lot de réglages appliqué sans verdict.
                e.Cancel = true;
                DemandeArret();
            };
            MajEtat();
        }

        private CheckBox Case(string texte, int y, bool coche)
        {
            return new CheckBox
            {
                Text = texte, Checked = coche, ForeColor = FpsUi.Ink, Font = FpsUi.Small,
                Location = new Point(20, y), Size = new Size(680, 22)
            };
        }

        private void MajEtat()
        {
            Competition.Etat e = null;
            try { e = Competition.Active(); } catch { }
            _annule.Visible = e != null;
            _go.Enabled = e == null && !_running;
            _cProfils.Enabled = _cModeJeu.Enabled = _cEsport.Enabled = _cPlafond.Enabled = e == null && !_running;
            if (e != null && !_running && _journal.TextLength == 0)
                Ajoute("Compétition active depuis le " + e.Active.ToString("dd/MM HH:mm") + ".", 0);
        }

        private void Ajoute(string m, int niveau)
        {
            string pre = niveau == 3 ? "✗ " : niveau == 2 ? "! " : niveau == 1 ? "✓ " : "  ";
            string ligne = DateTime.Now.ToString("HH:mm:ss") + " " + pre + m + "\r\n";
            try
            {
                if (IsDisposed) return;
                if (InvokeRequired) BeginInvoke((Action)(() => _journal.AppendText(ligne)));
                else _journal.AppendText(ligne);
            }
            catch { }
        }

        private void DemandeArret()
        {
            if (_stop) return;
            _stop = true;
            _go.Enabled = false; _go.Text = "Arrêt…";
            Ajoute("Arrêt demandé : fin propre dès que possible. Ce qui est déjà posé reste annulable.", 2);
        }

        private void Debut(bool interruptible)
        {
            _running = true; _stop = false;
            _close.Enabled = false; _annule.Enabled = false;
            _cProfils.Enabled = _cModeJeu.Enabled = _cEsport.Enabled = _cPlafond.Enabled = false;
            _go.Enabled = interruptible; _go.Text = interruptible ? "Arrêter" : "Activer";
        }

        private void Fin()
        {
            _running = false;
            _close.Enabled = _annule.Enabled = true;
            _go.Text = "Activer";
            MajEtat();
        }

        private void Activer()
        {
            var c = new Competition.Choix
            {
                Profils = _cProfils.Checked, ModeJeu = _cModeJeu.Checked,
                Esport = _cEsport.Checked, Plafond = _cPlafond.Checked
            };
            if (!c.Profils && !c.ModeJeu && !c.Esport && !c.Plafond) { Ajoute("Coche au moins une case.", 2); return; }
            if (MessageBox.Show(this, "Activer la Compétition ? Tout ce qu'elle pose pourra être retiré par « Annuler Compétition ».",
                    "ONYX", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            Debut(true);
            _journal.Clear();
            Task.Run(() =>
            {
                PreuveRun.Rapport r = null;
                try { r = Competition.Active(_all, c, Ajoute, () => _stop); }
                catch (Exception ex) { Ajoute("Erreur : " + ex.Message, 3); }
                try { AppStats.Invalidate(); } catch { }
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        if (r != null) _journal.AppendText("\r\n===== Réglages eSport =====\r\n" + r.Texte() + "\r\n");
                        Fin();
                    }));
                }
                catch { }
            });
        }

        private void Annuler()
        {
            if (MessageBox.Show(this, "Annuler la Compétition ? Ce qu'elle a posé est retiré ; tes propres réglages restent.",
                    "ONYX", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            Debut(false);   // une annulation ne s'interrompt pas
            Task.Run(() =>
            {
                try { Competition.Annule(_all, Ajoute); }
                catch (Exception ex) { Ajoute("Erreur pendant l'annulation : " + ex.Message, 3); }
                try { AppStats.Invalidate(); } catch { }
                try { BeginInvoke((Action)Fin); } catch { }
            });
        }
    }
}
