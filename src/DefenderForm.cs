using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Exclusions antivirus pour les jeux : l'analyse en temps réel de Windows Defender
    /// scanne les fichiers de jeu à chaque accès → saccades et chargements plus longs.
    /// Exclure les dossiers de jeux DE CONFIANCE supprime ce coût. Réversible. Honnête sur
    /// le compromis : n'exclus QUE des installations de jeux que tu sais saines.
    /// </summary>
    internal class DefenderForm : Form
    {
        private readonly Action<string, int> _log;
        private CheckedListBox _list;
        private Label _summary;
        private Button _btnApply, _btnScan, _btnClose;
        private List<Row> _rows = new List<Row>();

        private static readonly Color Accent = Theme.AccentColor;

        private class Row { public string Name; public string Path; public bool Excluded; }

        public DefenderForm(Action<string, int> log)
        {
            _log = log;
            Build();
            Scan();
            Theme.Apply(this);
        }

        private void Build()
        {
            Text = "ONYX — Exclusions antivirus (jeux)";
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
                Text = "  Exclusions antivirus — moins de saccades dans les jeux",
                Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 12f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            var intro = new Label
            {
                Text = "Windows Defender scanne les fichiers de jeu à chaque accès, ce qui cause des micro-saccades et "
                     + "rallonge les chargements. Exclure les dossiers de jeux réputés sûrs supprime ce coût.\n"
                     + "⚠ Compromis de sécurité : n'exclus QUE des installations de jeux que tu sais saines. Réversible.",
                Location = new Point(18, 58), Size = new Size(624, 56), ForeColor = Color.FromArgb(60, 64, 72)
            };
            Controls.Add(intro);

            _list = new CheckedListBox
            {
                Location = new Point(18, 120), Size = new Size(624, 236), CheckOnClick = true,
                BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 9.5f), IntegralHeight = false,
                HorizontalScrollbar = true
            };
            Controls.Add(_list);

            _summary = new Label
            {
                Location = new Point(18, 362), Size = new Size(624, 22),
                Font = new Font("Segoe UI Semibold", 9.5f), ForeColor = Color.FromArgb(60, 64, 72)
            };
            Controls.Add(_summary);

            _btnScan = MakeBtn("Ré-analyser", 18, 392, 140, 38, false);
            _btnScan.Click += (s, e) => Scan();
            _btnApply = MakeBtn("APPLIQUER LES EXCLUSIONS COCHÉES", 168, 392, 320, 38, true);
            _btnApply.Click += OnApply;
            _btnClose = MakeBtn("Fermer", 552, 392, 90, 38, false);
            _btnClose.Click += (s, e) => Close();
            Controls.Add(_btnScan); Controls.Add(_btnApply); Controls.Add(_btnClose);
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
            _btnScan.Enabled = !busy; _btnApply.Enabled = !busy; _list.Enabled = !busy;
        }

        private void Scan()
        {
            SetBusy(true);
            _summary.Text = "Détection des jeux et lecture des exclusions...";
            Task.Run(() =>
            {
                var games = GameScan.Known();
                GameScan.Detect(games);
                List<string> excl = Sys.DefenderExclusions();
                var rows = new List<Row>();
                foreach (GameScan.GameInfo g in games)
                {
                    if (!g.Detected || string.IsNullOrEmpty(g.InstallPath)) continue;
                    bool already = excl.Any(e => string.Equals(e.TrimEnd('\\'), g.InstallPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
                    rows.Add(new Row { Name = g.Name, Path = g.InstallPath, Excluded = already });
                }
                try { BeginInvoke((Action)(() => Populate(rows))); } catch { }
            });
        }

        private void Populate(List<Row> rows)
        {
            _rows = rows;
            _list.Items.Clear();
            int on = 0;
            foreach (Row r in rows)
            {
                if (r.Excluded) on++;
                _list.Items.Add((r.Excluded ? "✔ " : "   ") + r.Name + "   —   " + r.Path, r.Excluded);
            }
            if (rows.Count == 0)
                _summary.Text = "Aucun jeu détecté avec un dossier d'installation connu. (Rien à exclure automatiquement.)";
            else
                _summary.Text = on + " jeu(x) déjà exclu(s) sur " + rows.Count + " détecté(s). Coche/décoche puis applique.";
            SetBusy(false);
        }

        private void OnApply(object sender, EventArgs e)
        {
            if (_rows.Count == 0) return;
            var wanted = new bool[_rows.Count];
            for (int i = 0; i < _rows.Count; i++) wanted[i] = _list.GetItemChecked(i);

            int toAdd = 0, toRemove = 0;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (wanted[i] && !_rows[i].Excluded) toAdd++;
                else if (!wanted[i] && _rows[i].Excluded) toRemove++;
            }
            if (toAdd == 0 && toRemove == 0) { MessageBox.Show(this, "Aucun changement.", "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

            string msg = "Appliquer les exclusions antivirus ?\n\n";
            if (toAdd > 0) msg += "• " + toAdd + " dossier(s) de jeu à EXCLURE de l'analyse (moins de saccades).\n";
            if (toRemove > 0) msg += "• " + toRemove + " exclusion(s) à RETIRER (analyse rétablie).\n";
            msg += "\nN'exclus que des jeux que tu sais sains.";
            if (MessageBox.Show(this, msg, "Exclusions antivirus", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                return;

            SetBusy(true);
            Task.Run(() =>
            {
                int added = 0, removed = 0;
                for (int i = 0; i < _rows.Count; i++)
                {
                    Row r = _rows[i];
                    if (wanted[i] && !r.Excluded) { if (Sys.DefenderAddExclusion(r.Path, _log)) { added++; _log("Exclusion ajoutée : " + r.Name, 1); } }
                    else if (!wanted[i] && r.Excluded) { if (Sys.DefenderRemoveExclusion(r.Path, _log)) { removed++; _log("Exclusion retirée : " + r.Name, 0); } }
                }
                _log("Exclusions antivirus : " + added + " ajoutée(s), " + removed + " retirée(s).", 1);
                try { BeginInvoke((Action)Scan); } catch { }
            });
        }
    }
}
