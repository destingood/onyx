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
    /// jeu. Lire, afficher dans le dossier, copier (se colle dans Discord), mettre à la Corbeille
    /// après confirmation. Aucune suppression définitive.
    /// </summary>
    internal sealed class ClipsForm : Form
    {
        private const int VigL = 192, VigH = 108;

        private readonly ListBox _jeux;
        private readonly ListView _liste;
        private readonly ImageList _images;
        private readonly Label _etat;
        private readonly Button _lire, _dossier, _copier, _corbeille, _actualiser, _ajouter;
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

            _images = new ImageList { ImageSize = new Size(VigL, VigH), ColorDepth = ColorDepth.Depth32Bit };
            _liste = new ListView
            {
                View = View.LargeIcon, LargeImageList = _images, MultiSelect = true, HideSelection = false,
                BackColor = FpsUi.Card, ForeColor = FpsUi.Ink, BorderStyle = BorderStyle.FixedSingle, Font = FpsUi.Small
            };
            _liste.AccessibleName = "Clips";
            _liste.SelectedIndexChanged += (s, e) => MajBoutons();
            _liste.ItemActivate += (s, e) => Lire();
            _liste.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) Corbeille(); else if (e.Control && e.KeyCode == Keys.C) Copier(); };
            Controls.Add(_liste);

            _lire = Bouton("Lire", (s, e) => Lire());
            _dossier = Bouton("Afficher dans le dossier", (s, e) => Dossier());
            _copier = Bouton("Copier le fichier", (s, e) => Copier());
            _corbeille = Bouton("Corbeille…", (s, e) => Corbeille());
            _actualiser = Bouton("Actualiser", (s, e) => Charge());
            _ajouter = Bouton("Ajouter un dossier…", (s, e) => Ajoute());

            _etat = new Label { ForeColor = FpsUi.Dim, Font = FpsUi.Small, AutoEllipsis = true };
            Controls.Add(_etat);

            Resize += (s, e) => Place();
            FormClosed += (s, e) => { _ferme = true; foreach (Image i in _images.Images) i.Dispose(); };
            Shown += (s, e) => Charge();
            Place();
            MajBoutons();
        }

        /// <summary>Bandeau du haut (note, puis réglages de capture) : la liste commence sous lui.</summary>
        private readonly Panel Haut;
        private int HautH = 44;

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
            int y = 14 + HautH + 10, bas = H - 96;
            _jeux.SetBounds(m, y, 200, bas - y);
            _liste.SetBounds(m + 212, y, W - 2 * m - 212, bas - y);
            int x = m, by = H - 82;
            foreach (Button b in new[] { _lire, _dossier, _copier, _corbeille })
            {
                int w = TextRenderer.MeasureText(b.Text, b.Font).Width + 36;
                b.SetBounds(x, by, w, 34); x += w + 10;
            }
            int xr = W - m;
            foreach (Button b in new[] { _ajouter, _actualiser })
            {
                int w = TextRenderer.MeasureText(b.Text, b.Font).Width + 36;
                xr -= w; b.SetBounds(xr, by, w, 34); xr -= 10;
            }
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
            _etat.Text = texte;
        }

        private void Remplit()
        {
            string jeu = _jeux.SelectedIndex > 0 && _jeux.SelectedIndex < _jeuxAffiches.Count ? _jeuxAffiches[_jeux.SelectedIndex] : null;
            List<Clips.Clip> l = Clips.Trie(_tous, jeu);
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
            _lire.Enabled = n == 1; _dossier.Enabled = n == 1;
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
            _etat.Text = ok && restants == 0 ? quoi + " à la Corbeille." : "Corbeille : " + (chemins.Count - restants) + " sur " + chemins.Count + " déplacés.";
            Charge();
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
