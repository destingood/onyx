using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>Gestionnaire des programmes au démarrage : activer/désactiver ce qui se lance au boot.</summary>
    internal class StartupForm : Form
    {
        private readonly Action<string, int> _log;
        private ListView _list;
        private List<Sys.StartupEntry> _entries = new List<Sys.StartupEntry>();
        private bool _loading;

        public StartupForm(Action<string, int> log)
        {
            _log = log;
            Build();
            Reload();
            Theme.Apply(this);
        }

        private void Build()
        {
            Text = "ONYX — Programmes au démarrage";
            ClientSize = new Size(680, 440);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(560, 360);
            BackColor = Color.FromArgb(245, 246, 248);
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  Programmes au démarrage", Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 13f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            _list = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true, FullRowSelect = true,
                GridLines = true, HideSelection = false, Font = new Font("Segoe UI", 9f)
            };
            _list.Columns.Add("Programme", 190);
            _list.Columns.Add("Portée", 130);
            _list.Columns.Add("Commande", 340);
            _list.ItemChecked += OnItemChecked;
            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 10, 12, 6) };
            host.Controls.Add(_list);
            Controls.Add(host);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(12, 7, 12, 7) };
            var hint = new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(110, 115, 125),
                Text = "Décoche un programme pour l'empêcher de se lancer au démarrage. Réversible : recoche-le."
            };
            var reload = new Button { Text = "Rafraîchir", Width = 110, Dock = DockStyle.Right, FlatStyle = FlatStyle.Flat, BackColor = Color.White };
            reload.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            reload.Click += (s, e) => Reload();
            var close = new Button { Text = "Fermer", Width = 100, Dock = DockStyle.Right, FlatStyle = FlatStyle.Flat, BackColor = Color.White };
            close.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            close.Click += (s, e) => Close();
            var sp = new Label { Width = 8, Dock = DockStyle.Right };
            bottom.Controls.Add(hint);
            bottom.Controls.Add(reload);
            bottom.Controls.Add(sp);
            bottom.Controls.Add(close);
            Controls.Add(bottom);
            Controls.SetChildIndex(host, 0);
        }

        private void Reload()
        {
            _loading = true;
            _list.Items.Clear();
            _entries = Sys.ListStartup();
            int on = 0;
            foreach (Sys.StartupEntry e in _entries)
            {
                var it = new ListViewItem(e.Name);
                it.SubItems.Add(e.Scope);
                it.SubItems.Add(e.Command);
                it.Checked = e.Enabled;
                it.Tag = e;
                if (e.Enabled) on++;
                _list.Items.Add(it);
            }
            Text = "ONYX — Programmes au démarrage (" + on + " actifs / " + _entries.Count + ")";
            _loading = false;
        }

        private void OnItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (_loading) return;
            var entry = e.Item.Tag as Sys.StartupEntry;
            if (entry == null) return;
            try
            {
                Sys.SetStartupEnabled(entry, e.Item.Checked);
                entry.Enabled = e.Item.Checked;
                if (_log != null)
                    _log("Démarrage : " + entry.Name + (e.Item.Checked ? " activé" : " désactivé") + ".", e.Item.Checked ? 0 : 1);
            }
            catch (Exception ex)
            {
                if (_log != null) _log("Démarrage (" + entry.Name + ") : " + ex.Message, 3);
            }
        }
    }
}
