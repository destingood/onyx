using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Fenêtre « Optimiser et prouver » : explique, lance PreuveRun hors du fil d'interface,
    /// affiche le journal en direct puis le rapport. Arrêter = fin propre après la mesure en cours.
    /// Sert aussi à montrer le rapport de la vérification faite après redémarrage.
    /// </summary>
    internal class PreuveForm : Form
    {
        private readonly List<Tweak> _all;
        private readonly TextBox _journal;
        private readonly Button _go, _close;
        private volatile bool _stop;
        private bool _running;

        public PreuveForm(List<Tweak> all) : this(all, null) { }

        /// <param name="dejaFait">Rapport à afficher tel quel (vérification après redémarrage), sans relancer.</param>
        public PreuveForm(List<Tweak> all, PreuveRun.Rapport dejaFait)
        {
            _all = all;
            Text = "ONYX — Optimiser et prouver";
            ClientSize = new Size(720, 520);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = FpsUi.BgMain; ForeColor = FpsUi.Ink; Font = FpsUi.Body;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var titre = new Label
            {
                Text = "Optimiser et prouver", Font = FpsUi.H2, ForeColor = FpsUi.Gold,
                Location = new Point(20, 16), AutoSize = true
            };
            var expl = new Label
            {
                Text = "ONYX mesure la latence de ton PC au repos (DPC/ISR des pilotes, précision du minuteur), "
                     + "applique la sélection Auto par catégorie, re-mesure après chaque lot et annule ce qui "
                     + "dégrade la mesure. Un réglage neutre est gardé : beaucoup n'agissent qu'en jeu.\r\n"
                     + "Compte 2 à 5 minutes. Ne lance rien de lourd pendant la mesure. Sauvegarde .reg et point "
                     + "de restauration créés avant tout changement.",
                ForeColor = FpsUi.Dim, Font = FpsUi.Small,
                Location = new Point(20, 46), Size = new Size(680, 68)
            };
            _journal = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true,
                BackColor = FpsUi.Card, ForeColor = FpsUi.Ink, BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 9f), Location = new Point(20, 120), Size = new Size(680, 340)
            };
            _go = FpsUi.GoldButton("Démarrer"); _go.SetBounds(20, 474, 140, 32);
            _go.Click += (s, e) => { if (_running) DemandeArret(); else Demarre(); };
            _close = FpsUi.GhostButton("Fermer"); _close.SetBounds(600, 474, 100, 32);
            _close.Click += (s, e) => Close();
            Controls.Add(titre); Controls.Add(expl); Controls.Add(_journal); Controls.Add(_go); Controls.Add(_close);

            FormClosing += (s, e) =>
            {
                if (!_running) return;
                // Fermer en pleine mesure laisserait un lot appliqué sans verdict : on demande l'arrêt
                // et on attend la fin propre.
                e.Cancel = true;
                DemandeArret();
            };

            if (dejaFait != null)
            {
                _go.Visible = false;
                _journal.Text = "Vérification après redémarrage\r\n\r\n" + dejaFait.Texte();
            }
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
            Ajoute("Arrêt demandé : fin après la mesure en cours.", 2);
        }

        private void Demarre()
        {
            if (MessageBox.Show(this, "Lancer l'optimisation prouvée ? Des réglages vont être appliqués, puis annulés s'ils dégradent la latence mesurée.",
                    "ONYX", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            _running = true; _stop = false;
            _go.Text = "Arrêter"; _close.Enabled = false;
            _journal.Clear();
            Task.Run(() =>
            {
                PreuveRun.Rapport r = null;
                try { r = PreuveRun.Lance(_all, Ajoute, () => _stop); }
                catch (Exception ex) { Ajoute("Erreur : " + ex.Message, 3); }
                try { BeginInvoke((Action)(() => Fini(r))); } catch { }
            });
        }

        private void Fini(PreuveRun.Rapport r)
        {
            _running = false;
            _go.Visible = false; _close.Enabled = true;
            if (r == null) return;
            _journal.AppendText("\r\n===== Rapport =====\r\n" + r.Texte());
            if (r.RedemarrageRequis
                && MessageBox.Show(this, "Des réglages demandent un redémarrage. ONYX les vérifiera au prochain lancement. Redémarrer maintenant ?",
                       "ONYX", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                try { Sys.Run(Sys.Sys32("shutdown.exe"), "/r /t 10 /c \"ONYX : redémarrage pour vérifier les réglages\""); } catch { }
            }
        }
    }
}
