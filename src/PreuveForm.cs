using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Fenêtre « Optimiser et prouver » : explique, lance PreuveRun hors du fil d'interface,
    /// affiche le journal en direct puis le rapport. Arrêter = fin propre entre deux lots.
    /// Porte aussi la validation EN JEU facultative (PreuveJeu) : mesure avant, puis après.
    /// Sert enfin à montrer le rapport de la vérification faite après redémarrage.
    /// </summary>
    internal class PreuveForm : Form
    {
        private readonly List<Tweak> _all;
        private readonly TextBox _journal;
        private readonly Button _go, _jeu, _close;
        private volatile bool _stop;
        private bool _running;
        private Button _actif;   // le bouton qui sert d'« Arrêter » pendant l'opération en cours

        public PreuveForm(List<Tweak> all) : this(all, null) { }

        /// <param name="dejaFait">Rapport à afficher tel quel (vérification après redémarrage), sans relancer.</param>
        public PreuveForm(List<Tweak> all, PreuveRun.Rapport dejaFait)
        {
            _all = all;
            Text = "ONYX — Optimiser et prouver";
            ClientSize = new Size(720, 560);
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
                     + "dégrade la mesure. Un réglage neutre est gardé : beaucoup n'agissent qu'en jeu. "
                     + "Compte 2 à 5 minutes, sans rien de lourd en cours. Sauvegarde .reg avant tout changement ; "
                     + "le moniteur de latence en direct doit être fermé.\r\n"
                     + "Facultatif, pour juger aussi les FPS : « Mesure en jeu » AVANT de démarrer, puis à nouveau "
                     + "APRÈS (même jeu, même type de scène, 60 s chacune).",
                ForeColor = FpsUi.Dim, Font = FpsUi.Small,
                Location = new Point(20, 46), Size = new Size(680, 98)
            };
            _journal = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true,
                BackColor = FpsUi.Card, ForeColor = FpsUi.Ink, BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 9f), Location = new Point(20, 150), Size = new Size(680, 350)
            };
            _go = FpsUi.GoldButton("Démarrer"); _go.SetBounds(20, 514, 140, 32);
            _go.Click += (s, e) => { if (_running) DemandeArret(); else Demarre(); };
            _jeu = FpsUi.GhostButton(""); _jeu.SetBounds(170, 514, 220, 32); _jeu.ForeColor = FpsUi.Gold;
            _jeu.Click += (s, e) => { if (_running) DemandeArret(); else MesureEnJeu(); };
            _close = FpsUi.GhostButton("Fermer"); _close.SetBounds(600, 514, 100, 32);
            _close.Click += (s, e) => Close();
            Controls.Add(titre); Controls.Add(expl); Controls.Add(_journal);
            Controls.Add(_go); Controls.Add(_jeu); Controls.Add(_close);
            LibelleJeu();

            FormClosing += (s, e) =>
            {
                if (!_running) return;
                // Fermer en pleine opération laisserait un lot appliqué sans verdict : on demande
                // l'arrêt et on attend la fin propre.
                e.Cancel = true;
                DemandeArret();
            };

            if (dejaFait != null)
            {
                _go.Visible = false; _jeu.Visible = false;
                _journal.Text = "Vérification après redémarrage\r\n\r\n" + dejaFait.Texte();
            }
        }

        private void LibelleJeu()
        {
            bool apres = false;
            try { apres = PreuveJeu.EtapeApres(); } catch { }
            _jeu.Text = apres ? "🎮 Mesure en jeu — APRÈS" : "🎮 Mesure en jeu — avant";
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

        private void Debut(Button actif)
        {
            _running = true; _stop = false; _actif = actif;
            _go.Enabled = _jeu.Enabled = false; _close.Enabled = false;
            actif.Enabled = true; actif.Text = "Arrêter";
        }

        private void Fin()
        {
            _running = false;
            _go.Enabled = _jeu.Enabled = _close.Enabled = true;
            _go.Text = "Démarrer";
            LibelleJeu();
        }

        private void DemandeArret()
        {
            if (_stop) return;
            _stop = true;
            if (_actif != null) { _actif.Enabled = false; _actif.Text = "Arrêt…"; }
            Ajoute("Arrêt demandé : fin propre dès que possible.", 2);
        }

        // ------------------------------------------------------------------ preuve au repos

        private void Demarre()
        {
            if (MessageBox.Show(this, "Lancer l'optimisation prouvée ? Des réglages vont être appliqués, puis annulés s'ils dégradent la latence mesurée.",
                    "ONYX", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            Debut(_go);
            _journal.Clear();
            Task.Run(() =>
            {
                PreuveRun.Rapport r = null;
                try { r = PreuveRun.Lance(_all, Ajoute, () => _stop); }
                catch (Exception ex) { Ajoute("Erreur : " + ex.Message, 3); }
                try { BeginInvoke((Action)(() => FiniPreuve(r))); } catch { }
            });
        }

        private void FiniPreuve(PreuveRun.Rapport r)
        {
            Fin();
            if (r == null) return;
            _journal.AppendText("\r\n===== Rapport =====\r\n" + r.Texte());
            if (PreuveJeu.EtapeApres())
                _journal.AppendText("\r\nÉtape suivante facultative : « Mesure en jeu — APRÈS »"
                    + (r.RedemarrageRequis ? " (après le redémarrage)." : ".") + "\r\n");
            if (r.RedemarrageRequis
                && MessageBox.Show(this, "Des réglages demandent un redémarrage. ONYX les vérifiera au prochain lancement. Redémarrer maintenant ?",
                       "ONYX", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                try { Sys.Run(Sys.Sys32("shutdown.exe"), "/r /t 10 /c \"ONYX : redémarrage pour vérifier les réglages\""); } catch { }
            }
        }

        // ------------------------------------------------------------------ validation en jeu

        private void MesureEnJeu()
        {
            bool apres = PreuveJeu.EtapeApres();
            if (apres && PreuveRun.AttenteExiste())
            {
                MessageBox.Show(this, "Des réglages à redémarrage attendent encore leur vérification : redémarre le PC et relance ONYX avant la mesure « après ».",
                    "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Preuve.MesureJeu avant = apres ? PreuveJeu.Avant() : null;
            Debut(_jeu);
            Ajoute(apres ? "Mesure en jeu APRÈS (référence : " + avant.Resume() + ")." : "Mesure en jeu AVANT l'optimisation.", 0);
            Task.Run(() =>
            {
                Preuve.MesureJeu m = null;
                Preuve.Verdict v = Preuve.Verdict.Incomparable;
                try
                {
                    m = PreuveJeu.Mesure(Ajoute, () => _stop, apres ? avant.Jeu : null);
                    if (m != null && apres)
                    {
                        v = Preuve.JugeJeu(avant, m);
                        if (v == Preuve.Verdict.Regression && _stop) v = Preuve.Verdict.Incomparable;   // non confirmée
                        else if (v == Preuve.Verdict.Regression)
                        {
                            Ajoute("Baisse possible : seconde mesure de " + PreuveJeu.SecondesJeu + " s pour confirmer, continue à jouer.", 2);
                            Preuve.MesureJeu m2 = PreuveJeu.Mesure(Ajoute, () => _stop, avant.Jeu);
                            if (m2 == null) v = Preuve.Verdict.Incomparable;   // non confirmée : on ne touche à rien
                            else { m = m2; v = Preuve.JugeJeu(avant, m2); }
                        }
                    }
                }
                catch (Exception ex) { Ajoute("Erreur : " + ex.Message, 3); m = null; }
                try { BeginInvoke((Action)(() => FiniJeu(apres, avant, m, v))); } catch { }
            });
        }

        private void FiniJeu(bool apres, Preuve.MesureJeu avant, Preuve.MesureJeu m, Preuve.Verdict v)
        {
            Fin();
            if (m == null) return;
            if (!apres)
            {
                if (!Preuve.JeuValide(m)) { Ajoute("Mesure « avant » non retenue : trop courte ou trop peu d'images. Recommence.", 2); return; }
                try { PreuveJeu.EnregistreAvant(m); }
                catch (Exception ex) { Ajoute("Mesure « avant » non enregistrée : " + ex.Message, 3); return; }
                Ajoute("Mesure « avant » enregistrée. Reviens ici et clique « Démarrer ».", 1);
                LibelleJeu();
                return;
            }

            _journal.AppendText("\r\n===== En jeu =====\r\nAvant : " + avant.Resume() + "\r\nAprès : " + m.Resume()
                + "\r\nVerdict : " + Preuve.Libelle(v) + "\r\n");
            if (v == Preuve.Verdict.Incomparable && Preuve.PlafondChange(avant, m))
            {
                Ajoute("Le plafond FPS a changé entre les deux mesures (" + avant.Plafond + " → " + m.Plafond
                    + ") : comparaison impossible, un plafond baisse les FPS par construction. Rien n'est modifié.", 2);
                return;
            }
            if (v == Preuve.Verdict.Incomparable)
            {
                Ajoute("Comparaison impossible (jeu différent, mesure interrompue ou trop courte) : rien n'est modifié, recommence la mesure « après ».", 2);
                return;
            }
            if (v == Preuve.Verdict.Regression)
            {
                Preuve.Attente derniere = PreuveJeu.DernierePreuve();
                List<Tweak> lot = derniere == null ? new List<Tweak>()
                    : _all.Where(t => derniere.Ids.Contains(t.Id)).ToList();
                if (lot.Count > 0 && MessageBox.Show(this,
                        "Baisse confirmée en jeu. Annuler les " + lot.Count + " réglage(s) appliqués par l'optimisation prouvée ?",
                        "ONYX", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                {
                    Annule(lot);
                    return;
                }
                Ajoute("Réglages conservés malgré la baisse (ton choix).", 2);
            }
            else Ajoute("Pas de baisse en jeu : réglages conservés.", 1);
            ClotureCycle();
        }

        private void Annule(List<Tweak> lot)
        {
            Debut(_jeu);
            _jeu.Enabled = false;   // une annulation ne s'interrompt pas
            Task.Run(() =>
            {
                // Un réglage que Windows dit déjà inactif (annulé après redémarrage, par exemple)
                // n'est pas « rétabli » : Revert impose des valeurs en dur.
                var actifs = lot.Where(t => { try { return t.Check == null || t.Check() != false; } catch { return true; } }).ToList();
                EngineResult r = null;
                try { r = Engine.Run(actifs, false, true, false, Ajoute); }
                catch (Exception ex) { Ajoute("Erreur pendant l'annulation : " + ex.Message, 3); }
                try { AppStats.Invalidate(); } catch { }
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        Fin();
                        if (r == null) return;
                        if (r.Failed.Count > 0)
                            Ajoute(r.Failed.Count + " réglage(s) n'ont pas pu être annulés (voir le journal) : la sauvegarde .reg reste disponible.", 3);
                        if (r.RebootNeeded) Ajoute("Redémarre le PC pour finir l'annulation.", 2);
                        ClotureCycle();
                    }));
                }
                catch { }
            });
        }

        /// <summary>Cycle avant/après terminé : la prochaine mesure en jeu repart d'un « avant ».</summary>
        private void ClotureCycle()
        {
            PreuveJeu.OublieAvant();
            LibelleJeu();
        }
    }
}
