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
    /// Fenêtre des préréglages Fortnite (ouverte depuis la fiche du jeu) : un mode choisi montre
    /// d'abord « réglage : avant → après », rien n'est écrit avant « Appliquer ». « Mesurer en jeu »
    /// reprend la mesure de 60 s de PreuveJeu, avant puis après : aucun chiffre de FPS n'est
    /// annoncé sans elle.
    /// </summary>
    internal sealed class ReglagesFortniteForm : Form
    {
        private const string FichierMesure = "bt-reglages-fortnite-avant.txt";

        private readonly TextBox _apercu;
        private readonly Label _etat;
        private readonly Button _appliquer, _retablir, _mesurer;
        private readonly List<Button> _modes = new List<Button>();
        private ReglagesFortnite.Mode? _choisi;
        private volatile bool _stop, _mesureEnCours;

        public ReglagesFortniteForm()
        {
            Text = "ONYX — Préréglages Fortnite";
            ClientSize = new Size(660, 540);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = FpsUi.BgMain; ForeColor = FpsUi.Ink; Font = FpsUi.Body;
            try { Icon = Logo.MakeIcon(32, FpsUi.Gold); } catch { }

            Controls.Add(new Label
            {
                Text = "Écrit dans GameUserSettings.ini, Fortnite fermé. Les valeurs sont une proposition d'ONYX, pas une mesure : "
                     + "« Mesurer en jeu » dit ce qu'elles valent sur ton PC. Jamais touchés : résolution, limite d'images, plein écran, moteur de rendu, DLSS/TSR.",
                Location = new Point(20, 16), Size = new Size(620, 52), ForeColor = FpsUi.Dim, Font = FpsUi.Small
            });

            int x = 20;
            foreach (ReglagesFortnite.Mode m in new[] { ReglagesFortnite.Mode.Competition, ReglagesFortnite.Mode.Performance, ReglagesFortnite.Mode.Equilibre, ReglagesFortnite.Mode.Qualite })
            {
                ReglagesFortnite.Mode mode = m;
                Button b = OnyxDesign.Button(ReglagesFortnite.NomMode(m));
                b.SetBounds(x, 76, 148, 34);
                b.Click += (s, e) => Choisit(mode);
                Controls.Add(b); _modes.Add(b);
                x += 157;
            }

            _apercu = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true,
                Location = new Point(20, 124), Size = new Size(620, 230),
                BackColor = FpsUi.Card, ForeColor = FpsUi.Ink, BorderStyle = BorderStyle.FixedSingle, Font = FpsUi.Body,
                Text = "Choisis un mode pour voir ce qui changerait."
            };
            Controls.Add(_apercu);

            _appliquer = OnyxDesign.PrimaryButton("Appliquer");
            _appliquer.SetBounds(20, 368, 150, 36); _appliquer.Enabled = false;
            _appliquer.Click += (s, e) => Applique();
            Controls.Add(_appliquer);

            _retablir = OnyxDesign.Button("Rétablir");
            _retablir.SetBounds(180, 368, 130, 36);
            _retablir.Click += (s, e) => Retablit();
            Controls.Add(_retablir);

            _mesurer = OnyxDesign.Button("Mesurer en jeu");
            _mesurer.SetBounds(320, 368, 200, 36);
            _mesurer.Click += (s, e) => Mesure();
            Controls.Add(_mesurer);

            Button fermer = OnyxDesign.Button("Fermer");
            fermer.SetBounds(530, 368, 110, 36);
            fermer.Click += (s, e) => Close();
            Controls.Add(fermer);

            _etat = new Label { Location = new Point(20, 416), Size = new Size(620, 110), ForeColor = FpsUi.Dim, Font = FpsUi.Small };
            Controls.Add(_etat);

            FormClosing += (s, e) => _stop = true;
            MajBoutons();
        }

        private void MajBoutons()
        {
            _retablir.Enabled = !_mesureEnCours && ReglagesFortnite.Etat().Count > 0;
            _appliquer.Enabled = !_mesureEnCours && _choisi.HasValue;
            foreach (Button b in _modes) b.Enabled = !_mesureEnCours;
            bool avant = Preuve.LitJeu(LitMesure()) != null;
            _mesurer.Text = _mesureEnCours ? "Mesure en cours…" : avant ? "Mesurer en jeu (après)" : "Mesurer en jeu (avant)";
            _mesurer.Enabled = !_mesureEnCours;
        }

        private void Choisit(ReglagesFortnite.Mode m)
        {
            List<string> absentes; string raison;
            List<ReglagesFortnite.Changement> c = ReglagesFortnite.Apercu(m, out absentes, out raison);
            _choisi = null;
            if (c == null) { _apercu.Text = raison; MajBoutons(); return; }
            var sb = new StringBuilder("« " + ReglagesFortnite.NomMode(m) + " » changerait :\r\n");
            if (c.Count == 0) sb.Append("  rien, le fichier est déjà réglé ainsi.");
            foreach (ReglagesFortnite.Changement x in c) sb.Append("  ").Append(ReglagesFortnite.Ligne(x)).Append("\r\n");
            if (absentes.Count > 0) sb.Append("\r\nAbsents du fichier, non ajoutés : ").Append(string.Join(", ", absentes.ConvertAll(ReglagesFortnite.Libelle))).Append('.');
            _apercu.Text = sb.ToString();
            if (c.Count > 0) _choisi = m;
            MajBoutons();
        }

        private void Applique()
        {
            if (!_choisi.HasValue) return;
            string r = ReglagesFortnite.Applique(_choisi.Value);
            _apercu.Text = r;
            _choisi = null;
            _etat.Text = Preuve.LitJeu(LitMesure()) != null
                ? "Mesure « avant » enregistrée : lance Fortnite, mets-toi dans une scène comparable, puis « Mesurer en jeu (après) »."
                : "Pour chiffrer l'effet : « Rétablir », mesure « avant » en jeu, ferme le jeu, réapplique, puis mesure « après ».";
            MajBoutons();
        }

        private void Retablit()
        {
            _apercu.Text = ReglagesFortnite.Retablit();
            MajBoutons();
        }

        // ------------------------------------------------------------------ mesure en jeu

        private static string LitMesure()
        {
            try { string p = AppPaths.File(FichierMesure); return File.Exists(p) ? File.ReadAllText(p) : null; } catch { return null; }
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
                try { File.WriteAllText(AppPaths.File(FichierMesure), Preuve.EcritJeu(m), new UTF8Encoding(false)); }
                catch (Exception ex) { _etat.Text = "Mesure « avant » non enregistrée : " + ex.Message; MajBoutons(); return; }
                _etat.Text = "Mesure « avant » : " + m.Resume() + ". Ferme Fortnite, applique un mode, puis « Mesurer en jeu (après) ».";
            }
            else
            {
                Preuve.Verdict v = Preuve.JugeJeu(avant, m);
                _apercu.Text = "Avant : " + avant.Resume() + "\r\nAprès : " + m.Resume() + "\r\n" + Preuve.RapportJeu(avant, m)
                    + "\r\nVerdict : " + Preuve.Libelle(v);
                _etat.Text = "Comparaison terminée. « Rétablir » remet tes anciens réglages si le résultat ne te plaît pas.";
                try { File.Delete(AppPaths.File(FichierMesure)); } catch { }
            }
            MajBoutons();
        }
    }
}
