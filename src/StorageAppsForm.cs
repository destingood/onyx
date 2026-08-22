using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// 📦 « Applications les plus lourdes » : l'inventaire que Windows ne donne pas honnêtement.
    /// « Programmes et fonctionnalités » affiche la taille déclarée dans le registre — souvent
    /// absente, souvent fausse. Ici, quand le dossier d'installation est connu, il est MESURÉ.
    ///
    /// Rien n'est supprimé par ONYX : « Désinstaller » lance le désinstalleur OFFICIEL de
    /// l'application. Effacer le dossier à la main laisserait une installation morte dans le
    /// registre — c'est exactement ce qu'on ne veut pas faire au PC de quelqu'un.
    /// </summary>
    internal class StorageAppsForm : Form
    {
        private readonly Action<string, int> _log;
        private ListView _list;
        private Label _summary;
        private Button _btnUninstall, _btnFolder, _btnIgnore, _btnScan, _btnClose;
        private List<Storage.AppEntry> _apps;
        private int _sortCol = 2;
        private bool _sortAsc;

        private static readonly Color Accent = Color.FromArgb(0, 150, 90);
        private static readonly Color Ink = Color.FromArgb(40, 44, 52);

        public StorageAppsForm(Action<string, int> log) : this(log, null) { }

        /// <summary>Une liste NON nulle est affichée telle quelle (la page Stockage a déjà mesuré,
        /// et le harnais de test peut ainsi construire la fenêtre sans lancer d'analyse). Null =
        /// la fenêtre fait son propre inventaire.</summary>
        public StorageAppsForm(Action<string, int> log, List<Storage.AppEntry> preloaded)
        {
            _log = log ?? delegate { };
            _apps = preloaded;
            Build();
            Theme.Apply(this);
            if (_apps != null) Populate(_apps);
            else Scan();
        }

        private void Build()
        {
            Text = "ONYX — Applications les plus lourdes";
            ClientSize = new Size(880, 560);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(760, 470);
            BackColor = Color.FromArgb(245, 246, 248);
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  📦 Applications — classées par poids réel sur le disque",
                Dock = DockStyle.Fill,
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 12.5f),
                TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            var actions = new Panel { Dock = DockStyle.Bottom, Height = 96, BackColor = Color.FromArgb(245, 246, 248) };
            _summary = new Label
            {
                Location = new Point(16, 8),
                Size = new Size(840, 36),
                Font = new Font("Segoe UI Semibold", 9.5f),
                ForeColor = Ink
            };
            actions.Controls.Add(_summary);

            _btnUninstall = MakeBtn("Désinstaller…", 16, 48, 150, 36, true);
            _btnUninstall.Click += OnUninstall;
            _btnFolder = MakeBtn("Ouvrir le dossier", 176, 48, 150, 36, false);
            _btnFolder.Click += (s, e) => { Storage.AppEntry a = Sel(); if (a != null) Storage.OpenFolder(a.InstallDir); };
            _btnIgnore = MakeBtn("Ne plus me proposer", 336, 48, 170, 36, false);
            _btnIgnore.Click += OnIgnore;
            _btnScan = MakeBtn("Analyser à nouveau", 516, 48, 160, 36, false);
            _btnScan.Click += (s, e) => Scan();
            _btnClose = MakeBtn("Fermer", 760, 48, 96, 36, false);
            _btnClose.Click += (s, e) => Close();
            actions.Controls.Add(_btnUninstall); actions.Controls.Add(_btnFolder);
            actions.Controls.Add(_btnIgnore); actions.Controls.Add(_btnScan); actions.Controls.Add(_btnClose);
            Controls.Add(actions);

            _list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                HideSelection = false,
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 9.5f)
            };
            _list.Columns.Add("Application", 300);
            _list.Columns.Add("Éditeur", 190);
            _list.Columns.Add("Taille", 90, HorizontalAlignment.Right);
            _list.Columns.Add("Installée le", 100);
            _list.Columns.Add("Dossier", 180);
            _list.ColumnClick += OnColumnClick;
            _list.DoubleClick += (s, e) => { Storage.AppEntry a = Sel(); if (a != null) Storage.OpenFolder(a.InstallDir); };
            Controls.Add(_list);
            _list.BringToFront();
        }

        private static Button MakeBtn(string text, int x, int y, int w, int h, bool primary)
        {
            var b = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, h),
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Accent : Color.White,
                ForeColor = primary ? Color.White : Ink,
                Font = primary ? new Font("Segoe UI Semibold", 9f) : new Font("Segoe UI", 9f)
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            return b;
        }

        private Storage.AppEntry Sel()
        {
            return _list.SelectedItems.Count == 0 ? null : _list.SelectedItems[0].Tag as Storage.AppEntry;
        }

        private void SetBusy(bool busy)
        {
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            _btnScan.Enabled = !busy; _btnUninstall.Enabled = !busy;
            _btnFolder.Enabled = !busy; _btnIgnore.Enabled = !busy; _list.Enabled = !busy;
        }

        private void Scan()
        {
            SetBusy(true);
            _list.Items.Clear();
            _summary.Text = "Mesure des dossiers d'installation… (le premier passage est le plus long, ensuite c'est mis en cache)";

            Task.Run(delegate
            {
                List<Storage.AppEntry> found = new List<Storage.AppEntry>();
                try
                {
                    found = Storage.ScanApps(delegate (int done, int total)
                    {
                        try
                        {
                            if (!IsHandleCreated) return;
                            BeginInvoke((Action)(() => _summary.Text = "Analyse… " + done + " / " + total + " applications"));
                        }
                        catch { }
                    });
                }
                catch { }
                try { BeginInvoke((Action)(() => Populate(found))); } catch { }
            });
        }

        private void Populate(List<Storage.AppEntry> found)
        {
            _apps = found ?? new List<Storage.AppEntry>();
            Sort();

            _list.BeginUpdate();
            _list.Items.Clear();
            long total = 0, top10 = 0;
            int rank = 0;
            foreach (Storage.AppEntry a in _apps)
            {
                total += a.SizeMB;
                if (rank++ < 10) top10 += a.SizeMB;

                var it = new ListViewItem(a.Name);
                it.SubItems.Add(string.IsNullOrEmpty(a.Publisher) ? "—" : a.Publisher);
                it.SubItems.Add(a.SizeText + (a.Estimated ? " ~" : ""));
                it.SubItems.Add(a.DateText);
                it.SubItems.Add(string.IsNullOrEmpty(a.InstallDir) ? "—" : a.InstallDir);
                it.Tag = a;
                // Repère visuel : au-delà de 10 Go une application mérite qu'on se pose la question.
                if (a.SizeMB >= 10240) it.ForeColor = Color.FromArgb(190, 60, 60);
                else if (a.SizeMB >= 3072) it.ForeColor = Color.FromArgb(190, 120, 0);
                _list.Items.Add(it);
            }
            _list.EndUpdate();

            _summary.Text = _apps.Count == 0
                ? "Aucune application mesurable trouvée."
                : _apps.Count + " application(s) · " + Storage.Human(total) + " au total · "
                  + "les 10 plus lourdes pèsent " + Storage.Human(top10)
                  + "\nUn « ~ » après la taille = valeur déclarée par le registre, pas mesurée. Clic sur une colonne pour trier.";
            SetBusy(false);
        }

        private void OnColumnClick(object sender, ColumnClickEventArgs e)
        {
            if (e.Column == _sortCol) _sortAsc = !_sortAsc;
            else { _sortCol = e.Column; _sortAsc = e.Column != 2; }   // la taille part du plus lourd
            Populate(_apps);
        }

        private void Sort()
        {
            if (_apps == null) return;
            int dir = _sortAsc ? 1 : -1;
            int col = _sortCol;
            _apps.Sort(delegate (Storage.AppEntry a, Storage.AppEntry b)
            {
                switch (col)
                {
                    case 0: return dir * string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
                    case 1: return dir * string.Compare(a.Publisher ?? "", b.Publisher ?? "", StringComparison.CurrentCultureIgnoreCase);
                    case 3: return dir * a.InstallDate.CompareTo(b.InstallDate);
                    case 4: return dir * string.Compare(a.InstallDir ?? "", b.InstallDir ?? "", StringComparison.CurrentCultureIgnoreCase);
                    default: return dir * a.SizeMB.CompareTo(b.SizeMB);
                }
            });
        }

        private void OnUninstall(object sender, EventArgs e)
        {
            Storage.AppEntry a = Sel();
            if (a == null) { MessageBox.Show(this, "Sélectionne une application.", "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (MessageBox.Show(this,
                    "Désinstaller « " + a.Name + " » ?\n\n"
                    + "Espace à récupérer : " + a.SizeText + "\n\n"
                    + "ONYX ne supprime rien lui-même : c'est le désinstalleur officiel de l'application qui s'ouvre, "
                    + "et c'est lui qui décide quoi retirer.",
                    "Désinstaller une application", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;

            if (Storage.Uninstall(a, _log))
                _summary.Text = "Désinstalleur lancé pour « " + a.Name + " ». Relance l'analyse quand c'est terminé.";
            else
                MessageBox.Show(this, "Cette application ne déclare pas de commande de désinstallation.\n\n"
                    + "Passe par Paramètres Windows → Applications installées.",
                    "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void OnIgnore(object sender, EventArgs e)
        {
            Storage.AppEntry a = Sel();
            if (a == null) { MessageBox.Show(this, "Sélectionne une application.", "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            StorageSettings st = StorageSettings.Load();
            st.Ignore(a.Key);
            st.Save();
            _log("« " + a.Name + " » ne sera plus proposée dans le centre de stockage (aucun fichier touché).", 0);
            _summary.Text = "« " + a.Name + " » ajoutée aux ignorées. Retire-la depuis Stockage → Configurer si tu changes d'avis.";
        }
    }
}
