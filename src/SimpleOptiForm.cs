using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    // ----------------------------------------------------------------------
    //  Mode SIMPLE : la gestion des optimisations façon « FPS doctor » —
    //  un interrupteur par réglage qui applique / rétablit IMMÉDIATEMENT
    //  (sauvegarde .reg automatique à chaque geste), état réel affiché,
    //  deux boosts (léger / complet) et un « tout rétablir ».
    //  Le mode expert (fenêtre principale) garde le contrôle fin.
    // ----------------------------------------------------------------------
    internal class SimpleOptiForm : Form
    {
        private readonly Action<string, int> _log;
        private readonly Func<bool> _requirePro;
        private readonly List<Tweak> _items;   // l'essentiel : Recommandé ∪ eSport
        private readonly Dictionary<string, NeonSwitch> _sw = new Dictionary<string, NeonSwitch>();
        private readonly Dictionary<string, Label> _lbl = new Dictionary<string, Label>();

        private Button _btnLight, _btnFull, _btnRevertAll, _btnRescan;
        private Panel _panel;
        private Label _status;
        private bool _busy;   // verrou anti-réentrance : une seule opération à la fois

        private static Color OnColor { get { return Theme.OkColor; } }   // suit le thème (néon en sombre)

        public SimpleOptiForm(List<Tweak> all, Func<bool> requirePro, Action<string, int> log)
        {
            _log = log;
            _requirePro = requirePro;
            _items = all.Where(t => t.Recommended || t.Esport).ToList();
            BuildUi();
            Theme.Apply(this);
            Shown += (s, e) => RefreshStates();   // scan initial en fond (Check() peut être lent)
        }

        private void BuildUi()
        {
            Text = "Optimisations — mode SIMPLE — DesTinGOOD";
            ClientSize = new Size(760, 700);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            Font = new Font("Segoe UI", 9f);

            var intro = new Label();
            intro.SetBounds(16, 10, 728, 34);
            intro.Text = "Chaque interrupteur applique ou rétablit IMMÉDIATEMENT, avec sauvegarde .reg automatique "
                       + "à chaque geste. Ici : l'essentiel. Le mode expert (fenêtre principale) garde tous les réglages.";
            intro.ForeColor = Theme.InkDimColor;

            _btnLight = MakeBtn("⚡ Boost léger (sûr)", 16, 50, 176, 34, true);
            _btnLight.Click += (s, e) => RunPreset(false);
            _btnFull = MakeBtn("🚀 Boost complet", 198, 50, 156, 34, true);
            _btnFull.BackColor = Color.FromArgb(200, 80, 0);
            _btnFull.Click += (s, e) => RunPreset(true);
            _btnRevertAll = MakeBtn("↩ Tout rétablir", 360, 50, 140, 34, false);
            _btnRevertAll.Click += (s, e) => RunRevertAll();
            _btnRescan = MakeBtn("🔄 Re-scan", 506, 50, 108, 34, false);
            _btnRescan.Click += (s, e) => RefreshStates();

            _panel = new Panel();
            _panel.SetBounds(16, 94, 728, 556);
            _panel.AutoScroll = true;
            _panel.BackColor = Color.White;

            var tip = new ToolTip();
            tip.AutoPopDelay = 20000;
            tip.InitialDelay = 350;

            int y = 8;
            foreach (string category in Cat.Order)
            {
                List<Tweak> items = _items.Where(t => t.Category == category).ToList();
                if (items.Count == 0) continue;

                var gb = new GroupBox();
                gb.Text = category;
                gb.SetBounds(8, y, 688, 32 + items.Count * 26);
                gb.Font = new Font("Segoe UI Semibold", 9f);
                gb.ForeColor = Color.FromArgb(50, 70, 130);

                int i = 0;
                foreach (Tweak t in items)
                {
                    // Interrupteur pilule néon + libellé cliquable (façon FPS doctor).
                    var sw = new NeonSwitch();
                    sw.SetBounds(12, 22 + i * 26, 46, 22);
                    sw.Tag = t;
                    sw.CheckedChanged += OnSwitchToggled;   // geste utilisateur uniquement
                    _sw[t.Id] = sw;

                    var lbl = new Label();
                    lbl.Text = t.Name + (t.Reboot ? "  (redémarrage requis)" : "");
                    lbl.SetBounds(66, 24 + i * 26, 606, 20);
                    lbl.Font = new Font("Segoe UI", 9f);
                    lbl.ForeColor = SystemColors.ControlText;
                    lbl.Cursor = Cursors.Hand;
                    NeonSwitch captured = sw;
                    lbl.Click += (s, e) => { if (captured.Enabled) captured.Toggle(); };
                    tip.SetToolTip(lbl, t.Desc);
                    tip.SetToolTip(sw, t.Desc);
                    _lbl[t.Id] = lbl;

                    gb.Controls.Add(sw);
                    gb.Controls.Add(lbl);
                    i++;
                }
                _panel.Controls.Add(gb);
                y += gb.Height + 8;
            }

            _status = new Label();
            _status.SetBounds(16, 656, 728, 38);
            _status.ForeColor = Theme.InkDimColor;
            _status.Text = "Scan de l'état réel en cours...";

            Controls.AddRange(new Control[] { intro, _btnLight, _btnFull, _btnRevertAll, _btnRescan, _panel, _status });
        }

        // --- Interrupteur : application / rétablissement immédiat -------------
        private void OnSwitchToggled(object sender, EventArgs e)
        {
            var sw = (NeonSwitch)sender;
            var t = (Tweak)sw.Tag;
            if (_busy) { sw.SetCheckedSilent(!sw.Checked); return; }   // ceinture (le panel est déjà gelé)
            bool turnOn = sw.Checked;                                  // état APRÈS le geste
            RunOperation(new List<Tweak> { t }, turnOn, false,
                (turnOn ? "Application : " : "Rétablissement : ") + t.Name);
        }

        // --- Presets façon FPS doctor -----------------------------------------
        private void RunPreset(bool full)
        {
            if (_busy) return;
            if (full && _requirePro != null && !_requirePro()) return;
            List<Tweak> targets = _items
                .Where(t => (full || t.Recommended) && !_sw[t.Id].Checked)
                .ToList();
            if (targets.Count == 0)
            {
                SetStatus("Tout est déjà appliqué pour ce boost. ✔", 1);
                return;
            }
            RunOperation(targets, true, true,
                (full ? "Boost complet" : "Boost léger") + " : " + targets.Count + " réglage(s)...");
        }

        private void RunRevertAll()
        {
            if (_busy) return;
            List<Tweak> targets = _items.Where(t => _sw[t.Id].Checked).ToList();
            if (targets.Count == 0)
            {
                SetStatus("Rien à rétablir : tous les interrupteurs sont sur OFF.", 0);
                return;
            }
            RunOperation(targets, false, true, "Rétablissement de " + targets.Count + " réglage(s)...");
        }

        // --- Moteur (fond) + retour d'état réel --------------------------------
        private void RunOperation(List<Tweak> targets, bool apply, bool restorePoint, string intro)
        {
            _busy = true;
            SetBusyUi(true);
            SetStatus(intro, 0);
            Task.Run(() =>
            {
                EngineResult r = Engine.Run(targets, apply, true, restorePoint, RelayLog);

                // État réel APRÈS l'opération (en fond : certains Check passent par netsh).
                var states = new Dictionary<string, bool>();
                foreach (Tweak t in targets)
                {
                    bool? chk = null;
                    try { if (t.Check != null) chk = t.Check(); } catch { }
                    states[t.Id] = chk.HasValue ? chk.Value : apply;   // indéterminé → geste demandé
                }

                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        foreach (KeyValuePair<string, bool> kv in states) SetSwitch(kv.Key, kv.Value);
                        string msg = r.PrepFailed
                            ? "Interrompu : " + r.PrepError
                            : "Terminé : " + r.Ok + " OK" + (r.Ko > 0 ? ", " + r.Ko + " échec(s)" : "")
                              + (r.RebootNeeded ? " — redémarrage conseillé." : ".");
                        SetStatus(msg, r.PrepFailed || r.Ko > 0 ? 2 : 1);
                        _busy = false;
                        SetBusyUi(false);
                    }));
                }
                catch { _busy = false; }
            });
        }

        /// <summary>Re-scan complet de l'état réel (au premier affichage et sur demande).</summary>
        private void RefreshStates()
        {
            if (_busy) return;
            _busy = true;
            SetBusyUi(true);
            SetStatus("Scan de l'état réel...", 0);
            Task.Run(() =>
            {
                var states = new Dictionary<string, bool>();
                foreach (Tweak t in _items)
                {
                    bool on = false;
                    try { on = t.Check != null && t.Check() == true; } catch { }
                    states[t.Id] = on;
                }
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        foreach (KeyValuePair<string, bool> kv in states) SetSwitch(kv.Key, kv.Value);
                        int on = states.Values.Count(v => v);
                        SetStatus(on + " / " + _items.Count + " réglages essentiels actifs. "
                                + "Un clic sur un interrupteur = effet immédiat.", 0);
                        _busy = false;
                        SetBusyUi(false);
                    }));
                }
                catch { _busy = false; }
            });
        }

        private void SetSwitch(string id, bool on)
        {
            NeonSwitch sw;
            if (!_sw.TryGetValue(id, out sw)) return;
            sw.SetCheckedSilent(on);
            Label lbl;
            if (_lbl.TryGetValue(id, out lbl))
                lbl.ForeColor = on ? OnColor : Theme.InkColor;
        }

        private void SetBusyUi(bool busy)
        {
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            _panel.Enabled = !busy;
            _btnLight.Enabled = !busy;
            _btnFull.Enabled = !busy;
            _btnRevertAll.Enabled = !busy;
            _btnRescan.Enabled = !busy;
        }

        private void SetStatus(string text, int level)
        {
            _status.Text = text;
            _status.ForeColor = level == 1 ? OnColor
                              : level >= 2 ? Color.FromArgb(200, 120, 0)
                              : Theme.InkDimColor;
        }

        /// <summary>Relaye vers le journal principal (déjà appelé depuis des tâches de fond ailleurs).</summary>
        private void RelayLog(string msg, int level)
        {
            if (_log == null) return;
            try { _log(msg, level); } catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_busy)
            {
                e.Cancel = true;
                SetStatus("Patiente : une opération est en cours...", 2);
                return;
            }
            base.OnFormClosing(e);
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
