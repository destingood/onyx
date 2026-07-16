using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>Panneau d'inventaire matériel : composants détectés, groupés, avec export.</summary>
    internal class SystemInfoForm : Form
    {
        private readonly Action<string, int> _log;
        private ListView _list;
        private List<ComponentInfo.Section> _sections;
        private string _diagText = "";

        public SystemInfoForm(Action<string, int> log)
        {
            _log = log;
            Build();
            Load += (s, e) => Reload();
        }

        private void Build()
        {
            Text = "BT Optimizer — Composants du système";
            ClientSize = new Size(640, 520);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(520, 400);
            BackColor = Color.FromArgb(245, 246, 248);
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  Composants détectés", Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 13f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            _list = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = false,
                HeaderStyle = ColumnHeaderStyle.None, ShowGroups = true, Font = new Font("Segoe UI", 9.5f)
            };
            _list.Columns.Add("Propriété", 230);
            _list.Columns.Add("Valeur", 380);
            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 10, 12, 6) };
            host.Controls.Add(_list);
            Controls.Add(host);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(12, 7, 12, 7) };
            var refresh = MakeBtn("Rafraîchir", 110, DockStyle.Left);
            refresh.Click += (s, e) => Reload();
            var export = MakeBtn("Exporter (.txt)", 130, DockStyle.Left);
            export.Click += OnExport;
            var copy = MakeBtn("Copier", 100, DockStyle.Left);
            copy.Click += (s, e) => { try { Clipboard.SetText(_diagText + "\n" + ComponentInfo.ToText(_sections)); } catch { } };
            var close = MakeBtn("Fermer", 100, DockStyle.Right);
            close.Click += (s, e) => Close();
            bottom.Controls.Add(new Label { Dock = DockStyle.Fill });
            bottom.Controls.Add(export); bottom.Controls.Add(copy); bottom.Controls.Add(refresh);
            bottom.Controls.Add(close);
            Controls.Add(bottom);
            Controls.SetChildIndex(host, 0);

            Theme.Apply(this);
        }

        private static Button MakeBtn(string text, int w, DockStyle dock)
        {
            var b = new Button { Text = text, Width = w, Dock = dock, FlatStyle = FlatStyle.Flat, BackColor = Color.White, Margin = new Padding(4, 0, 4, 0) };
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            return b;
        }

        private void Reload()
        {
            Cursor = Cursors.WaitCursor;
            _list.BeginUpdate();
            _list.Items.Clear();
            _list.Groups.Clear();

            // ---- Diagnostic (constats actionnables, en tête) ----
            try
            {
                var diag = new ListViewGroup("Diagnostic") { HeaderAlignment = HorizontalAlignment.Left };
                _list.Groups.Add(diag);
                var db = new System.Text.StringBuilder();
                foreach (Diagnostics.Finding fd in Diagnostics.Run())
                {
                    string icon = fd.Level == 2 ? "✗" : (fd.Level == 1 ? "!" : "✓");
                    db.AppendLine("  [" + icon + "] " + fd.Text);
                    Color c = fd.Level == 2 ? Color.FromArgb(200, 40, 40)
                            : (fd.Level == 1 ? Color.FromArgb(200, 120, 0) : Color.FromArgb(0, 140, 80));
                    var it = new ListViewItem(icon) { Group = diag, UseItemStyleForSubItems = false };
                    it.ForeColor = c;
                    var sub = it.SubItems.Add(fd.Text);
                    sub.ForeColor = c;
                    _list.Items.Add(it);
                }
                _diagText = db.Length > 0 ? "[Diagnostic]\n" + db.ToString() : "";
            }
            catch { _diagText = ""; }

            _sections = ComponentInfo.Gather();
            foreach (ComponentInfo.Section sec in _sections)
            {
                var g = new ListViewGroup(sec.Title) { HeaderAlignment = HorizontalAlignment.Left };
                _list.Groups.Add(g);
                foreach (string[] row in sec.Rows)
                {
                    var it = new ListViewItem(row[0]) { Group = g };
                    it.SubItems.Add(row[1]);
                    _list.Items.Add(it);
                }
            }
            _list.EndUpdate();
            Cursor = Cursors.Default;
        }

        private void OnExport(object sender, EventArgs e)
        {
            try
            {
                string path = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "bt-composants.txt");
                System.IO.File.WriteAllText(path, _diagText + "\n" + ComponentInfo.ToText(_sections), new System.Text.UTF8Encoding(false));
                if (_log != null) _log("Composants exportés : " + path, 1);
                System.Diagnostics.Process.Start("notepad.exe", "\"" + path + "\"");
            }
            catch (Exception ex) { if (_log != null) _log("Export impossible : " + ex.Message, 3); }
        }
    }
}
