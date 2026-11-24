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
    /// favoris. Le plus récent illustré « à la une », les autres en grille de cartes (vignette du flux,
    /// sinon jaquette du jeu suivi). Aucun extrait d'article ; un clic ouvre l'article dans le navigateur.
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
        private readonly GrilleActus _flow;
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

            _flow = new GrilleActus(this);
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
            Invalidate();
        }

        private const int Ecart = 14, LargeurMin = 270;

        /// <summary>Grille : autant de colonnes de LargeurMin que la largeur le permet ; la une prend toute la ligne.</summary>
        private void MajOnglets()
        {
            for (int i = 0; i < _onglets.Length; i++)
            {
                bool actif = i == _onglet;
                Button b = _onglets[i];
                b.ForeColor = actif ? FpsUi.Gold : FpsUi.Ink;
                b.BackColor = actif ? FpsUi.BlendGold(FpsUi.Card, 0.16f) : FpsUi.CardHi;
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

        internal int AffichagesPourTest, RechargesPourTest;
        private List<Actu> _listeRendue;

        private void Recharge()
        {
            RechargesPourTest++;
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

        private List<Actu> ListeFavoris()
        {
            // Le fichier des favoris ne garde pas l'image : on la reprend des titres chargés.
            var charges = new Dictionary<string, Actu>();
            foreach (Actu a in _toutes) if (a.Lien != null && !charges.ContainsKey(a.Lien)) charges[a.Lien] = a;
            return Magasin.FavorisActus.Where(f => f != null)
                    .Select(f =>
                    {
                        Actu c;
                        charges.TryGetValue(f.Url ?? "", out c);
                        return new Actu { Titre = f.Titre, Lien = f.Url, Source = f.Source, Date = f.Date, Image = c != null ? c.Image : null, AppId = c != null ? c.AppId : 0 };
                    })
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
            _listeRendue = null;
            _flow.Vider();
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
            // Mêmes titres dans le même ordre : rien à reconstruire (l'âge se calcule à la peinture).
            if (!_etat.Visible && _listeRendue != null && _listeRendue.Count == liste.Count && _flow.Nombre > 0
                && !liste.Where((a, i) => !ReferenceEquals(a, _listeRendue[i])).Any())
                return;
            _listeRendue = liste;
            _etat.Visible = false; _reessayer.Visible = false;
            AffichagesPourTest++;
            _flow.Definir(liste);
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

        // ------------------------------------------------------------------ cartes

        private static readonly Font PoliceUne = FpsUi.F(16f, true);
        private static readonly Font PoliceBadge = FpsUi.F(7.5f, true);
        private static readonly Font PoliceFond = FpsUi.F(14f, true);

        /// <summary>Couleur d'une source : une par site, violet pour les jeux suivis.</summary>
        internal static Color CouleurSource(string source)
        {
            Color[] c = { FpsUi.Gold, FpsUi.Warn, FpsUi.Ok, Color.FromArgb(240, 110, 160) };   // pas de rouge : il signale une erreur
            for (int i = 0; i < Actus.Sources.Length; i++)
                if (string.Equals(Actus.Sources[i].Nom, source, StringComparison.OrdinalIgnoreCase)) return c[i % c.Length];
            return FpsUi.Violet;
        }

        /// <summary>
        /// Grille d'un seul tenant : les titres sont peints, pas posés en contrôles. Cent vingt cartes
        /// en fenêtres coûtaient ~300 ms (création et mise en page) à l'arrivée du flux.
        /// </summary>
        private sealed class GrilleActus : ScrollableControl
        {
            internal readonly PageActus Page;
            private readonly List<Vignette> _v = new List<Vignette>();
            private Vignette _sous;

            public GrilleActus(PageActus page)
            {
                Page = page;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                    | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
                AutoScroll = true; TabStop = true;
                BackColor = FpsUi.BgMain;
                Name = "actus-grille"; AccessibleName = "Actualités"; AccessibleRole = AccessibleRole.List;
            }

            public int Nombre { get { return _v.Count; } }

            public void Definir(List<Actu> liste)
            {
                _v.Clear(); _sous = null; Cursor = Cursors.Default;
                int debut = 0;
                if (liste.Count >= 4 && (liste[0].Image != null || liste[0].AppId > 0)) { _v.Add(new Une(this, liste[0])); debut = 1; }
                for (int i = debut; i < liste.Count; i++) _v.Add(new Carte(this, liste[i]));
                AutoScrollPosition = Point.Empty;
                Place();
                Invalidate();
            }

            public void Vider()
            {
                _v.Clear(); _sous = null; Cursor = Cursors.Default;
                AutoScrollMinSize = Size.Empty;
                Invalidate();
            }

            /// <summary>Même grille qu'avant : la une sur toute la largeur, puis des colonnes d'au moins LargeurMin.</summary>
            private void Place()
            {
                int dispo = Math.Max(220, ClientSize.Width - 22);
                int cols = Math.Max(1, (dispo + Ecart) / (LargeurMin + Ecart));
                int w = Math.Max(200, (dispo - Ecart * cols) / cols);
                int h = w * 9 / 16 + Carte.HauteurTexte;
                int y = 4, col = 0;
                foreach (var v in _v)
                {
                    if (v is Une)
                    {
                        int lh = Math.Max(210, Math.Min(300, (cols * (w + Ecart)) * 30 / 100));
                        v.Bounds = new Rectangle(0, y, cols * (w + Ecart) - Ecart, lh);
                        y += lh + Ecart; col = 0;
                        continue;
                    }
                    v.Bounds = new Rectangle(col * (w + Ecart), y, w, h);
                    if (++col == cols) { col = 0; y += h + Ecart; }
                }
                if (col > 0) y += h + Ecart;
                var min = _v.Count == 0 ? Size.Empty : new Size(0, y + 12);
                if (AutoScrollMinSize != min) AutoScrollMinSize = min;
            }

            protected override void OnResize(EventArgs e) { base.OnResize(e); Place(); Invalidate(); }

            internal void Redessine(Vignette v)
            {
                if (IsDisposed) return;
                var r = v.Bounds; r.Offset(0, AutoScrollPosition.Y); r.Inflate(1, 1);
                Invalidate(r);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                int dy = AutoScrollPosition.Y;
                foreach (var v in _v)
                {
                    var r = v.Bounds; r.Offset(0, dy);
                    if (!r.IntersectsWith(e.ClipRectangle)) continue;
                    var etat = g.Save();
                    try
                    {
                        g.TranslateTransform(r.X, r.Y);
                        g.SetClip(new Rectangle(0, 0, r.Width, r.Height), System.Drawing.Drawing2D.CombineMode.Intersect);
                        v.Peint(g);
                    }
                    catch { }
                    finally { g.Restore(etat); }
                }
            }

            private Vignette Sous(Point p)
            {
                p.Offset(0, -AutoScrollPosition.Y);
                foreach (var v in _v) if (v.Bounds.Contains(p)) return v;
                return null;
            }

            private void Survole(Vignette v)
            {
                if (v == _sous) return;
                if (_sous != null) { _sous.Survol = false; Redessine(_sous); }
                _sous = v;
                if (v != null) { v.Survol = true; Redessine(v); }
                Cursor = v != null ? Cursors.Hand : Cursors.Default;
            }

            protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); Survole(Sous(e.Location)); }
            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Survole(null); }
            protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (!Focused) Focus(); }

            protected override void OnMouseClick(MouseEventArgs e)
            {
                base.OnMouseClick(e);
                if (e.Button != MouseButtons.Left) return;
                var v = Sous(e.Location);
                if (v != null) v.Clic(new Point(e.X - v.Bounds.X, e.Y - AutoScrollPosition.Y - v.Bounds.Y));
            }

            protected override void OnScroll(ScrollEventArgs se)
            {
                base.OnScroll(se);
                if (ClientRectangle.Contains(PointToClient(MousePosition))) Survole(Sous(PointToClient(MousePosition)));
            }

            protected override bool IsInputKey(Keys keyData)
            {
                switch (keyData & Keys.KeyCode)
                {
                    case Keys.Up: case Keys.Down: case Keys.PageUp: case Keys.PageDown: case Keys.Home: case Keys.End: return true;
                }
                return base.IsInputKey(keyData);
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                int y = -AutoScrollPosition.Y, page = Math.Max(60, ClientSize.Height - 60);
                switch (e.KeyCode)
                {
                    case Keys.Down: y += 60; break;
                    case Keys.Up: y -= 60; break;
                    case Keys.PageDown: y += page; break;
                    case Keys.PageUp: y -= page; break;
                    case Keys.Home: y = 0; break;
                    case Keys.End: y = int.MaxValue / 2; break;
                    default: return;
                }
                AutoScrollPosition = new Point(0, Math.Max(0, y));
                e.Handled = true;
            }

            protected override AccessibleObject CreateAccessibilityInstance() { return new GrilleAcc(this); }

            private sealed class GrilleAcc : ControlAccessibleObject
            {
                private readonly GrilleActus _g;
                public GrilleAcc(GrilleActus g) : base(g) { _g = g; }
                public override AccessibleRole Role { get { return AccessibleRole.List; } }
                public override int GetChildCount() { return _g._v.Count; }
                public override AccessibleObject GetChild(int index) { return index >= 0 && index < _g._v.Count ? new VignetteAcc(_g, _g._v[index], this) : null; }
            }

            private sealed class VignetteAcc : AccessibleObject
            {
                private readonly GrilleActus _g; private readonly Vignette _v; private readonly AccessibleObject _parent;
                public VignetteAcc(GrilleActus g, Vignette v, AccessibleObject parent) { _g = g; _v = v; _parent = parent; }
                public override string Name { get { return _v.Article.Titre ?? ""; } }
                public override string Description { get { return (_v.Article.Source ?? "") + " · Ouvrir l'article"; } }
                public override AccessibleRole Role { get { return AccessibleRole.Link; } }
                public override AccessibleObject Parent { get { return _parent; } }
                public override string DefaultAction { get { return "Ouvrir"; } }
                public override void DoDefaultAction() { PageBonsPlans.Ouvre(_v.Article.Lien); }
                public override Rectangle Bounds
                {
                    get { var r = _v.Bounds; r.Offset(0, _g.AutoScrollPosition.Y); return _g.RectangleToScreen(r); }
                }
            }
        }

        /// <summary>Base commune : image (vignette du flux, sinon jaquette du jeu), survol, étoile, clic.</summary>
        private abstract class Vignette
        {
            /// <summary>Les textes GDI ignorent sinon la translation de la grille (ils s'empileraient en haut à gauche).</summary>
            protected const TextFormatFlags Tf = TextFormatFlags.PreserveGraphicsTranslateTransform | TextFormatFlags.PreserveGraphicsClipping;
            private readonly GrilleActus _hote;
            protected readonly PageActus _page;
            protected readonly Actu _a;
            internal Rectangle Bounds;
            internal bool Survol;
            protected Rectangle _etoile;
            internal Actu Article { get { return _a; } }
            protected bool _survol { get { return Survol; } }
            protected int Width { get { return Bounds.Width; } }
            protected int Height { get { return Bounds.Height; } }
            // Calculés à chaque peinture : une carte gardée d'une visite à l'autre reste à l'heure.
            protected string AgeAffiche { get { return Actus.Age(_a.Date, DateTime.UtcNow); } }
            protected bool EstNouveau { get { DateTime n = DateTime.UtcNow; return _a.Date != DateTime.MinValue && _a.Date <= n && (n - _a.Date).TotalHours < 3; } }

            protected Vignette(GrilleActus hote, Actu a) { _hote = hote; _page = hote.Page; _a = a; }

            /// <summary>Peint la vignette dans un repère où (0, 0) est son coin haut gauche.</summary>
            public abstract void Peint(Graphics g);

            internal void Clic(Point p)
            {
                if (_etoile.Contains(p)) { _page.BasculeFavori(_a); _hote.Redessine(this); return; }
                PageBonsPlans.Ouvre(_a.Lien);
            }

            private void Pret()
            {
                try { if (!_hote.IsDisposed && _hote.IsHandleCreated) _hote.BeginInvoke((Action)(() => _hote.Redessine(this))); } catch { }
            }


            /// <summary>Image prête, ou null (chargement en cours ou absente : un fond la remplace).</summary>
            protected Image ImageArticle()
            {
                Image img = null;
                try
                {
                    if (_a.Image != null) img = GameArt.GetUrl(_a.Image, Pret);
                    if (img == null && _a.AppId > 0 && (_a.Image == null || GameArt.EchecUrl(_a.Image))) img = GameArt.Get(_a.AppId, Pret);
                }
                catch { }
                return img;
            }

            /// <summary>Remplit r sans déformer (recadrage au centre) ; zoom > 1 au survol.</summary>
            protected static void Couverture(Graphics g, Image img, Rectangle r, float zoom)
            {
                float cible = r.Width / (float)r.Height, source = img.Width / (float)img.Height;
                float sw = source > cible ? img.Height * cible : img.Width;
                float sh = source > cible ? img.Height : img.Width / cible;
                sw /= zoom; sh /= zoom;
                var src = new RectangleF((img.Width - sw) / 2f, (img.Height - sh) / 2f, sw, sh);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(img, r, src, GraphicsUnit.Pixel);
            }

            /// <summary>Sans image : dégradé à la couleur de la source et son nom en filigrane.</summary>
            protected void Fond(Graphics g, Rectangle r)
            {
                Color c = CouleurSource(_a.Source);
                using (var b = new System.Drawing.Drawing2D.LinearGradientBrush(r, Color.FromArgb(70, c), FpsUi.Card, 35f))
                    g.FillRectangle(b, r);
                TextRenderer.DrawText(g, _a.Source ?? "", PoliceFond, r, Color.FromArgb(110, c),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | Tf);
            }

            protected void Etoile(Graphics g, Rectangle r)
            {
                _etoile = r;
                bool fav = _page.EstFavori(_a);
                using (var b = new SolidBrush(Color.FromArgb(_survol || fav ? 200 : 150, 8, 11, 16))) g.FillEllipse(b, r);
                TextRenderer.DrawText(g, fav ? "★" : "☆", FpsUi.H3, r, fav ? FpsUi.Gold : FpsUi.Ink,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | Tf);
            }

            /// <summary>Pastille pleine (« NOUVEAU », « À LA UNE ») ; renvoie sa largeur.</summary>
            protected static int Badge(Graphics g, string texte, Color fond, Color encre, int x, int y)
            {
                Size t = TextRenderer.MeasureText(texte, PoliceBadge);
                var r = new Rectangle(x, y, t.Width + 10, 18);
                using (var p = FpsUi.Round(r, 9f)) using (var b = new SolidBrush(fond)) g.FillPath(b, p);
                TextRenderer.DrawText(g, texte, PoliceBadge, r, encre, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | Tf);
                return r.Width;
            }

            /// <summary>Point de couleur, source et âge.</summary>
            protected void Meta(Graphics g, int x, int y, int largeur, Color encre)
            {
                Color c = CouleurSource(_a.Source);
                using (var b = new SolidBrush(c)) g.FillEllipse(b, x, y + 5, 8, 8);
                string age = AgeAffiche, t = string.IsNullOrEmpty(age) ? (_a.Source ?? "") : (_a.Source ?? "") + "  ·  " + age;
                TextRenderer.DrawText(g, t, FpsUi.Tiny, new Rectangle(x + 14, y, Math.Max(20, largeur - 14), 18), encre,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | Tf);
            }
        }

        /// <summary>Carte de la grille : vignette 16:9, puis source, âge et titre sur deux lignes.</summary>
        private sealed class Carte : Vignette
        {
            public const int HauteurTexte = 84;

            public Carte(GrilleActus hote, Actu a) : base(hote, a) { }

            public override void Peint(Graphics g)
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var tout = new Rectangle(0, 0, Width - 1, Height - 1);
                FpsUi.PaintCard(g, tout, _survol ? FpsUi.CardHi : FpsUi.Card, _survol ? FpsUi.Gold : FpsUi.Border, 10f);

                var ri = new Rectangle(1, 1, Width - 2, Height - HauteurTexte - 1);
                Region avant = g.Clip;
                using (var p = FpsUi.Round(new Rectangle(1, 1, Width - 3, Height - 3), 9f))
                {
                    g.SetClip(p, System.Drawing.Drawing2D.CombineMode.Intersect);
                    g.SetClip(ri, System.Drawing.Drawing2D.CombineMode.Intersect);
                    Image img = ImageArticle();
                    if (img != null) { try { Couverture(g, img, ri, _survol ? 1.06f : 1f); } catch { Fond(g, ri); } }
                    else Fond(g, ri);
                    // Fondu bas : l'image se fond dans la carte.
                    var bas = new Rectangle(ri.X, ri.Bottom - 28, ri.Width, 29);
                    using (var b = new System.Drawing.Drawing2D.LinearGradientBrush(bas, Color.FromArgb(0, FpsUi.Card), _survol ? FpsUi.CardHi : FpsUi.Card, 90f))
                        g.FillRectangle(b, bas);
                }
                g.Clip = avant;

                if (EstNouveau) Badge(g, "NOUVEAU", FpsUi.Gold, FpsUi.BgMain, 10, 10);
                Etoile(g, new Rectangle(Width - 38, 8, 30, 30));

                Meta(g, 12, ri.Bottom + 6, Width - 24, FpsUi.Dim);
                TextRenderer.DrawText(g, _a.Titre ?? "", FpsUi.H3, new Rectangle(12, ri.Bottom + 26, Width - 24, HauteurTexte - 32), _survol ? FpsUi.Gold : FpsUi.Ink,
                    TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | Tf | TextFormatFlags.TextBoxControl);
            }
        }

        /// <summary>« À la une » : le titre le plus récent, image pleine largeur, titre en grand sur un dégradé.</summary>
        private sealed class Une : Vignette
        {
            public Une(GrilleActus hote, Actu a) : base(hote, a) { }

            public override void Peint(Graphics g)
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var tout = new Rectangle(0, 0, Width - 1, Height - 1);
                FpsUi.PaintCard(g, tout, FpsUi.Card, _survol ? FpsUi.Gold : FpsUi.Border, 12f);

                Region avant = g.Clip;
                using (var p = FpsUi.Round(new Rectangle(1, 1, Width - 3, Height - 3), 11f))
                {
                    g.SetClip(p, System.Drawing.Drawing2D.CombineMode.Intersect);
                    var ri = new Rectangle(1, 1, Width - 2, Height - 2);
                    Image img = ImageArticle();
                    if (img != null) { try { Couverture(g, img, ri, _survol ? 1.04f : 1f); } catch { Fond(g, ri); } }
                    else Fond(g, ri);
                    // Dégradés bas et gauche : le titre reste lisible sur n'importe quelle image.
                    using (var b = new System.Drawing.Drawing2D.LinearGradientBrush(ri, Color.FromArgb(0, FpsUi.BgMain), Color.FromArgb(235, FpsUi.BgMain), 90f))
                    {
                        var blend = new System.Drawing.Drawing2D.Blend { Positions = new[] { 0f, 0.35f, 1f }, Factors = new[] { 0f, 0.15f, 1f } };
                        b.Blend = blend;
                        g.FillRectangle(b, ri);
                    }
                    var gauche = new Rectangle(ri.X, ri.Y, ri.Width * 2 / 3, ri.Height);
                    using (var b = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(gauche.X - 1, gauche.Y, gauche.Width + 2, gauche.Height), Color.FromArgb(150, FpsUi.BgMain), Color.FromArgb(0, FpsUi.BgMain), 0f))
                        g.FillRectangle(b, gauche);
                }
                g.Clip = avant;

                int x = 22, larg = Math.Min(Width - 44, Math.Max(320, Width * 68 / 100));
                int bx = x + Badge(g, "À LA UNE", FpsUi.Gold, FpsUi.BgMain, x, 18) + 6;
                if (EstNouveau) Badge(g, "NOUVEAU", Color.FromArgb(220, FpsUi.BgMain), FpsUi.Gold, bx, 18);
                Etoile(g, new Rectangle(Width - 46, 14, 32, 32));

                Size t = TextRenderer.MeasureText(g, _a.Titre ?? "", PoliceUne, new Size(larg, 200), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | Tf);
                int th = Math.Min(t.Height, PoliceUne.Height * 3 + 4);
                int ty = Height - 22 - 20 - th;
                TextRenderer.DrawText(g, _a.Titre ?? "", PoliceUne, new Rectangle(x, ty, larg, th), _survol ? FpsUi.Gold : FpsUi.Ink,
                    TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | Tf | TextFormatFlags.TextBoxControl);
                Meta(g, x, Height - 22 - 16, larg, FpsUi.Ink);
            }
        }
    }
}
