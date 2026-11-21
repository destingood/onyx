using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BTOptimizer
{
    internal class PageLab : FpsPage
    {
        private readonly BufferedPanel _scroll = new BufferedPanel(), _content = new BufferedPanel(), _hero = new BufferedPanel();
        private readonly BufferedFlow _grid = new BufferedFlow();
        private readonly List<ModuleCard> _cards = new List<ModuleCard>();
        private readonly Button[] _filters = new Button[4];
        private readonly string[] _categories = { "Tous les outils", "Mesurer", "Optimiser", "Personnaliser" };
        private readonly int[] _counts = { 14, 6, 5, 3 };
        private readonly string[] _specs = { "FPS · frametime", "Interruptions · réactivité", "CPU · GPU", "Fluidité en jeu", "Débit · ping", "Écran · limites FPS", "Capteurs en jeu", "Entrées · fréquence", "OBS · capture", "XMP · Resizable BAR", "Caches des applications", "Fréquences · puissance", "Visée · affichage", "Couleurs · écran" };
        private readonly TextBox _search = new TextBox();
        private readonly BufferedPanel _searchBox = new BufferedPanel();
        private readonly Label _results = FpsUi.Text("", FpsUi.Small, FpsUi.Dim);
        private readonly Label _empty = FpsUi.Text("Aucun outil ne correspond. Essaie un autre terme ou une autre catégorie.", FpsUi.Body, FpsUi.Dim);
        private Button _benchmark;
        private ModuleCard _latency, _hardware;
        private ScrollWheelFilter _wheel;
        private int _category;
        private static readonly Font HeroFont = FpsUi.F(25, true);

        public PageLab(DashboardForm host) : base(host)
        {
            _scroll.AutoScroll = true; _scroll.BackColor = FpsUi.BgMain;
            _content.BackColor = FpsUi.BgMain; _scroll.Controls.Add(_content); Controls.Add(_scroll);
            _wheel = new ScrollWheelFilter(_scroll); Application.AddMessageFilter(_wheel);
            _hero.Paint += PaintHero; _content.Controls.Add(_hero);
            _benchmark = OnyxDesign.PrimaryButton("Comparer mes FPS  →");
            _benchmark.Click += (s, e) => Host.OpenDialog(new BenchmarkFpsForm(Host.Log));
            _hero.Controls.Add(_benchmark);
            _latency = new ModuleCard(1, NavIcons.Optimisations, "Latence Windows", "DPC / ISR · Trouve les interruptions", () => Host.OpenDialog(new LiveMonForm(Host.Log)), 1, "DIAGNOSTIQUER", true);
            _hardware = new ModuleCard(1, NavIcons.Systeme, "Puissance matérielle", "Évalue ton processeur et ton GPU", () => Host.OpenDialog(new BenchForm(Host.Log)), 2, "ÉVALUER", true);
            _content.Controls.Add(_latency); _content.Controls.Add(_hardware);
            _grid.Name = "module-grid"; _grid.BackColor = FpsUi.BgMain;
            _grid.AutoScroll = false; _grid.Padding = Padding.Empty;
            _content.Controls.Add(_grid);
            for (int i = 0; i < _filters.Length; i++)
            {
                int category = i;
                _filters[i] = OnyxDesign.Button(_categories[i] + "   " + _counts[i]);
                _filters[i].Name = "module-filter-" + i;
                _filters[i].Font = FpsUi.H3;
                _filters[i].Click += (s, e) => { _category = category; Filter(); };
                _content.Controls.Add(_filters[i]);
            }
            _results.AutoSize = false; _content.Controls.Add(_results);
            _empty.AutoSize = false; _content.Controls.Add(_empty);
            _searchBox.BackColor = Color.Transparent;
            _searchBox.Paint += (s, e) => OnyxDesign.PaintCard(e.Graphics, _searchBox.ClientRectangle, FpsUi.Card, _search.Focused ? FpsUi.GoldDim : FpsUi.Border, 8);
            _search.Name = "module-search"; _search.PlaceholderText = "Rechercher un outil…";
            _search.AccessibleName = "Rechercher un outil de performance";
            _search.BackColor = FpsUi.Card; _search.ForeColor = FpsUi.Ink; _search.Font = FpsUi.Body; _search.BorderStyle = BorderStyle.None;
            _search.TextChanged += (s, e) => Filter();
            _search.GotFocus += (s, e) => _searchBox.Invalidate(); _search.LostFocus += (s, e) => _searchBox.Invalidate();
            _searchBox.Controls.Add(_search); Controls.Add(_searchBox);
            Add(1, NavIcons.Laboratoire, "Benchmark FPS", "Compare les performances avant et après optimisation.", () => Host.OpenDialog(new BenchmarkFpsForm(Host.Log)));
            Add(1, NavIcons.Optimisations, "Latence DPC / ISR", "Mesure les interruptions et la latence de Windows.", () => Host.OpenDialog(new LiveMonForm(Host.Log)));
            Add(1, NavIcons.Systeme, "Benchmark matériel", "Évalue les performances du processeur et du GPU.", () => Host.OpenDialog(new BenchForm(Host.Log)));
            Add(1, NavIcons.Jeux, "Compteur FPS", "Observe la fluidité de ton jeu en temps réel.", () => Host.OpenDialog(new FpsMonForm(Host.Log)));
            Add(1, NavIcons.Outils, "Test réseau", "Vérifie ton débit descendant et ta latence.", () => Host.OpenDialog(new SpeedTestForm(Host.Log)));
            Add(2, NavIcons.Collection, "Objectif 500 FPS", "Vérifie les réglages de ton écran et de tes jeux.", () => Host.OpenDialog(new Fps500Form(Host.Log)));
            Add(3, NavIcons.Systeme, "Overlay matériel", "CPU, GPU et températures affichés dans le jeu.", () => Host.OpenDialog(new StatsOverlayForm(Host.Log)));
            Add(1, NavIcons.Jeux, "Test manette", "Détection et fréquence de tes périphériques.", () => Host.OpenDialog(new ControllerForm(Host.Log)));
            Add(2, NavIcons.Outils, "Streaming", "Les réglages RTSS, OBS et NVIDIA pour ton stream.", () => Host.OpenDialog(new StreamGuideForm(Host.Log)));
            Add(2, NavIcons.Systeme, "BIOS & mémoire", "XMP, Resizable BAR et configuration du BIOS.", () => Host.OpenDialog(new BiosGuideForm(Host.Log)));
            Add(2, NavIcons.Stockage, "Nettoyage avancé", "Examine les caches de chaque application.", () => Host.OpenDialog(new Winapp2Form(Host.Log)));
            Add(2, NavIcons.Optimisations, "Overclock GPU", "Ajuste les fréquences de la carte graphique.", () => Host.OpenDialog(new OverclockForm(Host.Log)));
            Add(3, NavIcons.Jeux, "Réticule", "Un viseur adapté à tes préférences de jeu.", () => Host.OpenDialog(new CrosshairForm(Host.Log)));
            Add(3, NavIcons.Systeme, "Couleurs & vibrance", "Ajuste le rendu des couleurs de ton écran.", () => Host.OpenDialog(new ColorFilterForm(Host.Log)));
            Filter();
            Resize += (s, e) => DoLayout();
        }
        private void Add(int category, int icon, string title, string description, Action action)
        {
            int index = _cards.Count;
            var card = new ModuleCard(category, icon, title, description, action, index, _specs[index], false);
            _cards.Add(card); _grid.Controls.Add(card);
        }
        private bool Matches(ModuleCard card)
        {
            return (_category == 0 || card.Category == _category)
                && (card.Text + " " + card.Description + " " + card.Specification).IndexOf(_search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase) >= 0;
        }
        private void Filter()
        {
            int visible = 0;
            _grid.SuspendLayout();
            foreach (var card in _cards) { bool match = Matches(card); card.Visible = match; if (match) visible++; }
            _grid.ResumeLayout();
            for (int i = 0; i < _filters.Length; i++)
            {
                if (_filters[i] == null) continue;
                bool active = i == _category;
                _filters[i].BackColor = active ? FpsUi.BlendGold(FpsUi.Card, .12f) : FpsUi.Card;
                _filters[i].ForeColor = active ? FpsUi.Gold : FpsUi.Dim;
                _filters[i].FlatAppearance.BorderSize = active ? 1 : 0;
                _filters[i].FlatAppearance.BorderColor = active ? FpsUi.GoldDim : FpsUi.Border;
                _filters[i].Invalidate();
            }
            _results.Text = (_category == 0 ? "EXPLORER LE LABORATOIRE" : _categories[_category].ToUpperInvariant())
                + "    /    " + visible + (visible == 1 ? " outil disponible" : " outils disponibles");
            _empty.Visible = visible == 0;
            _scroll.AutoScrollPosition = Point.Empty;
            DoLayout();
        }
        private void DoLayout()
        {
            _searchBox.SetBounds(Math.Max(500, Width - 284), 22, 250, 36);
            _search.SetBounds(12, 9, 226, 20);
            _scroll.SetBounds(34, 98, Math.Max(400, Width - 50), Math.Max(150, Height - 114));
            int width = Math.Max(400, _scroll.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2);
            int main = (width - 16) * 62 / 100;
            _hero.SetBounds(0, 0, main, 224);
            _benchmark.SetBounds(24, 176, 218, 34);
            _latency.SetBounds(main + 16, 0, width - main - 16, 104);
            _hardware.SetBounds(main + 16, 120, width - main - 16, 104);
            int tabWidth = (width - 24) / 4;
            for (int i = 0; i < _filters.Length; i++)
                if (_filters[i] != null) _filters[i].SetBounds(i * (tabWidth + 8), 246, tabWidth, 44);
            _results.SetBounds(0, 310, width, 22);
            int columns = width >= 960 ? 3 : 2;
            int cardWidth = (width - (columns - 1) * 16) / columns;
            int visible = 0;
            _grid.SuspendLayout();
            foreach (var card in _cards)
            {
                if (!Matches(card)) continue;
                card.Size = new Size(cardWidth, 180);
                card.Margin = new Padding(0, 0, ++visible % columns == 0 ? 0 : 16, 16);
            }
            int gridHeight = Math.Max(100, ((visible + columns - 1) / columns) * 196);
            _grid.SetBounds(0, 348, width, gridHeight);
            _grid.ResumeLayout();
            _empty.SetBounds(24, 364, width - 48, 60);
            _content.Size = new Size(width, 348 + gridHeight);
        }
        private void PaintHero(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            OnyxDesign.PaintCard(g, _hero.ClientRectangle, Color.FromArgb(29, 26, 20), OnyxDesign.Edge, 14);
            int artX = _hero.Width - 140;
            var saved = g.Save();
            using (var clip = FpsUi.Round(new RectangleF(1, 1, _hero.Width - 2, _hero.Height - 2), 13)) g.SetClip(clip);
            using (var pen = new Pen(Color.FromArgb(12, FpsUi.Gold)))
                for (int x = artX - 90; x < _hero.Width + 224; x += 16) g.DrawLine(pen, x, 0, x - 180, 224);
            g.Restore(saved);
            OnyxDesign.Text(g, "LE POINT DE DÉPART  /  BENCHMARK FPS", FpsUi.Tiny, FpsUi.Gold, new Rectangle(24, 20, _hero.Width - 48, 20));
            OnyxDesign.Text(g, "La précision fait", HeroFont, FpsUi.Ink, new Rectangle(22, 47, _hero.Width - 130, 44));
            OnyxDesign.Text(g, "la différence.", HeroFont, FpsUi.Gold, new Rectangle(22, 87, _hero.Width - 130, 44));
            OnyxDesign.Text(g, "Mesure ta fluidité avant et après tes réglages.", FpsUi.Body, FpsUi.Dim, new Rectangle(24, 136, _hero.Width - 48, 24));
            DrawInstrument(g, new Rectangle(artX, 52, 112, 74), 0, FpsUi.Gold);
            OnyxDesign.Text(g, "MESURER → COMPARER", FpsUi.Tiny, FpsUi.Dim, new Rectangle(_hero.Width - 184, 184, 170, 18));
        }
        public override void OnShown() { DoLayout(); }
#if BTTEST
        internal void PreviewScroll(int y) { _scroll.AutoScrollPosition = new Point(0, y); }
        internal void ValidatePerformanceUi()
        {
            for (int category = 0; category < 4; category++)
            {
                _filters[category].PerformClick();
                int visible = 0;
                foreach (var card in _cards) if (card.Visible) visible++;
                if (visible != _counts[category]) throw new InvalidOperationException("Filtre de performance incorrect : " + category);
            }
            _filters[0].PerformClick();
            _search.Text = "FPS";
            int found = 0;
            foreach (var card in _cards) if (card.Visible) found++;
            if (found != 3) throw new InvalidOperationException("Recherche FPS incorrecte : " + found);
            _search.Text = "__aucun_outil_73498__";
            if (!_empty.Visible) throw new InvalidOperationException("État vide absent.");
            _search.Text = "";
            foreach (var card in _cards)
                if (card.Right > _grid.ClientSize.Width || card.Bottom > _grid.ClientSize.Height)
                    throw new InvalidOperationException("Carte hors de la grille.");
            if (_hardware.Right > _content.Width || _filters[3].Right > _content.Width)
                throw new InvalidOperationException("En-tête hors du cadre.");
        }
#endif
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            PaintPremiumTitle(e.Graphics, "PERFORMANCES", "Ton laboratoire pour la fluidité, la réactivité et le matériel.");
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && _wheel != null) { Application.RemoveMessageFilter(_wheel); _wheel = null; }
            base.Dispose(disposing);
        }

        // Pictogrammes de fonctions : illustrations, sans mesures ni scores fictifs.
        private static void DrawInstrument(Graphics g, Rectangle r, int type, Color color)
        {
            var saved = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(r.X, r.Y); g.ScaleTransform(r.Width / 112f, r.Height / 74f);
            using (var faint = new Pen(Color.FromArgb(42, color)))
            using (var stroke = new Pen(Color.FromArgb(190, color), 1.5f))
            {
                if (type % 4 == 0)
                {
                    using (var frame = FpsUi.Round(new RectangleF(3, 5, 106, 61), 8)) g.DrawPath(faint, frame);
                    g.DrawLine(faint, 13, 54, 100, 54);
                    int[] bars = { 20, 32, 25, 39, 32, 43, 38 };
                    for (int i = 0; i < bars.Length; i++)
                    {
                        using (var fill = new SolidBrush(Color.FromArgb(45 + i * 20, color))) g.FillRectangle(fill, 14 + i * 12, 54 - bars[i], 6, bars[i]);
                    }
                }
                else if (type % 4 == 1)
                {
                    for (int y = 12; y < 72; y += 16) g.DrawLine(faint, 3, y, 109, y);
                    g.DrawLines(stroke, new[] { new Point(3, 48), new Point(24, 48), new Point(34, 22), new Point(43, 58), new Point(53, 37), new Point(64, 42), new Point(77, 15), new Point(86, 48), new Point(109, 48) });
                    g.DrawEllipse(stroke, 73, 11, 8, 8);
                }
                else if (type % 4 == 2)
                {
                    using (var chip = FpsUi.Round(new RectangleF(31, 12, 50, 50), 8)) g.DrawPath(stroke, chip);
                    g.DrawRectangle(faint, 41, 22, 30, 30);
                    for (int i = 0; i < 4; i++)
                    {
                        int t = 21 + i * 11; g.DrawLine(faint, 20, t, 30, t); g.DrawLine(faint, 82, t, 92, t);
                        g.DrawLine(faint, 40 + i * 11, 1, 40 + i * 11, 11); g.DrawLine(faint, 40 + i * 11, 63, 40 + i * 11, 73);
                    }
                    using (var fill = new SolidBrush(color)) g.FillEllipse(fill, 52, 33, 8, 8);
                }
                else
                {
                    g.DrawEllipse(faint, 25, 6, 62, 62); g.DrawEllipse(stroke, 39, 20, 34, 34);
                    g.DrawLine(stroke, 56, 0, 56, 28); g.DrawLine(stroke, 56, 46, 56, 74);
                    g.DrawLine(stroke, 17, 37, 47, 37); g.DrawLine(stroke, 65, 37, 95, 37);
                }
            }
            g.Restore(saved);
        }

        private sealed class ModuleCard : Button
        {
            public readonly int Category;
            public readonly string Description, Specification;
            private readonly int _icon, _number;
            private readonly bool _featured;
            private float _hover;
            private Anim.Handle _transition;
            public ModuleCard(int category, int icon, string title, string description, Action action, int number, string specification, bool featured)
            {
                Category = category; _icon = icon; _number = number; Text = title; Description = description; Specification = specification; _featured = featured;
                AccessibleName = title; AccessibleDescription = description;
                BackColor = FpsUi.BgMain; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                MouseEnter += (s, e) => Hover(1); MouseLeave += (s, e) => Hover(0);
                GotFocus += (s, e) => Invalidate(); LostFocus += (s, e) => Invalidate();
                Click += (s, e) => action();
            }
            private void Hover(float target)
            {
                if (_transition != null) _transition.Cancelled = true;
                float start = _hover;
                _transition = Anim.Tween(140, t => { if (!IsDisposed) { _hover = start + (target - start) * t; Invalidate(); } });
            }
            protected override void Dispose(bool disposing) { if (_transition != null) _transition.Cancelled = true; base.Dispose(disposing); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; g.Clear(FpsUi.BgMain);
                bool active = _hover > .4f || (Focused && ShowFocusCues);
                OnyxDesign.PaintCard(g, ClientRectangle, FpsUi.BlendGold(FpsUi.Card, _hover * .05f), active ? FpsUi.GoldDim : FpsUi.Border, 12);
                if (_featured)
                {
                    OnyxDesign.Text(g, Specification, FpsUi.Tiny, FpsUi.Gold, new Rectangle(18, 14, Width - 116, 18));
                    OnyxDesign.Text(g, Text, FpsUi.H2, FpsUi.Ink, new Rectangle(18, 37, Width - 110, 28));
                    OnyxDesign.Text(g, Description, FpsUi.Small, FpsUi.Dim, new Rectangle(18, 72, Width - 36, 20));
                    DrawInstrument(g, new Rectangle(Width - 90, 15, 72, 48), _number, active ? FpsUi.Gold : FpsUi.GoldDim);
                    return;
                }
                OnyxDesign.IconWell(g, _icon, new Rectangle(18, 18, 34, 34), active);
                OnyxDesign.Text(g, Category == 1 ? "MESURER" : Category == 2 ? "OPTIMISER" : "PERSONNALISER", FpsUi.Tiny, FpsUi.GoldDim, new Rectangle(64, 22, Width - 82, 20));
                int instrument = _number == 1 || _number == 7 ? 1 : _number >= 12 ? 3
                    : _number == 0 || _number == 3 || _number == 4 || _number == 5 ? 0 : 2;
                DrawInstrument(g, new Rectangle(Width - 84, 12, 66, 44), instrument, active ? FpsUi.Gold : FpsUi.GoldDim);
                OnyxDesign.Text(g, Text, FpsUi.H2, FpsUi.Ink, new Rectangle(18, 64, Width - 36, 28));
                OnyxDesign.Text(g, Description, FpsUi.Body, FpsUi.Dim, new Rectangle(18, 99, Width - 36, 40), true);
                using (var line = new Pen(FpsUi.Border)) g.DrawLine(line, 18, 146, Width - 18, 146);
                OnyxDesign.Text(g, Specification, FpsUi.Tiny, FpsUi.Dim2, new Rectangle(18, 157, Width - 57, 18));
                OnyxDesign.Text(g, "↗", FpsUi.H2, active ? FpsUi.Gold : FpsUi.Dim, new Rectangle(Width - 36, 152, 24, 24));
            }
        }
    }
}
