using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// 🎬 « Vidéos, archives &amp; gros fichiers » : la gestion du stockage dans TOUS les domaines,
    /// pas seulement les applications. Balaye les disques (hors système, hors dossiers de jeux et
    /// d'applications — leurs fichiers leur appartiennent), classe par taille, et laisse trier.
    ///
    /// La seule suppression proposée est la CORBEILLE WINDOWS : récupérable tant qu'elle n'est
    /// pas vidée. ONYX ne fait jamais de suppression définitive sur des fichiers personnels.
    /// </summary>
    internal class StorageFilesForm : Form
    {
        private readonly Action<string, int> _log;
        private ListView _list;
        private Label _summary;
        private Button _btnRecycle, _btnReveal, _btnIgnore, _btnScan, _btnClose;
        private List<Storage.StorageItem> _items;
        private readonly bool _selfScan;   // vrai = la fenêtre balaye elle-même (ouverte depuis un menu)
        private bool _partial;
        private int _sortCol = 2;
        private bool _sortAsc;

        private static readonly Color Accent = Color.FromArgb(0, 150, 90);
        private static readonly Color Ink = Color.FromArgb(40, 44, 52);

        /// <summary>Liste NON nulle = affichée telle quelle (la page Stockage a déjà balayé, et le
        /// harnais construit la fenêtre sans lancer d'analyse). Null = la fenêtre balaye elle-même.</summary>
        public StorageFilesForm(Action<string, int> log, List<Storage.StorageItem> preloaded)
        {
            _log = log ?? delegate { };
            _items = preloaded;
            _selfScan = preloaded == null;
            Build();
            Theme.Apply(this);
            if (_items != null) Populate(_items);
            else Scan();
        }

        private void Build()
        {
            Text = "ONYX — Vidéos, archives & gros fichiers";
            ClientSize = new Size(920, 570);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(800, 470);
            BackColor = Color.FromArgb(245, 246, 248);
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  🎬 Gros fichiers — vidéos, archives, et tout le reste. Suppression = corbeille, récupérable.",
                Dock = DockStyle.Fill,
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 12f),
                TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            var actions = new Panel { Dock = DockStyle.Bottom, Height = 96, BackColor = Color.FromArgb(245, 246, 248) };
            _summary = new Label
            {
                Location = new Point(16, 8),
                Size = new Size(880, 36),
                Font = new Font("Segoe UI Semibold", 9.5f),
                ForeColor = Ink
            };
            actions.Controls.Add(_summary);

            _btnRecycle = MakeBtn("Mettre à la corbeille…", 16, 48, 180, 36, true);
            _btnRecycle.Click += OnRecycle;
            _btnReveal = MakeBtn("Ouvrir l'emplacement", 206, 48, 170, 36, false);
            _btnReveal.Click += (s, e) => { var it = Sel(); if (it != null && it.Count > 0) Storage.RevealFile(it[0].Key); };
            _btnIgnore = MakeBtn("Ne plus me proposer", 386, 48, 170, 36, false);
            _btnIgnore.Click += OnIgnore;
            _btnScan = MakeBtn("Analyser à nouveau", 566, 48, 160, 36, false);
            _btnScan.Click += (s, e) => Scan();
            _btnClose = MakeBtn("Fermer", 800, 48, 96, 36, false);
            _btnClose.Click += (s, e) => Close();
            actions.Controls.Add(_btnRecycle); actions.Controls.Add(_btnReveal);
            actions.Controls.Add(_btnIgnore); actions.Controls.Add(_btnScan); actions.Controls.Add(_btnClose);
            Controls.Add(actions);

            _list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = true,      // trier ses fichiers se fait par lots — sélection multiple
                HideSelection = false,
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 9.5f)
            };
            _list.Columns.Add("Fichier", 280);
            _list.Columns.Add("Type", 80);
            _list.Columns.Add("Taille", 90, HorizontalAlignment.Right);
            _list.Columns.Add("Modifié", 110);
            _list.Columns.Add("Dossier", 320);
            _list.ColumnClick += OnColumnClick;
            _list.DoubleClick += (s, e) => { var it = Sel(); if (it != null && it.Count > 0) Storage.RevealFile(it[0].Key); };
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

        private List<Storage.StorageItem> Sel()
        {
            var sel = new List<Storage.StorageItem>();
            foreach (ListViewItem it in _list.SelectedItems)
            {
                var s = it.Tag as Storage.StorageItem;
                if (s != null) sel.Add(s);
            }
            return sel;
        }

        private void SetBusy(bool busy)
        {
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            _btnScan.Enabled = !busy; _btnRecycle.Enabled = !busy;
            _btnReveal.Enabled = !busy; _btnIgnore.Enabled = !busy; _list.Enabled = !busy;
        }

        private void Scan()
        {
            SetBusy(true);
            _list.Items.Clear();
            _summary.Text = "Balayage des disques… (quelques secondes par disque, les plus gros sortent en premier)";

            Task.Run(delegate
            {
                var items = new List<Storage.StorageItem>();
                bool partial = false;
                try
                {
                    StorageSettings st = StorageSettings.Load();
                    List<Storage.FileEntry> files = Storage.ScanBigFiles(st.MinFileMB, Storage.AppRoots(), out partial);
                    foreach (Storage.FileEntry f in files)
                    {
                        if (st.IsIgnored(f.Path)) continue;
                        items.Add(Storage.FileToItem(f));
                        if (items.Count >= 300) break;
                    }
                }
                catch { }
                _partial = partial;
                try { BeginInvoke((Action)(() => Populate(items))); } catch { }
            });
        }

        private static string CatLabel(Storage.StorageItem s)
        {
            var f = s.Tag as Storage.FileEntry;
            if (f == null) return "—";
            if (f.Cat == "videos") return "Vidéo";
            if (f.Cat == "archives") return "Archive";
            return "Autre";
        }

        private void Populate(List<Storage.StorageItem> items)
        {
            _items = items ?? new List<Storage.StorageItem>();
            Sort();

            _list.BeginUpdate();
            _list.Items.Clear();
            long total = 0;
            foreach (Storage.StorageItem s in _items)
            {
                total += s.SizeMB;
                var f = s.Tag as Storage.FileEntry;
                var it = new ListViewItem(s.Name);
                it.SubItems.Add(CatLabel(s));
                it.SubItems.Add(s.SizeText);
                it.SubItems.Add(f != null && f.AgeText.Length > 0 ? f.AgeText : "—");
                it.SubItems.Add(s.Path ?? "—");
                it.Tag = s;
                // Repère visuel : à partir de 5 Go un fichier mérite qu'on se pose la question.
                if (s.SizeMB >= 5120) it.ForeColor = Color.FromArgb(190, 60, 60);
                else if (s.SizeMB >= 1024) it.ForeColor = Color.FromArgb(190, 120, 0);
                _list.Items.Add(it);
            }
            _list.EndUpdate();

            _summary.Text = _items.Count == 0
                ? "Rien au-dessus du seuil hors applications et jeux — ton espace part ailleurs (voir la page Stockage)."
                : _items.Count + " fichier(s) · " + Storage.Human(total) + " au total"
                  + (_partial ? " · analyse plafonnée dans le temps : les plus gros sont là, relance pour creuser" : "")
                  + "\nSélection multiple possible (Ctrl / Maj). Tout part à la CORBEILLE : récupérable tant qu'elle n'est pas vidée.";
            SetBusy(false);
        }

        private void OnColumnClick(object sender, ColumnClickEventArgs e)
        {
            if (e.Column == _sortCol) _sortAsc = !_sortAsc;
            else { _sortCol = e.Column; _sortAsc = e.Column != 2; }   // la taille part du plus lourd
            Populate(_items);
        }

        private void Sort()
        {
            if (_items == null) return;
            int dir = _sortAsc ? 1 : -1;
            int col = _sortCol;
            _items.Sort(delegate (Storage.StorageItem a, Storage.StorageItem b)
            {
                var fa = a.Tag as Storage.FileEntry;
                var fb = b.Tag as Storage.FileEntry;
                switch (col)
                {
                    case 0: return dir * string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
                    case 1: return dir * string.Compare(CatLabel(a), CatLabel(b), StringComparison.CurrentCultureIgnoreCase);
                    case 3:
                        DateTime da = fa != null ? fa.Modified : DateTime.MinValue;
                        DateTime db = fb != null ? fb.Modified : DateTime.MinValue;
                        return dir * da.CompareTo(db);
                    case 4: return dir * string.Compare(a.Path ?? "", b.Path ?? "", StringComparison.CurrentCultureIgnoreCase);
                    default: return dir * a.SizeMB.CompareTo(b.SizeMB);
                }
            });
        }

        private void OnRecycle(object sender, EventArgs e)
        {
            List<Storage.StorageItem> sel = Sel();
            if (sel.Count == 0)
            {
                MessageBox.Show(this, "Sélectionne au moins un fichier (Ctrl ou Maj pour plusieurs).",
                    "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            long mb = 0;
            foreach (Storage.StorageItem s in sel) mb += s.SizeMB;
            var msg = new System.Text.StringBuilder();
            msg.Append("Envoyer ").Append(sel.Count).Append(" fichier(s) à la corbeille — ")
               .Append(Storage.Human(mb)).Append(" ?\n\n");
            int n = 0;
            foreach (Storage.StorageItem s in sel)
            {
                if (n++ >= 8) { msg.Append("  • … et ").Append(sel.Count - 8).Append(" autre(s)\n"); break; }
                msg.Append("  • ").Append(s.Name).Append("  (").Append(s.SizeText).Append(")\n");
            }
            msg.Append("\nIls restent RÉCUPÉRABLES dans la corbeille Windows tant qu'elle n'est pas vidée.");

            if (MessageBox.Show(this, msg.ToString(), "ONYX — mettre à la corbeille",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2) != DialogResult.OK) return;

            SetBusy(true);
            var paths = new List<string>();
            foreach (Storage.StorageItem s in sel) paths.Add(s.Key);

            Task.Run(delegate
            {
                Storage.RecycleFiles(paths);
                // Le gain annoncé est VÉRIFIÉ : seuls les fichiers réellement partis comptent.
                long freed = 0;
                var gone = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (Storage.StorageItem s in sel)
                {
                    bool exists = true;
                    try { exists = File.Exists(s.Key); } catch { }
                    if (!exists) { freed += s.SizeMB; gone.Add(s.Key); }
                }
                long freedDone = freed;
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        _items.RemoveAll(s => gone.Contains(s.Key));
                        Populate(_items);
                        _log(gone.Count + " fichier(s) mis à la corbeille (" + Storage.Human(freedDone) + ").", 1);
                        try { AppStats.Invalidate(); } catch { }
                        MessageBox.Show(this,
                            gone.Count > 0
                                ? "✨ " + gone.Count + " fichier(s) à la corbeille — " + Storage.Human(freedDone) + " récupérables sur le disque."
                                  + "\n\n(L'espace revient vraiment quand la corbeille est vidée : module ♻ Corbeille de la page Stockage.)"
                                : "Aucun fichier n'a pu partir (verrouillé ou déjà déplacé).",
                            "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }));
                }
                catch { }
            });
        }

        private void OnIgnore(object sender, EventArgs e)
        {
            List<Storage.StorageItem> sel = Sel();
            if (sel.Count == 0)
            {
                MessageBox.Show(this, "Sélectionne au moins un fichier.", "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            StorageSettings st = StorageSettings.Load();
            foreach (Storage.StorageItem s in sel) st.Ignore(s.Key);
            st.Save();
            _items.RemoveAll(s => st.IsIgnored(s.Key));
            Populate(_items);
            _log(sel.Count + " fichier(s) retiré(s) des propositions (aucun fichier touché).", 0);
        }
    }
}
