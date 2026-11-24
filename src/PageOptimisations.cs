using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    // Page Optimisations : cartes a interrupteurs (par categorie), cablees au
    // moteur reel (Engine.Run applique/retablit chaque optimisation). Les cartes sont
    // peintes par un seul controle virtualise (TweakListe) : ouverture < 150 ms.
    internal class PageOptimisations : FpsPage
    {
        private readonly List<Tweak> _tweaks;
        private TweakListe _liste;
        private Panel _chips;
        private string _filter = null; // null = tout
        private readonly List<Button> _chipBtns = new List<Button>();
        private bool _built;
        private bool _statesLoaded;
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
            _chips.SetBounds(34, 90, 10, 40);   // sous le sous-titre (compteur d'actives) dessiné à y=64
            _chips.BackColor = Color.Transparent;
            Controls.Add(_chips);

            _liste = new TweakListe();
            _liste.Bascule += OnToggle;
            Controls.Add(_liste);

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

        private Button _bCompet, _bProve, _bAuto, _bReco, _bSur, _bEsport, _bReset;
        private readonly ToolTip _tip = new ToolTip { AutoPopDelay = 15000, InitialDelay = 300 };

        private void BuildPresets()
        {
            _bReco = OnyxDesign.AccentButton("Recommandé"); _bReco.Height = 30; _bReco.Width = 118;
            _bReco.Click += (s, e) => Batch(t => t.Recommended, true, "Recommandé");
            // « Sûr » : seulement ce qui s'annule à l'identique, sans effet de bord, au gain mesuré ou sans enjeu de performance.
            _bSur = OnyxDesign.Button("Sûr"); _bSur.Height = 30; _bSur.Width = 70; _bSur.ForeColor = FpsUi.Gold;
            _bSur.Click += (s, e) => Batch(t => t.Sur, true, "Sûr — annulable à l'identique");
            _tip.SetToolTip(_bSur, "Seulement des réglages sans effet de bord, annulables à l'identique, au gain mesuré ou sans enjeu de performance.");
            _bEsport = OnyxDesign.Button("eSport"); _bEsport.Height = 30; _bEsport.Width = 82; _bEsport.ForeColor = FpsUi.Gold;
            _bEsport.Click += (s, e) => Batch(t => t.Esport, true, "eSport");
            _bReset = OnyxDesign.Button("Réinitialiser"); _bReset.Height = 30; _bReset.Width = 100; _bReset.ForeColor = FpsUi.Err;
            _bReset.Click += (s, e) => Batch(t => true, false, "Réinitialisation");
            _bAuto = OnyxDesign.AccentButton("⚙ Auto"); _bAuto.Height = 30; _bAuto.Width = 92;
            _bAuto.Click += (s, e) => ApplyAuto();
            // Optimisation PROUVÉE : applique par lots, mesure, annule ce qui dégrade (PreuveRun).
            _bProve = OnyxDesign.PrimaryButton("Optimiser et mesurer"); _bProve.Height = 30; _bProve.Width = 172;
            _bProve.Click += (s, e) =>
            {
                using (var f = new PreuveForm(_tweaks)) f.ShowDialog(FindForm());
                try { AppStats.Invalidate(); } catch { }
                RefreshStatesAsync();
            };
            // Mode COMPÉTITION : profils, Mode Jeu auto, eSport prouvé, plafond — annulable d'un clic.
            _bCompet = OnyxDesign.Button("🏁 Compétition"); _bCompet.Height = 30; _bCompet.Width = 130; _bCompet.ForeColor = FpsUi.Gold;
            _bCompet.Click += (s, e) =>
            {
                using (var f = new CompetitionForm(_tweaks)) f.ShowDialog(FindForm());
                try { AppStats.Invalidate(); } catch { }
                RefreshStatesAsync();
            };
            Controls.Add(_bCompet); Controls.Add(_bProve); Controls.Add(_bAuto); Controls.Add(_bReco); Controls.Add(_bSur); Controls.Add(_bEsport); Controls.Add(_bReset);
        }

        // Auto-tune : détecte le matériel et applique la sélection adaptée (niveau équilibré).
        private void ApplyAuto()
        {
            Cursor = Cursors.WaitCursor;
            Task.Run(() =>
            {
                var ids = new HashSet<string>();
                try { HwProfile hw = Hardware.Detect(); ids = Hardware.AutoTuneIds(_tweaks, hw, Hardware.LevelBalanced); } catch { }
                try { BeginInvoke((Action)(() => { Cursor = Cursors.Default; Batch(t => ids.Contains(t.Id), true, "Auto — adapté à ton PC"); })); } catch { }
            });
        }


        private void Batch(Func<Tweak, bool> selector, bool apply, string label)
        {
            var list = new List<Tweak>();
            foreach (Tweak t in _tweaks) if (selector(t)) list.Add(t);
            if (list.Count == 0) return;
            if (MessageBox.Show(FindForm(), (apply ? "Appliquer" : "Rétablir") + " " + list.Count + " optimisation(s) — " + label + " ?",
                "ONYX", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            _bAuto.Enabled = _bReco.Enabled = _bSur.Enabled = _bEsport.Enabled = _bReset.Enabled = false;
            Task.Run(() =>
            {
                try { Engine.Run(list, apply, apply, false, Host.Log); } catch { }  // backup .reg oui, point de restauration non (trop lent)
                try { AppStats.Invalidate(); } catch { }  // l'état opti a changé : dashboard/collection recalculeront
                try { BeginInvoke((Action)(() => { _bAuto.Enabled = _bReco.Enabled = _bSur.Enabled = _bEsport.Enabled = _bReset.Enabled = true; RefreshStatesAsync(); })); } catch { }
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
            var b = OnyxDesign.Button(text);
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
            b.FlatAppearance.BorderColor = on ? FpsUi.Gold : FpsUi.Border;
            b.BackColor = on ? FpsUi.BlendGold(FpsUi.Card, 0.12f) : FpsUi.Card;
            b.ForeColor = on ? FpsUi.Gold : FpsUi.Dim;
        }

        private void Populate()
        {
            var visibles = new List<Tweak>();
            foreach (Tweak t in _tweaks)
            {
                if (_filter != null && t.Category != _filter) continue;
                if (!Match(t)) continue;
                visibles.Add(t);
            }
            _liste.Definir(visibles);
            Invalidate(new Rectangle(0, 0, ClientSize.Width, 90));   // sous-titre (compteur)
        }

        private void OnToggle(Tweak t, bool apply)
        {
            _liste.Occupe(t.Id, true);
            var list = new List<Tweak>(); list.Add(t);
            Task.Run(() =>
            {
                try { Engine.Run(list, apply, false, false, Host.Log); } catch { }
                try { AppStats.Invalidate(); } catch { }  // une opti a changé : invalide le cache partagé
                bool? st = null; try { st = t.Check != null ? t.Check() : (bool?)apply; } catch { }
                bool final = st ?? apply;
                try { BeginInvoke((Action)(() => { _liste.Etat(t.Id, final); _liste.Occupe(t.Id, false); Invalidate(new Rectangle(0, 0, ClientSize.Width, 90)); })); } catch { }
            });
        }

        private void RefreshStatesAsync()
        {
            var snapshot = new List<Tweak>(_liste.Nombre);
            for (int i = 0; i < _liste.Nombre; i++) snapshot.Add(_liste.At(i));
            Task.Run(() =>
            {
                var res = new Dictionary<string, bool>();
                foreach (Tweak t in snapshot) { bool? st = null; try { st = t.Check != null ? t.Check() : null; } catch { } if (st.HasValue) res[t.Id] = st.Value; }
                try { BeginInvoke((Action)(() => { foreach (var kv in res) _liste.Etat(kv.Key, kv.Value); _statesLoaded = true; _liste.EtatsCharges(); Invalidate(new Rectangle(0, 0, ClientSize.Width, 90)); })); }
                catch { }
            });
        }

        private void DoLayout()
        {
            if (_liste == null) return;
            int right = ClientSize.Width - 34;
            int y = Host != null ? Host.ContentTop(96) : 96;
            _search.SetBounds(right - 260, 24, 260, 26);
            if (_bReset != null)
            {
                int x = 34;
                foreach (Button b in new[] { _bProve, _bAuto, _bSur, _bReco, _bEsport, _bCompet, _bReset })
                {
                    if (x + b.Width > right && x > 34) { x = 34; y += 40; }
                    b.Location = new Point(x, y);
                    b.Height = 32;
                    x += b.Width + 8;
                }
                y += 48;
            }
            // Toutes les catégories restent accessibles, même dans une petite fenêtre.
            int chipX = 0, chipY = 0, chipWidth = Math.Max(200, right - 34);
            foreach (Button b in _chipBtns)
            {
                if (chipX + b.Width > chipWidth && chipX > 0) { chipX = 0; chipY += 36; }
                b.Location = new Point(chipX, chipY);
                chipX += b.Width + 8;
            }
            _chips.SetBounds(34, y, chipWidth, chipY + 32);
            int ft = _chips.Bottom + 16;
            var cible = new Rectangle(34, ft, ClientSize.Width - 50, Math.Max(80, ClientSize.Height - ft - 16));
            if (_liste.Bounds != cible) _liste.Bounds = cible;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            PaintPremiumTitle(e.Graphics, "OPTIMISER WINDOWS", Subtitle());
        }

        // Sous-titre vivant : nombre d'optimisations actives (vue complète) ou nombre affiché (vue filtrée).
        private string Subtitle()
        {
            if (!_built || _liste == null) return null;
            int shown = _liste.Nombre;
            if (_filter == null && _query.Length == 0)
            {
                if (!_statesLoaded) return shown + " optimisations";
                int active = _liste.Actives();
                return active + " / " + shown + " optimisations actives";
            }
            string s = shown + (shown > 1 ? " optimisations affichées" : " optimisation affichée");
            if (_query.Length > 0 && _search != null) s += "  ·  « " + _search.Text.Trim() + " »";
            return s;
        }
    }
}
