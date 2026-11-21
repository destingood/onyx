using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace BTOptimizer
{
    internal class PageLab : FpsPage
    {
        private readonly BufferedFlow _grid = new BufferedFlow();
        private readonly List<ModuleCard> _cards = new List<ModuleCard>();
        private readonly Button[] _filters = new Button[4];
        private ScrollWheelFilter _wheel;
        private int _category;

        public PageLab(DashboardForm host) : base(host)
        {
            _grid.Name = "module-grid";
            _grid.AutoScroll = true; _grid.BackColor = FpsUi.BgMain; _grid.Padding = new Padding(0, 0, 0, 16);
            Controls.Add(_grid);
            _wheel = new ScrollWheelFilter(_grid); Application.AddMessageFilter(_wheel);
            string[] categories = { "Tous les outils", "Mesurer", "Optimiser", "Personnaliser" };
            for (int i = 0; i < categories.Length; i++)
            {
                int category = i;
                _filters[i] = OnyxDesign.Button(categories[i]);
                _filters[i].Name = "module-filter-" + i;
                _filters[i].Width = TextRenderer.MeasureText(categories[i], FpsUi.Small).Width + 32;
                _filters[i].Height = 32;
                _filters[i].Click += (s, e) => { _category = category; Filter(); };
                Controls.Add(_filters[i]);
            }
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
            var card = new ModuleCard(category, icon, title, description, action, _cards.Count + 1);
            _cards.Add(card); _grid.Controls.Add(card);
        }
        private void Filter()
        {
            for (int i = 0; i < _filters.Length; i++)
            {
                _filters[i].BackColor = i == _category ? FpsUi.BlendGold(FpsUi.Card, .12f) : FpsUi.Card;
                _filters[i].ForeColor = i == _category ? FpsUi.Gold : FpsUi.Dim;
                _filters[i].FlatAppearance.BorderSize = i == _category ? 1 : 0;
                _filters[i].FlatAppearance.BorderColor = i == _category ? FpsUi.GoldDim : FpsUi.Border;
            }
            _grid.SuspendLayout();
            foreach (var card in _cards) card.Visible = _category == 0 || card.Category == _category;
            _grid.AutoScrollPosition = Point.Empty;
            _grid.ResumeLayout();
            DoLayout();
        }
        private void DoLayout()
        {
            int x = 34;
            foreach (var filter in _filters) { if (filter == null) continue; filter.Location = new Point(x, 94); x += filter.Width + 8; }
            _grid.SetBounds(34, 146, Math.Max(200, ClientSize.Width - 50), Math.Max(100, ClientSize.Height - 162));
            int available = Math.Max(280, _grid.ClientSize.Width - SystemInformation.VerticalScrollBarWidth);
            int columns = Math.Max(1, available / 310), width = available / columns - 16;
            _grid.SuspendLayout();
            foreach (Control card in _grid.Controls) card.Size = new Size(width, 184);
            _grid.ResumeLayout();
        }
        public override void OnShown() { DoLayout(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            PaintPremiumTitle(e.Graphics, "PERFORMANCES & OUTILS", "Mesure, compare, ajuste. Les outils pour aller plus loin avec ton PC.");
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && _wheel != null) { Application.RemoveMessageFilter(_wheel); _wheel = null; }
            base.Dispose(disposing);
        }

        private sealed class ModuleCard : Button
        {
            public readonly int Category;
            private readonly int _icon, _number;
            private readonly string _description;
            private bool _hot;
            public ModuleCard(int category, int icon, string title, string description, Action action, int number)
            {
                Category = category; _icon = icon; _number = number; Text = title; _description = description;
                AccessibleName = title; AccessibleDescription = description;
                Margin = new Padding(0, 0, 16, 16); BackColor = FpsUi.BgMain;
                FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                MouseEnter += (s, e) => { _hot = true; Invalidate(); }; MouseLeave += (s, e) => { _hot = false; Invalidate(); };
                GotFocus += (s, e) => Invalidate(); LostFocus += (s, e) => Invalidate();
                Click += (s, e) => action();
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; g.Clear(FpsUi.BgMain);
                bool active = _hot || (Focused && ShowFocusCues);
                OnyxDesign.PaintCard(g, ClientRectangle, active ? FpsUi.CardHi : FpsUi.Card, active ? FpsUi.GoldDim : FpsUi.Border, 12);
                OnyxDesign.IconWell(g, _icon, new Rectangle(20, 20, 50, 50), active);
                OnyxDesign.Text(g, _number.ToString("00"), FpsUi.Small, FpsUi.Dim2, new Rectangle(Width - 42, 22, 28, 20));
                OnyxDesign.Text(g, Text, FpsUi.H2, FpsUi.Ink, new Rectangle(20, 88, Width - 40, 28));
                OnyxDesign.Text(g, _description, FpsUi.Body, FpsUi.Dim, new Rectangle(20, 124, Width - 64, 44), true);
                OnyxDesign.Text(g, "↗", FpsUi.H2, active ? FpsUi.Gold : FpsUi.Dim2, new Rectangle(Width - 35, Height - 34, 24, 24));
            }
        }
    }
}
