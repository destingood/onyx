using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// 🧹 « Jeux dormants » : classe tes jeux par ESPACE DORMANT (taille × temps sans y jouer)
    /// pour voir d'un coup d'œil où récupérer des dizaines de Go. Un disque système saturé fait
    /// ramer Windows entier — et on garde tous des jeux de 100 Go oubliés depuis un an.
    ///
    /// Rien n'est supprimé sans toi : les seules actions sont « Désinstaller » (qui délègue au
    /// désinstalleur officiel) et « Masquer » (purement visuel).
    /// </summary>
    internal class DormantGamesForm : Form
    {
        private readonly Action<string, int> _log;
        private ListView _list;
        private Label _summary;
        private Button _btnScan, _btnUninstall, _btnHide, _btnFolder, _btnClose;
        private List<DormantGames.Entry> _entries = new List<DormantGames.Entry>();

        private static readonly Color Accent = Color.FromArgb(0, 150, 90);
        private static readonly Color Ink = Color.FromArgb(40, 44, 52);
        private static readonly Color Sub = Color.FromArgb(96, 100, 108);

        public DormantGamesForm(Action<string, int> log)
        {
            _log = log;
            Build();
            Theme.Apply(this);
            Scan();
        }

        private void Build()
        {
            Text = "DesTinGOOD — Jeux dormants";
            ClientSize = new Size(820, 540);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(720, 460);
            BackColor = Color.FromArgb(245, 246, 248);
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  🧹 Jeux dormants — récupère de l'espace",
                Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 12.5f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            var actions = new Panel { Dock = DockStyle.Bottom, Height = 96, BackColor = Color.FromArgb(245, 246, 248) };
            _summary = new Label
            {
                Location = new Point(16, 8), Size = new Size(780, 36),
                Font = new Font("Segoe UI Semibold", 9.5f), ForeColor = Ink
            };
            actions.Controls.Add(_summary);

            _btnUninstall = MakeBtn("Désinstaller…", 16, 48, 150, 36, true);
            _btnUninstall.Click += OnUninstall;
            _btnHide = MakeBtn("Masquer", 176, 48, 120, 36, false);
            _btnHide.Click += OnHide;
            _btnFolder = MakeBtn("Ouvrir le dossier", 306, 48, 150, 36, false);
            _btnFolder.Click += (s, e) => { var it = Sel(); if (it != null) GameActions.OpenFolder(it.InstallDir); };
            _btnScan = MakeBtn("Analyser à nouveau", 466, 48, 160, 36, false);
            _btnScan.Click += (s, e) => Scan();
            _btnClose = MakeBtn("Fermer", 700, 48, 96, 36, false);
            _btnClose.Click += (s, e) => Close();
            actions.Controls.Add(_btnUninstall); actions.Controls.Add(_btnHide);
            actions.Controls.Add(_btnFolder); actions.Controls.Add(_btnScan); actions.Controls.Add(_btnClose);
            Controls.Add(actions);

            _list = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false,
                HideSelection = false, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 9.5f)
            };
            _list.Columns.Add("Jeu", 300);
            _list.Columns.Add("Plateforme", 110);
            _list.Columns.Add("Taille", 90);
            _list.Columns.Add("Dernière partie", 130);
            _list.Columns.Add("Dossier", 180);
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

        private DormantGames.Entry Sel()
        {
            return _list.SelectedItems.Count == 0 ? null : _list.SelectedItems[0].Tag as DormantGames.Entry;
        }

        private void SetBusy(bool busy)
        {
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            _btnScan.Enabled = !busy; _btnUninstall.Enabled = !busy;
            _btnHide.Enabled = !busy; _btnFolder.Enabled = !busy; _list.Enabled = !busy;
        }

        private void Scan()
        {
            SetBusy(true);
            _list.Items.Clear();
            _summary.Text = "Analyse des tailles en cours… (le premier passage est le plus long, ensuite c'est mis en cache)";

            Task.Run(() =>
            {
                List<DormantGames.Entry> found = DormantGames.Scan((done, total) =>
                {
                    try
                    {
                        if (!IsHandleCreated) return;
                        BeginInvoke((Action)(() => _summary.Text = "Analyse… " + done + " / " + total + " jeux"));
                    }
                    catch { }
                });
                try { BeginInvoke((Action)(() => Populate(found))); } catch { }
            });
        }

        private void Populate(List<DormantGames.Entry> found)
        {
            _entries = found ?? new List<DormantGames.Entry>();
            _list.BeginUpdate();
            _list.Items.Clear();

            long totalMB = 0, dormantMB = 0;
            foreach (DormantGames.Entry e in _entries)
            {
                if (GameHidden.IsHidden(e.Name)) continue;
                totalMB += e.SizeMB;
                if (e.IsDormant) dormantMB += e.SizeMB;

                var it = new ListViewItem(e.Name);
                it.SubItems.Add(e.Launcher ?? "?");
                it.SubItems.Add(e.SizeText);
                it.SubItems.Add(e.IdleText);
                it.SubItems.Add(e.InstallDir ?? "—");
                it.Tag = e;
                // Repère visuel : rouge = jamais joué ou 6 mois+, orange dès 3 mois.
                if (e.Never || e.DaysIdle >= 180) it.ForeColor = Color.FromArgb(190, 60, 60);
                else if (e.DaysIdle >= 90) it.ForeColor = Color.FromArgb(190, 120, 0);
                _list.Items.Add(it);
            }
            _list.EndUpdate();

            _summary.Text = _entries.Count == 0
                ? "Aucun jeu mesurable trouvé."
                : _list.Items.Count + " jeu(x) · " + Go(totalMB) + " au total · "
                  + Go(dormantMB) + " dormants (pas joués depuis 3 mois ou plus)"
                  + "\nTrié par espace dormant : les plus gros et les plus oubliés en haut.";
            SetBusy(false);
        }

        private static string Go(long mb)
        {
            return mb >= 1024 ? (mb / 1024.0).ToString("0.0") + " Go" : mb + " Mo";
        }

        private void OnUninstall(object sender, EventArgs e)
        {
            DormantGames.Entry it = Sel();
            if (it == null) { MessageBox.Show(this, "Sélectionne un jeu.", "Fluide", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (GameActions.Uninstall(this, it.Name, it.Game.SteamAppId))
                _log("Désinstallation lancée : " + it.Name + " (" + it.SizeText + " à récupérer).", 0);
        }

        private void OnHide(object sender, EventArgs e)
        {
            DormantGames.Entry it = Sel();
            if (it == null) { MessageBox.Show(this, "Sélectionne un jeu.", "Fluide", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            GameHidden.SetHidden(it.Name, true);
            _log("« " + it.Name + " » masqué de la bibliothèque (aucun fichier touché).", 0);
            Populate(_entries);
        }
    }
}
