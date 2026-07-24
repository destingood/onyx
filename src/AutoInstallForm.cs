using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace BTOptimizer
{
    // ----------------------------------------------------------------------
    //  Prérequis & installation automatique. Les bibliothèques indispensables
    //  aux jeux (VC++, DirectX, .NET, OpenAL) manquantes s'installent en 1 clic
    //  via winget. Trois modes faciles à régler : proposer / automatique / off.
    //  Cet écran est aussi la « proposition » affichée au premier lancement.
    // ----------------------------------------------------------------------
    internal class AutoInstallForm : Form
    {
        private readonly Action<string, int> _log;
        private RadioButton _rbAsk, _rbAuto, _rbOff;
        private Panel _list;
        private Label _status;
        private Button _btnInstall, _btnClose;
        private bool _busy;

        public AutoInstallForm(Action<string, int> log)
        {
            _log = log;
            BuildUi();
            Theme.Apply(this);
            LoadMode();
            RefreshList();
        }

        private void BuildUi()
        {
            Text = "ONYX — Prérequis & installation automatique";
            ClientSize = new Size(600, 470);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            Font = new Font("Segoe UI", 9f);

            var intro = new Label();
            intro.SetBounds(16, 12, 568, 52);
            intro.Text = "Certains jeux ne démarrent pas sans leurs prérequis (Visual C++, DirectX, .NET, "
                       + "OpenAL). ONYX peut les installer pour toi via winget (Microsoft). "
                       + "Choisis comment — c'est modifiable à tout moment ici.";
            intro.ForeColor = Theme.InkDimColor;

            // Modes (faciles à régler).
            _rbAsk = MakeRadio("Me le proposer au démarrage (recommandé)", 16, 72);
            _rbAuto = MakeRadio("Installer automatiquement, sans me demander", 16, 98);
            _rbOff = MakeRadio("Ne rien faire au démarrage", 16, 124);
            _rbAsk.CheckedChanged += OnModeChanged;
            _rbAuto.CheckedChanged += OnModeChanged;
            _rbOff.CheckedChanged += OnModeChanged;

            var sep = new Label();
            sep.SetBounds(16, 156, 568, 1);
            sep.BorderStyle = BorderStyle.Fixed3D;

            var head = new Label();
            head.SetBounds(16, 166, 568, 20);
            head.Font = new Font("Segoe UI Semibold", 9f);
            head.Text = "Prérequis des jeux";

            _list = new Panel();
            _list.SetBounds(16, 190, 568, 190);
            _list.AutoScroll = true;
            _list.BackColor = Color.White;

            _status = new Label();
            _status.SetBounds(16, 386, 568, 20);
            _status.ForeColor = Theme.InkDimColor;

            _btnInstall = MakeBtn("⬇ Installer les prérequis manquants", 16, 414, 320, 40, true);
            _btnInstall.Click += OnInstallClicked;
            _btnClose = MakeBtn("Fermer", 480, 414, 104, 40, false);
            _btnClose.Click += (s, e) => Close();

            Controls.AddRange(new Control[] { intro, _rbAsk, _rbAuto, _rbOff, sep, head, _list, _status, _btnInstall, _btnClose });
        }

        private void LoadMode()
        {
            string m = AutoInstall.Mode;
            _rbAuto.Checked = m == AutoInstall.ModeAuto;
            _rbOff.Checked = m == AutoInstall.ModeOff;
            _rbAsk.Checked = !(_rbAuto.Checked || _rbOff.Checked);
        }

        private void OnModeChanged(object sender, EventArgs e)
        {
            var rb = sender as RadioButton;
            if (rb == null || !rb.Checked) return;
            string m = _rbAuto.Checked ? AutoInstall.ModeAuto : _rbOff.Checked ? AutoInstall.ModeOff : AutoInstall.ModeAsk;
            AutoInstall.SetMode(m);
            if (_log != null)
                _log(m == AutoInstall.ModeAuto ? "Prérequis : installation automatique au démarrage activée."
                   : m == AutoInstall.ModeOff ? "Prérequis : installation automatique désactivée."
                   : "Prérequis : proposition au démarrage (par défaut).", 0);
        }

        private void RefreshList()
        {
            _list.SuspendLayout();
            _list.Controls.Clear();
            int y = 6, missing = 0;
            try
            {
                foreach (LibScan.LibItem it in LibScan.Items())
                {
                    if (!it.Essential) continue;
                    bool ok = false;
                    try { ok = it.Installed(); } catch { }
                    if (!ok) missing++;

                    var dot = new Label();
                    dot.SetBounds(8, y + 1, 18, 18);
                    dot.Text = ok ? "✔" : "○";
                    dot.ForeColor = ok ? Theme.OkColor : Color.FromArgb(200, 120, 0);
                    dot.Font = new Font("Segoe UI Semibold", 9f);

                    var name = new Label();
                    name.SetBounds(30, y + 1, 520, 18);
                    name.Text = it.Name + (ok ? "" : "   — manquant");
                    name.ForeColor = ok ? Theme.InkDimColor : Theme.InkColor;

                    _list.Controls.Add(dot);
                    _list.Controls.Add(name);
                    y += 24;
                }
            }
            catch { }
            _list.ResumeLayout();

            _btnInstall.Enabled = !_busy && missing > 0;
            _btnInstall.Text = missing > 0 ? "⬇ Installer les " + missing + " prérequis manquants"
                                           : "✔ Tous les prérequis sont présents";
            if (!_busy)
                SetStatus(missing == 0 ? "Rien à installer : tout est là. ✔"
                                       : missing + " prérequis manquant(s) — un clic pour tout installer.",
                          missing == 0 ? 1 : 0);
        }

        private void OnInstallClicked(object sender, EventArgs e)
        {
            if (_busy) return;
            List<LibScan.LibItem> missing = AutoInstall.MissingEssentials();
            if (missing.Count == 0) { RefreshList(); return; }
            _busy = true;
            SetBusyUi(true);
            SetStatus("Installation de " + missing.Count + " prérequis via winget — patiente...", 0);
            AutoInstall.InstallInBackground(this, missing, _log, (ok, total) =>
            {
                _busy = false;
                SetBusyUi(false);
                RefreshList();
                SetStatus("Terminé : " + ok + "/" + total + " prérequis installé(s)."
                    + (ok < total ? " Les échecs restent installables depuis « Bibliothèques »." : ""),
                    ok == total ? 1 : 2);
            });
        }

        private void SetBusyUi(bool busy)
        {
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            _btnInstall.Enabled = !busy;
            _rbAsk.Enabled = _rbAuto.Enabled = _rbOff.Enabled = !busy;
        }

        private void SetStatus(string text, int level)
        {
            _status.Text = text;
            _status.ForeColor = level == 1 ? Theme.OkColor : level >= 2 ? Color.FromArgb(200, 120, 0) : Theme.InkDimColor;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_busy) { e.Cancel = true; SetStatus("Patiente : installation en cours...", 2); return; }
            base.OnFormClosing(e);
        }

        private RadioButton MakeRadio(string text, int x, int y)
        {
            var rb = new RadioButton();
            rb.Text = text;
            rb.SetBounds(x, y, 560, 22);
            rb.Font = new Font("Segoe UI", 9f);
            return rb;
        }

        private static Button MakeBtn(string text, int x, int y, int w, int h, bool primary)
        {
            var b = new Button();
            b.Text = text;
            b.SetBounds(x, y, w, h);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = primary ? 0 : 1;
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            b.BackColor = primary ? Color.FromArgb(0, 150, 90) : Color.White;
            b.ForeColor = primary ? Color.White : Color.FromArgb(40, 44, 52);
            b.UseVisualStyleBackColor = false;
            return b;
        }
    }
}
