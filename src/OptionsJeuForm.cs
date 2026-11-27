using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Options d'un jeu (clic droit sur une tuile, ou fiche du jeu) : nom affiché, exécutable,
    /// arguments, dossier de départ, étiquettes, jaquette et fond personnalisés. Champs vides = lancement habituel.
    /// <see cref="Change"/> dit à l'appelant de redessiner la bibliothèque.
    /// </summary>
    internal sealed class OptionsJeuForm : Form
    {
        private readonly GameScan.GameInfo _g;
        private readonly TextBox _nom, _cible, _args, _dossier, _etiquettes;
        private readonly Button _jaquette, _origine, _fond, _fondOrigine, _icone, _iconeOrigine;
        private readonly Label _etat;

        public bool Change { get; private set; }

        public OptionsJeuForm(GameScan.GameInfo g)
        {
            _g = g;
            Text = "ONYX — Options de " + g.Name;
            ClientSize = new Size(560, 528);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false;
            Font = FpsUi.Body;

            OptionsJeu.Options o = OptionsJeu.De(g.Name) ?? new OptionsJeu.Options();
            int y = 16;
            _nom = Champ("Nom affiché dans la bibliothèque (vide : « " + g.Name + " »)", o.Nom, ref y, null);
            _cible = Champ("Exécutable (vide : lancement habituel, par " + (string.IsNullOrEmpty(g.Store) ? "le dossier du jeu" : g.Store) + ")", o.Cible, ref y, ChoisirExe);
            _args = Champ("Arguments de lancement (ex. -novid -high)", o.Arguments, ref y, null);
            _dossier = Champ("Dossier de départ (vide : celui de l'exécutable)", o.Dossier, ref y, ChoisirDossier);
            _etiquettes = Champ("Étiquettes, séparées par des virgules (ex. solo, coop, à finir) : filtre de la bibliothèque",
                EtiquettesJeux.Saisie(EtiquettesJeux.Pour(g.Name)), ref y, null);

            var note = new Label
            {
                Text = g.SteamId > 0 ? "Jeu Steam : avec des arguments, ONYX lance « steam.exe -applaunch " + g.SteamId + " … » (Steam peut demander confirmation)."
                     : string.Equals(g.Store, "BATTLE.NET", StringComparison.OrdinalIgnoreCase) ? "Battle.net : les arguments se règlent dans l'appli Battle.net, ONYX les ignore."
                     : "Les arguments sont passés tels quels à l'exécutable.",
                Location = new Point(16, y), Size = new Size(528, 34), ForeColor = FpsUi.Dim,
            };
            Controls.Add(note);
            y += 42;

            _jaquette = OnyxDesign.Button("Changer la jaquette…");
            _jaquette.SetBounds(16, y, 190, 32);
            _jaquette.Click += (s, e) => ChoisirJaquette();
            Controls.Add(_jaquette);
            _origine = OnyxDesign.Button("Jaquette d'origine");
            _origine.SetBounds(214, y, 170, 32);
            _origine.Enabled = JaquettesPerso.A(g.Name);
            _origine.Click += (s, e) => JaquetteOrigine();
            Controls.Add(_origine);
            y += 40;

            _fond = OnyxDesign.Button("Changer le fond…");
            _fond.SetBounds(16, y, 190, 32);
            _fond.Click += (s, e) => ChoisirFond();
            Controls.Add(_fond);
            _fondOrigine = OnyxDesign.Button("Fond d'origine");
            _fondOrigine.SetBounds(214, y, 170, 32);
            _fondOrigine.Enabled = FondsPerso.A(g.Name);
            _fondOrigine.Click += (s, e) => FondOrigine();
            Controls.Add(_fondOrigine);
            y += 40;

            _icone = OnyxDesign.Button("Changer l'icône…");
            _icone.SetBounds(16, y, 190, 32);
            _icone.Click += (s, e) => ChoisirIcone();
            Controls.Add(_icone);
            _iconeOrigine = OnyxDesign.Button("Icône d'origine");
            _iconeOrigine.SetBounds(214, y, 170, 32);
            _iconeOrigine.Enabled = IconesPerso.A(g.Name);
            _iconeOrigine.Click += (s, e) => IconeOrigine();
            Controls.Add(_iconeOrigine);
            y += 40;

            _etat = new Label { Location = new Point(16, y), Size = new Size(528, 20), ForeColor = FpsUi.Dim };
            Controls.Add(_etat);

            var ok = OnyxDesign.AccentButton("Enregistrer");
            ok.SetBounds(ClientSize.Width - 16 - 130 - 10 - 110, ClientSize.Height - 48, 130, 34);
            ok.Click += (s, e) => Enregistrer();
            var ca = OnyxDesign.Button("Annuler");
            ca.SetBounds(ClientSize.Width - 16 - 110, ClientSize.Height - 48, 110, 34);
            ca.DialogResult = DialogResult.Cancel;
            Controls.Add(ok); Controls.Add(ca);
            AcceptButton = ok; CancelButton = ca;
            try { Theme.Apply(this); } catch { }
        }

        private TextBox Champ(string libelle, string valeur, ref int y, Action<TextBox> parcourir)
        {
            Controls.Add(new Label { Text = libelle, Location = new Point(16, y), Size = new Size(528, 18), AutoEllipsis = true });
            var tb = new TextBox { Text = valeur ?? "", Location = new Point(16, y + 20), Width = parcourir != null ? 420 : 528 };
            Controls.Add(tb);
            if (parcourir != null)
            {
                var b = OnyxDesign.Button("Parcourir…");
                b.SetBounds(444, y + 18, 100, 28);
                b.Click += (s, e) => parcourir(tb);
                Controls.Add(b);
            }
            y += 56;
            return tb;
        }

        private void ChoisirExe(TextBox tb)
        {
            using (var d = new OpenFileDialog { Title = "Exécutable du jeu", Filter = "Programmes (*.exe)|*.exe", CheckFileExists = true })
            {
                try { if (!string.IsNullOrEmpty(_g.InstallPath) && Directory.Exists(_g.InstallPath)) d.InitialDirectory = _g.InstallPath; } catch { }
                if (d.ShowDialog(this) == DialogResult.OK) tb.Text = d.FileName;
            }
        }

        private void ChoisirDossier(TextBox tb)
        {
            using (var d = new FolderBrowserDialog { Description = "Dossier de départ du jeu", UseDescriptionForTitle = true })
            {
                try { if (Directory.Exists(tb.Text)) d.SelectedPath = tb.Text; else if (!string.IsNullOrEmpty(_g.InstallPath)) d.SelectedPath = _g.InstallPath; } catch { }
                if (d.ShowDialog(this) == DialogResult.OK) tb.Text = d.SelectedPath;
            }
        }

        private void ChoisirJaquette()
        {
            using (var d = new OpenFileDialog { Title = "Image de la jaquette", Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|Tous les fichiers|*.*", CheckFileExists = true })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    JaquettesPerso.Pose(_g.Name, d.FileName);
                    Change = true;
                    _origine.Enabled = true;
                    _etat.ForeColor = FpsUi.Gold; _etat.Text = "Jaquette changée.";
                }
                catch (Exception ex) { _etat.ForeColor = FpsUi.Dim; _etat.Text = "Image illisible : " + ex.Message; }
            }
        }

        private void JaquetteOrigine()
        {
            bool ok = JaquettesPerso.Retire(_g.Name, Handle);
            Change = true;
            _origine.Enabled = JaquettesPerso.A(_g.Name);
            _etat.Text = ok ? "Jaquette d'origine rétablie (l'image choisie est dans la Corbeille)." : "La copie n'a pas pu aller à la Corbeille.";
        }

        private void ChoisirFond()
        {
            using (var d = new OpenFileDialog { Title = "Image de fond de la fiche", Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|Tous les fichiers|*.*", CheckFileExists = true })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    FondsPerso.Pose(_g.Name, d.FileName);
                    Change = true;
                    _fondOrigine.Enabled = true;
                    _etat.ForeColor = FpsUi.Gold; _etat.Text = "Fond changé : il s'affiche derrière la fiche du jeu.";
                }
                catch (Exception ex) { _etat.ForeColor = FpsUi.Dim; _etat.Text = "Image illisible : " + ex.Message; }
            }
        }

        private void ChoisirIcone()
        {
            using (var d = new OpenFileDialog { Title = "Icône du jeu (vue liste)", Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.ico|Tous les fichiers|*.*", CheckFileExists = true })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    IconesPerso.Pose(_g.Name, d.FileName);
                    Change = true;
                    _iconeOrigine.Enabled = true;
                    _etat.ForeColor = FpsUi.Gold; _etat.Text = "Icône changée : elle s'affiche dans la vue liste.";
                }
                catch (Exception ex) { _etat.ForeColor = FpsUi.Dim; _etat.Text = "Image illisible : " + ex.Message; }
            }
        }

        private void IconeOrigine()
        {
            bool ok = IconesPerso.Retire(_g.Name, Handle);
            Change = true;
            _iconeOrigine.Enabled = IconesPerso.A(_g.Name);
            _etat.Text = ok ? "Icône d'origine rétablie (l'image choisie est dans la Corbeille)." : "La copie n'a pas pu aller à la Corbeille.";
        }

        private void FondOrigine()
        {
            bool ok = FondsPerso.Retire(_g.Name, Handle);
            Change = true;
            _fondOrigine.Enabled = FondsPerso.A(_g.Name);
            _etat.Text = ok ? "Fond d'origine rétabli (l'image choisie est dans la Corbeille)." : "La copie n'a pas pu aller à la Corbeille.";
        }

        private void Enregistrer()
        {
            string cible = _cible.Text.Trim(), dossier = _dossier.Text.Trim();
            if (cible.Length > 0 && !File.Exists(cible)) { _etat.Text = "Exécutable introuvable : " + cible; _cible.Focus(); return; }
            if (dossier.Length > 0 && !Directory.Exists(dossier)) { _etat.Text = "Dossier introuvable : " + dossier; _dossier.Focus(); return; }
            if (!OptionsJeu.Enregistre(_g.Name, _nom.Text, cible, _args.Text, dossier)) { _etat.Text = "Enregistrement impossible (fichier en lecture seule ?)."; return; }
            if (!EtiquettesJeux.Enregistre(_g.Name, _etiquettes.Text)) { _etat.Text = "Étiquettes non enregistrées (fichier en lecture seule, ou " + EtiquettesJeux.MaxJeux + " jeux étiquetés au plus)."; return; }
            Change = true;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
