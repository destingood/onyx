using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    // Page Optimisations : cartes a interrupteurs (par categorie), cablees au
    // moteur reel (Engine.Run applique/retablit chaque optimisation).
    internal class PageOptimisations : FpsPage
    {
        private readonly List<Tweak> _tweaks;
        private FlowLayoutPanel _flow;
        private Panel _chips;
        private string _filter = null; // null = tout
        private readonly List<Button> _chipBtns = new List<Button>();
        private readonly Dictionary<string, ToggleSwitch> _toggles = new Dictionary<string, ToggleSwitch>();
        private bool _built;

        public PageOptimisations(DashboardForm host) : base(host)
        {
            _tweaks = Catalog.All();
            Build();
        }

        public override void OnShown()
        {
            if (!_built) { Populate(); _built = true; }
            RefreshStatesAsync();
        }

        private void Build()
        {
            _chips = new Panel();
            _chips.SetBounds(34, 66, 10, 40);
            _chips.BackColor = Color.Transparent;
            Controls.Add(_chips);

            _flow = new FlowLayoutPanel();
            _flow.AutoScroll = true;
            _flow.BackColor = Color.Transparent;
            _flow.Padding = new Padding(28, 4, 20, 20);
            Controls.Add(_flow);

            BuildPresets();
            BuildChips();
            Resize += (s, e) => DoLayout();
            DoLayout();
        }

        private Button _bReco, _bEsport, _bReset;

        private void BuildPresets()
        {
            _bReco = FpsUi.NeonButton("Recommandé"); _bReco.Height = 30; _bReco.Width = 118;
            _bReco.Click += (s, e) => Batch(t => t.Recommended, true, "Recommandé");
            _bEsport = FpsUi.GhostButton("eSport"); _bEsport.Height = 30; _bEsport.Width = 82; _bEsport.ForeColor = FpsUi.Neon;
            _bEsport.Click += (s, e) => { if (Pro("Preset eSport")) Batch(t => t.Esport, true, "eSport"); };
            _bReset = FpsUi.GhostButton("Réinitialiser"); _bReset.Height = 30; _bReset.Width = 100; _bReset.ForeColor = FpsUi.Err;
            _bReset.Click += (s, e) => Batch(t => true, false, "Réinitialisation");
            Controls.Add(_bReco); Controls.Add(_bEsport); Controls.Add(_bReset);
        }

        private bool Pro(string feat)
        {
            if (License.ProUnlocked) return true;
            using (var f = new LicenseKeyForm(feat)) f.ShowDialog(FindForm());
            return License.ProUnlocked;
        }

        private void Batch(Func<Tweak, bool> selector, bool apply, string label)
        {
            var list = new List<Tweak>();
            foreach (Tweak t in _tweaks) if (selector(t)) list.Add(t);
            if (list.Count == 0) return;
            if (MessageBox.Show(FindForm(), (apply ? "Appliquer" : "Rétablir") + " " + list.Count + " optimisation(s) — " + label + " ?",
                "DesTinGOOD", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            _bReco.Enabled = _bEsport.Enabled = _bReset.Enabled = false;
            Task.Run(() =>
            {
                try { Engine.Run(list, apply, apply, apply, Host.Log); } catch { }
                try { BeginInvoke((Action)(() => { _bReco.Enabled = _bEsport.Enabled = _bReset.Enabled = true; RefreshStatesAsync(); })); } catch { }
            });
        }

        private void BuildChips()
        {
            int x = 0;
            AddChip("Tout", null, ref x);
            foreach (string c in Cat.Order) AddChip(Short(c), c, ref x);
            _chips.Width = x;
        }

        private static string Short(string cat)
        {
            // Retire l'emoji de tete pour les puces.
            int sp = cat.IndexOf(' ');
            return sp > 0 && sp <= 3 ? cat.Substring(sp + 1) : cat;
        }

        private void AddChip(string text, string cat, ref int x)
        {
            var b = new Button();
            b.Text = text;
            b.AutoSize = false;
            b.Height = 30;
            b.Width = TextRenderer.MeasureText(text, FpsUi.Small).Width + 26;
            b.Left = x; b.Top = 4;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 1;
            b.Font = FpsUi.Small;
            b.Cursor = Cursors.Hand;
            b.Tag = cat;
            b.Click += (s, e) => { _filter = (string)((Button)s).Tag; StyleChips(); Populate(); RefreshStatesAsync(); };
            _chips.Controls.Add(b);
            _chipBtns.Add(b);
            x += b.Width + 8;
            StyleChip(b, cat == _filter);
        }

        private void StyleChips() { foreach (var b in _chipBtns) StyleChip(b, (string)b.Tag == _filter); }

        private void StyleChip(Button b, bool on)
        {
            b.FlatAppearance.BorderColor = on ? FpsUi.Neon : FpsUi.Border;
            b.BackColor = on ? Color.FromArgb(18, 34, 26) : FpsUi.Card;
            b.ForeColor = on ? FpsUi.Neon : FpsUi.Dim;
        }

        private void Populate()
        {
            _flow.SuspendLayout();
            _flow.Controls.Clear();
            _toggles.Clear();
            foreach (Tweak t in _tweaks)
            {
                if (_filter != null && t.Category != _filter) continue;
                _flow.Controls.Add(MakeCard(t));
            }
            _flow.ResumeLayout();
        }

        private Control MakeCard(Tweak t)
        {
            var card = new Panel();
            card.Size = new Size(348, 150);
            card.Margin = new Padding(10);
            card.BackColor = Color.Transparent;
            card.Paint += (s, e) => FpsUi.PaintCard(e.Graphics, ((Panel)s).ClientRectangle, FpsUi.Card, FpsUi.Border, 12f);

            var name = new Label();
            name.Text = t.Name;
            name.Font = FpsUi.H3; name.ForeColor = FpsUi.Ink; name.BackColor = Color.Transparent;
            name.SetBounds(16, 14, 258, 44);

            var desc = new Label();
            desc.Text = t.Desc; desc.Font = FpsUi.Small; desc.ForeColor = FpsUi.Dim; desc.BackColor = Color.Transparent;
            desc.SetBounds(16, 62, 316, 74);

            var tog = new ToggleSwitch();
            tog.Location = new Point(288, 16);
            bool locked = !License.ProUnlocked && t.Esport && !t.Recommended;
            tog.Locked = locked;
            tog.Toggled += (s, e) => OnToggle(t, tog);
            _toggles[t.Id] = tog;

            card.Controls.Add(name); card.Controls.Add(desc); card.Controls.Add(tog);

            if (t.Reboot)
            {
                var rb = FpsUi.Text("redémarrage requis", FpsUi.Small, FpsUi.Warn);
                rb.Location = new Point(16, 128); rb.Font = new Font("Segoe UI", 7.5f);
                card.Controls.Add(rb);
            }
            return card;
        }

        private void OnToggle(Tweak t, ToggleSwitch tog)
        {
            if (tog.Locked) return;
            if (!License.ProUnlocked && t.Esport && !t.Recommended)
            {
                using (var f = new LicenseKeyForm("Optimisation avancée")) f.ShowDialog(FindForm());
                if (!License.ProUnlocked) { tog.On = false; return; }
            }
            bool apply = tog.On;
            tog.Enabled = false;
            var list = new List<Tweak>(); list.Add(t);
            Task.Run(() =>
            {
                try { Engine.Run(list, apply, false, false, Host.Log); } catch { }
                bool? st = null; try { st = t.Check != null ? t.Check() : (bool?)apply; } catch { }
                bool final = st ?? apply;
                try { BeginInvoke((Action)(() => { tog.On = final; tog.Enabled = true; })); } catch { }
            });
        }

        private void RefreshStatesAsync()
        {
            var snapshot = new List<Tweak>(_toggles.Keys.Count);
            foreach (Tweak t in _tweaks) if (_toggles.ContainsKey(t.Id)) snapshot.Add(t);
            Task.Run(() =>
            {
                var res = new Dictionary<string, bool>();
                foreach (Tweak t in snapshot) { bool? st = null; try { st = t.Check != null ? t.Check() : null; } catch { } if (st.HasValue) res[t.Id] = st.Value; }
                try { BeginInvoke((Action)(() => { foreach (var kv in res) if (_toggles.ContainsKey(kv.Key)) _toggles[kv.Key].On = kv.Value; })); }
                catch { }
            });
        }

        private void DoLayout()
        {
            if (_flow == null) return;
            _flow.SetBounds(20, 112, ClientSize.Width - 40, ClientSize.Height - 112);
            if (_bReset != null)
            {
                int rx = ClientSize.Width - 34;
                _bReset.Location = new Point(rx - _bReset.Width, 22); rx -= _bReset.Width + 8;
                _bEsport.Location = new Point(rx - _bEsport.Width, 22); rx -= _bEsport.Width + 8;
                _bReco.Location = new Point(rx - _bReco.Width, 22);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            PaintTitle(e.Graphics, "OPTIMISATIONS", null);
        }
    }
}
