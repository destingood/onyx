using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>Inventaire matériel + diagnostic santé avec corrections en un clic.</summary>
    internal class SystemInfoForm : Form
    {
        private static readonly Color Accent = Color.FromArgb(0, 150, 90);

        private readonly Action<string, int> _log;
        private ListView _list;
        private Panel _diagWrap;
        private TableLayoutPanel _diagTable;
        private ToolTip _tip;
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
            Text = "BT Optimizer — Composants & diagnostic";
            ClientSize = new Size(660, 580);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(560, 480);
            BackColor = Color.FromArgb(245, 246, 248);
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            _tip = new ToolTip { AutoPopDelay = 20000, InitialDelay = 300 };

            var banner = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  Composants & diagnostic santé", Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 13f), TextAlign = ContentAlignment.MiddleLeft
            });

            // ---- Panneau diagnostic (constats + boutons de correction) ----
            _diagWrap = new Panel { Dock = DockStyle.Top, Height = 176, Padding = new Padding(12, 6, 12, 6) };
            var diagHead = new Label
            {
                Text = "Diagnostic santé — corrige en un clic", Dock = DockStyle.Top, Height = 22,
                Font = new Font("Segoe UI Semibold", 10f), ForeColor = Color.FromArgb(50, 70, 130)
            };
            _diagTable = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true,
                GrowStyle = TableLayoutPanelGrowStyle.AddRows, BackColor = Color.Transparent
            };
            _diagTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _diagTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _diagWrap.Controls.Add(_diagTable);
            _diagWrap.Controls.Add(diagHead);
            _diagWrap.Controls.SetChildIndex(_diagTable, 0);

            // ---- Inventaire ----
            _list = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = false,
                HeaderStyle = ColumnHeaderStyle.None, ShowGroups = true, Font = new Font("Segoe UI", 9.5f)
            };
            _list.Columns.Add("Propriété", 230);
            _list.Columns.Add("Valeur", 400);
            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 4, 12, 6) };
            host.Controls.Add(_list);

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

            Controls.Add(banner);
            Controls.Add(_diagWrap);
            Controls.Add(host);
            Controls.Add(bottom);
            Controls.SetChildIndex(banner, 3);
            Controls.SetChildIndex(_diagWrap, 2);
            Controls.SetChildIndex(bottom, 1);
            Controls.SetChildIndex(host, 0);

            Theme.Apply(this);
        }

        private static Button MakeBtn(string text, int w, DockStyle dock)
        {
            var b = new Button { Text = text, Width = w, Dock = dock, FlatStyle = FlatStyle.Flat, BackColor = Color.White, Margin = new Padding(4, 0, 4, 0) };
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            return b;
        }

        private static Color DiagColor(int level)
        {
            bool d = Theme.Dark;
            if (level == 2) return d ? Color.FromArgb(240, 100, 100) : Color.FromArgb(200, 40, 40);
            if (level == 1) return d ? Color.FromArgb(240, 185, 70) : Color.FromArgb(190, 120, 0);
            return d ? Color.FromArgb(90, 205, 150) : Color.FromArgb(0, 140, 80);
        }

        private Button MakeFixButton(Diagnostics.Finding fd)
        {
            bool primary = fd.Level == 2;
            var b = new Button
            {
                Text = fd.FixLabel, Width = 122, Height = 26, Dock = DockStyle.Right,
                FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 3, 2, 3),
                BackColor = primary ? Accent : (Theme.Dark ? Color.FromArgb(44, 48, 56) : Color.White),
                ForeColor = primary ? Color.White : (Theme.Dark ? Color.FromArgb(210, 216, 222) : Color.FromArgb(40, 44, 52)),
                Font = new Font("Segoe UI", 8.75f), Tag = fd
            };
            b.FlatAppearance.BorderColor = primary ? Accent : Color.FromArgb(120, 126, 134);
            b.Click += (s, e) => OnFix((Diagnostics.Finding)((Button)s).Tag);
            return b;
        }

        private void SetBusy(bool busy)
        {
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            if (_diagTable != null) _diagTable.Enabled = !busy;
        }

        private void Reload()
        {
            Cursor = Cursors.WaitCursor;

            // ---- Diagnostic ----
            _diagTable.SuspendLayout();
            _diagTable.Controls.Clear();
            _diagTable.RowStyles.Clear();
            _diagTable.RowCount = 0;
            var db = new System.Text.StringBuilder();
            int r = 0;
            try
            {
                foreach (Diagnostics.Finding fd in Diagnostics.Run())
                {
                    string icon = fd.Level == 2 ? "✗" : (fd.Level == 1 ? "!" : "✓");
                    db.AppendLine("  [" + icon + "] " + fd.Text);
                    var lbl = new Label
                    {
                        Text = icon + "   " + fd.Text, Dock = DockStyle.Fill, AutoEllipsis = true,
                        TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent,
                        ForeColor = DiagColor(fd.Level), Margin = new Padding(2, 1, 6, 1),
                        Font = new Font("Segoe UI", 9.5f)
                    };
                    _tip.SetToolTip(lbl, fd.Text);
                    _diagTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
                    _diagTable.Controls.Add(lbl, 0, r);
                    if (fd.Fix != FixKind.None)
                        _diagTable.Controls.Add(MakeFixButton(fd), 1, r);
                    r++;
                }
                _diagTable.RowCount = r;
            }
            catch { }
            _diagText = db.Length > 0 ? "[Diagnostic]\n" + db.ToString() : "";
            _diagTable.ResumeLayout();

            // ---- Inventaire ----
            _list.BeginUpdate();
            _list.Items.Clear();
            _list.Groups.Clear();
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

        private void OnFix(Diagnostics.Finding fd)
        {
            try
            {
                switch (fd.Fix)
                {
                    case FixKind.CleanDisk:
                        using (var f = new CleanupForm(_log)) f.ShowDialog(this);
                        Reload();
                        break;

                    case FixKind.Timer1ms:
                        Native.SetTimer1ms(true);
                        if (_log != null) _log("Timer forcé à 1 ms depuis le diagnostic.", 1);
                        Reload();
                        break;

                    case FixKind.DisableVbs:
                        DisableVbs();
                        break;

                    case FixKind.OpenRestore:
                        StartShell("SystemPropertiesProtection.exe", null);
                        break;

                    case FixKind.WindowsUpdate:
                        if (!StartShell("ms-settings:windowsupdate", null))
                            StartShell("control.exe", "/name Microsoft.WindowsUpdate");
                        break;
                }
            }
            catch (Exception ex) { if (_log != null) _log("Action impossible : " + ex.Message, 3); }
        }

        private bool StartShell(string file, string args)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = true };
                if (args != null) psi.Arguments = args;
                System.Diagnostics.Process.Start(psi);
                return true;
            }
            catch { return false; }
        }

        private void DisableVbs()
        {
            if (MessageBox.Show(this,
                    "Désactiver l'intégrité de la mémoire (VBS / HVCI) ?\n\n"
                    + "• Gain : moins de latence et plus de FPS en jeu.\n"
                    + "• Contrepartie : c'est une protection de sécurité de Windows. La désactiver réduit la protection contre certains pilotes malveillants.\n"
                    + "• Réversible : une sauvegarde .reg est créée ; tu peux réactiver via « Rétablir » dans l'app.\n"
                    + "• Un redémarrage est nécessaire pour appliquer.",
                    "Intégrité de la mémoire", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                return;

            Tweak t = Catalog.All().Find(x => x.Id == "vbs_off");
            if (t == null) { if (_log != null) _log("Optimisation « vbs_off » introuvable.", 3); return; }

            SetBusy(true);
            Task.Run(() =>
            {
                EngineResult res = Engine.Run(new List<Tweak> { t }, true, true, false, _log);
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        SetBusy(false);
                        Reload();
                        if (res != null && res.PrepFailed)
                            MessageBox.Show(this, "Échec : " + res.PrepError, "BT Optimizer",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                        else
                            MessageBox.Show(this, "Intégrité de la mémoire désactivée.\nRedémarre pour que le changement prenne effet.",
                                "BT Optimizer", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }));
                }
                catch { }
            });
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
