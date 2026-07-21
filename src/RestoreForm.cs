using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Points de restauration système : le filet de sécurité ultime. Crée un point AVANT
    /// de bidouiller, liste les points existants, et ouvre la restauration Windows pour
    /// revenir en arrière si besoin. Active la restauration système si un outil l'a coupée.
    /// </summary>
    internal class RestoreForm : Form
    {
        private readonly Action<string, int> _log;
        private ListView _list;
        private Label _summary;
        private Button _btnCreate, _btnEnable, _btnRollback, _btnScan, _btnClose;

        private static readonly Color Accent = Color.FromArgb(0, 150, 90);

        public RestoreForm(Action<string, int> log)
        {
            _log = log;
            Build();
            Scan();
            Theme.Apply(this);
        }

        private void Build()
        {
            Text = "DesTinGOOD — Points de restauration";
            ClientSize = new Size(660, 470);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = Color.FromArgb(245, 246, 248);
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  Points de restauration — reviens en arrière en sécurité",
                Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 12.5f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            var intro = new Label
            {
                Text = "Un point de restauration = une photo du système Windows à un instant T. Crées-en un AVANT "
                     + "de gros changements : en cas de souci, tu remets Windows exactement dans cet état (tes fichiers "
                     + "personnels ne sont pas touchés).",
                Location = new Point(18, 58), Size = new Size(624, 44), ForeColor = Color.FromArgb(60, 64, 72)
            };
            Controls.Add(intro);

            _list = new ListView
            {
                Location = new Point(18, 108), Size = new Size(624, 218),
                View = View.Details, FullRowSelect = true, GridLines = false, MultiSelect = false
            };
            _list.Columns.Add("Date", 160);
            _list.Columns.Add("Description", 380);
            _list.Columns.Add("N°", 60, HorizontalAlignment.Right);
            Controls.Add(_list);

            _summary = new Label
            {
                Location = new Point(18, 332), Size = new Size(624, 22),
                Font = new Font("Segoe UI Semibold", 9.5f), ForeColor = Color.FromArgb(60, 64, 72)
            };
            Controls.Add(_summary);

            _btnCreate = MakeBtn("➕ Créer un point maintenant", 18, 362, 220, 38, true);
            _btnCreate.Click += OnCreate;
            _btnRollback = MakeBtn("Restaurer Windows (rstrui)...", 250, 362, 220, 38, false);
            _btnRollback.ForeColor = Color.FromArgb(180, 70, 20);
            _btnRollback.Click += (s, e) => { try { Process.Start(new ProcessStartInfo("rstrui.exe") { UseShellExecute = true }); } catch (Exception ex) { _log("rstrui : " + ex.Message, 2); } };
            _btnEnable = MakeBtn("Activer la restauration système", 482, 362, 160, 38, false);
            _btnEnable.Click += OnEnable;
            Controls.Add(_btnCreate); Controls.Add(_btnRollback); Controls.Add(_btnEnable);

            _btnScan = MakeBtn("Rafraîchir", 18, 414, 120, 34, false);
            _btnScan.Click += (s, e) => Scan();
            _btnClose = MakeBtn("Fermer", 552, 414, 90, 34, false);
            _btnClose.Click += (s, e) => Close();
            Controls.Add(_btnScan); Controls.Add(_btnClose);
        }

        private static Button MakeBtn(string text, int x, int y, int w, int h, bool primary)
        {
            var b = new Button
            {
                Text = text, Location = new Point(x, y), Size = new Size(w, h), FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Color.FromArgb(40, 44, 52),
                Font = primary ? new Font("Segoe UI Semibold", 9.5f) : new Font("Segoe UI", 9f)
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            return b;
        }

        private void SetBusy(bool busy)
        {
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            _btnCreate.Enabled = !busy; _btnEnable.Enabled = !busy; _btnScan.Enabled = !busy; _list.Enabled = !busy;
        }

        private void Scan()
        {
            SetBusy(true);
            _summary.Text = "Lecture des points de restauration...";
            Task.Run(() =>
            {
                List<Sys.RestorePoint> points = Sys.ListRestorePoints();
                try { BeginInvoke((Action)(() => Populate(points))); } catch { }
            });
        }

        private void Populate(List<Sys.RestorePoint> points)
        {
            _list.Items.Clear();
            foreach (Sys.RestorePoint p in points)
            {
                var it = new ListViewItem(p.When == DateTime.MinValue ? "?" : p.When.ToString("dd/MM/yyyy HH:mm"));
                it.SubItems.Add(p.Description);
                it.SubItems.Add(p.Seq.ToString());
                _list.Items.Add(it);
            }
            _summary.Text = points.Count == 0
                ? "Aucun point de restauration. Crées-en un (ou active la restauration système si le bouton échoue)."
                : points.Count + " point(s) de restauration. Le plus récent : " + points[0].When.ToString("dd/MM/yyyy HH:mm") + ".";
            SetBusy(false);
        }

        private void OnCreate(object sender, EventArgs e)
        {
            string desc = "DesTinGOOD " + DateTime.Now.ToString("dd/MM HH:mm");
            SetBusy(true);
            _summary.Text = "Création du point de restauration (jusqu'à 1 min)...";
            Task.Run(() =>
            {
                Sys.CreateRestorePoint(desc, _log);
                System.Threading.Thread.Sleep(1500);
                List<Sys.RestorePoint> points = Sys.ListRestorePoints();
                try { BeginInvoke((Action)(() => Populate(points))); } catch { }
            });
        }

        private void OnEnable(object sender, EventArgs e)
        {
            SetBusy(true);
            Task.Run(() =>
            {
                Sys.EnableSystemRestore(_log);
                try { BeginInvoke((Action)Scan); } catch { }
            });
        }
    }
}
