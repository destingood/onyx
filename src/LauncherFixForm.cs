using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// 🛠 Panneau « Réparer l'installation des jeux » : diagnostic des causes connues d'échec
    /// d'installation/mise à jour des launchers (EA app INST-3-xxxx, Steam « erreur d'écriture
    /// disque », Epic, Battle.net) + réparations ciblées. Les problèmes détectés sont pré-cochés.
    /// </summary>
    internal class LauncherFixForm : Form
    {
        private readonly Action<string, int> _log;
        private CheckedListBox _list;
        private Label _summary;
        private Button _btnScan, _btnFix, _btnClose;
        private List<LauncherFix.Item> _items = new List<LauncherFix.Item>();

        private static readonly Color Accent = Color.FromArgb(0, 150, 90);

        public LauncherFixForm(Action<string, int> log)
        {
            _log = log;
            Build();
            Scan();
            Theme.Apply(this);
        }

        private void Build()
        {
            Text = "DesTinGOOD — Réparer l'installation des jeux";
            ClientSize = new Size(760, 480);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = Color.FromArgb(245, 246, 248);
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  🛠 Un jeu refuse de s'installer ou de se mettre à jour ?",
                Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 12f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            Controls.Add(new Label
            {
                Text = "Erreurs EA app (INST-3-xxxx), « erreur d'écriture disque » Steam, installs Epic qui échouent, "
                     + "Battle.net qui boucle sur une mise à jour : voici les causes connues côté Windows. "
                     + "On ne vide que des caches régénérés automatiquement — jamais tes jeux. Les problèmes sont pré-cochés ⚠.",
                Location = new Point(18, 58), Size = new Size(724, 50), ForeColor = Color.FromArgb(60, 64, 72)
            });

            _list = new CheckedListBox
            {
                Location = new Point(18, 114), Size = new Size(724, 258), CheckOnClick = true,
                BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 9.5f),
                IntegralHeight = false, HorizontalScrollbar = true
            };
            Controls.Add(_list);

            _summary = new Label
            {
                Location = new Point(18, 380), Size = new Size(724, 40),
                Font = new Font("Segoe UI Semibold", 9.5f), ForeColor = Color.FromArgb(60, 64, 72)
            };
            Controls.Add(_summary);

            _btnScan = MakeBtn("Analyser à nouveau", 18, 428, 160, 38, false);
            _btnScan.Click += (s, e) => Scan();
            _btnFix = MakeBtn("RÉPARER LA SÉLECTION", 188, 428, 240, 38, true);
            _btnFix.Click += OnFix;
            _btnClose = MakeBtn("Fermer", 652, 428, 90, 38, false);
            _btnClose.Click += (s, e) => Close();
            Controls.Add(_btnScan); Controls.Add(_btnFix); Controls.Add(_btnClose);
        }

        private static Button MakeBtn(string text, int x, int y, int w, int h, bool primary)
        {
            var b = new Button
            {
                Text = text, Location = new Point(x, y), Size = new Size(w, h), FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Accent : Color.White,
                ForeColor = primary ? Color.White : Color.FromArgb(40, 44, 52),
                Font = primary ? new Font("Segoe UI Semibold", 9.5f) : new Font("Segoe UI", 9f)
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            return b;
        }

        private void SetBusy(bool busy)
        {
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            _btnScan.Enabled = !busy; _btnFix.Enabled = !busy; _list.Enabled = !busy;
        }

        private void Scan()
        {
            SetBusy(true);
            _summary.Text = "Analyse en cours...";
            Task.Run(() =>
            {
                List<LauncherFix.Item> items = new List<LauncherFix.Item>();
                try { items = LauncherFix.Analyze(); }
                catch (Exception ex) { _log("Analyse launchers : " + ex.Message, 3); }
                try { BeginInvoke((Action)(() => Populate(items))); } catch { }
            });
        }

        private void Populate(List<LauncherFix.Item> items)
        {
            _items = items;
            _list.Items.Clear();
            int problems = 0;
            foreach (LauncherFix.Item it in items)
            {
                if (it.Problem) problems++;
                string prefix = it.Problem ? "⚠  " : "✔  ";
                _list.Items.Add(prefix + it.Name + "   —   " + it.Status, it.DefaultCheck);
            }
            _summary.Text = problems == 0
                ? "✔ Aucune cause bloquante détectée. Si l'install échoue encore, coche un cache launcher et répare-le."
                : problems + " cause(s) probable(s) détectée(s) — coche puis clique RÉPARER LA SÉLECTION.";
            SetBusy(false);
        }

        private void OnFix(object sender, EventArgs e)
        {
            var sel = new List<LauncherFix.Item>();
            for (int i = 0; i < _list.Items.Count && i < _items.Count; i++)
                if (_list.GetItemChecked(i)) sel.Add(_items[i]);
            if (sel.Count == 0)
            {
                MessageBox.Show(this, "Coche au moins un point à réparer.", "DesTinGOOD",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(this,
                    "Réparer " + sel.Count + " point(s) ?\n\n"
                    + "Ferme d'abord le launcher concerné (EA app, Steam, Epic, Battle.net) pour que les caches "
                    + "puissent être vidés.\n\nAucun jeu n'est supprimé — uniquement des caches régénérés.",
                    "Réparer", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            SetBusy(true);
            _summary.Text = "Réparation en cours...";
            Task.Run(() =>
            {
                foreach (LauncherFix.Item it in sel)
                {
                    try { it.Fix(_log); }
                    catch (Exception ex) { _log("Réparation « " + it.Name + " » : " + ex.Message, 3); }
                }
                _log("Réparation launchers : " + sel.Count + " point(s) traité(s).", 1);
                List<LauncherFix.Item> after = new List<LauncherFix.Item>();
                try { after = LauncherFix.Analyze(); } catch { }
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        Populate(after);
                        MessageBox.Show(this, "Réparation terminée. Relance ton launcher et retente l'installation.",
                            "DesTinGOOD", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }));
                }
                catch { }
            });
        }
    }
}
