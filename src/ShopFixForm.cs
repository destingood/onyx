using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Panneau « Boutiques en jeu qui chargent à l'infini » : analyse des causes connues
    /// (DNS filtrant, hosts, services Store/Xbox, politiques, IPv6, proxy, heure, cache
    /// web Steam) et réparation en un clic des points cochés.
    /// </summary>
    internal class ShopFixForm : Form
    {
        private readonly Action<string, int> _log;
        private CheckedListBox _list;
        private Label _summary;
        private Button _btnAnalyze, _btnRepair, _btnClose;
        private List<ShopFix.Item> _items = new List<ShopFix.Item>();

        private static readonly Color Accent = Color.FromArgb(0, 150, 90);

        public ShopFixForm(Action<string, int> log)
        {
            _log = log;
            Build();
            Analyze();
            Theme.Apply(this);
        }

        private void Build()
        {
            Text = "DesTinGOOD — Boutiques & contenu en jeu";
            ClientSize = new Size(680, 500);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = Color.FromArgb(245, 246, 248);
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  Boutiques / contenu en jeu qui chargent à l'infini — diagnostic & réparation",
                Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 12f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            var intro = new Label
            {
                Text = "Boutique Steam vide, articles/skins qui tournent sans fin, boutique intégrée d'un jeu morte ? "
                     + "Chaque point ci-dessous est une cause connue. Les points ⚠ détectés sont pré-cochés ; "
                     + "la réparation est sans danger et journalisée.",
                Location = new Point(18, 60), Size = new Size(644, 46), ForeColor = Color.FromArgb(60, 64, 72)
            };
            Controls.Add(intro);

            _list = new CheckedListBox
            {
                Location = new Point(18, 110), Size = new Size(644, 268), CheckOnClick = true,
                BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 9.5f), IntegralHeight = false,
                HorizontalScrollbar = true
            };
            Controls.Add(_list);

            _summary = new Label
            {
                Location = new Point(18, 386), Size = new Size(644, 22),
                ForeColor = Color.FromArgb(60, 64, 72), Font = new Font("Segoe UI Semibold", 9.5f)
            };
            Controls.Add(_summary);

            var note = new Label
            {
                Text = "Note : certains points annulent des optimisations agressives (elles apparaîtront « non actives » "
                     + "dans la fenêtre principale). Le cache Steam ferme Steam le temps du nettoyage.",
                Location = new Point(18, 408), Size = new Size(644, 34), ForeColor = Color.Gray,
                Font = new Font("Segoe UI", 8.5f)
            };
            Controls.Add(note);

            _btnAnalyze = MakeBtn("Analyser à nouveau", 18, 448, 160, 38, false);
            _btnAnalyze.Click += (s, e) => Analyze();
            _btnRepair = MakeBtn("RÉPARER LA SÉLECTION", 188, 448, 250, 38, true);
            _btnRepair.Click += OnRepair;
            _btnClose = MakeBtn("Fermer", 572, 448, 90, 38, false);
            _btnClose.Click += (s, e) => Close();
            Controls.Add(_btnAnalyze); Controls.Add(_btnRepair); Controls.Add(_btnClose);
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
            _btnAnalyze.Enabled = !busy; _btnRepair.Enabled = !busy; _list.Enabled = !busy;
        }

        private void Analyze()
        {
            SetBusy(true);
            _summary.Text = "Analyse en cours...";
            Task.Run(() =>
            {
                List<ShopFix.Item> items = ShopFix.Analyze();
                try { BeginInvoke((Action)(() => Populate(items))); } catch { }
            });
        }

        private void Populate(List<ShopFix.Item> items)
        {
            _items = items;
            _list.Items.Clear();
            int problems = 0;
            foreach (ShopFix.Item it in items)
            {
                if (it.Problem) problems++;
                string prefix = it.Problem ? "⚠  " : "✔  ";
                _list.Items.Add(prefix + it.Name + "   —   " + it.Status, it.DefaultCheck);
            }
            _summary.Text = problems == 0
                ? "Aucune cause probable détectée côté Windows. Pense au cache Steam (coche-le) si le problème persiste."
                : problems + " cause(s) probable(s) détectée(s) — clique sur RÉPARER LA SÉLECTION.";
            SetBusy(false);
        }

        private void OnRepair(object sender, EventArgs e)
        {
            var sel = new List<ShopFix.Item>();
            for (int i = 0; i < _list.Items.Count && i < _items.Count; i++)
                if (_list.GetItemChecked(i)) sel.Add(_items[i]);
            if (sel.Count == 0)
            {
                MessageBox.Show(this, "Coche au moins un point à réparer.", "DesTinGOOD",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            bool closesSteam = false, needReboot = false, securitySensitive = false;
            foreach (ShopFix.Item it in sel)
            {
                if (it.ClosesSteam) closesSteam = true;
                if (it.NeedReboot) needReboot = true;
                if (it.SecuritySensitive) securitySensitive = true;
            }

            string msg = "Réparer les " + sel.Count + " point(s) cochés ?";
            if (closesSteam) msg += "\n\n• Steam sera fermé proprement pour vider son cache web (les téléchargements en cours reprendront à la réouverture).";
            if (securitySensitive) msg += "\n• Le DNS filtrant (anti-malware / contrôle parental) sera remis en AUTOMATIQUE : cette protection sera retirée.";
            if (needReboot) msg += "\n• Un point nécessite un REDÉMARRAGE pour prendre effet.";
            if (MessageBox.Show(this, msg, "Boutiques & contenu en jeu",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            SetBusy(true);
            _summary.Text = "Réparation en cours...";
            Task.Run(() =>
            {
                foreach (ShopFix.Item it in sel)
                {
                    try { it.Repair(_log); }
                    catch (Exception ex) { _log("Réparation « " + it.Name + " » : " + ex.Message, 3); }
                }
                _log("Boutiques & contenu en jeu : réparation terminée (" + sel.Count + " point(s))."
                     + " Relance le jeu / Steam pour vérifier.", 1);
                List<ShopFix.Item> after = ShopFix.Analyze();
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        Populate(after);
                        if (needReboot)
                            MessageBox.Show(this,
                                "Réparation terminée. Redémarre le PC pour appliquer le point IPv6.",
                                "DesTinGOOD", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }));
                }
                catch { }
            });
        }
    }
}
