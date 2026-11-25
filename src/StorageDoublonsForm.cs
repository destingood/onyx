using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// 📑 « Fichiers &amp; jeux en double » : un groupe par fichier copié plusieurs fois, puis les
    /// jeux installés plusieurs fois.
    ///
    /// Garanties : l'exemplaire gardé ne peut pas être coché ; chaque copie cochée est comparée
    /// OCTET PAR OCTET à l'exemplaire gardé juste avant de partir, et reste en place au moindre
    /// écart ; seule la CORBEILLE Windows est utilisée. Les jeux en double sont montrés, jamais
    /// supprimés ici : leur lanceur les désinstalle proprement.
    /// </summary>
    internal class StorageDoublonsForm : Form
    {
        private sealed class Ligne
        {
            public Doublons.Groupe G;
            public Doublons.Copie C;       // null pour une installation de jeu
            public Doublons.Install I;
            public Doublons.DossierDouble D;
            public string DossierChemin;   // l'un des deux dossiers d'un couple
            public bool Proposable { get { return C != null && !C.Garder; } }
        }

        private readonly Action<string, int> _log;
        private ListView _list;
        private Label _summary;
        private Button _btnCheck, _btnRecycle, _btnReveal, _btnIgnore, _btnScan, _btnClose;
        private Storage.DoublonsTrouves _d;
        private bool _busy;

        private static readonly Color Accent = Color.FromArgb(0, 150, 90);
        private static readonly Color Ink = Color.FromArgb(40, 44, 52);
        private static readonly Color Muted = Color.FromArgb(120, 126, 136);

        /// <summary><paramref name="preloaded"/> : les éléments du module « doublons » de la page
        /// Stockage (Tag = groupe ou jeu). Null = la fenêtre balaye elle-même.</summary>
        public StorageDoublonsForm(Action<string, int> log, List<Storage.StorageItem> preloaded)
        {
            _log = log ?? delegate { };
            Build();
            Theme.Apply(this);
            if (preloaded != null)
            {
                _d = new Storage.DoublonsTrouves();
                foreach (Storage.StorageItem it in preloaded)
                {
                    var g = it.Tag as Doublons.Groupe;
                    if (g != null) { _d.Fichiers.Add(g); continue; }
                    var j = it.Tag as Doublons.JeuDouble;
                    if (j != null) { _d.Jeux.Add(j); continue; }
                    var x = it.Tag as Doublons.DossierDouble;
                    if (x != null) _d.Dossiers.Add(x);
                }
                Populate();
            }
            else Scan();
        }

        private void Build()
        {
            Text = "ONYX — Fichiers & jeux en double";
            ClientSize = new Size(980, 600);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(840, 480);
            BackColor = Color.FromArgb(245, 246, 248);
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  📑 Copies identiques par leur contenu, et jeux installés deux fois. Un exemplaire est toujours gardé.",
                Dock = DockStyle.Fill,
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 12f),
                TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            var actions = new Panel { Dock = DockStyle.Bottom, Height = 100, BackColor = Color.FromArgb(245, 246, 248) };
            _summary = new Label { Location = new Point(16, 8), Size = new Size(940, 40), Font = new Font("Segoe UI Semibold", 9.5f), ForeColor = Ink };
            actions.Controls.Add(_summary);

            _btnCheck = MakeBtn("Cocher les copies en trop", 16, 52, 190, 36, false);
            _btnCheck.Click += (s, e) => CocherTout(true);
            _btnRecycle = MakeBtn("Vérifier et mettre à la corbeille…", 216, 52, 230, 36, true);
            _btnRecycle.Click += OnRecycle;
            _btnReveal = MakeBtn("Ouvrir l'emplacement", 456, 52, 160, 36, false);
            _btnReveal.Click += (s, e) => Reveal();
            _btnIgnore = MakeBtn("Ne plus me proposer", 626, 52, 150, 36, false);
            _btnIgnore.Click += OnIgnore;
            _btnScan = MakeBtn("Analyser à nouveau", 786, 52, 100, 36, false);
            _btnScan.Text = "Ré-analyser";
            _btnScan.Click += (s, e) => Scan();
            _btnClose = MakeBtn("Fermer", 896, 52, 70, 36, false);
            _btnClose.Click += (s, e) => Close();
            actions.Controls.AddRange(new Control[] { _btnCheck, _btnRecycle, _btnReveal, _btnIgnore, _btnScan, _btnClose });
            Controls.Add(actions);

            _list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                CheckBoxes = true,
                MultiSelect = true,
                HideSelection = false,
                ShowGroups = true,
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 9.5f)
            };
            _list.Columns.Add("Fichier / installation", 270);
            _list.Columns.Add("Statut", 170);
            _list.Columns.Add("Taille", 90, HorizontalAlignment.Right);
            _list.Columns.Add("Modifié", 100);
            _list.Columns.Add("Dossier", 330);
            _list.ItemCheck += delegate (object s, ItemCheckEventArgs e)
            {
                // L'exemplaire gardé et les jeux ne se cochent pas : rien ne peut les envoyer à la corbeille.
                var l = _list.Items[e.Index].Tag as Ligne;
                if (l == null || !l.Proposable) e.NewValue = CheckState.Unchecked;
            };
            _list.ItemChecked += (s, e) => { if (!_busy) Resume(); };
            _list.DoubleClick += (s, e) => Reveal();
            Controls.Add(_list);
            _list.BringToFront();
        }

        private static Button MakeBtn(string text, int x, int y, int w, int h, bool primary)
        {
            var b = new Button
            {
                Text = text, Location = new Point(x, y), Size = new Size(w, h), FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Ink,
                Font = primary ? new Font("Segoe UI Semibold", 9f) : new Font("Segoe UI", 9f)
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            return b;
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            foreach (Button b in new[] { _btnCheck, _btnRecycle, _btnReveal, _btnIgnore, _btnScan }) b.Enabled = !busy;
            _list.Enabled = !busy;
        }

        private void Scan()
        {
            SetBusy(true);
            _list.Items.Clear();
            _list.Groups.Clear();
            _summary.Text = "Recherche des doublons… (balayage des disques et des jeux, puis lecture de 16 extraits par fichier candidat)";
            Task.Run(delegate
            {
                Storage.DoublonsTrouves d = null;
                try
                {
                    StorageSettings st = StorageSettings.Load();
                    bool partial;
                    List<Storage.FileEntry> files = Storage.ScanBigFiles(st.MinFileMB, Storage.AppRoots(), out partial);
                    d = Storage.ChercheDoublons(files, DormantGames.Scan(null), st);
                }
                catch { }
                try { BeginInvoke((Action)(() => { _d = d ?? new Storage.DoublonsTrouves(); Populate(); })); } catch { }
            });
        }

        private void Populate()
        {
            _list.BeginUpdate();
            _list.Items.Clear();
            _list.Groups.Clear();
            foreach (Doublons.Groupe g in _d.Fichiers)
            {
                Doublons.Copie k = g.Gardee;
                if (k == null || g.EnTrop == 0) continue;
                var grp = new ListViewGroup(k.Name + "  —  " + g.Copies.Count + " exemplaires · "
                                            + Storage.Human(g.RecuperableBytes / 1048576) + " récupérables");
                _list.Groups.Add(grp);
                foreach (Doublons.Copie c in g.Copies)
                {
                    var it = new ListViewItem(c.Name, grp);
                    it.SubItems.Add(!c.Garder ? "copie en trop"
                        : c.Origine != null ? "✔ gardé (fichier de " + c.Origine + ")"
                        : c.Protege ? "✔ gardé (fait partie d'un logiciel)"
                        : "✔ gardé");
                    it.SubItems.Add(Storage.Human(c.Bytes / 1048576));
                    it.SubItems.Add(Age(c.Modified));
                    it.SubItems.Add(c.Dir ?? "—");
                    it.Tag = new Ligne { G = g, C = c };
                    if (c.Garder) it.ForeColor = Muted;
                    _list.Items.Add(it);
                }
            }
            foreach (Doublons.DossierDouble x in _d.Dossiers)
            {
                var grp = new ListViewGroup("📁 Dossier présent deux fois  —  " + x.Fichiers + " gros fichier(s) identique(s) · "
                                            + Storage.Human(x.Bytes / 1048576) + " en double");
                _list.Groups.Add(grp);
                foreach (string p in new[] { x.A, x.B })
                {
                    string nom = p;
                    try { nom = Path.GetFileName(p.TrimEnd('\\')); } catch { }
                    var it = new ListViewItem(nom, grp);
                    it.SubItems.Add("à vérifier : copie complète ?");
                    it.SubItems.Add("—");
                    it.SubItems.Add("—");
                    it.SubItems.Add(p);
                    it.Tag = new Ligne { D = x, DossierChemin = p };
                    it.ForeColor = Muted;
                    _list.Items.Add(it);
                }
            }
            if (_d.Jeux.Count > 0)
            {
                foreach (Doublons.JeuDouble j in _d.Jeux)
                {
                    var grp = new ListViewGroup("🕹 " + j.Nom + "  —  installé " + j.Installs.Count + " fois · "
                                                + Storage.Human(j.RecuperableMB) + " en garder une seule");
                    _list.Groups.Add(grp);
                    foreach (Doublons.Install i in j.Installs)
                    {
                        var it = new ListViewItem(string.IsNullOrEmpty(i.Lanceur) ? "Installation" : i.Lanceur, grp);
                        it.SubItems.Add("à désinstaller par son lanceur");
                        it.SubItems.Add(Storage.Human(i.MB));
                        it.SubItems.Add("—");
                        it.SubItems.Add(i.Dossier);
                        it.Tag = new Ligne { I = i };
                        it.ForeColor = Muted;
                        _list.Items.Add(it);
                    }
                }
            }
            _list.EndUpdate();
            SetBusy(false);
            Resume();
        }

        private void Resume()
        {
            long recup = 0;
            int groupes = 0;
            foreach (Doublons.Groupe g in _d.Fichiers) if (g.EnTrop > 0) { recup += g.RecuperableBytes; groupes++; }
            long jeux = 0;
            foreach (Doublons.JeuDouble j in _d.Jeux) jeux += j.RecuperableMB;
            long coche = 0; int n = 0;
            foreach (Ligne l in Coches()) { coche += l.C.Bytes; n++; }

            long dos = 0;
            foreach (Doublons.DossierDouble x in _d.Dossiers) dos += x.Bytes;

            if (groupes == 0 && _d.Jeux.Count == 0 && _d.Dossiers.Count == 0)
            {
                _summary.Text = "Aucun doublon parmi les fichiers au-dessus du seuil de la page Stockage (ni dossier copié, ni jeu installé deux fois).";
                return;
            }
            _summary.Text = groupes + " fichier(s) en plusieurs exemplaires · " + Storage.Human(recup / 1048576) + " récupérables"
                + (_d.Dossiers.Count > 0 ? " · " + _d.Dossiers.Count + " dossier(s) copié(s) en entier (" + Storage.Human(dos / 1048576) + ")" : "")
                + (_d.Jeux.Count > 0 ? " · " + _d.Jeux.Count + " jeu(x) installé(s) plusieurs fois (" + Storage.Human(jeux) + ")" : "")
                + "\n" + (n > 0 ? n + " copie(s) cochée(s) · " + Storage.Human(coche / 1048576) + ". "
                                : "Coche les copies à supprimer. ")
                + "Chacune est comparée octet par octet à l'exemplaire gardé avant de partir à la corbeille.";
        }

        private List<Ligne> Coches()
        {
            var l = new List<Ligne>();
            foreach (ListViewItem it in _list.CheckedItems)
            {
                var x = it.Tag as Ligne;
                if (x != null && x.Proposable) l.Add(x);
            }
            return l;
        }

        private void CocherTout(bool v)
        {
            _busy = true;
            _list.BeginUpdate();
            foreach (ListViewItem it in _list.Items)
            {
                var l = it.Tag as Ligne;
                if (l != null && l.Proposable) it.Checked = v;
            }
            _list.EndUpdate();
            _busy = false;
            Resume();
        }

        private void Reveal()
        {
            if (_list.SelectedItems.Count == 0) return;
            var l = _list.SelectedItems[0].Tag as Ligne;
            if (l == null) return;
            if (l.C != null) Storage.RevealFile(l.C.Path);
            else if (l.I != null) Storage.OpenFolder(l.I.Dossier);
            else if (l.DossierChemin != null) Storage.OpenFolder(l.DossierChemin);
        }

        private static string Age(DateTime d)
        {
            if (d == DateTime.MinValue) return "—";
            int j = Math.Max(0, (int)(DateTime.Now - d).TotalDays);
            if (j < 1) return "aujourd'hui";
            if (j < 30) return "il y a " + j + " j";
            if (j < 365) return "il y a " + (j / 30) + " mois";
            return "il y a " + (j / 365) + " an(s)";
        }

        private void OnRecycle(object sender, EventArgs e)
        {
            List<Ligne> sel = Coches();
            if (sel.Count == 0)
            {
                MessageBox.Show(this, "Coche au moins une copie en trop (ou « Cocher les copies en trop »).",
                    "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            long total = 0;
            foreach (Ligne l in sel) total += l.C.Bytes;
            var msg = new System.Text.StringBuilder();
            msg.Append("Vérifier puis envoyer ").Append(sel.Count).Append(" copie(s) à la corbeille — ")
               .Append(Storage.Human(total / 1048576)).Append(" ?\n\n");
            int n = 0;
            foreach (Ligne l in sel)
            {
                if (n++ >= 8) { msg.Append("  • … et ").Append(sel.Count - 8).Append(" autre(s)\n"); break; }
                msg.Append("  • ").Append(l.C.Path).Append('\n');
            }
            msg.Append("\nChaque copie est d'abord comparée octet par octet à l'exemplaire gardé : au moindre écart, elle reste en place. ")
               .Append("L'exemplaire gardé n'est jamais touché. Les copies restent récupérables dans la corbeille tant qu'elle n'est pas vidée.");
            if (MessageBox.Show(this, msg.ToString(), "ONYX — supprimer les doublons",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;

            SetBusy(true);
            IntPtr owner = Handle;
            Task.Run(delegate
            {
                var verifiees = new List<Ligne>();
                var epargnees = new List<Ligne>();
                long fait = 0;
                for (int i = 0; i < sel.Count; i++)
                {
                    Ligne l = sel[i];
                    Doublons.Copie k = l.G.Gardee;
                    int rang = i + 1;
                    long base0 = fait;
                    bool ok = k != null && Doublons.Identiques(k.Path, l.C.Path, delegate (long lu)
                    {
                        if ((lu & ((256L << 20) - 1)) != 0) return;   // un point d'avancement tous les 256 Mo
                        Avance("Vérification octet par octet : copie " + rang + "/" + sel.Count + " · "
                               + Storage.Human((base0 + lu) / 1048576) + " sur " + Storage.Human(total / 1048576));
                    });
                    fait += l.C.Bytes;
                    (ok ? verifiees : epargnees).Add(l);
                }

                var paths = new List<string>();
                foreach (Ligne l in verifiees) paths.Add(l.C.Path);
                if (paths.Count > 0) { Avance("Envoi à la corbeille…"); Storage.RecycleFiles(paths, owner); }

                var parties = new List<Ligne>();
                long libere = 0;
                foreach (Ligne l in verifiees)
                {
                    bool existe = true;
                    try { existe = File.Exists(l.C.Path); } catch { }
                    if (!existe) { parties.Add(l); libere += l.C.Bytes; }
                }
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        foreach (Ligne l in parties) l.G.Copies.Remove(l.C);
                        _d.Fichiers.RemoveAll(g => g.EnTrop == 0);
                        Populate();
                        _log(parties.Count + " doublon(s) mis à la corbeille (" + Storage.Human(libere / 1048576) + ").", 1);
                        try { AppStats.Invalidate(); } catch { }
                        var r = new System.Text.StringBuilder();
                        if (parties.Count > 0) r.Append("✨ ").Append(parties.Count).Append(" copie(s) à la corbeille — ").Append(Storage.Human(libere / 1048576)).Append('.');
                        else r.Append("Aucune copie n'est partie.");
                        if (epargnees.Count > 0)
                            r.Append("\n\n").Append(epargnees.Count).Append(" copie(s) épargnée(s) : leur contenu n'est pas identique à l'exemplaire gardé, ")
                             .Append("ou l'un des deux est ouvert ailleurs ou a disparu.");
                        int echec = verifiees.Count - parties.Count;
                        if (echec > 0) r.Append("\n\n").Append(echec).Append(" copie(s) vérifiée(s) n'ont pas pu partir (ouvertes ailleurs, chemin trop long ou suppression annulée).");
                        if (parties.Count > 0)
                            r.Append("\n\nElles sont dans la corbeille Windows, sauf celles que Windows a proposé de supprimer définitivement (trop grosses pour elle) si tu as accepté.")
                             .Append("\n(L'espace revient vraiment quand la corbeille est vidée : module ♻ Corbeille de la page Stockage.)");
                        MessageBox.Show(this, r.ToString(), "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }));
                }
                catch { }
            });
        }

        private void Avance(string texte)
        {
            try { BeginInvoke((Action)(() => _summary.Text = texte)); } catch { }
        }

        private void OnIgnore(object sender, EventArgs e)
        {
            var cles = new List<string>();
            foreach (ListViewItem it in _list.SelectedItems)
            {
                var l = it.Tag as Ligne;
                if (l == null) continue;
                if (l.Proposable) cles.Add(l.C.Path);
                else if (l.D != null) cles.Add(Storage.CleDossier(l.D));
                else if (l.I != null)
                    foreach (Doublons.JeuDouble j in _d.Jeux)
                        if (j.Installs.Contains(l.I)) cles.Add("jeu-double:" + j.Nom);
            }
            if (cles.Count == 0)
            {
                MessageBox.Show(this, "Sélectionne une copie en trop ou une installation de jeu.", "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            StorageSettings st = StorageSettings.Load();
            foreach (string k in cles) st.Ignore(k);
            st.Save();
            // Une copie écartée devient un exemplaire gardé : elle n'est plus jamais proposée.
            foreach (Doublons.Groupe g in _d.Fichiers)
                foreach (Doublons.Copie c in g.Copies)
                    if (st.IsIgnored(c.Path)) { c.Reference = true; c.Garder = true; }
            _d.Fichiers.RemoveAll(g => g.EnTrop == 0);
            _d.Jeux.RemoveAll(j => st.IsIgnored("jeu-double:" + j.Nom));
            _d.Dossiers.RemoveAll(x => st.IsIgnored(Storage.CleDossier(x)));
            Populate();
            _log(cles.Count + " doublon(s) retiré(s) des propositions (aucun fichier touché).", 0);
        }
    }
}
