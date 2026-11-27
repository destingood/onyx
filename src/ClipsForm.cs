using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Bibliothèque des clips : les vidéos de Captures, Vidéos et des dossiers ajoutés, rangées par
    /// jeu. Lire, afficher dans le dossier, copier (se colle dans Discord), renommer, mettre à la
    /// Corbeille après confirmation. Limite d'espace : ONYX propose d'envoyer les plus anciens à la
    /// Corbeille, jamais sans confirmation. Aucune suppression définitive.
    /// </summary>
    internal sealed class ClipsForm : Form
    {
        private const int VigL = 192, VigH = 108;

        private readonly ListBox _jeux;
        private readonly ListView _liste;
        private readonly ImageList _images;
        private readonly Label _etat;
        private readonly Button _lire, _dossier, _copier, _renommer, _corbeille, _actualiser, _ajouter, _liberer;
        private readonly ComboBox _limite, _tri;
        /// <summary>Ordre choisi, gardé tant qu'ONYX reste ouvert.</summary>
        private static int _ordre;
        private readonly Label _limiteTitre, _espace;
        private List<Clips.Clip> _enTrop = new List<Clips.Clip>();
        private string _apres;   // message à montrer une fois la liste rechargée
        private bool _pret;
        private readonly string _jeuVoulu;
        private List<Clips.Clip> _tous = new List<Clips.Clip>();
        private readonly List<string> _jeuxAffiches = new List<string>();
        private int _genCharge, _genVig;
        private volatile bool _ferme;

        public ClipsForm() : this(null) { }

        public ClipsForm(string jeu)
        {
            _jeuVoulu = jeu;
            Text = "ONYX — Clips";
            ClientSize = new Size(960, 640);
            MinimumSize = new Size(760, 480);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = FpsUi.BgMain; ForeColor = FpsUi.Ink; Font = FpsUi.Body;
            try { Icon = Logo.MakeIcon(32, FpsUi.Gold); } catch { }

            Controls.Add(Haut = new Panel { BackColor = FpsUi.BgMain });
            Haut.Controls.Add(new Label
            {
                Text = "Tes captures, rangées par jeu (dossier du jeu, sinon nom du fichier). Dossiers lus : Captures de Windows, Vidéos, "
                     + "et ceux que tu ajoutes. Rien n'est supprimé définitivement : « Corbeille » se rattrape depuis la Corbeille de Windows.",
                Location = new Point(0, 0), Size = new Size(900, 40), ForeColor = FpsUi.Dim, Font = FpsUi.Small, Name = "clips-note"
            });

            _jeux = new ListBox { BackColor = FpsUi.Card, ForeColor = FpsUi.Ink, BorderStyle = BorderStyle.FixedSingle, Font = FpsUi.Body, IntegralHeight = false };
            _jeux.AccessibleName = "Jeux";
            _jeux.SelectedIndexChanged += (s, e) => Remplit();
            Controls.Add(_jeux);
            _tri = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, BackColor = FpsUi.Card, ForeColor = FpsUi.Ink, FlatStyle = FlatStyle.Flat, Font = FpsUi.Small };
            _tri.AccessibleName = "Ordre des clips";
            _tri.Items.AddRange(Clips.Ordres);
            _tri.SelectedIndex = _ordre >= 0 && _ordre < Clips.Ordres.Length ? _ordre : 0;
            _tri.SelectedIndexChanged += (s, e) => { _ordre = _tri.SelectedIndex; if (_pret) Remplit(); };
            Controls.Add(_tri);

            _images = new ImageList { ImageSize = new Size(VigL, VigH), ColorDepth = ColorDepth.Depth32Bit };
            _liste = new ListView
            {
                View = View.LargeIcon, LargeImageList = _images, MultiSelect = true, HideSelection = false,
                BackColor = FpsUi.Card, ForeColor = FpsUi.Ink, BorderStyle = BorderStyle.FixedSingle, Font = FpsUi.Small
            };
            _liste.AccessibleName = "Clips";
            _liste.SelectedIndexChanged += (s, e) => MajBoutons();
            _liste.ItemActivate += (s, e) => Lire();
            _liste.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete) Corbeille();
                else if (e.KeyCode == Keys.F2) Renommer();
                else if (e.Control && e.KeyCode == Keys.C) Copier();
                else if (e.Control && e.KeyCode == Keys.A) ToutSelectionner();
            };
            Controls.Add(_liste);

            _lire = Bouton("Lire", (s, e) => Lire());
            _dossier = Bouton("Afficher dans le dossier", (s, e) => Dossier());
            _copier = Bouton("Copier", (s, e) => Copier());
            _renommer = Bouton("Renommer…", (s, e) => Renommer());
            _corbeille = Bouton("Corbeille…", (s, e) => Corbeille());
            _actualiser = Bouton("Actualiser", (s, e) => Charge());
            _ajouter = Bouton("Ajouter un dossier…", (s, e) => Ajoute());

            // Limite d'espace : un choix, l'espace occupé, et le bouton qui libère (visible si dépassée).
            _limiteTitre = new Label { Text = "Limite d'espace :", ForeColor = FpsUi.Dim, Font = FpsUi.Small, TextAlign = ContentAlignment.MiddleLeft };
            Controls.Add(_limiteTitre);
            _limite = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, BackColor = FpsUi.Card, ForeColor = FpsUi.Ink, FlatStyle = FlatStyle.Flat, Font = FpsUi.Small };
            _limite.AccessibleName = "Limite d'espace des clips";
            int choisie = Clips.LimiteGo();
            var valeurs = new List<int>(Clips.LimitesGo);
            if (!valeurs.Contains(choisie)) { valeurs.Add(choisie); valeurs.Sort(); }   // valeur écrite à la main dans le fichier
            foreach (int go in valeurs) _limite.Items.Add(go == 0 ? "Aucune" : go + " Go");
            _limite.Tag = valeurs;
            _limite.SelectedIndex = Math.Max(0, valeurs.IndexOf(choisie));
            _limite.SelectedIndexChanged += (s, e) => ChangeLimite();
            Controls.Add(_limite);
            _espace = new Label { ForeColor = FpsUi.Dim, Font = FpsUi.Small, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
            Controls.Add(_espace);
            _liberer = Bouton("Libérer…", (s, e) => Libere());
            _liberer.Visible = false;

            _etat = new Label { ForeColor = FpsUi.Dim, Font = FpsUi.Small, AutoEllipsis = true };
            Controls.Add(_etat);

            Resize += (s, e) => Place();
            FormClosed += (s, e) => { _ferme = true; foreach (Image i in _images.Images) i.Dispose(); };
            Shown += (s, e) => Charge();
            FormClosing += (s, e) => _stop = true;
            ConstruitCapture();
            Place();
            MajBoutons();
            _pret = true;
        }

        /// <summary>Bandeau du haut (note, puis réglages de capture) : la liste commence sous lui.</summary>
        private readonly Panel Haut;
        private const int HautH = 150;

        private Button Bouton(string texte, EventHandler clic)
        {
            Button b = OnyxDesign.Button(texte);
            b.Click += clic;
            Controls.Add(b);
            return b;
        }

        private void Place()
        {
            int W = ClientSize.Width, H = ClientSize.Height, m = 20;
            Haut.SetBounds(m, 14, W - 2 * m, HautH);
            foreach (Control c in Haut.Controls) if (c.Name == "clips-note") c.Width = Haut.Width;
            PlaceCapture();
            int y = 14 + HautH + 10, bas = H - 138;
            _jeux.SetBounds(m, y, 200, bas - y - 34);
            _tri.SetBounds(m, bas - 26, 200, 26);
            _liste.SetBounds(m + 212, y, W - 2 * m - 212, bas - y);
            int x = m, by = H - 82;
            foreach (Button b in new[] { _lire, _dossier, _copier, _renommer, _corbeille })
            {
                int w = TextRenderer.MeasureText(b.Text, b.Font).Width + 36;
                b.SetBounds(x, by, w, 34); x += w + 10;
            }
            // Ligne du dessus : limite d'espace à gauche, dossiers à droite.
            int ly = H - 126, xr = W - m;
            foreach (Button b in new[] { _ajouter, _actualiser })
            {
                int w = TextRenderer.MeasureText(b.Text, b.Font).Width + 36;
                xr -= w; b.SetBounds(xr, ly, w, 34); xr -= 10;
            }
            _limiteTitre.SetBounds(m, ly, 104, 34);
            _limite.SetBounds(m + 106, ly + 5, 96, 26);
            int lx = m + 212;
            if (_liberer.Visible)
            {
                int w = TextRenderer.MeasureText(_liberer.Text, _liberer.Font).Width + 36;
                _liberer.SetBounds(lx, ly, w, 34); lx += w + 10;
            }
            _espace.SetBounds(lx, ly, Math.Max(0, xr - lx), 34);
            _etat.SetBounds(m, H - 38, W - 2 * m, 20);
        }

        // ------------------------------------------------------------------ chargement

        private void Charge()
        {
            int gen = ++_genCharge;
            _actualiser.Enabled = false;
            _etat.Text = "Recherche des clips…";
            Task.Run(() =>
            {
                List<string> racines = Clips.Racines();
                List<Clips.Clip> l = Clips.Balaye(racines, Clips.MaxFichiers);
                try { if (!_ferme) BeginInvoke((Action)(() => Charge(gen, racines, l))); } catch { }
            });
        }

        private void Charge(int gen, List<string> racines, List<Clips.Clip> l)
        {
            if (gen != _genCharge || _ferme) return;
            _tous = l;
            _actualiser.Enabled = true;
            string avant = _jeux.SelectedIndex > 0 && _jeux.SelectedIndex < _jeuxAffiches.Count ? _jeuxAffiches[_jeux.SelectedIndex] : _jeuVoulu;

            var parJeu = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (Clips.Clip c in l) { int n; parJeu.TryGetValue(c.Jeu, out n); parJeu[c.Jeu] = n + 1; }
            _jeuxAffiches.Clear(); _jeuxAffiches.Add(null);
            _jeux.BeginUpdate(); _jeux.Items.Clear();
            _jeux.Items.Add("Tous (" + l.Count + ")");
            foreach (var kv in parJeu) if (kv.Key != Clips.Autres) { _jeuxAffiches.Add(kv.Key); _jeux.Items.Add(kv.Key + " (" + kv.Value + ")"); }
            if (parJeu.ContainsKey(Clips.Autres)) { _jeuxAffiches.Add(Clips.Autres); _jeux.Items.Add(Clips.Autres + " (" + parJeu[Clips.Autres] + ")"); }
            int sel = 0;
            if (avant != null) { int i = _jeuxAffiches.FindIndex(j => j != null && string.Equals(j, avant, StringComparison.OrdinalIgnoreCase)); if (i > 0) sel = i; }
            _jeux.EndUpdate();
            _jeux.SelectedIndex = sel;   // déclenche Remplit

            string texte = l.Count == 0 ? "Aucun clip trouvé dans : " + string.Join(", ", racines) + "."
                : l.Count + (l.Count > 1 ? " clips" : " clip") + (l.Count >= Clips.MaxFichiers ? " (limite de " + Clips.MaxFichiers + " atteinte)" : "")
                  + " dans " + racines.Count + (racines.Count > 1 ? " dossiers." : " dossier.");
            if (_jeuVoulu != null && sel == 0 && l.Count > 0) texte += " Aucun clip rangé sous « " + _jeuVoulu + " » : tout est affiché.";
            _etat.Text = _apres ?? texte;
            _apres = null;
            MajEspace();
        }

        // ------------------------------------------------------------------ limite d'espace

        private int LimiteChoisie()
        {
            var valeurs = (List<int>)_limite.Tag;
            return _limite.SelectedIndex >= 0 && _limite.SelectedIndex < valeurs.Count ? valeurs[_limite.SelectedIndex] : 0;
        }

        private void ChangeLimite()
        {
            if (!_pret) return;
            try { Clips.EcritLimite(LimiteChoisie()); }
            catch (Exception ex) { _etat.Text = "Limite non enregistrée : " + ex.Message; }
            MajEspace();
        }

        /// <summary>Espace occupé par les clips trouvés, et ce qu'il faudrait retirer pour tenir la limite.</summary>
        private void MajEspace()
        {
            int go = LimiteChoisie();
            long total = Clips.Total(_tous);
            _enTrop = Clips.EnTrop(_tous, go * (1L << 30));
            long octets = Clips.Total(_enTrop);
            string texte = _tous.Count == 0 ? "" : "Tes clips occupent " + Clips.TailleLisible(total) + ".";
            if (_enTrop.Count > 0)
                texte = Clips.TailleLisible(total) + " occupés, limite dépassée : " + _enTrop.Count
                      + (_enTrop.Count > 1 ? " clips les plus anciens" : " clip le plus ancien") + " à retirer.";
            _espace.Text = texte;
            _espace.ForeColor = _enTrop.Count > 0 ? FpsUi.Gold : FpsUi.Dim;
            _liberer.Text = "Libérer " + Clips.TailleLisible(octets) + "…";
            _liberer.Visible = _enTrop.Count > 0;
            Place();
        }

        /// <summary>Envoie à la Corbeille les clips les plus anciens qui dépassent la limite, après confirmation.</summary>
        private void Libere()
        {
            if (_enTrop.Count == 0) return;
            var lot = new List<Clips.Clip>(_enTrop);
            var sb = new System.Text.StringBuilder();
            sb.Append("Limite d'espace : ").Append(LimiteChoisie()).Append(" Go. Pour repasser dessous, mettre à la Corbeille ")
              .Append(lot.Count > 1 ? "les " + lot.Count + " clips les plus anciens" : "le clip le plus ancien")
              .Append(" (").Append(Clips.TailleLisible(Clips.Total(lot))).Append(") ?\n\n");
            for (int i = 0; i < lot.Count && i < 12; i++)
                sb.Append(lot[i].Date.ToString("dd/MM/yyyy")).Append("  ").Append(Path.GetFileName(lot[i].Chemin)).Append("\n");
            if (lot.Count > 12) sb.Append("… et ").Append(lot.Count - 12).Append(lot.Count - 12 > 1 ? " autres.\n" : " autre.\n");
            sb.Append("\nTu peux les restaurer depuis la Corbeille de Windows tant qu'elle n'est pas vidée.");
            if (MessageBox.Show(this, sb.ToString(), "ONYX — Clips", MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
            var chemins = lot.ConvertAll(x => x.Chemin);
            bool ok = Storage.RecycleFiles(chemins, Handle);
            int restants = chemins.FindAll(File.Exists).Count;
            _apres = ok && restants == 0 ? (lot.Count > 1 ? lot.Count + " clips" : "1 clip") + " à la Corbeille."
                                         : "Corbeille : " + (chemins.Count - restants) + " sur " + chemins.Count + " déplacés.";
            Charge();
        }

        private void ToutSelectionner()
        {
            _liste.BeginUpdate();
            foreach (ListViewItem it in _liste.Items) it.Selected = true;
            _liste.EndUpdate();
            MajBoutons();
        }

        private void Remplit()
        {
            string jeu = _jeux.SelectedIndex > 0 && _jeux.SelectedIndex < _jeuxAffiches.Count ? _jeuxAffiches[_jeux.SelectedIndex] : null;
            List<Clips.Clip> l = Clips.Trie(_tous, jeu, _tri.SelectedIndex);
            _liste.BeginUpdate();
            _liste.Items.Clear();
            foreach (Image i in _images.Images) i.Dispose();
            _images.Images.Clear();
            foreach (Clips.Clip c in l)
            {
                var it = new ListViewItem(Path.GetFileNameWithoutExtension(c.Chemin) + "\n" + c.Date.ToString("dd/MM/yyyy HH:mm") + " · " + Clips.TailleLisible(c.Octets));
                it.Tag = c; it.ToolTipText = c.Chemin;
                _liste.Items.Add(it);
            }
            _liste.ShowItemToolTips = true;
            _liste.EndUpdate();
            MajBoutons();
            Vignettes(++_genVig, l);
        }

        /// <summary>Vignettes de l'Explorateur, une par une sur un fil STA dédié (exigence du Shell).</summary>
        private void Vignettes(int gen, List<Clips.Clip> l)
        {
            var t = new Thread(() =>
            {
                for (int i = 0; i < l.Count && !_ferme && gen == _genVig; i++)
                {
                    Bitmap b = Clips.Vignette(l[i].Chemin, VigL, VigH);
                    if (b == null) continue;
                    int index = i;
                    try
                    {
                        BeginInvoke((Action)(() =>
                        {
                            if (_ferme || gen != _genVig || index >= _liste.Items.Count) { b.Dispose(); return; }
                            _images.Images.Add(Cadre(b)); b.Dispose();
                            _liste.Items[index].ImageIndex = _images.Images.Count - 1;
                        }));
                    }
                    catch { b.Dispose(); return; }
                }
            });
            t.IsBackground = true;
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }

        /// <summary>Centre la vignette (format libre) dans la case 16:9 de la liste.</summary>
        private static Bitmap Cadre(Bitmap b)
        {
            var r = new Bitmap(VigL, VigH);
            using (Graphics g = Graphics.FromImage(r))
            {
                g.Clear(FpsUi.BgMain);
                double k = Math.Min(VigL / (double)b.Width, VigH / (double)b.Height);
                int w = (int)(b.Width * k), h = (int)(b.Height * k);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(b, new Rectangle((VigL - w) / 2, (VigH - h) / 2, w, h));
            }
            return r;
        }

        // ------------------------------------------------------------------ actions

        private List<Clips.Clip> Choisis()
        {
            var res = new List<Clips.Clip>();
            foreach (ListViewItem it in _liste.SelectedItems) res.Add((Clips.Clip)it.Tag);
            return res;
        }

        private void MajBoutons()
        {
            int n = _liste.SelectedItems.Count;
            _lire.Enabled = n == 1; _dossier.Enabled = n == 1; _renommer.Enabled = n == 1;
            _copier.Enabled = n > 0; _corbeille.Enabled = n > 0;
        }

        private void Lire()
        {
            List<Clips.Clip> c = Choisis();
            if (c.Count != 1) return;
            try { Process.Start(new ProcessStartInfo(c[0].Chemin) { UseShellExecute = true }); }
            catch (Exception ex) { _etat.Text = "Lecture impossible : " + ex.Message; }
        }

        private void Dossier()
        {
            List<Clips.Clip> c = Choisis();
            if (c.Count == 1) Storage.RevealFile(c[0].Chemin);
        }

        private void Copier()
        {
            List<Clips.Clip> c = Choisis();
            if (c.Count == 0) return;
            var f = new StringCollection();
            foreach (Clips.Clip x in c) if (File.Exists(x.Chemin)) f.Add(x.Chemin);
            if (f.Count == 0) { _etat.Text = "Fichier introuvable : « Actualiser »."; return; }
            try
            {
                Clipboard.SetFileDropList(f);
                _etat.Text = (f.Count > 1 ? f.Count + " fichiers copiés" : "Fichier copié") + " : colle-le (Ctrl+V) dans Discord ou un dossier.";
            }
            catch (Exception ex) { _etat.Text = "Copie impossible : " + ex.Message; }
        }

        /// <summary>Renomme le clip choisi (F2) : même dossier, même extension, jamais d'écrasement.</summary>
        private void Renommer()
        {
            List<Clips.Clip> c = Choisis();
            if (c.Count != 1) return;
            Clips.Clip clip = c[0];
            string ext = Path.GetExtension(clip.Chemin);
            // Rangé d'après son nom (pas d'après un dossier) : le dire, sinon le clip change de jeu sans prévenir.
            bool parNom = clip.Racine != null && clip.Jeu != Clips.Autres
                && Clips.JeuDepuisChemin(clip.Racine, Path.Combine(Path.GetDirectoryName(clip.Chemin) ?? "", "x" + ext)) == Clips.Autres;
            string saisi = SaisieTexte.Demande(this, "ONYX — Renommer le clip",
                "Nouveau nom (l'extension " + ext + " ne change pas)."
                    + (parNom ? " Ce clip est rangé sous « " + clip.Jeu + " » d'après son nom : garde le nom du jeu et la date pour qu'il y reste." : ""),
                Path.GetFileNameWithoutExtension(clip.Chemin),
                t => { string raison; return Clips.NomPropre(t, ext, out raison) == null ? raison : null; }, Clips.NomMax + 8);
            if (saisi == null) return;
            string erreur, nom = Clips.NomPropre(saisi, ext, out erreur);
            if (nom == null) { _etat.Text = erreur; return; }
            string nouveau = Clips.Renomme(clip.Chemin, nom, out erreur);
            if (nouveau == null) { _etat.Text = "Renommage impossible : " + erreur; return; }
            if (nouveau == clip.Chemin) return;
            string jeu = clip.Racine != null ? Clips.JeuDepuisChemin(clip.Racine, nouveau) : clip.Jeu;
            _apres = "Renommé en « " + Path.GetFileName(nouveau) + " »."
                + (!string.Equals(jeu, clip.Jeu, StringComparison.OrdinalIgnoreCase) ? " Il est maintenant rangé sous « " + jeu + " »." : "");
            Charge();
        }

        private void Corbeille()
        {
            List<Clips.Clip> c = Choisis();
            if (c.Count == 0) return;
            long octets = 0; foreach (Clips.Clip x in c) octets += x.Octets;
            string quoi = c.Count == 1 ? "« " + Path.GetFileName(c[0].Chemin) + " »" : c.Count + " clips";
            if (MessageBox.Show(this, "Mettre " + quoi + " (" + Clips.TailleLisible(octets) + ") à la Corbeille ?\n\nTu peux les restaurer depuis la Corbeille de Windows tant qu'elle n'est pas vidée.",
                    "ONYX — Clips", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            var chemins = c.ConvertAll(x => x.Chemin);
            bool ok = Storage.RecycleFiles(chemins, Handle);
            int restants = chemins.FindAll(File.Exists).Count;
            _apres = ok && restants == 0 ? quoi + " à la Corbeille." : "Corbeille : " + (chemins.Count - restants) + " sur " + chemins.Count + " déplacés.";
            Charge();
        }

        // ------------------------------------------------------------------ capture des 30 dernières secondes

        private const string FichierMesure = "bt-clips-fond-avant.txt";
        private Label _capEtat, _capAide;
        private Button _capAction, _capMesure;
        private volatile bool _stop, _occupe;

        private void ConstruitCapture()
        {
            Haut.Controls.Add(new Label { Text = "ENREGISTRER CE QUI S'EST PASSÉ", Name = "cap-titre", ForeColor = FpsUi.Gold, Font = FpsUi.Small, AutoSize = false });
            _capEtat = new Label { ForeColor = FpsUi.Ink, Font = FpsUi.Small, AutoSize = false };
            _capAide = new Label { ForeColor = FpsUi.Dim, Font = FpsUi.Small, AutoSize = false };
            _capAction = OnyxDesign.Button("Activer");
            _capAction.Click += (s, e) => ActionCapture();
            _capMesure = OnyxDesign.Button("Mesurer le coût en jeu");
            _capMesure.Click += (s, e) => MesureCout();
            Haut.Controls.Add(_capEtat); Haut.Controls.Add(_capAide); Haut.Controls.Add(_capAction); Haut.Controls.Add(_capMesure);
            MajCapture();
        }

        private void PlaceCapture()
        {
            if (_capEtat == null) return;
            int w = Haut.Width, bw = 250;
            foreach (Control c in Haut.Controls) if (c.Name == "cap-titre") c.SetBounds(0, 48, w - bw - 12, 18);
            _capEtat.SetBounds(0, 68, w - bw - 12, 44);
            _capAide.SetBounds(0, 114, w - bw - 12, 34);
            _capAction.SetBounds(w - bw, 52, bw, 34);
            _capMesure.SetBounds(w - bw, 94, bw, 34);
        }

        private static Tweak Reglage(string id) { return Catalog.All().Find(x => x.Id == id); }

        private void MajCapture()
        {
            CaptureFond.Etat e = CaptureFond.Lit();
            List<string> bloqueurs = CaptureFond.BloqueursAppliques();
            bool parOnyx = CaptureFond.AppliqueParOnyx();
            _capEtat.Text = CaptureFond.Explique(e, bloqueurs.Count > 0);
            if (bloqueurs.Count > 0) _capAction.Text = "Rétablir « Désactiver Game DVR »";
            else if (e == CaptureFond.Etat.Active) _capAction.Text = parOnyx ? "Rétablir" : "Déjà active";
            else if (e == CaptureFond.Etat.BloqueeParStrategie) _capAction.Text = "Bloquée";
            else _capAction.Text = "Activer";
            _capAction.Enabled = !_occupe && (bloqueurs.Count > 0 || e == CaptureFond.Etat.Coupee || (e == CaptureFond.Etat.Active && parOnyx));
            bool avant = Preuve.LitJeu(LitMesure()) != null;
            _capMesure.Text = _occupe ? "Patiente…" : avant ? "Mesurer le coût (après)" : "Mesurer le coût (avant)";
            _capMesure.Enabled = !_occupe;
            _capAide.Text = "Raccourci : Win+Alt+G enregistre les 30 dernières secondes, Win+Alt+R lance ou arrête un enregistrement."
                + (CaptureFond.NvidiaApp() ? " Relecture instantanée NVIDIA : Alt+Z puis Alt+F10 (réglée dans l'appli NVIDIA, ONYX ne la pilote pas)." : "");
        }

        private void ActionCapture()
        {
            List<string> bloqueurs = CaptureFond.BloqueursAppliques();
            var lot = new List<Tweak>();
            bool appliquer;
            if (bloqueurs.Count > 0)
            {
                if (MessageBox.Show(this, "Rétablir « Désactiver Game DVR » : la Game Bar pourra de nouveau capturer, et ce réglage ne sera plus appliqué. Continuer ?",
                        "ONYX — Clips", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                foreach (string id in bloqueurs) { Tweak b = Reglage(id); if (b != null) lot.Add(b); }
                appliquer = false;
            }
            else
            {
                Tweak t = Reglage(CaptureFond.IdReglage);
                if (t == null) return;
                lot.Add(t);
                appliquer = CaptureFond.Lit() != CaptureFond.Etat.Active;
            }
            if (lot.Count == 0) return;
            _occupe = true; MajCapture();
            var journal = new List<string>();
            Task.Run(() =>
            {
                EngineResult r = null;
                try { r = Engine.Run(lot, appliquer, true, false, (m, n) => { if (n >= 2) lock (journal) journal.Add(m); }); }
                catch (Exception ex) { lock (journal) journal.Add(ex.Message); }
                try
                {
                    if (!_ferme) BeginInvoke((Action)(() =>
                    {
                        _occupe = false;
                        MajCapture();
                        _etat.Text = r != null && r.Ko == 0 && !r.PrepFailed
                            ? (appliquer ? "Capture allumée : " : "Rétabli : ") + string.Join(", ", lot.ConvertAll(x => x.Name)) + "."
                            : "Rien n'a changé : " + string.Join(" ", journal);
                    }));
                }
                catch { }
            });
        }

        private static string LitMesure()
        {
            try { string p = AppPaths.File(FichierMesure); return File.Exists(p) ? File.ReadAllText(p) : null; } catch { return null; }
        }

        private void Log(string texte, int niveau)
        {
            try { if (!_ferme) BeginInvoke((Action)(() => { if (!_ferme) _etat.Text = texte; })); } catch { }
        }

        /// <summary>Mesure de 60 s en jeu (PreuveJeu), avant puis après : seul moyen de chiffrer le coût.</summary>
        private void MesureCout()
        {
            Preuve.MesureJeu avant = Preuve.LitJeu(LitMesure());
            _occupe = true; _stop = false; MajCapture();
            Task.Run(() =>
            {
                Preuve.MesureJeu m = null;
                try { m = PreuveJeu.Mesure(Log, () => _stop, avant != null ? avant.Jeu : null); }
                catch (Exception ex) { Log("Erreur : " + ex.Message, 3); }
                try { if (!_ferme) BeginInvoke((Action)(() => FinMesure(avant, m))); } catch { }
            });
        }

        private void FinMesure(Preuve.MesureJeu avant, Preuve.MesureJeu m)
        {
            _occupe = false;
            if (m == null) _etat.Text = "Mesure interrompue ou indisponible : rien n'est comparé.";
            else if (!Preuve.JeuValide(m)) _etat.Text = "Mesure non retenue : trop courte ou trop peu d'images. Recommence.";
            else if (avant == null)
            {
                try
                {
                    File.WriteAllText(AppPaths.File(FichierMesure), Preuve.EcritJeu(m), new System.Text.UTF8Encoding(false));
                    _etat.Text = "Mesure « avant » : " + m.Resume() + ". Change l'état de la capture, puis « Mesurer le coût (après) » dans une scène comparable.";
                }
                catch (Exception ex) { _etat.Text = "Mesure « avant » non enregistrée : " + ex.Message; }
            }
            else
            {
                Preuve.Verdict v = Preuve.JugeJeu(avant, m);
                MessageBox.Show(this, "Avant : " + avant.Resume() + "\nAprès : " + m.Resume() + "\n" + Preuve.RapportJeu(avant, m) + "\nVerdict : " + Preuve.Libelle(v),
                    "ONYX — Coût de la capture", MessageBoxButtons.OK, MessageBoxIcon.Information);
                _etat.Text = "Comparaison terminée.";
                try { File.Delete(AppPaths.File(FichierMesure)); } catch { }
            }
            MajCapture();
        }

        private void Ajoute()
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Dossier où ton logiciel range ses clips (OBS, NVIDIA, Medal…)";
                dlg.ShowNewFolderButton = false;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try { Clips.AjouteDossier(dlg.SelectedPath); }
                catch (Exception ex) { _etat.Text = "Dossier non enregistré : " + ex.Message; return; }
            }
            Charge();
        }
    }
}
