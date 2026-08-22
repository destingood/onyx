using System;
using System.Drawing;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// L'ÉCRAN QUI MONTRE AVANT D'ENVOYER.
    ///
    /// C'est ici que vit le verrou 2 d'EnvoiDiagnostic : le texte occupe la quasi-totalité de la
    /// fenêtre, et il est là AVANT que le bouton d'envoi existe. Un consentement donné sans voir
    /// le contenu n'est pas un consentement — mettre le rapport derrière un « détails… » repliés
    /// reviendrait à ne pas le montrer.
    ///
    /// Le bouton « Envoyer » est DÉSACTIVÉ, avec son motif écrit dessus, quand l'envoi ne peut
    /// pas aboutir : pas de point de collecte, ou des données personnelles subsistantes. Même
    /// règle que le bouton LatencyMon — un bouton qui ne peut rien faire ne doit pas se proposer.
    /// </summary>
    internal sealed class RapportDiagnosticForm : Form
    {
        private readonly TextBox _texte;
        private readonly Label _etat;
        private RapportDiagnostic.Rapport _rapport;

        public RapportDiagnosticForm()
        {
            Text = "ONYX — rapport de diagnostic";
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false; MaximizeBox = true;
            ClientSize = new Size(760, 560);
            MinimumSize = new Size(640, 420);

            var haut = new Label
            {
                Dock = DockStyle.Top, Height = 58, Padding = new Padding(12, 10, 12, 6),
                Text = "Voici EXACTEMENT ce qui serait envoyé. Rien ne part tant que tu ne cliques "
                     + "pas sur « Envoyer ».\r\nLes noms de dossier personnels sont déjà masqués."
            };

            _texte = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.Both, WordWrap = false,
                Font = new Font(FontFamily.GenericMonospace, 8.5f),
                Text = "Construction du rapport…"
            };

            var bas = new Panel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(8) };
            _etat = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };

            var fermer = Bouton("Fermer", 100);
            fermer.Dock = DockStyle.Right;
            fermer.Click += (s, e) => Close();

            var envoyer = Bouton("Envoyer", 150);
            envoyer.Dock = DockStyle.Right;
            envoyer.Enabled = false;
            envoyer.Click += (s, e) => Envoie(envoyer);

            var copier = Bouton("Copier", 110);
            copier.Dock = DockStyle.Right;
            copier.Click += (s, e) =>
            {
                try { Clipboard.SetText(_texte.Text); copier.Text = "✓ Copié"; }
                catch (Exception ex) { JournalTechnique.Echec("RapportDiagnosticForm.Copier", ex); }
            };

            var enregistrer = Bouton("Enregistrer", 130);
            enregistrer.Dock = DockStyle.Right;
            enregistrer.Click += (s, e) =>
            {
                string p = RapportDiagnostic.Enregistre(_rapport);
                _etat.Text = p.Length > 0 ? "Enregistré : " + p : "Enregistrement impossible.";
            };

            bas.Controls.Add(_etat);
            bas.Controls.Add(enregistrer);
            bas.Controls.Add(copier);
            bas.Controls.Add(envoyer);
            bas.Controls.Add(fermer);

            Controls.Add(_texte);
            Controls.Add(bas);
            Controls.Add(haut);

            try { Theme.Apply(this); } catch { }

            // La construction lit les journaux : quelques centaines de millisecondes. On la fait
            // en fond pour que la fenêtre paraisse tout de suite plutôt que de rester blanche.
            Shown += (s, e) => System.Threading.Tasks.Task.Run(() =>
            {
                RapportDiagnostic.Rapport r = RapportDiagnostic.Construire();
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        _rapport = r;
                        _texte.Text = r.Texte;
                        _texte.Select(0, 0);
                        Actualise(envoyer);
                    }));
                }
                catch { }   // fenêtre fermée entre-temps : sans conséquence
            });
        }

        /// <summary>Décide de l'état du bouton d'envoi, et DIT pourquoi quand il est coupé.</summary>
        private void Actualise(Button envoyer)
        {
            if (_rapport == null) return;

            if (_rapport.DonneesPerso.Count > 0)
            {
                envoyer.Enabled = false;
                envoyer.Text = "Envoi bloqué";
                _etat.Text = "Envoi BLOQUÉ : " + string.Join(", ", _rapport.DonneesPerso.ToArray())
                           + " détecté dans le rapport. Il reste chez toi.";
                return;
            }

            if (!EnvoiDiagnostic.Configure)
            {
                envoyer.Enabled = false;
                envoyer.Text = "Envoi non configuré";
                _etat.Text = "Réf. " + _rapport.Reference
                           + " — aucun point de collecte dans cette version : enregistre le "
                           + "rapport et transmets-le toi-même.";
                return;
            }

            envoyer.Enabled = true;
            _etat.Text = "Réf. " + _rapport.Reference + " — prêt à être envoyé.";
        }

        private void Envoie(Button envoyer)
        {
            // Une confirmation APRÈS avoir lu, pas à la place de lire.
            DialogResult d = MessageBox.Show(this,
                "Envoyer ce rapport à l'auteur d'ONYX ?\r\n\r\n"
                + "Il contient l'état de ta machine et ce qui a échoué — pas tes fichiers, "
                + "pas tes jeux, pas ton nom.\r\n\r\nRéférence : " + _rapport.Reference,
                "Confirmer l'envoi", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (d != DialogResult.Yes) return;

            envoyer.Enabled = false;
            envoyer.Text = "Envoi…";
            System.Threading.Tasks.Task.Run(() =>
            {
                FormatDiagnostic.Resultat res = EnvoiDiagnostic.Envoie(_rapport);
                // Quel que soit le résultat, le rapport reste chez la personne : c'est ce qui
                // rend un échec d'envoi sans conséquence.
                string local = RapportDiagnostic.Enregistre(_rapport);
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        _etat.Text = FormatDiagnostic.Explique(res, _rapport.Reference)
                                   + (local.Length > 0 ? "  (copie : " + local + ")" : "");
                        envoyer.Text = res == FormatDiagnostic.Resultat.Envoye ? "✓ Envoyé" : "Envoyer";
                        envoyer.Enabled = res != FormatDiagnostic.Resultat.Envoye;
                    }));
                }
                catch { }
            });
        }

        private static Button Bouton(string texte, int largeur)
        {
            var b = new Button
            {
                Text = texte, Width = largeur, Height = 28,
                FlatStyle = FlatStyle.Flat, BackColor = Color.White
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            return b;
        }
    }
}
