using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// ⚡ Config auto + preuve — détecte le matériel (CPU/GPU/RAM/disque), calcule la meilleure
    /// config SÛRE adaptée (réutilise Hardware.AutoTuneIds), l'applique avec sauvegarde .reg et
    /// réversibilité, puis affiche un AVANT/APRÈS honnête.
    ///
    /// Vérité assumée (décidée avec l'utilisateur) : les vrais gains FPS se voient EN JEU
    /// (1% low, latence, stabilité du frametime) et parfois seulement après un redémarrage —
    /// pas dans un chiffre synthétique instantané. D'où le bouton « mesurer en jeu ».
    /// </summary>
    internal class AutoConfigForm : Form
    {
        private readonly Action<string, int> _log;
        private HwProfile _hw;
        private List<Tweak> _all;
        private List<Tweak> _sel = new List<Tweak>();
        private Label _hwLabel, _selLabel, _before, _after, _note;
        private Button _btnApply, _btnRevert, _btnFps, _btnClose;
        private bool _busy;

        private static readonly Color Accent = Color.FromArgb(79, 70, 229);
        private static readonly Color Bg = Color.FromArgb(245, 246, 248);
        private static readonly Color Ink = Color.FromArgb(40, 44, 52);
        private static readonly Color Sub = Color.FromArgb(96, 100, 108);

        public AutoConfigForm(Action<string, int> log)
        {
            _log = log;
            try
            {
                _all = Catalog.All();
                _hw = Hardware.Detect();
                HashSet<string> ids = Hardware.AutoTuneIds(_all, _hw, Hardware.LevelBalanced);
                _sel = _all.Where(t => ids.Contains(t.Id)).ToList();
            }
            catch (Exception ex) { _log("Config auto : " + ex.Message, 3); }
            Build();
            Theme.Apply(this);
            _hwLabel.Text = _hw != null ? _hw.Summary() : "Matériel indéterminé";
            _selLabel.Text = _sel.Count + " optimisation(s) sûre(s) recommandée(s) pour ta machine "
                + "(niveau Équilibré — réversibles, sauvegarde automatique, rien de dangereux).";
            _before.Text = "État actuel : " + Snapshot();
        }

        private void Build()
        {
            Text = "Fluide — Config auto + preuve";
            ClientSize = new Size(700, 470);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = Bg;
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  ⚡ Config auto adaptée à ton PC (+ preuve)",
                Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 12.5f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            _hwLabel = new Label { Location = new Point(18, 66), Size = new Size(664, 22), Font = new Font("Segoe UI Semibold", 9.5f), ForeColor = Ink };
            _selLabel = new Label { Location = new Point(18, 92), Size = new Size(664, 40), ForeColor = Sub };
            Controls.Add(_hwLabel); Controls.Add(_selLabel);

            var box = new Panel { Location = new Point(18, 140), Size = new Size(664, 108), BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
            _before = new Label { Location = new Point(14, 14), Size = new Size(636, 22), Font = new Font("Consolas", 9.5f), ForeColor = Ink };
            _after = new Label { Location = new Point(14, 42), Size = new Size(636, 22), Font = new Font("Consolas", 9.5f), ForeColor = Accent };
            var lgd = new Label { Location = new Point(14, 74), Size = new Size(636, 22), ForeColor = Sub, Text = "Mesures instantanées : résolution du timer + RAM libre." };
            box.Controls.Add(_before); box.Controls.Add(_after); box.Controls.Add(lgd);
            Controls.Add(box);

            _note = new Label
            {
                Location = new Point(18, 258), Size = new Size(664, 60), ForeColor = Sub,
                Text = "⚠ Honnêteté : le delta instantané est souvent faible ou nul. La plupart des gains "
                     + "agissent EN JEU (fluidité, 1% low, latence d'input) et certains seulement après un "
                     + "redémarrage. Pour VOIR le vrai gain, utilise « Mesurer en jeu » avant/après en jouant."
            };
            Controls.Add(_note);

            _btnApply = MakeBtn("Appliquer la config auto (sûr, réversible)", 18, 330, 320, 42, true);
            _btnApply.Click += OnApply;
            _btnFps = MakeBtn("📈 Mesurer en jeu", 350, 330, 180, 42, false);
            _btnFps.Click += OnFps;
            Controls.Add(_btnApply); Controls.Add(_btnFps);

            _btnRevert = MakeBtn("Rétablir la sélection", 18, 384, 200, 36, false);
            _btnRevert.Click += OnRevert;
            _btnClose = MakeBtn("Fermer", 600, 384, 82, 36, false);
            _btnClose.Click += (s, e) => Close();
            Controls.Add(_btnRevert); Controls.Add(_btnClose);
        }

        private static Button MakeBtn(string text, int x, int y, int w, int h, bool primary)
        {
            var b = new Button
            {
                Text = text, Location = new Point(x, y), Size = new Size(w, h), FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Ink,
                Font = primary ? new Font("Segoe UI Semibold", 9.5f) : new Font("Segoe UI", 9f)
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            return b;
        }

        // Instantané des métriques mesurables tout de suite (timer + RAM libre).
        private string Snapshot()
        {
            double timer = 0; long free = 0;
            try { timer = Native.CurrentTimerMs(); } catch { }
            try
            {
                long total = _hw != null ? (long)_hw.RamGB * 1024 : 0;
                long used = NativeMem.UsedPhysMB();
                free = Math.Max(0, total - used);
            }
            catch { }
            return "timer " + timer.ToString("0.0") + " ms   ·   RAM libre ~" + free + " Mo";
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            _btnApply.Enabled = !busy; _btnRevert.Enabled = !busy; _btnFps.Enabled = !busy;
        }

        private void OnApply(object sender, EventArgs e)
        {
            if (_busy || _sel.Count == 0) return;
            if (MessageBox.Show(this,
                    "Appliquer " + _sel.Count + " optimisation(s) sûre(s) adaptée(s) à ton PC ?\n\n"
                    + "• Sauvegarde .reg automatique avant modification\n"
                    + "• Entièrement réversible (bouton « Rétablir »)\n"
                    + "• Rien de dangereux (mitigations/VBS exclus)",
                    "Config auto", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            SetBusy(true);
            _after.Text = "Application en cours…";
            Task.Run(() =>
            {
                EngineResult r = Engine.Run(_sel, true, true, false, _log);
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        _after.Text = "Après :        " + Snapshot()
                            + "   (" + r.Ok + " appliquée(s)" + (r.Ko > 0 ? ", " + r.Ko + " échec(s)" : "") + ")";
                        SetBusy(false);
                        if (r.RebootNeeded)
                            MessageBox.Show(this, "Certaines optimisations demandent un REDÉMARRAGE pour agir pleinement.",
                                "Config auto", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }));
                }
                catch { }
            });
        }

        private void OnRevert(object sender, EventArgs e)
        {
            if (_busy || _sel.Count == 0) return;
            if (MessageBox.Show(this, "Rétablir les " + _sel.Count + " optimisation(s) de cette sélection ?",
                    "Rétablir", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;
            SetBusy(true);
            Task.Run(() =>
            {
                Engine.Run(_sel, false, true, false, _log);
                try { BeginInvoke((Action)(() => { _after.Text = "Rétabli :      " + Snapshot(); SetBusy(false); })); }
                catch { }
            });
        }

        private void OnFps(object sender, EventArgs e)
        {
            // La vraie preuve : FPS/1% low en jeu, via le moniteur existant.
            try { using (var f = new FpsMonForm(_log)) f.ShowDialog(this); }
            catch (Exception ex) { _log("Moniteur FPS : " + ex.Message, 2); }
        }
    }
}
