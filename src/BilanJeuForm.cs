using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Fenêtre « Bilan de jeu » (bouton de Mes jeux) : résumé de <see cref="BilanJeu"/> sur 7 jours,
    /// 30 jours ou tout le journal, puis les jeux les plus joués en barres. Lecture seule.
    /// </summary>
    internal sealed class BilanJeuForm : Form
    {
        private const int Rang = 48;      // hauteur d'une ligne de jeu
        private const int MaxLignes = 6;

        private readonly List<JournalJeu.Partie> _parties;
        private readonly DateTime _maintenant;
        private readonly List<Button> _periodes = new List<Button>();
        private readonly Label _resume;
        private readonly BufferedPanel _jeux;
        private BilanJeu.Resultat _r;
        private int _jours = 7;
        private List<SuccesSteam.Deblocage> _succes;   // null tant que le cache Steam n'est pas lu
        private readonly Button _enregistre, _copie, _texte;
        private readonly List<string> _favoris;
        private readonly ContextMenuStrip _menuTexte = new ContextMenuStrip { Font = FpsUi.Body, ShowImageMargin = false };

        /// <param name="favoris">Noms des jeux favoris ★ de la bibliothèque, pour la carte postale.</param>
        public BilanJeuForm(List<JournalJeu.Partie> parties, DateTime maintenant, IEnumerable<string> favoris = null)
        {
            _parties = parties ?? new List<JournalJeu.Partie>();
            _favoris = favoris != null ? new List<string>(favoris) : new List<string>();
            _maintenant = maintenant;
            Text = "ONYX — Bilan de jeu";
            ClientSize = new Size(720, 566);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = FpsUi.BgMain; ForeColor = FpsUi.Ink; Font = FpsUi.Body;
            try { Icon = Logo.MakeIcon(32, FpsUi.Gold); } catch { }

            int x = 20;
            foreach (int j in new[] { 7, 30, 0 })
            {
                int jours = j;
                Button b = OnyxDesign.Button(j == 0 ? "Depuis le début" : j + " jours");
                b.SetBounds(x, 16, j == 0 ? 150 : 110, 34);
                b.Tag = jours;
                b.Click += (s, e) => Affiche(jours);
                Controls.Add(b); _periodes.Add(b);
                x += b.Width + 10;
            }

            _resume = new Label { Location = new Point(20, 62), Size = new Size(680, 100), ForeColor = FpsUi.Ink, Font = FpsUi.Body };
            Controls.Add(_resume);

            Controls.Add(new Label
            {
                Text = "JEUX LES PLUS JOUÉS", Location = new Point(20, 168), Size = new Size(680, 22),
                ForeColor = FpsUi.Dim, Font = FpsUi.Small
            });
            _jeux = new BufferedPanel { Location = new Point(20, 192), Size = new Size(680, Rang * MaxLignes + 20), BackColor = FpsUi.BgMain };
            _jeux.Paint += (s, e) => DessineJeux(e.Graphics);
            Controls.Add(_jeux);

            // Carte image à partager : enregistrée dans Images\ONYX ou copiée. Rien n'est envoyé.
            _enregistre = OnyxDesign.Button("Enregistrer l'image…");
            _enregistre.SetBounds(20, 520, 190, 34);
            _enregistre.Click += (s, e) => EnregistreImage();
            Controls.Add(_enregistre);
            _copie = OnyxDesign.Button("Copier l'image");
            _copie.SetBounds(220, 520, 150, 34);
            _copie.Click += (s, e) => CopieImage();
            Controls.Add(_copie);
            // Carte postale en texte : à coller sur un forum (BBCode), un site (HTML) ou ailleurs.
            _texte = OnyxDesign.Button("Copier le texte ▾");
            _texte.SetBounds(380, 520, 190, 34);
            _menuTexte.Items.Add("Code HTML (site, blog)", null, (s, e) => CopieTexte(CartePostale.Html(Postale()), "HTML copié ✓"));
            _menuTexte.Items.Add("BBCode (forum)", null, (s, e) => CopieTexte(CartePostale.BBCode(Postale()), "BBCode copié ✓"));
            _menuTexte.Items.Add("Texte simple", null, (s, e) => CopieTexte(CartePostale.Texte(Postale()), "Texte copié ✓"));
            _texte.Click += (s, e) => _menuTexte.Show(_texte, new Point(0, _texte.Height));
            Controls.Add(_texte);
            FormClosed += (s, e) => _menuTexte.Dispose();

            Button fermer = OnyxDesign.Button("Fermer");
            fermer.SetBounds(590, 520, 110, 34);
            fermer.Click += (s, e) => Close();
            Controls.Add(fermer);
            CancelButton = fermer;

            Affiche(7);
            // Succès Steam datés, lus dans le cache local de Steam : la ligne s'ajoute une fois lus.
            HandleCreated += (s, e) => Task.Run(() =>
            {
                List<SuccesSteam.Deblocage> l = SuccesSteam.ChargeTous();
                try { BeginInvoke((Action)(() => { if (IsDisposed) return; _succes = l; Affiche(_jours); })); } catch { }
            });
        }

        /// <summary>Pour le test d'interface : vrai une fois le cache Steam lu.</summary>
        internal bool SuccesLus { get { return _succes != null; } }
        internal string TexteResume { get { return _resume.Text; } }

        private void Affiche(int jours)
        {
            _jours = jours;
            _r = BilanJeu.Calcule(_parties, _maintenant, jours);
            foreach (Button b in _periodes)
            {
                bool actif = (int)b.Tag == jours;
                b.ForeColor = actif ? FpsUi.Gold : FpsUi.Dim;
                b.BackColor = actif ? FpsUi.BlendGold(FpsUi.Card, .1f) : FpsUi.Card;
                b.FlatAppearance.BorderSize = actif ? 1 : 0;
                b.FlatAppearance.BorderColor = actif ? FpsUi.GoldDim : FpsUi.Border;
            }
            List<string> lignes = BilanJeu.Resume(_r);
            string succes = SuccesSteam.LigneBilan(_succes, _maintenant, jours);
            if (succes != null) lignes.Add(succes);
            _resume.Text = string.Join("\r\n", lignes);
            _enregistre.Enabled = _copie.Enabled = _texte.Enabled = _r.Parties > 0;
            _jeux.Invalidate();
        }

        /// <summary>La carte de la période affichée (pour le test d'interface aussi).</summary>
        internal Bitmap Carte()
        {
            return BilanCarte.Dessine(_r, SuccesSteam.LigneBilan(_succes, _maintenant, _jours), _maintenant);
        }

        private void EnregistreImage()
        {
            string dossier = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            try { if (!string.IsNullOrEmpty(dossier)) dossier = System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dossier, "ONYX")).FullName; } catch { }
            using (var d = new SaveFileDialog
            {
                Title = "Enregistrer la carte du bilan", Filter = "Image PNG|*.png", DefaultExt = "png", AddExtension = true,
                FileName = BilanJeu.NomImage(_jours, _maintenant), InitialDirectory = dossier ?? ""
            })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try { using (Bitmap b = Carte()) b.Save(d.FileName, System.Drawing.Imaging.ImageFormat.Png); }
                catch (Exception ex) { MessageBox.Show(this, "Enregistrement impossible : " + ex.Message, "Bilan de jeu", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        /// <summary>Carte postale de la période affichée (pour le test d'interface aussi).</summary>
        internal CartePostale.Contenu Postale()
        {
            return CartePostale.Prepare(_r, SuccesSteam.LigneBilan(_succes, _maintenant, _jours), _favoris);
        }

        private void CopieTexte(string texte, string fait)
        {
            try
            {
                Clipboard.SetText(texte);
                _texte.Text = fait;
                var t = new Timer { Interval = 2000 };
                t.Tick += (s, e) => { t.Stop(); t.Dispose(); if (!_texte.IsDisposed) _texte.Text = "Copier le texte ▾"; };
                t.Start();
            }
            catch (Exception ex) { MessageBox.Show(this, "Copie impossible : " + ex.Message, "Bilan de jeu", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void CopieImage()
        {
            try
            {
                using (Bitmap b = Carte()) Clipboard.SetImage(b);
                _copie.Text = "Image copiée ✓";
                var t = new Timer { Interval = 2000 };
                t.Tick += (s, e) => { t.Stop(); t.Dispose(); if (!_copie.IsDisposed) _copie.Text = "Copier l'image"; };
                t.Start();
            }
            catch (Exception ex) { MessageBox.Show(this, "Copie impossible : " + ex.Message, "Bilan de jeu", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void DessineJeux(Graphics g)
        {
            if (_r == null || _r.Jeux.Count == 0) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int max = Math.Max(1, _r.Jeux[0].Secondes);
            int w = _jeux.Width - 4;
            int n = Math.Min(MaxLignes, _r.Jeux.Count);
            using (var fond = new SolidBrush(FpsUi.Card))
            using (var barre = new SolidBrush(FpsUi.Gold))
            {
                for (int i = 0; i < n; i++)
                {
                    BilanJeu.Ligne l = _r.Jeux[i];
                    int y = i * Rang;
                    TextRenderer.DrawText(g, l.Jeu, FpsUi.Body, new Rectangle(0, y, 300, 22), FpsUi.Ink,
                        TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    TextRenderer.DrawText(g, BilanJeu.Detail(l), FpsUi.Small, new Rectangle(300, y + 2, w - 300, 20), FpsUi.Dim,
                        TextFormatFlags.Right | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    var piste = new Rectangle(0, y + 26, w, 8);
                    g.FillRectangle(fond, piste);
                    int lg = Math.Max(4, (int)Math.Round((double)piste.Width * l.Secondes / max));
                    g.FillRectangle(barre, new Rectangle(piste.X, piste.Y, lg, piste.Height));
                }
                if (_r.Jeux.Count > n)
                    TextRenderer.DrawText(g, "+ " + (_r.Jeux.Count - n) + " autres jeux", FpsUi.Small,
                        new Rectangle(0, n * Rang, w, 20), FpsUi.Dim, TextFormatFlags.Left | TextFormatFlags.NoPrefix);
            }
        }
    }
}
