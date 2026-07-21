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
        private TextBox _search;
        private string _query = "";

        public PageOptimisations(DashboardForm host) : base(host)
        {
            _tweaks = Catalog.All();
            Build();
        }

        public override void OnShown()
        {
            DoLayout();
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

            _search = new TextBox();
            try { _search.PlaceholderText = "Rechercher une optimisation…"; } catch { }
            _search.BackColor = FpsUi.Card; _search.ForeColor = FpsUi.Ink;
            _search.BorderStyle = BorderStyle.FixedSingle; _search.Font = FpsUi.Small;
            _search.SetBounds(400, 24, 220, 26);
            _search.TextChanged += (s, e) => { _query = _search.Text.Trim().ToLowerInvariant(); if (_built) { Populate(); RefreshStatesAsync(); } };
            Controls.Add(_search);

            BuildPresets();
            BuildChips();
            Resize += (s, e) => DoLayout();
            DoLayout();
        }

        private bool Match(Tweak t)
        {
            if (_query.Length == 0) return true;
            return (t.Name != null && t.Name.ToLowerInvariant().Contains(_query))
                || (t.Desc != null && t.Desc.ToLowerInvariant().Contains(_query))
                || (t.Category != null && t.Category.ToLowerInvariant().Contains(_query));
        }

        private Button _bAuto, _bReco, _bEsport, _bReset;

        private void BuildPresets()
        {
            _bReco = FpsUi.NeonButton("Recommandé"); _bReco.Height = 30; _bReco.Width = 118;
            _bReco.Click += (s, e) => Batch(t => t.Recommended, true, "Recommandé");
            _bEsport = FpsUi.GhostButton("eSport"); _bEsport.Height = 30; _bEsport.Width = 82; _bEsport.ForeColor = FpsUi.Neon;
            _bEsport.Click += (s, e) => { if (Pro("Preset eSport")) Batch(t => t.Esport, true, "eSport"); };
            _bReset = FpsUi.GhostButton("Réinitialiser"); _bReset.Height = 30; _bReset.Width = 100; _bReset.ForeColor = FpsUi.Err;
            _bReset.Click += (s, e) => Batch(t => true, false, "Réinitialisation");
            _bAuto = FpsUi.NeonButton("⚙ Auto"); _bAuto.Height = 30; _bAuto.Width = 92;
            _bAuto.Click += (s, e) => ApplyAuto();
            Controls.Add(_bAuto); Controls.Add(_bReco); Controls.Add(_bEsport); Controls.Add(_bReset);
        }

        // Auto-tune : détecte le matériel et applique la sélection adaptée (niveau équilibré).
        private void ApplyAuto()
        {
            if (!Pro("Auto-tune (adapté à ton PC)")) return;
            Cursor = Cursors.WaitCursor;
            Task.Run(() =>
            {
                var ids = new HashSet<string>();
                try { HwProfile hw = Hardware.Detect(); ids = Hardware.AutoTuneIds(_tweaks, hw, Hardware.LevelBalanced); } catch { }
                try { BeginInvoke((Action)(() => { Cursor = Cursors.Default; Batch(t => ids.Contains(t.Id), true, "Auto — adapté à ton PC"); })); } catch { }
            });
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
            _bAuto.Enabled = _bReco.Enabled = _bEsport.Enabled = _bReset.Enabled = false;
            Task.Run(() =>
            {
                try { Engine.Run(list, apply, apply, false, Host.Log); } catch { }  // backup .reg oui, point de restauration non (trop lent)
                try { BeginInvoke((Action)(() => { _bAuto.Enabled = _bReco.Enabled = _bEsport.Enabled = _bReset.Enabled = true; RefreshStatesAsync(); })); } catch { }
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
            var old = new Control[_flow.Controls.Count];
            _flow.Controls.CopyTo(old, 0);
            _flow.Controls.Clear();
            _toggles.Clear();
            foreach (Tweak t in _tweaks)
            {
                if (_filter != null && t.Category != _filter) continue;
                if (!Match(t)) continue;
                _flow.Controls.Add(MakeCard(t));
            }
            _flow.ResumeLayout();
            foreach (Control c in old) { try { c.Dispose(); } catch { } }  // libère les handles GDI des anciennes cartes
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
                _bReco.Location = new Point(rx - _bReco.Width, 22); rx -= _bReco.Width + 8;
                _bAuto.Location = new Point(rx - _bAuto.Width, 22);
                if (_search != null) _search.SetBounds(Math.Max(180, _bAuto.Left - 12 - 220), 24, 220, 26);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            PaintTitle(e.Graphics, "OPTIMISATIONS", null);
        }
    }
}
