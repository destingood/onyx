using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Page « Actus » : les titres des sites d'actualité et des flux Steam des jeux suivis, et les
    /// favoris. Titres seulement (aucun extrait d'article) ; un clic ouvre l'article dans le navigateur.
    /// Premier chargement à la première ouverture ; une réponse arrivée après une relance est ignorée.
    /// </summary>
    internal sealed class PageActus : FpsPage
    {
        private const int OngletToutes = 0, OngletSuivis = 1, OngletFavoris = 2;
        private static readonly string[] NomsOnglets = { "Toutes", "Mes jeux suivis", "Favoris" };
        private const string TexteErreur = "Source indisponible pour l'instant.";

        private readonly TextBox _search;
        private readonly Button[] _onglets = new Button[NomsOnglets.Length];
        private readonly Label _lSource, _etat;
        private readonly ComboBox _source;
        private readonly Button _actualiser, _reessayer;
        private readonly BufferedFlow _flow;
        private ScrollWheelFilter _wheel;

        private int _onglet, _generation;
        private bool _charge, _occupe, _erreur, _majCombo;
        private string _suivisCharges = "";
        private List<Actu> _toutes = new List<Actu>();

        public PageActus(DashboardForm host) : base(host)
        {
            _search = new TextBox();
            try { _search.PlaceholderText = "Filtrer les titres…"; } catch { }
            _search.BackColor = FpsUi.Card; _search.ForeColor = FpsUi.Ink;
            _search.BorderStyle = BorderStyle.FixedSingle; _search.Font = FpsUi.Small;
            _search.TextChanged += (s, e) => Affiche();   // filtre local : pas d'appel réseau
            Controls.Add(_search);

            for (int i = 0; i < _onglets.Length; i++)
            {
                int k = i;
                _onglets[i] = FpsUi.GhostButton(NomsOnglets[i]);
                _onglets[i].Height = 30;
                _onglets[i].Click += (s, e) => { _onglet = k; MajOnglets(); Affiche(); };
                Controls.Add(_onglets[i]);
            }

            _lSource = new Label { Text = "Source", AutoSize = true, Font = FpsUi.Small, ForeColor = FpsUi.Dim, BackColor = Color.Transparent };
            Controls.Add(_lSource);
            _source = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
                BackColor = FpsUi.Card, ForeColor = FpsUi.Ink, Font = FpsUi.Small, Width = 170,
            };
            _source.Items.Add("Toutes");
            foreach (Actus.SourceActu s in Actus.Sources) _source.Items.Add(s.Nom);
            _source.SelectedIndex = 0;
            _source.SelectedIndexChanged += (s, e) => { if (!_majCombo) Affiche(); };
            Controls.Add(_source);

            _actualiser = FpsUi.GhostButton("Actualiser");
            _actualiser.Size = new Size(110, 28);
            _actualiser.Click += (s, e) => Recharge();
            Controls.Add(_actualiser);

            _flow = new BufferedFlow();
            _flow.AutoScroll = true;
            _flow.FlowDirection = FlowDirection.TopDown;
            _flow.WrapContents = false;
            _flow.BackColor = Color.Transparent;
            _flow.Padding = new Padding(0, 4, 0, 12);
            _flow.Resize += (s, e) => AjusteLignes();
            Controls.Add(_flow);
            _wheel = new ScrollWheelFilter(_flow);
            try { Application.AddMessageFilter(_wheel); } catch { }

            _etat = new Label { AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Font = FpsUi.Body, ForeColor = FpsUi.Dim, BackColor = FpsUi.BgMain, Visible = false };
            Controls.Add(_etat);
            _reessayer = FpsUi.GoldButton("Réessayer");
            _reessayer.Size = new Size(130, 34); _reessayer.Visible = false;
            _reessayer.Click += (s, e) => Recharge();
            Controls.Add(_reessayer);
            _etat.BringToFront(); _reessayer.BringToFront();

            MajOnglets();
            Resize += (s, e) => DoLayout();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _wheel != null) { try { Application.RemoveMessageFilter(_wheel); } catch { } _wheel = null; }
            base.Dispose(disposing);
        }

        public override void OnShown()
        {
            DoLayout();
            // Jeux suivis modifiés depuis Bons plans : leurs flux ne sont pas encore chargés.
            if (!_charge || SignatureSuivis() != _suivisCharges) { _charge = true; Recharge(); }
            else Affiche();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            PaintTitle(e.Graphics, "ACTUS", "Les derniers titres des sites de jeu vidéo et de tes jeux suivis");
        }

        // ------------------------------------------------------------------ disposition

        private void DoLayout()
        {
            int w = ClientSize.Width, h = ClientSize.Height, x0 = 34, droite = Math.Max(x0 + 300, w - 34);
            _search.SetBounds(Math.Max(x0 + 260, droite - 280), 24, Math.Min(280, droite - x0 - 260), 26);

            int x = x0, y = 90;
            foreach (Button b in _onglets)
            {
                b.Width = TextRenderer.MeasureText(b.Text, b.Font).Width + 28;
                b.Location = new Point(x, y);
                x += b.Width + 6;
            }
            x += 18;
            if (x + _lSource.Width + _source.Width + _actualiser.Width + 30 > droite) { x = x0; y += 38; }
            _lSource.Location = new Point(x, y + (30 - _lSource.Height) / 2);
            _source.Location = new Point(_lSource.Right + 6, y + (30 - _source.Height) / 2);
            _actualiser.Location = new Point(_source.Right + 18, y + 1);

            int haut = y + 42;
            _flow.SetBounds(x0, haut, Math.Max(200, w - x0 - 16), Math.Max(80, h - haut - 16));
            _etat.SetBounds(_flow.Left, _flow.Top + 30, Math.Max(200, droite - x0), 48);
            _reessayer.Location = new Point(_flow.Left + (_etat.Width - _reessayer.Width) / 2, _etat.Bottom + 6);
            AjusteLignes();
            Invalidate();
        }

        private void AjusteLignes()
        {
            int lw = Math.Max(200, _flow.ClientSize.Width - 22);
            foreach (Control c in _flow.Controls) c.Width = lw;
        }

        private void MajOnglets()
        {
            for (int i = 0; i < _onglets.Length; i++)
            {
                bool actif = i == _onglet;
                Button b = _onglets[i];
                b.ForeColor = actif ? FpsUi.Gold : FpsUi.Ink;
                b.BackColor = actif ? FpsUi.BlendGold(FpsUi.Card, 0.16f) : Color.FromArgb(31, 27, 22);
                b.FlatAppearance.BorderColor = FpsUi.Gold;
                b.FlatAppearance.BorderSize = actif ? 1 : 0;
            }
            _actualiser.Enabled = _onglet != OngletFavoris && !_occupe;
        }

        // ------------------------------------------------------------------ chargement

        private static string SignatureSuivis()
        {
            return string.Join(",", Magasin.Suivis.Where(j => j != null && j.AppId > 0).Take(10).Select(j => j.AppId));
        }

        private void Recharge()
        {
            int gen = ++_generation;
            // Copie sur le thread d'interface : la liste des suivis lui appartient.
            var suivis = Magasin.Suivis.Where(j => j != null).Select(j => new JeuSuivi { AppId = j.AppId, Nom = j.Nom }).ToList();
            _suivisCharges = SignatureSuivis();
            _occupe = true;
            MajOnglets();
            if (_onglet != OngletFavoris) Etat("Chargement…", false);
            Task.Run(async () =>
            {
                List<Actu> l;
                try { l = await OffresReseau.Actualites(suivis).ConfigureAwait(false); } catch { l = new List<Actu>(); }
                bool erreur = OffresReseau.DerniereErreur("actus");
                try
                {
                    if (IsDisposed || !IsHandleCreated) return;
                    BeginInvoke((Action)(() =>
                    {
                        if (IsDisposed || gen != _generation) return;
                        _occupe = false;
                        _toutes = l ?? new List<Actu>();
                        _erreur = erreur;
                        MajSources();
                        MajOnglets();
                        Affiche();
                    }));
                }
                catch { }
            });
        }

        // Liste des sources : les sites, puis les jeux suivis présents dans les titres chargés.
        private void MajSources()
        {
            string choisi = _source.SelectedItem as string;
            var noms = new List<string> { "Toutes" };
            noms.AddRange(Actus.Sources.Select(s => s.Nom));
            foreach (string s in _toutes.Select(a => a.Source).Where(s => !string.IsNullOrEmpty(s)).Distinct())
                if (!noms.Contains(s)) noms.Add(s);
            _majCombo = true;
            _source.Items.Clear();
            foreach (string n in noms) _source.Items.Add(n);
            int i = choisi == null ? 0 : noms.IndexOf(choisi);
            _source.SelectedIndex = i < 0 ? 0 : i;
            _majCombo = false;
        }

        private static bool EstSite(string source)
        {
            return Actus.Sources.Any(s => string.Equals(s.Nom, source, StringComparison.OrdinalIgnoreCase));
        }

        private static List<Actu> ListeFavoris()
        {
            return Magasin.FavorisActus.Where(f => f != null)
                    .Select(f => new Actu { Titre = f.Titre, Lien = f.Url, Source = f.Source, Date = f.Date })
                    .OrderByDescending(a => a.Date).ToList();
        }

        // ------------------------------------------------------------------ rendu

        private void Etat(string texte, bool erreur)
        {
            VideLignes();
            _etat.Text = texte; _etat.Visible = true;
            _reessayer.Visible = erreur;
        }

        private void VideLignes()
        {
            _flow.SuspendLayout();
            foreach (Control c in _flow.Controls.Cast<Control>().ToList()) c.Dispose();
            _flow.Controls.Clear();
            _flow.ResumeLayout();
        }

        private void Affiche()
        {
            if (_onglet != OngletFavoris && _occupe) { Etat("Chargement…", false); return; }

            List<Actu> baseActus;
            if (_onglet == OngletFavoris) baseActus = ListeFavoris();
            else if (_onglet == OngletSuivis)
            {
                if (Magasin.Suivis.Count == 0) { Etat("Suis un jeu dans Bons plans pour voir ses actus ici.", false); return; }
                baseActus = _toutes.Where(a => !EstSite(a.Source)).ToList();
            }
            else baseActus = _toutes;

            string source = _source.SelectedIndex > 0 ? _source.SelectedItem as string : null;
            List<Actu> liste = Actus.Filtre(baseActus, _search.Text, source);

            if (liste.Count == 0)
            {
                bool erreur = _onglet != OngletFavoris && _erreur && _toutes.Count == 0;
                Etat(erreur ? TexteErreur : "Aucun titre ne correspond à ces filtres.", erreur);
                return;
            }
            _etat.Visible = false; _reessayer.Visible = false;
            _flow.SuspendLayout();
            foreach (Control c in _flow.Controls.Cast<Control>().ToList()) c.Dispose();
            _flow.Controls.Clear();
            int lw = Math.Max(200, _flow.ClientSize.Width - 22);
            DateTime maintenant = DateTime.UtcNow;
            foreach (Actu a in liste) _flow.Controls.Add(new Ligne(this, a, lw, maintenant));
            _flow.ResumeLayout();
            try { _flow.AutoScrollPosition = Point.Empty; } catch { }
        }

        internal bool EstFavori(Actu a)
        {
            return a != null && !string.IsNullOrEmpty(a.Lien) && Favoris.Contient(Magasin.FavorisActus, a.Lien);
        }

        internal void BasculeFavori(Actu a)
        {
            if (a == null || string.IsNullOrEmpty(a.Lien)) return;
            Favoris.Bascule(Magasin.FavorisActus, Actus.EnFavori(a));
            Magasin.SauveFavorisActus();
            if (_onglet == OngletFavoris) Affiche();
        }

        /// <summary>Une ligne : ☆, titre, source et âge. Clic sur le titre : l'article dans le navigateur.</summary>
        private sealed class Ligne : Control
        {
            private const int H = 36;
            private readonly PageActus _page;
            private readonly Actu _a;
            private readonly string _meta;
            private bool _survol;
            private static readonly Rectangle REtoile = new Rectangle(4, 5, 26, 26);

            public Ligne(PageActus page, Actu a, int largeur, DateTime maintenantUtc)
            {
                _page = page; _a = a;
                string age = Actus.Age(a.Date, maintenantUtc);
                _meta = string.IsNullOrEmpty(age) ? (a.Source ?? "") : (a.Source ?? "") + " · " + age;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                Size = new Size(largeur, H);
                Margin = new Padding(0, 0, 0, 4);
                BackColor = FpsUi.BgMain;
                Cursor = Cursors.Hand;
                MouseEnter += (s, e) => { _survol = true; Invalidate(); };
                MouseLeave += (s, e) => { _survol = false; Invalidate(); };
            }

            protected override void OnMouseClick(MouseEventArgs e)
            {
                base.OnMouseClick(e);
                if (e.Button != MouseButtons.Left) return;
                if (REtoile.Contains(e.Location)) { _page.BasculeFavori(_a); if (!IsDisposed) Invalidate(); return; }
                PageBonsPlans.Ouvre(_a.Lien);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                FpsUi.PaintCard(g, new Rectangle(0, 0, Width - 1, H - 1), _survol ? FpsUi.CardHi : FpsUi.Card, FpsUi.Border, 8f);
                bool fav = _page.EstFavori(_a);
                TextRenderer.DrawText(g, fav ? "★" : "☆", FpsUi.H2, REtoile, fav ? FpsUi.Gold : FpsUi.Dim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                int mw = Math.Min(260, TextRenderer.MeasureText(_meta, FpsUi.Tiny).Width + 4);
                TextRenderer.DrawText(g, _meta, FpsUi.Tiny, new Rectangle(Width - mw - 12, 0, mw, H), FpsUi.Dim,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, _a.Titre ?? "", FpsUi.Body, new Rectangle(36, 0, Math.Max(40, Width - mw - 36 - 24), H), FpsUi.Ink,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
        }
    }
}
