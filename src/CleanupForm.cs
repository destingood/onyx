using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>Nettoyage disque : dossiers temporaires sûrs, avec tailles et sélection.</summary>
    internal class CleanupForm : Form
    {
        private readonly Action<string, int> _log;
        private CheckedListBox _list;
        private Label _total;
        private Button _btnAnalyze, _btnClean, _btnClose;
        private List<Sys.CleanTarget> _targets = new List<Sys.CleanTarget>();

        private static readonly Color Accent = Color.FromArgb(79, 70, 229);

        public CleanupForm(Action<string, int> log)
        {
            _log = log;
            Build();
            Analyze();
            Theme.Apply(this);
        }

        private void Build()
        {
            Text = "Fluide — Nettoyage disque";
            ClientSize = new Size(520, 380);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = Color.FromArgb(245, 246, 248);
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  Nettoyage disque", Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 13f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            _list = new CheckedListBox
            {
                Location = new Point(18, 66), Size = new Size(484, 220), CheckOnClick = true,
                BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 9.5f), IntegralHeight = false
            };
            Controls.Add(_list);

            _total = new Label { Location = new Point(18, 294), Size = new Size(484, 22), ForeColor = Color.FromArgb(60, 64, 72), Font = new Font("Segoe UI Semibold", 9.5f) };
            Controls.Add(_total);

            _btnAnalyze = MakeBtn("Analyser à nouveau", 18, 328, 150, 36, false);
            _btnAnalyze.Click += (s, e) => Analyze();
            _btnClean = MakeBtn("Nettoyer la sélection", 178, 328, 200, 36, true);
            _btnClean.Click += OnClean;
            _btnClose = MakeBtn("Fermer", 412, 328, 90, 36, false);
            _btnClose.Click += (s, e) => Close();
            Controls.Add(_btnAnalyze); Controls.Add(_btnClean); Controls.Add(_btnClose);
        }

        private static Button MakeBtn(string text, int x, int y, int w, int h, bool primary)
        {
            var b = new Button
            {
                Text = text, Location = new Point(x, y), Size = new Size(w, h), FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Color.FromArgb(40, 44, 52),
                Font = primary ? new Font("Segoe UI Semibold", 10f) : new Font("Segoe UI", 9f)
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            return b;
        }

        private void SetBusy(bool busy)
        {
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            _btnAnalyze.Enabled = !busy; _btnClean.Enabled = !busy; _list.Enabled = !busy;
        }

        private void Analyze()
        {
            SetBusy(true);
            _total.Text = "Analyse en cours...";
            Task.Run(() =>
            {
                List<Sys.CleanTarget> t = Sys.CleanTargets();
                try { BeginInvoke((Action)(() => Populate(t))); } catch { }
            });
        }

        private void Populate(List<Sys.CleanTarget> targets)
        {
            _targets = targets;
            _list.Items.Clear();
            long sum = 0;
            foreach (Sys.CleanTarget t in targets)
            {
                _list.Items.Add(string.Format("{0}   —   {1:N0} Mo", t.Name, t.SizeMB), t.SizeMB > 0);
                sum += t.SizeMB;
            }
            _total.Text = string.Format("Total récupérable : {0:N0} Mo", sum);
            SetBusy(false);
        }

        private void OnClean(object sender, EventArgs e)
        {
            var sel = new List<Sys.CleanTarget>();
            for (int i = 0; i < _list.Items.Count && i < _targets.Count; i++)
                if (_list.GetItemChecked(i)) sel.Add(_targets[i]);
            if (sel.Count == 0) return;

            if (MessageBox.Show(this,
                    "Supprimer définitivement le contenu des " + sel.Count + " emplacement(s) cochés ?\n"
                    + "(Fichiers temporaires — cette action n'est pas réversible, mais ces dossiers se régénèrent.)",
                    "Nettoyage disque", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                return;

            SetBusy(true);
            Task.Run(() =>
            {
                foreach (Sys.CleanTarget t in sel) Sys.CleanTargetNow(t, _log);
                List<Sys.CleanTarget> after = Sys.CleanTargets();
                try { BeginInvoke((Action)(() => { Populate(after); _log("Nettoyage terminé.", 1); })); } catch { }
            });
        }
    }
}
