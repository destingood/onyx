using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Panneau « Fenêtres qui saccadent » : analyse la configuration d'AFFICHAGE (et pas le
    /// matériel, qui n'y est presque jamais pour rien), classe les causes par impact, et propose
    /// une correction à la fois — parce qu'en changer deux d'un coup empêche de savoir laquelle
    /// a agi. Chaque correction est réversible et annoncée avant d'être appliquée.
    /// </summary>
    internal class WindowLagForm : Form
    {
        private readonly Action<string, int> _log;
        private FlowLayoutPanel _list;
        private Label _head;
        private Button _scan;
        private WindowLag.Report _rep;
        private int[] _hzBefore;                     // fréquences d'origine, pour le retour arrière
        private string[] _hzDevices;
        private Button _undoHz;
        private volatile bool _busy;

        public WindowLagForm(Action<string, int> log)
        {
            _log = log ?? delegate { };
            Text = "ONYX — Fenêtres qui saccadent (bureau, DWM)";
            ClientSize = new Size(700, 640);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = Color.White;
            Font = new Font("Segoe UI", 9.5f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            BuildUi();
            Theme.Apply(this);
            Scan();   // lecture pure : aucune écriture, aucun réseau
        }

        private void BuildUi()
        {
            var banner = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = Color.FromArgb(12, 14, 13) };
            banner.Controls.Add(new Label
            {
                Text = "  Fenêtres qui saccadent — ce que voit le compositeur de Windows", Dock = DockStyle.Fill,
                ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 13f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            _head = new Label
            {
                Location = new Point(20, 74), Size = new Size(660, 40), ForeColor = Theme.InkDimColor,
                Text = "Analyse en cours…"
            };
            Controls.Add(_head);

            _list = new FlowLayoutPanel
            {
                Location = new Point(20, 118), Size = new Size(660, 442), AutoScroll = true,
                FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.PanelColor
            };
            Controls.Add(_list);

            _scan = new Button { Text = "Ré-analyser", Location = new Point(20, 576), Size = new Size(140, 32), FlatStyle = FlatStyle.Flat };
            _scan.Click += (s, e) => Scan();
            Controls.Add(_scan);

            _undoHz = new Button
            {
                Text = "↩ Remettre les fréquences d'origine", Location = new Point(170, 576), Size = new Size(250, 32),
                FlatStyle = FlatStyle.Flat, Visible = false
            };
            _undoHz.Click += (s, e) => UndoHz();
            Controls.Add(_undoHz);

            var lat = new Button { Text = "Latence DPC", Location = new Point(430, 576), Size = new Size(130, 32), FlatStyle = FlatStyle.Flat };
            lat.Click += (s, e) => { try { var f = new LiveMonForm(_log); AnimFx.HookDialog(f); using (f) f.ShowDialog(this); } catch { } };
            Controls.Add(lat);

            var close = new Button { Text = "Fermer", Location = new Point(580, 576), Size = new Size(100, 32), FlatStyle = FlatStyle.Flat };
            close.Click += (s, e) => Close();
            Controls.Add(close);
        }

        private void Scan()
        {
            if (_busy) return;
            _busy = true;
            _scan.Enabled = false;
            _list.Controls.Clear();
            _head.Text = "Analyse en cours…";

            Task.Run(() =>
            {
                WindowLag.Report rep = null;
                try { rep = WindowLag.Analyse(); }
                catch { }
                try { BeginInvoke((Action)(() => { _rep = rep; Render(); _scan.Enabled = true; _busy = false; })); }
                catch { _busy = false; }
            });
        }

        private void Render()
        {
            _list.Controls.Clear();
            if (_rep == null) { _head.Text = "Analyse impossible."; return; }

            int n = _rep.Findings.Count;
            _head.Text = n == 0
                ? "Rien d'anormal côté affichage : ta configuration n'explique pas de saccade. Regarde alors la "
                  + "LATENCE DPC (bouton en bas) : un pilote qui monopolise le CPU fait saccader tout le bureau."
                : n + " cause(s) probable(s), de la plus lourde à la plus légère. Applique-les UNE PAR UNE et "
                  + "teste entre chaque : sinon tu ne sauras pas laquelle a agi.";

            foreach (WindowLag.Finding f in _rep.Findings) _list.Controls.Add(Card(f));
            _undoHz.Visible = _hzBefore != null;
        }

        /// <summary>Une carte de cause : pastille d'impact colorée, explication, bouton de correction.</summary>
        private Control Card(WindowLag.Finding f)
        {
            Color acc = f.Impact >= 70 ? Color.FromArgb(210, 80, 70)
                      : f.Impact >= 40 ? Color.FromArgb(220, 140, 50)
                      : Color.FromArgb(190, 160, 70);
            string level = f.Impact >= 70 ? "CAUSE PRINCIPALE" : f.Impact >= 40 ? "PROBABLE" : "SECONDAIRE";

            var pnl = new Panel { Width = 630, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 10) };
            pnl.Paint += (s, e) =>
            {
                FpsUi.PaintCard(e.Graphics, pnl.ClientRectangle, FpsUi.Card, FpsUi.Border, 10f);
                using (var br = new SolidBrush(acc)) e.Graphics.FillRectangle(br, 1, 10, 3, pnl.Height - 20);
            };

            var pill = new Label
            {
                Text = level + "  ·  impact " + f.Impact, Location = new Point(14, 8), AutoSize = true,
                Font = FpsUi.Tiny, ForeColor = acc, BackColor = Color.Transparent
            };
            var title = new Label
            {
                Text = f.Title, Location = new Point(14, 24), AutoSize = true, MaximumSize = new Size(600, 0),
                Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold), ForeColor = Theme.InkColor, BackColor = Color.Transparent,
                UseMnemonic = false
            };
            pnl.Controls.Add(pill); pnl.Controls.Add(title);
            int y = 24 + title.PreferredSize.Height + 6;

            var body = new Label
            {
                Text = f.Detail, Location = new Point(14, y), AutoSize = true, MaximumSize = new Size(600, 0),
                ForeColor = Theme.InkDimColor, BackColor = Color.Transparent, UseMnemonic = false
            };
            pnl.Controls.Add(body);
            y += body.PreferredSize.Height + 8;

            if (f.Fix != null)
            {
                var go = new Button
                {
                    Text = (f.Manual ? "↗  " : "▶  ") + f.FixLabel, Location = new Point(14, y), Size = new Size(340, 32),
                    FlatStyle = FlatStyle.Flat, UseMnemonic = false
                };
                if (!f.Manual) { go.BackColor = Theme.AccentColor; go.ForeColor = Color.FromArgb(16, 13, 9); go.FlatAppearance.BorderSize = 0; }
                var fin = f;
                go.Click += (s, e) => ApplyFix(fin, go);
                pnl.Controls.Add(go);
                y += 40;
            }
            pnl.Height = y + 6;
            return pnl;
        }

        private void ApplyFix(WindowLag.Finding f, Button go)
        {
            if (!f.Manual)
            {
                if (MessageBox.Show(this,
                        f.Title + "\n\n" + f.FixLabel + " ?\n\n"
                        + "C'est réversible, et il vaut mieux tester APRÈS chaque correction plutôt que "
                        + "d'en appliquer plusieurs d'un coup.",
                        "Fenêtres qui saccadent", MessageBoxButtons.OKCancel, MessageBoxIcon.Question,
                        MessageBoxDefaultButton.Button1) != DialogResult.OK) return;

                // Avant d'aligner les fréquences : mémoriser l'état pour le bouton « remettre ».
                if (f.FixLabel != null && f.FixLabel.StartsWith("Aligner") && _rep != null && _hzBefore == null)
                {
                    int c = _rep.Screens.Count;
                    _hzBefore = new int[c]; _hzDevices = new string[c];
                    for (int i = 0; i < c; i++) { _hzBefore[i] = _rep.Screens[i].CurrentHz; _hzDevices[i] = _rep.Screens[i].Device; }
                }
            }

            try { f.Fix(); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "L'action n'a pas abouti : " + ex.Message, "Fenêtres qui saccadent",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (f.Manual) return;
            go.Text = "✓  Appliqué"; go.Enabled = false;
            _log("Fenêtres qui saccadent : " + f.FixLabel, 1);
            MessageBox.Show(this,
                "Appliqué.\n\nDéplace maintenant une fenêtre pendant quelques secondes :\n"
                + "• c'est fluide → tu tiens la cause, garde ce réglage ;\n"
                + "• c'est pareil → remets-le comme avant et passe à la cause suivante.\n\n"
                + "(Certaines corrections ne prennent effet qu'après un redémarrage — c'est indiqué dans leur libellé.)",
                "Fenêtres qui saccadent", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Scan();
        }

        private void UndoHz()
        {
            if (_hzBefore == null) return;
            for (int i = 0; i < _hzBefore.Length; i++)
                if (_hzDevices[i] != null && _hzBefore[i] > 0) DisplayInfo.SetHz(_hzDevices[i], _hzBefore[i]);
            _hzBefore = null; _hzDevices = null;
            _undoHz.Visible = false;
            _log("Fréquences d'écran remises à leur valeur d'origine.", 1);
            Scan();
        }
    }
}
