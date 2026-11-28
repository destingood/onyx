using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Fenêtre des préréglages d'un jeu (Fortnite, Cyberpunk 2077 ; ouverte depuis la fiche du jeu) : un mode choisi montre
    /// d'abord « réglage : avant → après », rien n'est écrit avant « Appliquer ». « Mesurer en jeu »
    /// reprend la mesure de 60 s de PreuveJeu, avant puis après : aucun chiffre de FPS n'est
    /// annoncé sans elle. Résolution et mode d'affichage : au choix, seulement parmi les valeurs que le
    /// fichier du jeu et l'écran acceptent ; « Ne pas changer » par défaut.
    /// </summary>
    internal sealed class ReglagesJeuForm : Form
    {
        private readonly IReglagesJeu _jeu;
        private readonly TextBox _apercu;
        private readonly Label _etat, _obstacle;
        private readonly NumericUpDown _objectif;
        private readonly Button _appliquer, _retablir, _mesurer;
        private readonly List<Button> _modes = new List<Button>();
        private readonly ComboBox _res, _aff;
        private OptionsAffichage _options;
        private string _raisonAffichage;
        private bool _chargeAffichage;
        private int _mode = -1;       // dernier préréglage cliqué (-1 : aucun)
        private int? _choisi;
        private int _limite;          // limite d'images lue dans le fichier du jeu (0 : aucune)
        private bool _vsync, _lu;
        private volatile bool _stop, _mesureEnCours;

        public ReglagesJeuForm(IReglagesJeu jeu)
        {
            _jeu = jeu;
            Text = "ONYX — Préréglages " + jeu.Jeu;
            ClientSize = new Size(660, 632);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = FpsUi.BgMain; ForeColor = FpsUi.Ink; Font = FpsUi.Body;
            try { Icon = Logo.MakeIcon(32, FpsUi.Gold); } catch { }

            Controls.Add(new Label
            {
                Text = jeu.Note,
                Location = new Point(20, 16), Size = new Size(620, 52), ForeColor = FpsUi.Dim, Font = FpsUi.Small
            });

            int x = 20;
            for (int m = 0; m < 4; m++)
            {
                int mode = m;
                Button b = OnyxDesign.Button(jeu.NomMode(m));
                b.SetBounds(x, 76, 148, 34);
                b.Click += (s, e) => Choisit(mode);
                Controls.Add(b); _modes.Add(b);
                x += 157;
            }

            // FPS visés : ONYX n'a pas de base de prédiction ; il mesure en jeu puis conseille un cran.
            Controls.Add(new Label { Text = "FPS visés :", Location = new Point(20, 130), Size = new Size(90, 24), ForeColor = FpsUi.Ink });
            _objectif = new NumericUpDown
            {
                Minimum = ObjectifFps.Min, Maximum = ObjectifFps.Max, Increment = 10,
                Location = new Point(112, 126), Size = new Size(84, 28),
                BackColor = FpsUi.Card, ForeColor = FpsUi.Ink, BorderStyle = BorderStyle.FixedSingle,
                Value = ObjectifEnregistre()
            };
            _objectif.ValueChanged += (s, e) => { EnregistreObjectif(); MajObstacle(); };
            Controls.Add(_objectif);
            _obstacle = new Label { Location = new Point(208, 122), Size = new Size(432, 44), ForeColor = FpsUi.Dim, Font = FpsUi.Small };
            Controls.Add(_obstacle);

            Controls.Add(new Label { Text = "Résolution :", Location = new Point(20, 176), Size = new Size(90, 24), ForeColor = FpsUi.Ink });
            _res = Liste(112, 172, 150);
            Controls.Add(new Label { Text = "Affichage :", Location = new Point(276, 176), Size = new Size(82, 24), ForeColor = FpsUi.Ink });
            _aff = Liste(360, 172, 220);

            _apercu = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true,
                Location = new Point(20, 216), Size = new Size(620, 230),
                BackColor = FpsUi.Card, ForeColor = FpsUi.Ink, BorderStyle = BorderStyle.FixedSingle, Font = FpsUi.Body,
                Text = "Choisis un mode, ou une résolution ou un affichage, pour voir ce qui changerait."
            };
            Controls.Add(_apercu);

            _appliquer = OnyxDesign.PrimaryButton("Appliquer");
            _appliquer.SetBounds(20, 460, 150, 36); _appliquer.Enabled = false;
            _appliquer.Click += (s, e) => Applique();
            Controls.Add(_appliquer);

            _retablir = OnyxDesign.Button("Rétablir");
            _retablir.SetBounds(180, 460, 130, 36);
            _retablir.Click += (s, e) => Retablit();
            Controls.Add(_retablir);

            _mesurer = OnyxDesign.Button("Mesurer en jeu");
            _mesurer.SetBounds(320, 460, 200, 36);
            _mesurer.Click += (s, e) => Mesure();
            Controls.Add(_mesurer);

            Button fermer = OnyxDesign.Button("Fermer");
            fermer.SetBounds(530, 460, 110, 36);
            fermer.Click += (s, e) => Close();
            Controls.Add(fermer);

            _etat = new Label { Location = new Point(20, 508), Size = new Size(620, 110), ForeColor = FpsUi.Dim, Font = FpsUi.Small };
            Controls.Add(_etat);

            FormClosing += (s, e) => _stop = true;
            MajBoutons();
            int? actuel;
            _lu = _jeu.Situation(out actuel, out _limite, out _vsync);
            MajObstacle();
            ChargeAffichage();
        }

        // ------------------------------------------------------------------ résolution et affichage

        private sealed class Element
        {
            public string Valeur, Texte;
            public override string ToString() { return Texte; }
        }

        private ComboBox Liste(int x, int y, int largeur)
        {
            var c = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(x, y), Size = new Size(largeur, 28),
                BackColor = FpsUi.Card, ForeColor = FpsUi.Ink, FlatStyle = FlatStyle.Flat, Enabled = false
            };
            c.SelectedIndexChanged += (s, e) => { if (!_chargeAffichage) Choisit(_mode); };
            Controls.Add(c);
            return c;
        }

        /// <summary>Lit le fichier du jeu et les modes de l'écran hors du fil d'interface, puis remplit les listes.</summary>
        private void ChargeAffichage()
        {
            _res.Enabled = _aff.Enabled = false;
            Task.Run(() =>
            {
                string raison = null; OptionsAffichage o = null;
                try { o = _jeu.Affichage(out raison); } catch (Exception ex) { raison = "Affichage illisible : " + ex.Message; }
                try { if (!IsDisposed) BeginInvoke((Action)(() => { if (!IsDisposed) RemplitAffichage(o, raison); })); } catch { }
            });
        }

        private void RemplitAffichage(OptionsAffichage o, string raison)
        {
            _options = o; _raisonAffichage = o == null ? raison : o.Remarque;
            _chargeAffichage = true;
            _res.Items.Clear(); _aff.Items.Clear();
            if (o != null && o.ResolutionActuelle != null && o.Resolutions.Count > 0)
            {
                _res.Items.Add(new Element { Texte = "Ne pas changer (" + AffichageJeu.Texte(o.ResolutionActuelle) + ")" });
                foreach (string x in o.Resolutions) _res.Items.Add(new Element { Valeur = x, Texte = AffichageJeu.Texte(x) });
            }
            else _res.Items.Add(new Element { Texte = "Non proposée" });
            if (o != null && o.ModeActuel != null && o.Modes.Count > 0)
            {
                _aff.Items.Add(new Element { Texte = "Ne pas changer (" + o.LibelleMode(o.ModeActuel) + ")" });
                foreach (KeyValuePair<string, string> m in o.Modes) _aff.Items.Add(new Element { Valeur = m.Key, Texte = m.Value });
            }
            else _aff.Items.Add(new Element { Texte = "Non proposé" });
            _res.SelectedIndex = 0; _aff.SelectedIndex = 0;
            _chargeAffichage = false;
            MajBoutons();
        }

        /// <summary>Résolution et mode choisis dans les listes ; null si rien n'est choisi.</summary>
        private ChoixAffichage Choix()
        {
            var a = new ChoixAffichage
            {
                Resolution = _res.SelectedItem is Element ? ((Element)_res.SelectedItem).Valeur : null,
                Mode = _aff.SelectedItem is Element ? ((Element)_aff.SelectedItem).Valeur : null
            };
            return a.Vide ? null : a;
        }

        // ------------------------------------------------------------------ FPS visés

        private int Objectif { get { return (int)_objectif.Value; } }

        private Dictionary<string, int> Objectifs()
        {
            try { string p = AppPaths.File(ObjectifFps.Fichier); return ObjectifFps.Lire(File.Exists(p) ? File.ReadAllText(p, Encoding.UTF8) : null); }
            catch { return ObjectifFps.Lire(null); }
        }

        private int ObjectifEnregistre()
        {
            int v;
            return Objectifs().TryGetValue(_jeu.Jeu, out v) ? v : ObjectifFps.Defaut;
        }

        private void EnregistreObjectif()
        {
            try
            {
                Dictionary<string, int> d = Objectifs();
                d[_jeu.Jeu] = Objectif;
                File.WriteAllText(AppPaths.File(ObjectifFps.Fichier), ObjectifFps.Ecrire(d), new UTF8Encoding(false));
            }
            catch (Exception ex) { JournalTechnique.Echec("objectif FPS", ex); }
        }

        /// <summary>Limite d'images du jeu sous l'objectif, ou synchro verticale : dit AVANT de mesurer.</summary>
        private void MajObstacle()
        {
            string o = _lu ? ObjectifFps.Obstacle(_jeu.Jeu, Objectif, _limite, _vsync) : null;
            _obstacle.ForeColor = o != null && _limite > 0 && _limite < Objectif ? FpsUi.Gold : FpsUi.Dim;
            _obstacle.Text = o ?? "Mesure en jeu, puis ONYX conseille un cran plus léger ou plus beau selon l'écart. Aucune prédiction sans mesure.";
        }

        /// <summary>Conseil d'un cran d'après la mesure ; null si le fichier du jeu est illisible.</summary>
        private ObjectifFps.Avis Conseil(Preuve.MesureJeu m)
        {
            int? mode;
            _lu = _jeu.Situation(out mode, out _limite, out _vsync);   // relu : le joueur a pu changer la limite en jeu
            MajObstacle();
            return _lu ? ObjectifFps.Juge(_jeu.Jeu, m.FpsMoy, m.Low1, Objectif, mode, _limite, _vsync, _jeu.NomMode) : null;
        }

        private void MajBoutons()
        {
            _retablir.Enabled = !_mesureEnCours && _jeu.ARetablir;
            _appliquer.Enabled = !_mesureEnCours && _choisi.HasValue;
            foreach (Button b in _modes) b.Enabled = !_mesureEnCours;
            _res.Enabled = !_mesureEnCours && _res.Items.Count > 1;
            _aff.Enabled = !_mesureEnCours && _aff.Items.Count > 1;
            bool avant = Preuve.LitJeu(LitMesure()) != null;
            _mesurer.Text = _mesureEnCours ? "Mesure en cours…" : avant ? "Mesurer en jeu (après)" : "Mesurer en jeu (avant)";
            _mesurer.Enabled = !_mesureEnCours;
        }

        private void Choisit(int m)
        {
            _mode = m;
            ChoixAffichage a = Choix();
            _choisi = null;
            if (m < 0 && a == null)
            {
                _apercu.Text = "Choisis un mode, ou une résolution ou un affichage, pour voir ce qui changerait."
                    + (_raisonAffichage != null ? "\r\n\r\n" + _raisonAffichage : "");
                MajBoutons(); return;
            }
            List<string> absentes; string raison;
            List<string> c = _jeu.Apercu(m, a, out absentes, out raison);
            if (c == null) { _apercu.Text = raison; MajBoutons(); return; }
            string quoi = m >= 0 ? "« " + _jeu.NomMode(m) + " »" + (a != null ? " et l'affichage choisi" : "") : "L'affichage choisi";
            var sb = new StringBuilder(quoi + " changerait :\r\n");
            if (c.Count == 0) sb.Append("  rien, le fichier est déjà réglé ainsi.");
            foreach (string x in c) sb.Append("  ").Append(x).Append("\r\n");
            if (absentes.Count > 0) sb.Append("\r\nAbsents ou inattendus dans le fichier, non touchés : ").Append(string.Join(", ", absentes)).Append('.');
            if (a != null && _raisonAffichage != null) sb.Append("\r\n\r\n").Append(_raisonAffichage);
            _apercu.Text = sb.ToString();
            if (c.Count > 0) _choisi = m;
            MajBoutons();
        }

        private void Applique()
        {
            if (!_choisi.HasValue) return;
            string r = _jeu.Applique(_choisi.Value, Choix());
            _apercu.Text = r;
            _choisi = null; _mode = -1;
            ChargeAffichage();   // relit les valeurs actuelles ; remet « Ne pas changer »
            _etat.Text = Preuve.LitJeu(LitMesure()) != null
                ? "Mesure « avant » enregistrée : lance " + _jeu.Jeu + ", mets-toi dans une scène comparable, puis « Mesurer en jeu (après) »."
                : "Pour chiffrer l'effet : « Rétablir », mesure « avant » en jeu, ferme le jeu, réapplique, puis mesure « après ».";
            MajBoutons();
        }

        private void Retablit()
        {
            _apercu.Text = _jeu.Retablit();
            _choisi = null; _mode = -1;
            ChargeAffichage();
        }

        // ------------------------------------------------------------------ mesure en jeu

        private string LitMesure()
        {
            try { string p = AppPaths.File(_jeu.FichierMesure); return File.Exists(p) ? File.ReadAllText(p) : null; } catch { return null; }
        }

        private void Log(string texte, int niveau)
        {
            try { if (!IsDisposed) BeginInvoke((Action)(() => { if (!IsDisposed) _etat.Text = texte; })); } catch { }
        }

        private void Mesure()
        {
            Preuve.MesureJeu avant = Preuve.LitJeu(LitMesure());
            _mesureEnCours = true; _stop = false;
            MajBoutons();
            Task.Run(() =>
            {
                Preuve.MesureJeu m = null;
                try { m = PreuveJeu.Mesure(Log, () => _stop, avant != null ? avant.Jeu : null); }
                catch (Exception ex) { Log("Erreur : " + ex.Message, 3); }
                try { if (!IsDisposed) BeginInvoke((Action)(() => FinMesure(avant, m))); } catch { }
            });
        }

        private void FinMesure(Preuve.MesureJeu avant, Preuve.MesureJeu m)
        {
            _mesureEnCours = false;
            if (m == null) { _etat.Text = "Mesure interrompue ou indisponible : rien n'est comparé."; MajBoutons(); return; }
            if (!Preuve.JeuValide(m)) { _etat.Text = "Mesure non retenue : trop courte ou trop peu d'images. Recommence."; MajBoutons(); return; }
            if (avant == null)
            {
                try { File.WriteAllText(AppPaths.File(_jeu.FichierMesure), Preuve.EcritJeu(m), new UTF8Encoding(false)); }
                catch (Exception ex) { _etat.Text = "Mesure « avant » non enregistrée : " + ex.Message; MajBoutons(); return; }
                ObjectifFps.Avis a = Conseil(m);
                _etat.Text = "Mesure « avant » : " + m.Resume() + "." + (a != null ? " " + a.Texte : "")
                           + " Ferme " + _jeu.Jeu + ", applique un mode, puis « Mesurer en jeu (après) ».";
                if (a != null && a.Mode.HasValue) Choisit(a.Mode.Value);   // aperçu du mode conseillé, rien d'écrit
            }
            else
            {
                Preuve.Verdict v = Preuve.JugeJeu(avant, m);
                _apercu.Text = "Avant : " + avant.Resume() + "\r\nAprès : " + m.Resume() + "\r\n" + Preuve.RapportJeu(avant, m)
                    + "\r\nVerdict : " + Preuve.Libelle(v);
                ObjectifFps.Avis a = Conseil(m);
                if (a != null) _apercu.AppendText("\r\n\r\nFPS visés (" + Objectif + ") : " + a.Texte);
                _etat.Text = "Comparaison terminée. « Rétablir » remet tes anciens réglages si le résultat ne te plaît pas.";
                try { File.Delete(AppPaths.File(_jeu.FichierMesure)); } catch { }
            }
            MajBoutons();
        }
    }
}
