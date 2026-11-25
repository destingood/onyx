using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// 💽 « Qu'est-ce qui prend la place sur ce disque ? » — chaque dossier et fichier de la racine,
    /// du plus gros au plus petit, et on descend d'un double-clic jusqu'au coupable. Lecture seule :
    /// rien n'est supprimé d'ici, on montre et on ouvre l'emplacement dans l'Explorateur.
    /// </summary>
    internal class DriveUsageForm : Form
    {
        private readonly Storage.DriveView _drive;
        private readonly Dictionary<string, List<DiskWalk.Part>> _cache = new Dictionary<string, List<DiskWalk.Part>>(StringComparer.OrdinalIgnoreCase);
        private string _dir;
        private ListView _list;
        private Label _path, _summary;
        private Button _btnUp, _btnOpen, _btnClose;
        private CancellationTokenSource _cts;

        private static readonly Color Ink = Color.FromArgb(40, 44, 52);

        public DriveUsageForm(Storage.DriveView drive)
        {
            _drive = drive;
            Build();
            Theme.Apply(this);
            if (drive.Parts != null) _cache[drive.Root] = drive.Parts;
            // Au chargement, pas avant : une mesure qui finit avant la création de la fenêtre
            // n'aurait pas de handle où revenir.
            Load += (s, e) => Show(drive.Root);
        }

#if BTTEST
        internal void Entrer(string dir) { Show(dir); }
        internal bool Pret { get { return _cache.ContainsKey(_dir ?? ""); } }
#endif

        private void Build()
        {
            Text = "ONYX — Ce qui occupe " + _drive.Letter;
            ClientSize = new Size(860, 560);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(700, 420);
            BackColor = Color.FromArgb(245, 246, 248);
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Width = ClientSize.Width, Height = 84, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  Ce qui occupe " + _drive.Letter + "  ·  " + Storage.Human(_drive.UsedMB) + " occupés sur " + Storage.Human(_drive.TotalMB),
                Location = new Point(0, 8), Size = new Size(860, 30), Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
                ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 12f), TextAlign = ContentAlignment.MiddleLeft
            });
            _btnUp = MakeBtn("← Remonter", 12, 44, 110, 30);
            _btnUp.Click += (s, e) => Up();
            banner.Controls.Add(_btnUp);
            _path = new Label
            {
                Location = new Point(132, 44), Size = new Size(710, 30), Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
                ForeColor = Color.FromArgb(200, 204, 210), Font = new Font("Segoe UI", 9.5f), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true
            };
            banner.Controls.Add(_path);
            Controls.Add(banner);

            var actions = new Panel { Dock = DockStyle.Bottom, Width = ClientSize.Width, Height = 112, BackColor = Color.FromArgb(245, 246, 248) };
            _summary = new Label
            {
                Location = new Point(16, 6), Size = new Size(828, 58), Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
                Font = new Font("Segoe UI", 9f), ForeColor = Ink
            };
            actions.Controls.Add(_summary);
            _btnOpen = MakeBtn("Ouvrir dans l'Explorateur", 16, 68, 200, 34);
            _btnOpen.Click += (s, e) => { var p = Sel(); Storage.OpenFolder(p != null && p.IsDir ? p.Path : _dir); };
            _btnClose = MakeBtn("Fermer", 748, 68, 96, 34);
            _btnClose.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            _btnClose.Click += (s, e) => Close();
            actions.Controls.Add(_btnOpen); actions.Controls.Add(_btnClose);
            Controls.Add(actions);

            _list = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false,
                HideSelection = false, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 9.5f)
            };
            _list.Columns.Add("Élément", 380);
            _list.Columns.Add("Taille", 100, HorizontalAlignment.Right);
            _list.Columns.Add("Part du disque", 110, HorizontalAlignment.Right);
            _list.Columns.Add("Type", 180);
            _list.DoubleClick += (s, e) => { var p = Sel(); if (p != null && p.IsDir && !p.Denied) Show(p.Path); };
            _list.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { var p = Sel(); if (p != null && p.IsDir && !p.Denied) Show(p.Path); }
                else if (e.KeyCode == Keys.Back) Up();
            };
            Controls.Add(_list);
            _list.BringToFront();
            FormClosed += (s, e) => { var c = _cts; if (c != null) { try { c.Cancel(); } catch { } } };
        }

        private static Button MakeBtn(string text, int x, int y, int w, int h)
        {
            var b = new Button
            {
                Text = text, Location = new Point(x, y), Size = new Size(w, h), FlatStyle = FlatStyle.Flat,
                BackColor = Color.White, ForeColor = Ink, Font = new Font("Segoe UI", 9f)
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            return b;
        }

        private DiskWalk.Part Sel()
        {
            return _list.SelectedItems.Count == 0 ? null : _list.SelectedItems[0].Tag as DiskWalk.Part;
        }

        private bool AtRoot { get { return string.Equals(_dir, _drive.Root, StringComparison.OrdinalIgnoreCase); } }

        private void Up()
        {
            if (_dir == null || AtRoot) return;
            string parent = System.IO.Path.GetDirectoryName(_dir.TrimEnd('\\'));
            Show(string.IsNullOrEmpty(parent) ? _drive.Root : parent);
        }

        private void Show(string dir)
        {
            _dir = dir;
            _path.Text = dir;
            _btnUp.Enabled = !AtRoot;
            List<DiskWalk.Part> parts;
            if (_cache.TryGetValue(dir, out parts)) { Fill(parts); return; }

            var old = _cts; if (old != null) { try { old.Cancel(); } catch { } }
            var cts = new CancellationTokenSource(); _cts = cts;
            _list.Items.Clear();
            _summary.Text = "Mesure de " + dir + "…";
            Cursor = Cursors.WaitCursor;
            Task.Run(delegate
            {
                List<DiskWalk.Part> res = DiskWalk.Repartition(dir, cts.Token, 120000);
                if (cts.IsCancellationRequested) return;
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        if (cts.IsCancellationRequested || IsDisposed) return;
                        _cache[dir] = res;
                        Cursor = Cursors.Default;
                        if (string.Equals(_dir, dir, StringComparison.OrdinalIgnoreCase)) Fill(res);
                    }));
                }
                catch { }
            });
        }

        private void Fill(List<DiskWalk.Part> parts)
        {
            Cursor = Cursors.Default;
            _list.BeginUpdate();
            _list.Items.Clear();
            double disk = Math.Max(1, _drive.TotalMB) * 1048576.0;
            long sum = 0; bool partial = false, denied = false;
            foreach (DiskWalk.Part p in parts)
            {
                sum += p.Bytes;
                partial |= p.Partial; denied |= p.Denied;
                string type = p.Denied ? "Accès refusé" : !p.IsDir ? "Fichier" : p.Partial ? "Dossier (mesure incomplète)" : "Dossier";
                var it = new ListViewItem(Storage.PartName(p, AtRoot)) { Tag = p };
                it.SubItems.Add(p.Denied ? "?" : Taille(p.Bytes));
                double pct = p.Bytes * 100.0 / disk;
                it.SubItems.Add(p.Bytes <= 0 ? "—" : pct < 0.1 ? "< 0,1 %" : pct.ToString("0.0") + " %");
                it.SubItems.Add(type);
                if (p.Denied) it.ForeColor = Color.Gray;
                _list.Items.Add(it);
            }
            _list.EndUpdate();

            string txt = parts.Count + " éléments · " + Taille(sum) + " mesurés dans ce dossier. Double-clic sur un dossier pour y entrer.";
            if (AtRoot)
            {
                long ecart = _drive.UsedMB - sum / 1048576;
                if (ecart > 1024) txt += "\n" + Storage.Human(ecart) + " non attribués : dossiers protégés de Windows (points de restauration, métadonnées du disque).";
                else if (ecart < -1024) txt += "\nLe total dépasse l'espace occupé : Windows partage des fichiers entre dossiers (liens physiques), comptés à chaque endroit.";
            }
            if (partial) txt += "\nMesure coupée par le délai : chiffres minimaux.";
            else if (denied) txt += "\nCertains dossiers refusent la lecture : leur taille n'est pas comptée.";
            _summary.Text = txt;
        }

        private static string Taille(long bytes)
        {
            if (bytes <= 0) return "0";
            if (bytes < 1048576) return Math.Max(1, bytes / 1024) + " Ko";
            return Storage.Human(bytes / 1048576);
        }
    }
}
