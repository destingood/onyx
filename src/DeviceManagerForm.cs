using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>Gestionnaire de périphériques (lecture) : détecte les périphériques en erreur, liste le matériel, ouvre les outils Windows.</summary>
    internal class DeviceManagerForm : Form
    {
        private readonly Action<string, int> _log;
        private ListView _list;
        private Label _summary;
        private CheckBox _showAll;
        private Button _btnMgr, _btnScan, _btnRefresh, _btnExport, _btnClose;
        private ToolTip _tip;
        private List<Font> _ownedFonts;
        private Font _bold;
        private List<DeviceInfo.Device> _devices = new List<DeviceInfo.Device>();

        private Font Own(Font f) { _ownedFonts.Add(f); return f; }

        public DeviceManagerForm(Action<string, int> log)
        {
            _log = log;
            Build();
            Load += (s, e) => Reload();
        }

        private void Build()
        {
            Text = "DesTinGOOD — Gestionnaire de périphériques";
            ClientSize = new Size(680, 540);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(580, 440);
            BackColor = Color.FromArgb(245, 246, 248);
            _ownedFonts = new List<Font>();
            Font = Own(new Font("Segoe UI", 9f));
            _bold = Own(new Font("Segoe UI Semibold", 9.5f));
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            _tip = new ToolTip { AutoPopDelay = 20000, InitialDelay = 300 };

            var banner = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  Gestionnaire de périphériques", Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = Own(new Font("Segoe UI Semibold", 13f)), TextAlign = ContentAlignment.MiddleLeft
            });

            var head = new Panel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(12, 6, 12, 2) };
            _summary = new Label { Dock = DockStyle.Left, AutoSize = true, Font = Own(new Font("Segoe UI Semibold", 9.75f)), Text = "Analyse..." };
            _showAll = new CheckBox { Text = "Afficher tous les périphériques", Dock = DockStyle.Right, AutoSize = true, Checked = false };
            _showAll.CheckedChanged += (s, e) => Populate();
            head.Controls.Add(_summary);
            head.Controls.Add(_showAll);

            _list = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, ShowGroups = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable, Font = Own(new Font("Segoe UI", 9.5f))
            };
            _list.Columns.Add("Périphérique", 340);
            _list.Columns.Add("État", 300);
            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 4, 12, 6) };
            host.Controls.Add(_list);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(12, 7, 12, 7) };
            _btnMgr = MakeBtn("Gestionnaire Windows...", 170, DockStyle.Left);
            _tip.SetToolTip(_btnMgr, "Ouvre devmgmt.msc, le Gestionnaire de périphériques de Windows (pour mettre à jour/réinstaller un pilote).");
            _btnMgr.Click += (s, e) => StartShell("mmc.exe", "devmgmt.msc");
            _btnScan = MakeBtn("Rechercher le matériel", 160, DockStyle.Left);
            _tip.SetToolTip(_btnScan, "Relance une détection des modifications matérielles (équivalent « Rechercher les modifications matérielles »). Sûr.");
            _btnScan.Click += OnScan;
            _btnRefresh = MakeBtn("Rafraîchir", 100, DockStyle.Left);
            _btnRefresh.Click += (s, e) => Reload();
            _btnExport = MakeBtn("Exporter", 100, DockStyle.Left);
            _btnExport.Click += OnExport;
            _btnClose = MakeBtn("Fermer", 90, DockStyle.Right);
            _btnClose.Click += (s, e) => Close();
            bottom.Controls.Add(new Label { Dock = DockStyle.Fill });
            bottom.Controls.Add(_btnExport); bottom.Controls.Add(_btnRefresh);
            bottom.Controls.Add(_btnScan); bottom.Controls.Add(_btnMgr);
            bottom.Controls.Add(_btnClose);

            Controls.Add(banner);
            Controls.Add(head);
            Controls.Add(host);
            Controls.Add(bottom);
            Controls.SetChildIndex(banner, 3);
            Controls.SetChildIndex(head, 2);
            Controls.SetChildIndex(host, 0);
            Controls.SetChildIndex(bottom, 1);

            Theme.Apply(this);
        }

        private static Button MakeBtn(string text, int w, DockStyle dock)
        {
            var b = new Button { Text = text, Width = w, Dock = dock, FlatStyle = FlatStyle.Flat, BackColor = Color.White, Margin = new Padding(4, 0, 4, 0) };
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            return b;
        }

        private void StartShell(string file, string args)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(file, args) { UseShellExecute = true };
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex) { if (_log != null) _log("Ouverture impossible : " + ex.Message, 3); }
        }

        private void SetBusy(bool busy)
        {
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            Button[] bs = { _btnMgr, _btnScan, _btnRefresh, _btnExport };
            foreach (Button b in bs) b.Enabled = !busy;
        }

        private void Reload()
        {
            SetBusy(true);
            _summary.Text = "Analyse des périphériques...";
            Task.Run(() =>
            {
                List<DeviceInfo.Device> devs = DeviceInfo.ListAll();
                try
                {
                    if (IsDisposed) return;
                    BeginInvoke((Action)(() =>
                    {
                        _devices = devs;
                        SetBusy(false);
                        Populate();
                    }));
                }
                catch { }
            });
        }

        private void Populate()
        {
            int problems = 0;
            foreach (DeviceInfo.Device d in _devices) if (d.IsProblem) problems++;
            _summary.Text = _devices.Count + " périphériques · "
                + (problems == 0 ? "aucun problème détecté ✓" : problems + " en erreur ✗");
            _summary.ForeColor = problems == 0 ? Color.FromArgb(0, 140, 80) : Color.FromArgb(200, 40, 40);

            _list.BeginUpdate();
            _list.Items.Clear();
            _list.Groups.Clear();
            bool all = _showAll.Checked;

            var groups = new Dictionary<string, ListViewGroup>();
            foreach (DeviceInfo.Device d in _devices)
            {
                if (!all && !d.IsProblem) continue;
                string gk = d.IsProblem ? "⚠ Problèmes" : d.Class;
                ListViewGroup g;
                if (!groups.TryGetValue(gk, out g))
                {
                    g = new ListViewGroup(gk) { HeaderAlignment = HorizontalAlignment.Left };
                    groups[gk] = g;
                    _list.Groups.Add(g);
                }
                var it = new ListViewItem(d.Name) { Group = g, UseItemStyleForSubItems = false };
                var sub = it.SubItems.Add(d.IsProblem ? DeviceInfo.ErrorText(d.ErrorCode) : "OK");
                if (d.IsProblem)
                {
                    it.ForeColor = Color.FromArgb(200, 40, 40);
                    sub.ForeColor = Color.FromArgb(200, 40, 40);
                    it.Font = _bold;
                }
                _list.Items.Add(it);
            }

            if (_list.Items.Count == 0)
            {
                var g = new ListViewGroup("Diagnostic") { HeaderAlignment = HorizontalAlignment.Left };
                _list.Groups.Add(g);
                _list.Items.Add(new ListViewItem("Aucun périphérique en erreur. Coche « Afficher tous les périphériques » pour la liste complète.") { Group = g });
            }
            _list.EndUpdate();
        }

        private void OnScan(object sender, EventArgs e)
        {
            SetBusy(true);
            if (_log != null) _log("Recherche des modifications matérielles...", 0);
            Task.Run(() =>
            {
                try { Sys.Run(Sys.Sys32("pnputil.exe"), "/scan-devices"); } catch { }
                List<DeviceInfo.Device> devs = DeviceInfo.ListAll();
                try
                {
                    if (IsDisposed) return;
                    BeginInvoke((Action)(() =>
                    {
                        _devices = devs;
                        SetBusy(false);
                        Populate();
                        if (_log != null) _log("Détection matérielle terminée.", 1);
                    }));
                }
                catch { }
            });
        }

        private void OnExport(object sender, EventArgs e)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("=== PÉRIPHÉRIQUES — DesTinGOOD ===");
                sb.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                int problems = 0; foreach (DeviceInfo.Device d in _devices) if (d.IsProblem) problems++;
                sb.AppendLine(_devices.Count + " périphériques, " + problems + " en erreur.");
                sb.AppendLine();
                if (problems > 0)
                {
                    sb.AppendLine("[Problèmes]");
                    foreach (DeviceInfo.Device d in _devices)
                        if (d.IsProblem) sb.AppendLine("  ✗ " + d.Name + " — " + DeviceInfo.ErrorText(d.ErrorCode));
                    sb.AppendLine();
                }
                sb.AppendLine("[Tous les périphériques]");
                foreach (DeviceInfo.Device d in _devices)
                    sb.AppendLine("  [" + d.Class + "] " + d.Name + (d.IsProblem ? "  (" + DeviceInfo.ErrorText(d.ErrorCode) + ")" : ""));

                string path = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "bt-peripheriques.txt");
                System.IO.File.WriteAllText(path, sb.ToString(), new System.Text.UTF8Encoding(false));
                if (_log != null) _log("Périphériques exportés : " + path, 1);
                System.Diagnostics.Process.Start("notepad.exe", "\"" + path + "\"");
            }
            catch (Exception ex) { if (_log != null) _log("Export impossible : " + ex.Message, 3); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_tip != null) { try { _tip.RemoveAll(); _tip.Dispose(); } catch { } _tip = null; }
                if (_ownedFonts != null)
                {
                    foreach (Font f in _ownedFonts) { try { f.Dispose(); } catch { } }
                    _ownedFonts.Clear();
                }
            }
            base.Dispose(disposing);
        }
    }
}
