using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Page « Bons plans » : promos (ou tout le catalogue) de 14 stores au choix (Steam, GOG, Nintendo, itch.io, et via CheapShark
    /// Epic, Fanatical, Ubisoft…), nouveautés Steam, jeux gratuits (Epic, GamerPower), jeux suivis et favoris. Rien n'est chargé avant la première ouverture ; chaque chargement porte
    /// un numéro de génération et une réponse arrivée après un changement de filtre est ignorée.
    /// </summary>
    internal sealed class PageBonsPlans : FpsPage
    {
        private const int OngletPromos = 0, OngletGratuits = 1, OngletNouveautes = 2, OngletSuivis = 3, OngletFavoris = 4, OngletPopulaires = 5;
        private const int ParPage = 24, LotsMax = 3, PasSteam = 50;
        private static readonly string[] NomsOnglets = { "Promos", "Gratuits", "Nouveautés", "Suivis", "Favoris", "Populaires" };
        private static readonly Offres.Tranche[] Tranches = Offres.Tranches;
        private static readonly string[] ModesOffres = { "En promo", "Tous les jeux" };
        private static readonly KeyValuePair<string, string>[] Tris =
        {
            new KeyValuePair<string, string>("Remise", "remise"),
            new KeyValuePair<string, string>("Prix ↑", "prix"),
            new KeyValuePair<string, string>("Prix ↓", "prixdesc"),
            new KeyValuePair<string, string>("Récents", "recent"),
            new KeyValuePair<string, string>("Nom", "nom"),
        };
        private const string TexteMax = "50 jeux suivis au maximum : retire-en un d'abord.";
        private const string TexteErreur = "Source indisponible pour l'instant.";

        // Une seule info-bulle pour toutes les cartes : chaque ToolTip est une fenêtre, et une
        // fenêtre par carte coûtait à la création et à chaque changement de focus.
        internal readonly ToolTip Infobulles = new ToolTip();
        private readonly PartnerBanner _partner = new PartnerBanner();
        private readonly Button _support;
        private readonly TextBox _search;
        private readonly Button[] _onglets = new Button[NomsOnglets.Length];
        private readonly Button[] _pilules = new Button[Tranches.Length];
        private readonly Label _lPrix, _lCat, _lPlat, _lTri, _lOffres, _etat, _numPage, _info;
        private readonly ComboBox _cat, _plat, _tri, _offres;
        private readonly Button _categoryChoice, _platformChoice, _sortChoice, _offresChoice;
        private readonly Button _verifier, _installes, _souhaits, _reessayer, _prec, _suiv;
        private readonly Button _stores;
        private readonly ContextMenuStrip _menuStores;
        private bool _storesChanges;
        private int _masquesRetires;   // offres retirées par « J'ai déjà » / « Pas pour moi » depuis le dernier chargement
        private readonly ContextMenuStrip _menuCarte = new ContextMenuStrip { Font = FpsUi.Body, ShowImageMargin = false };
        private readonly ContextMenuStrip _menuSouhaits = new ContextMenuStrip { Font = FpsUi.Body, ShowImageMargin = false };
        private bool OngletMasquable { get { return _onglet != OngletSuivis && _onglet != OngletFavoris; } }
        private readonly Dictionary<string, int> _pagesStores = new Dictionary<string, int>();   // prochaine page par store
        private readonly HashSet<string> _finis = new HashSet<string>();   // stores sans page suivante
        private readonly BufferedFlow _flow;
        private readonly LinkLabel _pied;
        private readonly Label _mention;
        private readonly Timer _attente;
        private readonly OnyxScrollPanel _viewport = new OnyxScrollPanel();
        private readonly BufferedPanel _filters = new BufferedPanel(), _searchBox = new BufferedPanel();
        private readonly Label _context = new Label(), _resultsLabel = new Label();

        private int _onglet, _tranche, _generation, _pageIdx, _debutSteam;   // _tranche : index dans Tranches (0 : Tous)
        private decimal _tauxUsd;   // dollars pour 1 euro (BCE) ; 0 tant qu'inconnu
        private bool _charge, _occupe, _steamFini, _majCombos;
        private List<Offre> _resultats = new List<Offre>();
        private List<Offre> _gratuitsBruts;
        private DateTime _gratuitsLe;   // UTC ; au-delà d'une heure, la liste est rechargée
        private List<Offre> _populairesBruts;
        private DateTime _populairesLe;   // UTC ; même règle

        public PageBonsPlans(DashboardForm host) : base(host)
        {
            _support = OnyxDesign.Button("Soutenir ONYX  ↗");
            _support.AccessibleDescription = "Soutien facultatif au développement d’ONYX";
            _support.Click += (s, e) => Ouvre(Monetisation.Support(MonetisationRuntime.Settings));
            Controls.Add(_support); Controls.Add(_partner);
            _search = new TextBox();
            try { _search.PlaceholderText = "Rechercher un jeu…"; } catch { }
            _search.BackColor = FpsUi.Card; _search.ForeColor = FpsUi.Ink;
            _search.BorderStyle = BorderStyle.None; _search.Font = FpsUi.Body;
            _search.AccessibleName = "Rechercher une offre de jeu";
            _search.TextChanged += (s, e) => { _attente.Stop(); _attente.Start(); };
            _searchBox.Paint += (s, e) => OnyxDesign.PaintCard(e.Graphics, _searchBox.ClientRectangle, FpsUi.Card, _search.Focused ? FpsUi.GoldDim : FpsUi.Border, 8);
            _search.GotFocus += (s, e) => _searchBox.Invalidate(); _search.LostFocus += (s, e) => _searchBox.Invalidate();
            _searchBox.Controls.Add(_search); Controls.Add(_searchBox);
            _filters.Paint += (s, e) => OnyxDesign.PaintCard(e.Graphics, _filters.ClientRectangle, FpsUi.Card, FpsUi.Border, 12);
            Controls.Add(_filters);
            _context.Font = FpsUi.Body; _context.ForeColor = FpsUi.Dim; _context.BackColor = Color.Transparent; _context.AutoSize = false;
            _filters.Controls.Add(_context);
            _resultsLabel.Font = FpsUi.Small; _resultsLabel.ForeColor = FpsUi.Dim; _resultsLabel.AutoSize = false; Controls.Add(_resultsLabel);

            _attente = new Timer { Interval = 400 };   // 400 ms sans frappe avant l'appel
            _attente.Tick += (s, e) => { _attente.Stop(); Recharge(); };

            for (int i = 0; i < _onglets.Length; i++)
            {
                int k = i;
                _onglets[i] = OnyxDesign.Button(NomsOnglets[i]);
                _onglets[i].Height = 40; _onglets[i].Font = FpsUi.H3; _onglets[i].Name = "deals-tab-" + i;
                _onglets[i].Click += (s, e) => ChoisitOnglet(k);
                Controls.Add(_onglets[i]);
            }

            _lPrix = Etiquette("BUDGET");
            for (int i = 0; i < _pilules.Length; i++)
            {
                int k = i;
                _pilules[i] = OnyxDesign.Button(Tranches[i].Libelle);
                _pilules[i].Height = 28;
                _pilules[i].Width = TextRenderer.MeasureText(_pilules[i].Text, FpsUi.Small).Width + 20;
                _pilules[i].Click += (s, e) => ChoisitTranche(k);
                Controls.Add(_pilules[i]);
            }

            _lCat = Etiquette("Catégorie");
            _cat = Liste(150, new[] { "Toutes" }.Concat(Offres.Categories.Select(c => c.Key)));
            _lPlat = Etiquette("Plateforme");
            _plat = Liste(130, new[] { "Toutes" }.Concat(Offres.Plateformes));
            _lTri = Etiquette("Tri");
            _tri = Liste(110, Tris.Select(t => t.Key));
            _lOffres = Etiquette("Offres");
            _offres = Liste(130, ModesOffres);
            _categoryChoice = Choice(_cat, "Catégorie de jeu"); _platformChoice = Choice(_plat, "Plateforme"); _sortChoice = Choice(_tri, "Trier les offres");
            _offresChoice = Choice(_offres, "Jeux en promo seulement, ou tous les jeux");

            _stores = OnyxDesign.Button("Stores");
            _stores.Height = 28;
            _stores.Click += (s, e) => MenuStores();
            Controls.Add(_stores);
            _menuStores = new ContextMenuStrip { ShowCheckMargin = true, ShowImageMargin = false, Font = FpsUi.Small };
            // Le menu reste ouvert pendant qu'on coche ; le choix est appliqué à sa fermeture.
            _menuStores.Closing += (s, e) => { if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked) e.Cancel = true; };
            _menuStores.Closed += (s, e) =>
            {
                if (!_storesChanges) return;
                _storesChanges = false;
                Magasin.SauveBoutiques();
                MajStores();
                Recharge();
            };
            MajStores();

            _verifier = OnyxDesign.Button("Vérifier maintenant");
            _verifier.Height = 28; _verifier.Width = 150;
            _verifier.Click += (s, e) => Verifie();
            Controls.Add(_verifier);
            _installes = OnyxDesign.Button("Suivre mes jeux installés");
            _installes.Height = 28; _installes.Width = 190;
            _installes.Click += (s, e) => SuitInstalles();
            Controls.Add(_installes);
            _souhaits = OnyxDesign.Button("Importer mes souhaits  ⌄");
            _souhaits.Height = 28; _souhaits.Width = 220;
            _souhaits.AccessibleName = "Importer une liste de souhaits Steam ou GOG";
            _menuSouhaits.BackColor = FpsUi.Card; _menuSouhaits.ForeColor = FpsUi.Ink;
            _menuSouhaits.Items.Add("Depuis Steam (compte de ce PC)", null, (s, e) => ImporteSouhaits());
            _menuSouhaits.Items.Add("Depuis GOG (pseudo, liste publique)…", null, (s, e) => ImporteSouhaitsGog());
            _souhaits.Click += (s, e) => _menuSouhaits.Show(_souhaits, new Point(0, _souhaits.Height + 4));
            Controls.Add(_souhaits);

            _flow = new BufferedFlow();
            _flow.AutoScroll = false;
            _flow.BackColor = Color.Transparent;
            _flow.Padding = Padding.Empty;
            _viewport.Content.Controls.Add(_flow); Controls.Add(_viewport);

            _etat = new Label { AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Font = FpsUi.Body, ForeColor = FpsUi.Dim, BackColor = FpsUi.BgMain, Visible = false };
            Controls.Add(_etat);
            _reessayer = OnyxDesign.AccentButton("Réessayer");
            _reessayer.Size = new Size(130, 34); _reessayer.Visible = false;
            _reessayer.Click += (s, e) => { _gratuitsBruts = null; Recharge(); };
            Controls.Add(_reessayer);
            _etat.BringToFront(); _reessayer.BringToFront();

            _prec = OnyxDesign.Button("‹ Précédent");
            _prec.Size = new Size(110, 28);
            _prec.Click += (s, e) => { if (_pageIdx > 0) { _pageIdx--; Affiche(false); } };
            Controls.Add(_prec);
            _numPage = Etiquette("Page 1");
            _numPage.AutoSize = false; _numPage.TextAlign = ContentAlignment.MiddleCenter; _numPage.Size = new Size(110, 28);
            _suiv = OnyxDesign.Button("Suivant ›");
            _suiv.Size = new Size(110, 28);
            _suiv.Click += (s, e) => Suivant();
            Controls.Add(_suiv);
            _info = Etiquette("");
            _info.AutoSize = false; _info.AutoEllipsis = true; _info.TextAlign = ContentAlignment.MiddleLeft;

            _pied = new LinkLabel
            {
                // Battle.net n'a aucune source de prix publique : simple lien vers sa boutique.
                Text = "Prix : Steam, GOG, Nintendo eShop, itch.io, CheapShark (prix US convertis au taux BCE) · Gratuits : Epic Games Store, GamerPower.com · Battle.net : prix sur sa boutique",
                Font = FpsUi.Tiny, ForeColor = FpsUi.Dim2, BackColor = FpsUi.BgMain,
                LinkColor = FpsUi.GoldDim, ActiveLinkColor = FpsUi.Gold, VisitedLinkColor = FpsUi.GoldDim,
                LinkBehavior = LinkBehavior.HoverUnderline, AutoSize = false,
            };
            Lien(_pied, "GamerPower.com", "https://www.gamerpower.com");
            Lien(_pied, "Steam", "https://store.steampowered.com");
            Lien(_pied, "Epic Games Store", "https://store.epicgames.com/fr/");
            Lien(_pied, "GOG", "https://www.gog.com/fr");
            Lien(_pied, "Nintendo eShop", "https://www.nintendo.com/fr-fr/");
            Lien(_pied, "itch.io", "https://itch.io");
            Lien(_pied, "CheapShark", "https://www.cheapshark.com");
            Lien(_pied, "Battle.net", "https://eu.shop.battle.net/fr-fr");
            _pied.LinkClicked += (s, e) => Ouvre(e.Link.LinkData as string);
            Controls.Add(_pied);
            // Une seule mention d'affiliation pour toute la page, à la place d'un badge sur chaque lien.
            _mention = new Label { Text = Monetisation.Notice, Font = FpsUi.Tiny, ForeColor = FpsUi.Dim2, BackColor = FpsUi.BgMain, AutoSize = false, UseMnemonic = false };
            Controls.Add(_mention);

            _majCombos = true;
            _cat.SelectedIndex = 0; _plat.SelectedIndex = 0; _tri.SelectedIndex = 0; _offres.SelectedIndex = 0;
            _majCombos = false; SyncChoices();
            _cat.SelectedIndexChanged += (s, e) => { SyncChoices(); if (!_majCombos) Recharge(); };
            _plat.SelectedIndexChanged += (s, e) => { SyncChoices(); if (!_majCombos) Recharge(); };
            _tri.SelectedIndexChanged += (s, e) => { SyncChoices(); if (!_majCombos) Recharge(); };
            _offres.SelectedIndexChanged += (s, e) => { SyncChoices(); if (!_majCombos) { MajOnglets(); Recharge(); } };

            foreach (Control control in new Control[] { _lPrix, _lCat, _lPlat, _lTri, _lOffres, _cat, _plat, _tri, _offres, _stores, _verifier, _installes, _souhaits }) _filters.Controls.Add(control);
            foreach (var pill in _pilules) _filters.Controls.Add(pill);
            _cat.AccessibleName = "Catégorie de jeu"; _plat.AccessibleName = "Plateforme"; _tri.AccessibleName = "Trier les offres"; _offres.AccessibleName = "Offres affichées";
            MajOnglets();
            _partner.VisibilityChanged += DoLayout;
            Resize += (s, e) => DoLayout();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { _attente.Dispose(); } catch { }
                try { _menuStores.Dispose(); } catch { }
                try { _menuSouhaits.Dispose(); } catch { }
            }
            base.Dispose(disposing);
        }

        public override void OnShown()
        {
            _partner.RefreshCampaign();
            DoLayout();
            if (SuiviVeille.AlertesNonVues > 0) { _charge = true; MontreSuivis(); return; }
            MajOnglets();
            if (_charge) return;
            _charge = true;
            Recharge();
        }

        /// <summary>Affiche le sous-onglet « Suivis » (alertes pas encore vues).</summary>
        public void MontreSuivis()
        {
            _charge = true;
            ChoisitOnglet(OngletSuivis);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            PaintPremiumTitle(e.Graphics, "BONS PLANS", "Repère les offres, compare les prix et garde tes jeux à l’œil.");
        }

        // ------------------------------------------------------------------ disposition

        private Label Etiquette(string t)
        {
            var l = new Label { Text = t, AutoSize = true, Font = FpsUi.Small, ForeColor = FpsUi.Dim, BackColor = Color.Transparent };
            Controls.Add(l);
            return l;
        }

        private ComboBox Liste(int largeur, IEnumerable<string> elements)
        {
            var c = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
                BackColor = FpsUi.Card, ForeColor = FpsUi.Ink, Font = FpsUi.Small, Width = largeur,
            };
            foreach (string e in elements) c.Items.Add(e);
            Controls.Add(c);
            return c;
        }

        private Button Choice(ComboBox model, string name)
        {
            var button = OnyxDesign.Button(""); button.AccessibleName = name;
            button.Click += (s, e) =>
            {
                var menu = new ContextMenuStrip { Font = FpsUi.Body, ShowImageMargin = false, ShowCheckMargin = true };
                for (int i = 0; i < model.Items.Count; i++)
                {
                    int index = i;
                    var item = new ToolStripMenuItem(model.Items[i].ToString()) { Checked = i == model.SelectedIndex };
                    item.Click += (sender, args) => model.SelectedIndex = index; menu.Items.Add(item);
                }
                // Libération différée : détruire le menu dans Closed le laisse inscrit comme menu actif
                // de WinForms, qui le referme au clic suivant → ObjectDisposedException (plantage).
                menu.Closed += (sender, args) =>
                {
                    if (IsHandleCreated && !IsDisposed) BeginInvoke((Action)menu.Dispose); else menu.Dispose();
                };
                menu.Show(button, new Point(0, button.Height + 2));
            };
            _filters.Controls.Add(button); return button;
        }
        private void SyncChoices()
        {
            _categoryChoice.Text = (_cat.SelectedItem ?? "Toutes").ToString() + "   ▾";
            _platformChoice.Text = (_plat.SelectedItem ?? "Toutes").ToString() + "   ▾";
            _sortChoice.Text = (_tri.SelectedItem ?? "Remise").ToString() + "   ▾";
            _offresChoice.Text = (_offres.SelectedItem ?? ModesOffres[0]).ToString() + "   ▾";
        }

        private static void Lien(LinkLabel l, string mot, string url)
        {
            int i = l.Text.IndexOf(mot, StringComparison.Ordinal);
            if (i >= 0) l.Links.Add(i, mot.Length, url);
        }

        private void DoLayout()
        {
            int w = ClientSize.Width, h = ClientSize.Height, width = w - 68;
            _support.Visible = Monetisation.Support(MonetisationRuntime.Settings) != null;
            _support.SetBounds(w - 486, 20, 150, 36);
            _searchBox.SetBounds(w - 324, 20, 290, 36); _search.SetBounds(12, 9, 266, 22);
            int tabWidth = (width - 8 * (NomsOnglets.Length - 1)) / NomsOnglets.Length;
            for (int i = 0; i < _onglets.Length; i++) _onglets[i].SetBounds(34 + i * (tabWidth + 8), 98, tabWidth, 40);
            _filters.SetBounds(34, 146, width, 88);
            int row = 50;
            _lPrix.SetBounds(18, 22, 68, 20);
            int x = 92;
            foreach (Button pill in _pilules) { pill.SetBounds(x, 14, pill.Width, 32); x = pill.Right + 6; }
            _stores.SetBounds(width - 196, 14, 178, 32);
            _lCat.SetBounds(18, row + 5, 76, 22); _categoryChoice.SetBounds(100, row, 210, 30);
            _lTri.SetBounds(338, row + 5, 28, 22); _sortChoice.SetBounds(378, row, 190, 30);
            _lOffres.SetBounds(592, row + 5, 52, 22); _offresChoice.SetBounds(648, row, 180, 30);
            _lPlat.SetBounds(18, row + 5, 80, 22); _platformChoice.SetBounds(100, row, 236, 30);
            _verifier.SetBounds(width - 376, row, 158, 30); _installes.SetBounds(width - 208, row, 190, 30);
            // Le choix de catégorie occupe 100 → 310 sur la même ligne : le bouton ne déborde jamais dessus.
            int xs = Math.Max(322, width - 606);
            _souhaits.SetBounds(xs, row, Math.Max(120, Math.Min(220, width - 386 - xs)), 30);
            _context.SetBounds(18, 14, width - 36, 30);
            _resultsLabel.SetBounds(34, 244, width, 22);
            _partner.SetBounds(34, 276, width, 76);
            int top = _partner.Active ? 364 : 276;
            // La mention d'affiliation occupe une ligne sous le pied de page, seulement si une règle est active.
            bool mention = Monetisation.HasAffiliation(MonetisationRuntime.Settings);
            int bas = mention ? 16 : 0;
            _viewport.SetBounds(34, top, w - 50, Math.Max(100, h - top - 80 - bas));
            LayoutCards();
            _pied.SetBounds(34, h - 25 - bas, width, 18);
            _mention.Visible = mention; _mention.SetBounds(34, h - 23, width, 18);
            int py = h - 65 - bas;
            _suiv.SetBounds(w - 144, py, 110, 32); _numPage.SetBounds(_suiv.Left - 104, py, 96, 32);
            _prec.SetBounds(_numPage.Left - 118, py, 110, 32);
            _info.SetBounds(34, py, Math.Max(60, _prec.Left - 50), 32);
            _etat.SetBounds(58, _viewport.Top + 52, width - 48, 62);
            _reessayer.Location = new Point(34 + (width - _reessayer.Width) / 2, _etat.Bottom + 12);
            Invalidate();
        }
        private void LayoutCards()
        {
            int width = _viewport.ContentWidth;
            const int gap = 12;
            int cols = Math.Max(2, (width + gap) / (270 + gap));
            int cardWidth = Math.Max(240, (width - (cols - 1) * gap) / cols);
            int cardHeight = Carte.HeightForWidth(cardWidth);
            _flow.SuspendLayout();
            int i = 0;
            foreach (Control card in _flow.Controls)
            {
                card.Size = new Size(cardWidth, cardHeight);
                card.Margin = new Padding(0, 0, ++i % cols == 0 ? 0 : gap, gap);
            }
            int height = Math.Max(120, ((i + cols - 1) / cols) * (cardHeight + gap));
            _flow.SetBounds(0, 0, width, height); _viewport.ContentHeight = height;
            _flow.ResumeLayout();
        }

        private static void Style(Button b, bool actif)
        {
            b.ForeColor = actif ? FpsUi.Gold : FpsUi.Ink;
            b.BackColor = actif ? FpsUi.BlendGold(FpsUi.Card, 0.16f) : FpsUi.CardHi;
            b.FlatAppearance.BorderColor = FpsUi.Gold;
            b.FlatAppearance.BorderSize = actif ? 1 : 0;
        }

        private void MajOnglets()
        {
            int nv = SuiviVeille.AlertesNonVues;
            for (int i = 0; i < _onglets.Length; i++)
            {
                _onglets[i].Text = i == OngletSuivis && nv > 0 ? "Suivis •" + nv.ToString(CultureInfo.InvariantCulture) : NomsOnglets[i];
                Style(_onglets[i], i == _onglet);
            }
            bool prixActif = _onglet != OngletGratuits && _onglet != OngletFavoris && _onglet != OngletPopulaires;
            for (int i = 0; i < _pilules.Length; i++)
            {
                _pilules[i].Enabled = prixActif;
                Style(_pilules[i], prixActif && i == _tranche);
            }
            _cat.Enabled = prixActif;
            _lPrix.Visible = prixActif;
            foreach (var pill in _pilules) pill.Visible = prixActif;
            _lCat.Visible = prixActif;   // listes natives jamais montrées (boutons de choix) : les afficher un instant créait leur fenêtre
            _context.Visible = !prixActif;
            _context.Text = _onglet == OngletFavoris ? "Tes offres enregistrées, réunies au même endroit. Les prix actuels sont disponibles sur la boutique."
                : _onglet == OngletPopulaires ? "Les jeux les plus joués sur Steam en ce moment, d'après le classement public de Steam (joueurs connectés, mis à jour toutes les heures)."
                : "Jeux et contenus offerts : consulte les plateformes et la date de fin avant de les récupérer.";
            bool plateforme = _onglet == OngletGratuits;   // une seule plateforme ailleurs (Steam) ; Favoris : texte seul
            _lPlat.Visible = plateforme;
            bool tri = _onglet == OngletPromos || _onglet == OngletNouveautes;
            _lTri.Visible = tri;
            _verifier.Visible = _installes.Visible = _souhaits.Visible = _onglet == OngletSuivis;
            _stores.Visible = _onglet == OngletPromos;
            _lOffres.Visible = _offresChoice.Visible = _onglet == OngletPromos;
            _categoryChoice.Visible = prixActif; _platformChoice.Visible = plateforme; _sortChoice.Visible = tri;
            _cat.Visible = _plat.Visible = _tri.Visible = _offres.Visible = false;
            SyncChoices();
            Style(_stores, _onglet == OngletPromos && Magasin.Boutiques.Count < OffresSources.Boutiques.Length);
            DoLayout();
        }

        private void MajStores()
        {
            int n = Magasin.Boutiques.Count;
            _stores.Text = "Boutiques " + n.ToString(CultureInfo.InvariantCulture) + "/" + OffresSources.Boutiques.Length.ToString(CultureInfo.InvariantCulture) + " ▾";
            _stores.Width = TextRenderer.MeasureText(_stores.Text, _stores.Font).Width + 24;
            Style(_stores, _onglet == OngletPromos && n < OffresSources.Boutiques.Length);
        }

        private void MenuStores()
        {
            HashSet<string> choix = Magasin.Boutiques;
            _menuStores.Items.Clear();
            var tout = new ToolStripMenuItem("Tout cocher");
            tout.Click += (s, e) => CocheTout(true);
            var rien = new ToolStripMenuItem("Tout décocher");
            rien.Click += (s, e) => CocheTout(false);
            _menuStores.Items.Add(tout);
            _menuStores.Items.Add(rien);
            AjouteMasques();
            _menuStores.Items.Add(new ToolStripSeparator());
            foreach (OffresSources.Boutique b in OffresSources.Boutiques)
            {
                string cle = b.Cle;
                bool us = b.CheapShark > 0 || cle == "itch";
                var it = new ToolStripMenuItem(b.Nom + (us ? "   · prix US" : "")) { Checked = choix.Contains(cle), Tag = cle };
                it.Click += (s, e) =>
                {
                    var m = (ToolStripMenuItem)s;
                    m.Checked = !m.Checked;
                    if (m.Checked) choix.Add(cle); else choix.Remove(cle);
                    _storesChanges = true;
                };
                _menuStores.Items.Add(it);
            }
            _menuStores.Show(_stores, new Point(0, _stores.Height + 2));
        }

        private void CocheTout(bool oui)
        {
            HashSet<string> choix = Magasin.Boutiques;
            foreach (OffresSources.Boutique b in OffresSources.Boutiques) { if (oui) choix.Add(b.Cle); else choix.Remove(b.Cle); }
            foreach (ToolStripItem it in _menuStores.Items)
            {
                var m = it as ToolStripMenuItem;
                if (m != null && m.Tag is string) m.Checked = oui;
            }
            _storesChanges = true;
        }

        // ------------------------------------------------------------------ actions

        private void ChoisitOnglet(int i)
        {
            _onglet = i;
            // « Gratuit » n'existe pas en promos seules (il mène à Gratuits) : une tranche « Gratuit » venue d'ailleurs est levée.
            if (i == OngletPromos && PromosSeules() && Tranches[_tranche].MaxCts == 0) _tranche = 0;
            if (i == OngletSuivis) SuiviVeille.MarqueVues();
            _majCombos = true;
            _tri.SelectedIndex = Array.FindIndex(Tris, t => t.Value == (i == OngletNouveautes ? "recent" : "remise"));
            _majCombos = false;
            MajOnglets();
            Recharge();
        }

        private void ChoisitTranche(int i)
        {
            if (i < 0 || i >= Tranches.Length) return;
            // Steam n'a pas de filtre « gratuit en promo » : en promos seules, « Gratuit » mène aux jeux gratuits.
            if (Tranches[i].MaxCts == 0 && _onglet == OngletPromos && PromosSeules()) { ChoisitOnglet(OngletGratuits); return; }
            _tranche = i;
            MajOnglets();
            Recharge();
        }

        /// <summary>Onglet Promos : vrai si seuls les jeux en promo sont cherchés (faux : « Tous les jeux », prix normal compris).</summary>
        private bool PromosSeules() { return _offres.SelectedIndex <= 0; }

        private string Texte() { return (_search.Text ?? "").Trim(); }

        private Offres.Filtre FiltreCourant()
        {
            int c = _cat.SelectedIndex, p = _plat.SelectedIndex;
            return new Offres.Filtre
            {
                Texte = Texte(),
                PlafondCts = Tranches[_tranche].MaxCts,
                PrixMinCts = Tranches[_tranche].MinCts,
                TauxUsd = _tauxUsd,
                Tag = c > 0 && c <= Offres.Categories.Length ? Offres.Categories[c - 1].Value : 0,
                Plateforme = p > 0 && p <= Offres.Plateformes.Length ? Offres.Plateformes[p - 1] : null,
            };
        }

        private string TriCourant()
        {
            int i = _tri.SelectedIndex;
            return i >= 0 && i < Tris.Length ? Tris[i].Value : "remise";
        }

        private bool OngletSteam()
        {
            return _onglet == OngletPromos || _onglet == OngletNouveautes || (_onglet == OngletSuivis && Texte().Length > 0);
        }

        private void Recharge()
        {
            _attente.Stop();
            _generation++;
            _occupe = false;
            _pageIdx = 0; _debutSteam = 0; _steamFini = false;
            _pagesStores.Clear(); _finis.Clear();
            _resultats = new List<Offre>();
            _info.Text = "";
            _masquesRetires = 0;
            switch (_onglet)
            {
                case OngletGratuits: ChargeGratuits(_generation); break;
                case OngletFavoris: _resultats = ListeFavoris(); Affiche(false); break;
                case OngletPopulaires: ChargePopulaires(_generation); break;
                case OngletSuivis:
                    if (Texte().Length > 0) ChargeSteam(_generation, false);
                    else ChargeSuivis(_generation);
                    break;
                default: ChargeSteam(_generation, false); break;
            }
        }

        // Exécute sur le thread d'interface, sans effet si la page est détruite ou si un autre
        // chargement a été lancé entre-temps (gen < 0 : toujours appliqué).
        private void Retour(int gen, Action a)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke((Action)(() =>
                {
                    if (IsDisposed || (gen >= 0 && gen != _generation)) return;
                    try { a(); } catch { }
                }));
            }
            catch { }
        }

        private void EtatChargement()
        {
            VideCartes();
            _etat.Text = "Chargement…"; _etat.Visible = true; _reessayer.Visible = false;
            _occupe = true;
            MajPagination();
        }

        /// <summary>
        /// Stores interrogés : en Promos, ceux cochés (Steam seul si une catégorie est choisie : les autres
        /// stores n'en ont pas) ; ailleurs (Nouveautés, recherche des Suivis), Steam seul.
        /// </summary>
        private List<string> SourcesActives(Offres.Filtre f)
        {
            var l = new List<string>();
            HashSet<string> choix = Magasin.Boutiques;
            bool multi = _onglet == OngletPromos && f.Tag == 0;
            if (!multi || choix.Contains("steam")) l.Add("steam");
            if (!multi) return l;
            foreach (string s in new[] { "gog", "nintendo", "itch" }) if (choix.Contains(s)) l.Add(s);
            if (OffresSources.IdsCheapShark(choix).Length > 0) l.Add("cheapshark");
            return l;
        }

        private static string NomSource(string s) { return s == "cheapshark" ? "CheapShark" : OffresSources.NomBoutique(s); }

        /// <summary>
        /// Un lot : jusqu'à 3 pages Steam (jusqu'à remplir la page) et une page de chaque autre store, en
        /// parallèle. Le lot est trié à part et ajouté après les pages déjà vues : elles ne bougent pas.
        /// </summary>
        private void ChargeSteam(int gen, bool suite)
        {
            Offres.Filtre f = FiltreCourant();
            bool onglPromos = _onglet == OngletPromos, nouveautes = _onglet == OngletNouveautes;
            bool promos = onglPromos && PromosSeules();   // « Tous les jeux » : prix normal compris
            string tri = onglPromos || nouveautes ? TriCourant() : null;   // Suivis : ordre de pertinence de Steam
            List<string> sources = SourcesActives(f).Where(x => !_finis.Contains(x)).ToList();
            if (sources.Count == 0) { _steamFini = true; if (!suite) Affiche(false); else MajPagination(); return; }
            string ids = OffresSources.IdsCheapShark(Magasin.Boutiques);
            var pages = new Dictionary<string, int>();
            foreach (string x in sources) { int pg; pages[x] = _pagesStores.TryGetValue(x, out pg) ? pg : 0; }
            int debut = _debutSteam, deja = _resultats.Count;
            int besoin = (suite ? _pageIdx + 2 : 1) * ParPage;
            if (suite) { _occupe = true; _numPage.Text = "Chargement…"; MajPagination(); }
            else EtatChargement();

            Task.Run(async () =>
            {
                var nouveaux = new List<Offre>();
                var finis = new List<string>();
                var echecs = new List<string>();
                int d = debut;
                // Taux BCE (cache 12 h) : les prix en dollars entrent dans les tranches et le tri en euros.
                decimal taux = f.TauxUsd > 0m ? f.TauxUsd : await OffresReseau.TauxUsd().ConfigureAwait(false);
                f.TauxUsd = taux;
                bool descSteam = !nouveautes && OffresSources.TriPrixDecroissantSteam(f.PrixMinCts, f.PlafondCts);
                List<string> autres = sources.Where(x => x != "steam").ToList();
                var taches = autres.Select(x => OffresReseau.PromosStore(x, f.Texte, pages[x], ids, promos, f.PrixMinCts, f.PlafondCts, taux)).ToList();
                if (sources.Contains("steam"))
                {
                    bool fini = false;
                    try
                    {
                        for (int lot = 0; lot < LotsMax; lot++)
                        {
                            List<Offre> brut = await OffresReseau.RechercheSteam(f, promos, nouveautes, d).ConfigureAwait(false);
                            d += PasSteam;
                            // Liste vide : fin des résultats, sauf si Steam n'a pas répondu (« Suivant » reste possible).
                            if (brut.Count == 0) { fini = !OffresReseau.DerniereErreur("steam"); break; }
                            // Filtre local : la tranche seulement (texte et catégorie sont appliqués par Steam).
                            foreach (Offre o in brut) if (Offres.DansPrix(o, f.PrixMinCts, f.PlafondCts, taux)) nouveaux.Add(o);
                            // Recherche du plus cher au moins cher : passée sous la borne basse, la suite ne rentre plus dans la tranche.
                            Offre der = brut[brut.Count - 1];
                            if (descSteam && der.PrixCts >= 0 && der.PrixCts <= f.PrixMinCts) { fini = true; break; }
                            if (deja + nouveaux.Count >= besoin) break;
                        }
                    }
                    catch { fini = true; }
                    if (fini) finis.Add("steam");
                    if (OffresReseau.DerniereErreur("steam")) echecs.Add("steam");
                }
                for (int i = 0; i < autres.Count; i++)
                {
                    OffresReseau.PageStore p;
                    try { p = await taches[i].ConfigureAwait(false); } catch { p = new OffresReseau.PageStore { Echec = true }; }
                    foreach (Offre o in p.Offres) if (Offres.DansPrix(o, f.PrixMinCts, f.PlafondCts, taux)) nouveaux.Add(o);
                    if (p.Fini) finis.Add(autres[i]);
                    if (p.Echec) echecs.Add(autres[i]);
                }
                Retour(gen, () =>
                {
                    _occupe = false;
                    _debutSteam = d;
                    if (taux > 0m) _tauxUsd = taux;
                    foreach (string x in autres) _pagesStores[x] = pages[x] + 1;
                    foreach (string x in finis) _finis.Add(x);
                    _steamFini = SourcesActives(f).All(x => _finis.Contains(x));
                    var vus = new HashSet<string>(_resultats.Select(o => o.Cle));
                    List<Offre> ajout = nouveaux.Where(o => vus.Add(o.Cle)).ToList();
                    if (tri != null) ajout = Offres.Trier(ajout, tri, taux);
                    _resultats.AddRange(ajout);
                    if (suite && _resultats.Count > (_pageIdx + 1) * ParPage) _pageIdx++;
                    bool rien = echecs.Count == sources.Count && _resultats.Count == 0;
                    Affiche(rien);
                    if (!rien && echecs.Count > 0) _info.Text = "Sans réponse : " + string.Join(", ", echecs.Select(NomSource)) + ". Réessayez avec « Suivant ».";
                    else if (!suite && onglPromos && f.Tag > 0) _info.Text = "Catégorie choisie : Steam seulement (les autres stores n'ont pas de catégories).";
                    else if (!suite && taux <= 0m && f.PlafondCts != -1 && SourcesActives(f).Any(x => x == "cheapshark" || x == "itch"))
                        _info.Text = "Taux du dollar indisponible : les prix en dollars ne sont pas classés par tranche.";
                });
            });
        }

        private void ChargeGratuits(int gen)
        {
            if (_gratuitsBruts != null && DateTime.UtcNow - _gratuitsLe < TimeSpan.FromMinutes(60)) { AfficheGratuits(false); return; }
            EtatChargement();
            Task.Run(async () =>
            {
                List<Offre> l;
                try { l = await OffresReseau.Gratuits().ConfigureAwait(false); } catch { l = new List<Offre>(); }
                bool erreur = OffresReseau.DerniereErreur("epic") && OffresReseau.DerniereErreur("gamerpower");
                Retour(gen, () =>
                {
                    _occupe = false;
                    _gratuitsBruts = erreur && l.Count == 0 ? null : l;
                    _gratuitsLe = DateTime.UtcNow;
                    AfficheGratuits(erreur);
                });
            });
        }

        /// <summary>Classement Steam (cache 60 min), puis fiches des jeux pour le nom, le prix et l'image.</summary>
        private void ChargePopulaires(int gen)
        {
            if (_populairesBruts != null && DateTime.UtcNow - _populairesLe < TimeSpan.FromMinutes(60)) { AffichePopulaires(false); return; }
            EtatChargement();
            Task.Run(async () =>
            {
                var l = new List<Offre>();
                bool erreur = true;
                try
                {
                    List<Populaires.Rang> rangs = Populaires.Lit(await OffresReseau.Texte(Populaires.Url, 60).ConfigureAwait(false), Populaires.Max);
                    if (rangs.Count > 0)
                    {
                        List<Offre> fiches = await OffresReseau.FichesSteam(rangs.Select(r => r.AppId), 60).ConfigureAwait(false);
                        foreach (Populaires.Rang r in rangs)
                        {
                            Offre f = fiches.Find(o => o != null && o.SteamAppId == r.AppId);
                            if (f == null) continue;   // logiciel, outil ou fiche absente : pas montré
                            f.Joueurs = r.Joueurs;
                            l.Add(f);
                        }
                        erreur = l.Count == 0;
                    }
                }
                catch { }
                Retour(gen, () =>
                {
                    _occupe = false;
                    _populairesBruts = erreur ? null : l;
                    _populairesLe = DateTime.UtcNow;
                    AffichePopulaires(erreur);
                });
            });
        }

        private void AffichePopulaires(bool erreur)
        {
            string t = Offres.NomNormalise(Texte());
            _resultats = (_populairesBruts ?? new List<Offre>()).Where(o => t.Length == 0 || Offres.NomNormalise(o.Nom).Contains(t)).ToList();
            Affiche(erreur && _resultats.Count == 0);
        }

        private void AfficheGratuits(bool erreur)
        {
            Offres.Filtre f = FiltreCourant();
            f.PlafondCts = -1; f.Tag = 0;   // plafonds et catégorie désactivés ici
            _resultats = Offres.Filtrer(_gratuitsBruts ?? new List<Offre>(), f)
                .OrderBy(o => o.Fin.HasValue ? 0 : 1).ThenBy(o => o.Fin ?? DateTime.MaxValue).ToList();
            Affiche(erreur && _resultats.Count == 0);
        }

        private void ChargeSuivis(int gen)
        {
            var suivis = Magasin.Suivis.Where(j => j != null && j.AppId > 0)
                .Select(j => new JeuSuivi { AppId = j.AppId, Nom = j.Nom }).ToList();
            if (suivis.Count == 0) { Affiche(false); return; }
            List<Offre> fiches = SuiviVeille.DernieresFiches;
            if (suivis.All(j => fiches.Exists(o => o.SteamAppId == j.AppId))) { ConstruitSuivis(suivis, fiches, false); return; }
            EtatChargement();
            Task.Run(async () =>
            {
                List<Offre> l;
                try { l = await OffresReseau.FichesSteam(suivis.Select(j => j.AppId), 30).ConfigureAwait(false); } catch { l = new List<Offre>(); }
                bool erreur = OffresReseau.DerniereErreur("steam");
                Retour(gen, () => { _occupe = false; ConstruitSuivis(suivis, l, erreur); });
            });
        }

        private void ConstruitSuivis(List<JeuSuivi> suivis, List<Offre> fiches, bool erreur)
        {
            var liste = new List<Offre>();
            foreach (JeuSuivi j in suivis)
            {
                Offre f = fiches.Find(o => o != null && o.SteamAppId == j.AppId);
                liste.Add(f ?? new Offre
                {
                    Source = "steam", Id = j.AppId.ToString(CultureInfo.InvariantCulture), Nom = j.Nom,
                    SteamAppId = j.AppId, Plateformes = new[] { "Steam" },
                    Url = "https://store.steampowered.com/app/" + j.AppId.ToString(CultureInfo.InvariantCulture) + "/",
                });
            }
            _resultats = liste.OrderBy(o => o.Remise > 0 ? 0 : 1).ToList();   // promos en tête, ordre du suivi sinon
            if (erreur) _info.Text = "Prix : " + TexteErreur;
            Affiche(false);
        }

        private List<Offre> ListeFavoris()
        {
            var liste = new List<Offre>();
            foreach (Favori f in Magasin.FavorisJeux)
            {
                if (f == null || string.IsNullOrEmpty(f.Cle)) continue;
                int i = f.Cle.IndexOf(':');
                string src = i > 0 ? f.Cle.Substring(0, i) : (f.Source ?? "");
                string id = i > 0 ? f.Cle.Substring(i + 1) : f.Cle;
                int appId = 0;
                if (src != "steam" || !int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out appId)) appId = 0;
                liste.Add(new Offre
                {
                    Source = src, Id = id, Nom = f.Titre, Url = f.Url, SteamAppId = appId,
                    Plateformes = src == "gamerpower" ? new string[0] : new[] { OffresSources.NomBoutique(src) },
                });
            }
            liste.Reverse();   // le plus récent en tête
            return Offres.Filtrer(liste, new Offres.Filtre { Texte = Texte() });
        }

        private void Suivant()
        {
            if ((_pageIdx + 1) * ParPage < _resultats.Count) { _pageIdx++; Affiche(false); return; }
            if (OngletSteam() && !_steamFini && !_occupe) ChargeSteam(_generation, true);
        }

        private async void Verifie()
        {
            _verifier.Enabled = false;
            _info.Text = "Vérification…";
            try { await SuiviVeille.Controle(); } catch { }
            if (IsDisposed) return;
            _verifier.Enabled = true;
            _info.Text = OffresReseau.DerniereErreur("steam") ? TexteErreur : "Vérifié.";
            MajOnglets();
            if (_onglet == OngletSuivis && Texte().Length == 0) { string t = _info.Text; Recharge(); _info.Text = t; }
        }

        private void SuitInstalles()
        {
            _installes.Enabled = false;
            _info.Text = "Recherche des jeux installés…";
            Task.Run(() =>
            {
                List<GameLibrary.InstalledGame> jeux;
                try { jeux = GameLibrary.ScanAll() ?? new List<GameLibrary.InstalledGame>(); } catch { jeux = new List<GameLibrary.InstalledGame>(); }
                Retour(-1, () =>
                {
                    int n = 0; bool plein = false;
                    foreach (GameLibrary.InstalledGame g in jeux)
                    {
                        if (g == null || g.SteamAppId <= 0 || string.IsNullOrWhiteSpace(g.Name)) continue;
                        if (Magasin.Suivis.Exists(j => j.AppId == g.SteamAppId)) continue;
                        if (Magasin.Suivis.Count >= Suivi.Max) { plein = true; break; }
                        // Remise inconnue ici : le premier contrôle l'amorce sans notifier les promos déjà en cours.
                        if (Suivi.AjouteInstalle(Magasin.Suivis, g.SteamAppId, g.Name)) n++;
                    }
                    if (n > 0) Magasin.SauveSuivis();
                    _installes.Enabled = true;
                    string t = plein ? TexteMax
                        : n == 0 ? "Aucun nouveau jeu Steam installé à suivre."
                        : n.ToString(CultureInfo.InvariantCulture) + (n > 1 ? " jeux ajoutés aux suivis." : " jeu ajouté aux suivis.");
                    if (_onglet == OngletSuivis && Texte().Length == 0) Recharge();
                    _info.Text = t;
                });
            });
        }

        private const string TexteSouhaitsVide = "Liste vide ou profil privé : Steam > Profil > Modifier le profil > Confidentialité > Détails du jeu : Public.";

        /// <summary>Liste de souhaits du compte Steam de ce PC → suivis. Seul le SteamID part, et vers Steam.</summary>
        private void ImporteSouhaits()
        {
            _souhaits.Enabled = false;
            _info.Text = "Lecture de la liste de souhaits Steam…";
            Task.Run(async () =>
            {
                string message = null; List<int> ids = new List<int>(); var noms = new Dictionary<int, string>();
                try
                {
                    string steam = SteamGames.SteamPath();
                    string vdf = steam == null ? null : Path.Combine(steam, "config", "loginusers.vdf");
                    string url = ListeSouhaits.Url(vdf != null && File.Exists(vdf) ? ListeSouhaits.SteamId64(File.ReadAllText(vdf)) : null);
                    if (url == null) message = "Aucun compte Steam trouvé sur ce PC (connecte-toi une fois à Steam).";
                    else
                    {
                        string json = await OffresReseau.Texte(url, 0).ConfigureAwait(false);
                        if (json == null) message = TexteErreur;
                        else
                        {
                            ids = ListeSouhaits.AppIds(json);
                            if (ids.Count == 0) message = TexteSouhaitsVide;
                            else
                            {
                                // Noms par les fiches Steam (cache 30 min) ; sans fiche, « Jeu <appid> » jusqu'au prochain contrôle.
                                List<Offre> fiches = await OffresReseau.FichesSteam(ids.Take(Suivi.Max * 2), 30).ConfigureAwait(false);
                                foreach (Offre f in fiches) if (f != null && f.SteamAppId > 0 && !string.IsNullOrWhiteSpace(f.Nom)) noms[f.SteamAppId] = f.Nom;
                            }
                        }
                    }
                }
                catch (Exception ex) { JournalTechnique.Echec("PageBonsPlans.ImporteSouhaits", ex); message = TexteErreur; }
                Retour(-1, () =>
                {
                    _souhaits.Enabled = true;
                    if (message == null)
                    {
                        int ajoutes, deja, laisses;
                        ListeSouhaits.Importe(Magasin.Suivis, ids, noms, out ajoutes, out deja, out laisses);
                        if (ajoutes > 0) Magasin.SauveSuivis();
                        message = ListeSouhaits.Compte(ajoutes, deja, laisses);
                        if (_onglet == OngletSuivis && Texte().Length == 0) Recharge();
                    }
                    _info.Text = message;
                });
            });
        }

        private const string FichierPseudoGog = "bt-gog-pseudo.txt";

        /// <summary>
        /// Liste de souhaits GOG publique → suivis Steam (même jeu cherché sur Steam). Seul le pseudo
        /// part, vers gog.com ; les titres partent vers la recherche Steam. Le pseudo est gardé sur ce PC.
        /// </summary>
        private void ImporteSouhaitsGog()
        {
            string avant = "";
            try { string f = AppPaths.File(FichierPseudoGog); if (File.Exists(f)) avant = File.ReadAllText(f).Trim(); } catch { }
            string saisie = DemandePseudoGog(avant);
            if (saisie == null) return;
            string pseudo = ListeSouhaitsGog.Pseudo(saisie);
            if (pseudo == null) { _info.Text = "Pseudo GOG non reconnu : tape ton pseudo ou colle l'adresse de ton profil (gog.com/u/…)."; return; }
            try { File.WriteAllText(AppPaths.File(FichierPseudoGog), pseudo, new System.Text.UTF8Encoding(false)); } catch { }
            _souhaits.Enabled = false;
            _info.Text = "Lecture de la liste de souhaits GOG…";
            Task.Run(async () =>
            {
                string message = null; var ids = new List<int>(); var noms = new Dictionary<int, string>();
                var introuvables = new List<string>(); int lus = 0, injoignables = 0; bool tronque = false;
                try
                {
                    ListeSouhaitsGog.Page p1 = ListeSouhaitsGog.Lire(await OffresReseau.Texte(ListeSouhaitsGog.Url(pseudo, 1), 0).ConfigureAwait(false));
                    if (p1 == null) message = "Liste GOG introuvable : vérifie le pseudo et que ta liste de souhaits est publique (ou GOG est injoignable pour l'instant).";
                    else
                    {
                        var titres = new List<string>(p1.Titres);
                        for (int n = 2; n <= Math.Min(p1.Pages, ListeSouhaitsGog.PagesMax); n++)
                        {
                            ListeSouhaitsGog.Page pn = ListeSouhaitsGog.Lire(await OffresReseau.Texte(ListeSouhaitsGog.Url(pseudo, n), 0).ConfigureAwait(false));
                            if (pn == null || pn.Numero != n) { tronque = true; break; }   // page non servie : on s'arrête là
                            foreach (string t in pn.Titres) if (!titres.Contains(t)) titres.Add(t);
                        }
                        if (p1.Pages > ListeSouhaitsGog.PagesMax) tronque = true;
                        if (titres.Count > Suivi.Max * 2) { titres.RemoveRange(Suivi.Max * 2, titres.Count - Suivi.Max * 2); tronque = true; }
                        if (titres.Count == 0) message = "Liste de souhaits GOG vide (ou sans jeu).";
                        foreach (string t in titres)
                        {
                            int id = SteamAppIndex.ResolveMaintenant(t);
                            string court = id == 0 ? ListeSouhaitsGog.Simplifie(t) : null;   // « … : Édition Ultime » → nom du jeu
                            if (court != null) id = SteamAppIndex.ResolveMaintenant(court);
                            lus++;
                            if (lus % 10 == 0) { int fait = lus, tout = titres.Count; Retour(-1, () => _info.Text = "Recherche des jeux GOG sur Steam… " + fait + "/" + tout); }
                            if (id < 0) { injoignables++; continue; }
                            if (id == 0) { introuvables.Add(t); continue; }
                            if (!ids.Contains(id)) { ids.Add(id); noms[id] = t; }
                        }
                    }
                }
                catch (Exception ex) { JournalTechnique.Echec("PageBonsPlans.ImporteSouhaitsGog", ex); message = TexteErreur; }
                Retour(-1, () =>
                {
                    _souhaits.Enabled = true;
                    if (message == null)
                    {
                        int ajoutes, deja, laisses;
                        ListeSouhaits.Importe(Magasin.Suivis, ids, noms, out ajoutes, out deja, out laisses);
                        if (ajoutes > 0) Magasin.SauveSuivis();
                        message = ListeSouhaitsGog.Compte(lus - injoignables, introuvables, ListeSouhaits.Compte(ajoutes, deja, laisses), tronque)
                            + (injoignables > 0 ? " " + injoignables + " non vérifié" + (injoignables > 1 ? "s" : "") + " (Steam injoignable) : relance plus tard." : "")
                            + " Alertes au prix Steam.";
                        if (_onglet == OngletSuivis && Texte().Length == 0) Recharge();
                    }
                    _info.Text = message;
                });
            });
        }

        private string DemandePseudoGog(string defaut)
        {
            using (var f = new Form
            {
                Text = "ONYX — Liste de souhaits GOG", ClientSize = new Size(460, 168), FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false,
                BackColor = FpsUi.BgMain, ForeColor = FpsUi.Ink, Font = FpsUi.Body,
            })
            {
                f.Controls.Add(new Label
                {
                    Text = "Ton pseudo GOG, ou l'adresse de ton profil (gog.com/u/…). La liste doit être publique ; seul ce pseudo part, vers gog.com.",
                    Location = new Point(16, 14), Size = new Size(428, 42), ForeColor = FpsUi.Dim,
                });
                var tb = new TextBox { Text = defaut ?? "", Location = new Point(16, 64), Width = 428 };
                var ok = OnyxDesign.AccentButton("Importer");
                ok.SetBounds(460 - 16 - 110 - 10 - 120, 116, 120, 34); ok.DialogResult = DialogResult.OK;
                var ca = OnyxDesign.Button("Annuler");
                ca.SetBounds(460 - 16 - 110, 116, 110, 34); ca.DialogResult = DialogResult.Cancel;
                f.Controls.Add(tb); f.Controls.Add(ok); f.Controls.Add(ca);
                f.AcceptButton = ok; f.CancelButton = ca;
                try { Theme.Apply(f); } catch { }
                return f.ShowDialog(FindForm()) == DialogResult.OK ? tb.Text : null;
            }
        }

        internal bool EstFavori(Offre o) { return o != null && Favoris.Contient(Magasin.FavorisJeux, o.Cle); }
        internal bool EstSuivi(Offre o) { return o != null && o.SteamAppId > 0 && Magasin.Suivis.Exists(j => j.AppId == o.SteamAppId); }

        internal void BasculeFavori(Offre o)
        {
            if (o == null) return;
            Favoris.Bascule(Magasin.FavorisJeux, Favoris.DepuisOffre(o, DateTime.Now));
            Magasin.SauveFavorisJeux();
            if (_onglet == OngletFavoris) Recharge();
        }

        /// <summary>Entrée « Jeux masqués » du menu Boutiques : tout réafficher, ou un jeu à la fois.</summary>
        private void AjouteMasques()
        {
            List<OffresMasquees.Masque> l = Magasin.Masques;
            if (l.Count == 0) return;
            var racine = new ToolStripMenuItem("Jeux masqués (" + l.Count + ")");
            var tous = new ToolStripMenuItem("Tout réafficher");
            tous.Click += (s, e) => { l.Clear(); Magasin.SauveMasques(); if (OngletMasquable) Recharge(); };
            racine.DropDownItems.Add(tous);
            racine.DropDownItems.Add(new ToolStripSeparator());
            foreach (OffresMasquees.Masque m in l.OrderByDescending(x => x.Quand).Take(40))
            {
                string cle = m.Cle;
                var it = new ToolStripMenuItem("Réafficher " + m.Nom + (m.Raison == OffresMasquees.Possede ? "   · déjà possédé" : "   · pas pour moi"));
                it.Click += (s, e) => { OffresMasquees.Retire(l, cle); Magasin.SauveMasques(); if (OngletMasquable) Recharge(); };
                racine.DropDownItems.Add(it);
            }
            _menuStores.Items.Add(racine);
        }

        /// <summary>Menu « ⋯ » d'une carte : masquer le jeu des bons plans, sur toutes les boutiques.</summary>
        internal void MenuMasquer(Control ancre, Offre o)
        {
            if (o == null || !OngletMasquable) return;
            _menuCarte.Items.Clear();
            var deja = new ToolStripMenuItem("J’ai déjà ce jeu");
            deja.Click += (s, e) => Masque(o, OffresMasquees.Possede);
            var pas = new ToolStripMenuItem("Pas pour moi");
            pas.Click += (s, e) => Masque(o, OffresMasquees.PasPourMoi);
            _menuCarte.Items.Add(deja); _menuCarte.Items.Add(pas);
            _menuCarte.Items.Add(new ToolStripSeparator());
            _menuCarte.Items.Add(new ToolStripMenuItem("Réaffichable depuis « Boutiques »") { Enabled = false });
            _menuCarte.Show(ancre, new Point(0, ancre.Height + 2));
        }

        private void Masque(Offre o, string raison)
        {
            if (!OffresMasquees.Ajoute(Magasin.Masques, o.Nom, raison, DateTime.Now)) return;
            Magasin.SauveMasques();
            // Page reconstruite sans réseau : les offres suivantes remontent à la place des cartes
            // retirées (sinon elles glisseraient hors de toute page) ; la position de défilement reste.
            int position = _viewport.Offset;
            _masquesRetires += OffresMasquees.Filtre(_resultats, Magasin.Masques);
            while (_pageIdx > 0 && _pageIdx * ParPage >= _resultats.Count) _pageIdx--;
            Affiche(false);
            _viewport.ScrollTo(position);
        }

        private void InfoMasques()
        {
            if (_masquesRetires <= 0 || _info.Text.Length > 0 && !_info.Text.Contains("masqué")) return;
            _info.Text = _masquesRetires + (_masquesRetires > 1 ? " offres masquées" : " offre masquée")
                       + " (j’ai déjà / pas pour moi). « Boutiques » pour les réafficher.";
        }

        internal void BasculeSuivi(Offre o)
        {
            if (o == null || o.SteamAppId <= 0) return;
            if (EstSuivi(o)) Suivi.Retire(Magasin.Suivis, o.SteamAppId);
            else if (!Suivi.Ajoute(Magasin.Suivis, o.Source == "steam" ? o : new Offre { Source = "steam", Nom = o.Nom, SteamAppId = o.SteamAppId }))
            {
                if (Magasin.Suivis.Count >= Suivi.Max) _info.Text = TexteMax;
                return;
            }
            Magasin.SauveSuivis();
            if (_onglet == OngletSuivis && Texte().Length == 0) Recharge();
        }

        /// <summary>Ouvre un lien dans le navigateur, seulement s'il est absolu en http ou https.</summary>
        internal static void Ouvre(string url)
        {
            if (!Offres.LienSur(url)) return;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url.Trim()) { UseShellExecute = true }); } catch { }
        }

        // ------------------------------------------------------------------ rendu

        private void VideCartes()
        {
            _flow.SuspendLayout();
            foreach (Control c in _flow.Controls.Cast<Control>().ToList()) c.Dispose();
            _flow.Controls.Clear();
            _flow.ResumeLayout(); LayoutCards();
        }

        private void Affiche(bool erreur)
        {
            _flow.SuspendLayout();
            foreach (Control c in _flow.Controls.Cast<Control>().ToList()) c.Dispose();
            _flow.Controls.Clear();
            if (OngletMasquable) _masquesRetires += OffresMasquees.Filtre(_resultats, Magasin.Masques);
            InfoMasques();
            List<Offre> page = _resultats.Skip(_pageIdx * ParPage).Take(ParPage).ToList();
            if (page.Count == 0)
            {
                bool aide = _onglet == OngletSuivis && Texte().Length == 0 && !erreur;
                bool aucunStore = _onglet == OngletPromos && Magasin.Boutiques.Count == 0;
                _etat.Text = erreur ? TexteErreur
                    : aucunStore ? "Aucun store coché : ouvre « Boutiques » pour en choisir."
                    : aide ? "Cherche un jeu puis choisis « Suivre » pour être prévenu de ses promotions."
                    : "Aucun jeu ne correspond à ces filtres.";
                if (!erreur && _onglet == OngletFavoris) _etat.Text = "Aucun favori pour le moment. Ajoute une offre avec l’étoile pour la retrouver ici.";
                _etat.Visible = true;
                _reessayer.Visible = erreur;
            }
            else
            {
                _etat.Visible = false; _reessayer.Visible = false;
                foreach (Offre o in page) _flow.Controls.Add(new Carte(this, o));   // vignettes : cette page seulement
            }
            _flow.ResumeLayout();
            _viewport.ScrollTo(0); LayoutCards();
            MajPagination();
        }

        private void MajPagination()
        {
            bool plusLocal = (_pageIdx + 1) * ParPage < _resultats.Count;
            _prec.Enabled = !_occupe && _pageIdx > 0;
            _suiv.Enabled = !_occupe && (plusLocal || (OngletSteam() && !_steamFini && _resultats.Count > 0));
            if (!_occupe) _numPage.Text = "Page " + (_pageIdx + 1).ToString(CultureInfo.InvariantCulture);
            string titre = _onglet == OngletPromos && !PromosSeules() ? "TOUS LES JEUX" : NomsOnglets[_onglet].ToUpperInvariant();
            if (_tranche > 0 && _onglet != OngletGratuits && _onglet != OngletFavoris) titre += " · " + Tranches[_tranche].Libelle.ToUpperInvariant();
            _resultsLabel.Text = _occupe ? "RECHERCHE DES OFFRES…" : titre + "    /    " + _resultats.Count + (_resultats.Count == 1 ? " jeu chargé" : " jeux chargés");
        }

#if BTTEST
        internal void ValidatePartnerUi()
        {
            _partner.ValidateUi();
            if (_partner.Active && _partner.Bottom >= _viewport.Top) throw new InvalidOperationException("Partenaire superposé aux offres.");
            _partner.PreviewDismiss();
            if (_partner.Active || _viewport.Top != 276) throw new InvalidOperationException("Le partenaire masqué réserve encore de la place.");
        }
        internal bool BusyForTest { get { return _occupe; } }
        internal int PricedForTest { get { return _resultats.Count(o => o.PrixCts > 0); } }
        internal int InstantGamingLinksForTest { get { return _flow.Controls.OfType<Carte>().Count(c => c.HasInstantGaming); } }
        internal void PreviewInstantGaming()
        {
            var card = _flow.Controls.OfType<Carte>().FirstOrDefault(c => c.HasInstantGaming);
            if (card != null) _viewport.ScrollTo(card.Top);        }
        internal int OffersForTest { get { return _resultats.Count; } }
        internal void PreviewLocalTab(int tab)
        {
            _attente.Stop(); _generation++; _occupe = false; _onglet = tab; _pageIdx = 0;
            _resultats.Clear(); MajOnglets(); Affiche(false);
        }
        internal void ValidateDealsUi()
        {
            if (!ClientRectangle.Contains(_filters.Bounds) || _viewport.Bottom > _prec.Top)
                throw new InvalidOperationException("Filtres ou pagination hors de la page.");
            if (TextRenderer.MeasureText(_pied.Text, _pied.Font).Width > _pied.Width || _pied.Links.Count != 8)
                throw new InvalidOperationException("Pied de page coupé ou lien manquant.");
            // Tranches de prix, Boutiques, choix Offres… : rien ne déborde ni ne se chevauche dans le bandeau.
            var filtres = _filters.Controls.Cast<Control>().Where(c => c.Visible && c.Width > 0).ToList();
            foreach (Control c in filtres)
            {
                if (!_filters.ClientRectangle.Contains(c.Bounds)) throw new InvalidOperationException("Filtre hors du bandeau : " + c.Text);
                foreach (Control other in filtres)
                    if (c != other && c.Bounds.IntersectsWith(other.Bounds)) throw new InvalidOperationException("Filtres superposés : " + c.Text + " / " + other.Text);
            }
            foreach (Carte card in _flow.Controls)
            {
                foreach (Control control in card.Controls)
                {
                    if (!control.Visible) continue;
                    if (!card.ClientRectangle.Contains(control.Bounds)) throw new InvalidOperationException("Action hors de sa carte.");
                    foreach (Control other in card.Controls)
                        if (control != other && other.Visible && control.Bounds.IntersectsWith(other.Bounds))
                            throw new InvalidOperationException("Les actions d’une offre se chevauchent.");
                }
            }
            _viewport.ValidateScrolling();
            if (Monetisation.HasAffiliation(MonetisationRuntime.Settings)
                && (_mention.Width <= 0 || !ClientRectangle.Contains(_mention.Bounds) || _mention.Top < _pied.Bottom - 2 || _prec.Bottom > _pied.Top
                    || TextRenderer.MeasureText(_mention.Text, _mention.Font).Width > _mention.Width))
                throw new InvalidOperationException("Mention d’affiliation absente, coupée ou superposée.");
        }
        internal void PreviewEmpty(bool error) { _generation++; _occupe = false; _resultats.Clear(); Affiche(error); }
        internal void PreviewFree() { ChoisitOnglet(OngletGratuits); }
        internal void PreviewPopular() { ChoisitOnglet(OngletPopulaires); }
        internal void PreviewPricedPage()
        {
            int index = _resultats.FindIndex(o => o.PrixCts > 0 && o.SteamAppId > 0);
            if (index < 0) index = _resultats.FindIndex(o => o.PrixCts > 0);
            if (index >= 0)
            {
                _pageIdx = index / ParPage; Affiche(false);
                _viewport.ScrollTo(_flow.Controls[index % ParPage].Top);
            }
        }
#endif

        /// <summary>Couleur de la pastille d'un store sur les cartes.</summary>
        internal static Color CouleurStore(string source)
        {
            switch (source)
            {
                case "steam": return Color.FromArgb(102, 192, 244);
                case "gog": return Color.FromArgb(190, 130, 240);
                case "nintendo": return Color.FromArgb(240, 84, 74);
                case "epic": return Color.FromArgb(215, 215, 210);
                case "humble": return Color.FromArgb(236, 106, 86);
                case "fanatical": return Color.FromArgb(255, 152, 56);
                case "gmg": return Color.FromArgb(112, 210, 112);
                case "ubisoft": return Color.FromArgb(86, 152, 255);
                case "itch": return Color.FromArgb(250, 96, 96);
                case "indiegala": return Color.FromArgb(236, 90, 120);
                case "gamerpower": return FpsUi.Ok;
                default: return FpsUi.Gold;
            }
        }

        /// <summary>Couleur d'un store d'après son nom affiché (« Steam (France) » compris).</summary>
        internal static Color CouleurNom(string nom)
        {
            string n = (nom ?? "").Replace(" (France)", "");
            if (n == "Steam") return CouleurStore("steam");
            if (n == "GOG") return CouleurStore("gog");
            foreach (OffresSources.Boutique b in OffresSources.Boutiques) if (b.Nom == n) return CouleurStore(b.Cle);
            return FpsUi.Gold;
        }

        /// <summary>Une carte de jeu : jaquette, nom, remise, prix (US s'il est en dollars), store, fin d'offre, ⚖ (comparer), ☆ et 🔔.</summary>
        private sealed class Carte : BufferedPanel
        {
            private static readonly Font Barre = new Font(FpsUi.Small, FontStyle.Strikeout);
            private readonly PageBonsPlans _page;
            private readonly Offre _o;
            private readonly Button _open, _compare, _favorite, _follow, _more;
            private readonly LinkLabel _instantGaming;
            private ToolTip _tips { get { return _page.Infobulles; } }
            private bool _hover;
            // Le cadre suit le format horizontal des jaquettes au lieu d'une hauteur fixe.
            private static int ImageHeight(int width) { return (int)Math.Round((width - 16) * 9.0 / 16); }
            internal static int HeightForWidth(int width) { return ImageHeight(width) + 141 + (LigneInstantGaming ? 22 : 0); }
            // Ligne « Instant Gaming » réservée seulement si l'affiliation est active : sinon la carte garde sa taille.
            internal bool HasInstantGaming { get { return _instantGaming != null; } }
            private static bool LigneInstantGaming { get { return MonetisationRuntime.IsAffiliate(OffresSources.UrlRevendeur(InstantGaming, "jeu")); } }
            private static readonly OffresSources.Revendeur InstantGaming = OffresSources.Revendeurs.First(r => r.Nom == "Instant Gaming");
            private Rectangle Artwork { get { return new Rectangle(8, 8, Width - 16, ImageHeight(Width)); } }
            private int TitleY { get { return Artwork.Bottom + 10; } }
            private int PriceY { get { return TitleY + 29; } }
            public Carte(PageBonsPlans page, Offre offer)
            {
                _page = page; _o = offer; BackColor = FpsUi.BgMain; Size = new Size(280, HeightForWidth(280));
                AccessibleName = offer.Nom; _tips.SetToolTip(this, offer.Nom);
                _open = OnyxDesign.PrimaryButton("Voir l’offre  ↗");
                _open.Enabled = Offres.LienSur(offer.Url); _open.AccessibleName = "Voir l’offre : " + offer.Nom;
                _open.Click += (s, e) => MonetisationRuntime.OpenOffer(_o.Url);
                if (MonetisationRuntime.IsAffiliate(_o.Url))
                {
                    _open.AccessibleDescription = "Lien affilié : ONYX peut recevoir une commission si tu achètes.";
                    _tips.SetToolTip(_open, _open.AccessibleDescription);
                }
                _compare = OnyxDesign.Button("Comparer"); _compare.Click += (s, e) => ComparateurForm.Montre(_o, FindForm());
                _favorite = OnyxDesign.Button(""); _favorite.Font = FpsUi.H2;
                _favorite.Click += (s, e) => { _page.BasculeFavori(_o); if (!IsDisposed) RefreshActions(); };
                _follow = OnyxDesign.Button(""); _follow.Visible = _o.SteamAppId > 0;
                _follow.Click += (s, e) => { _page.BasculeSuivi(_o); if (!IsDisposed) RefreshActions(); };
                Controls.Add(_open); Controls.Add(_compare); Controls.Add(_favorite); Controls.Add(_follow);
                _more = OnyxDesign.Button("⋯"); _more.Visible = page.OngletMasquable;
                _more.AccessibleName = "Masquer ce jeu : " + offer.Nom;
                _tips.SetToolTip(_more, "J’ai déjà ce jeu / Pas pour moi : le masquer des bons plans");
                _more.Click += (s, e) => _page.MenuMasquer(_more, _o);
                Controls.Add(_more);
                // Lien discret vers la même recherche chez Instant Gaming (affilié). Pas pour un jeu gratuit.
                string ig = _o.PrixCts == 0 ? null : OffresSources.UrlRevendeur(InstantGaming, _o.Nom);
                if (ig != null && MonetisationRuntime.IsAffiliate(ig))
                {
                    const string lien = "Instant Gaming ↗";
                    _instantGaming = new LinkLabel
                    {
                        Text = lien, LinkArea = new LinkArea(0, lien.Length), Font = FpsUi.Tiny, AutoSize = false,
                        BackColor = FpsUi.Card, ForeColor = FpsUi.Dim2, LinkColor = FpsUi.Dim, ActiveLinkColor = FpsUi.Gold,
                        LinkBehavior = LinkBehavior.HoverUnderline, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false,
                        AccessibleName = "Chercher " + _o.Nom + " sur Instant Gaming",
                        AccessibleDescription = "Lien affilié : ONYX peut recevoir une commission si tu achètes. Le prix ne change pas."
                    };
                    _instantGaming.LinkClicked += (s, e) => MonetisationRuntime.OpenOffer(ig);
                    _tips.SetToolTip(_instantGaming, "Chercher ce jeu sur Instant Gaming — lien affilié, le prix ne change pas pour toi.");
                    Controls.Add(_instantGaming);
                }
                RefreshActions(); Resize += (s, e) => LayoutActions(); LayoutActions();
                MouseEnter += (s, e) => { _hover = true; Invalidate(); };
                MouseLeave += (s, e) => { _hover = false; Invalidate(); };
            }
            private void RefreshActions()
            {
                bool favorite = _page.EstFavori(_o), follow = _page.EstSuivi(_o);
                _favorite.Text = favorite ? "★" : "☆"; _favorite.ForeColor = favorite ? FpsUi.Gold : FpsUi.Dim;
                _favorite.AccessibleName = (favorite ? "Retirer des favoris : " : "Ajouter aux favoris : ") + _o.Nom;
                _tips.SetToolTip(_favorite, favorite ? "Retirer des favoris" : "Ajouter aux favoris");
                _follow.Text = follow ? "Suivi ✓" : "Suivre"; _follow.ForeColor = follow ? FpsUi.Gold : FpsUi.Ink;
                _follow.AccessibleName = (follow ? "Ne plus suivre : " : "Suivre les promotions : ") + _o.Nom;
                _tips.SetToolTip(_follow, "Être prévenu des promotions de ce jeu sur Steam");
            }
            private void LayoutActions()
            {
                int actionsY = PriceY + 50;
                _compare.SetBounds(Width - 92, PriceY - 1, 78, 26);
                _more.SetBounds(Width - 42, TitleY - 2, 28, 26);
                _favorite.SetBounds(Width - 46, actionsY, 32, 32);
                _follow.SetBounds(_favorite.Left - 80, actionsY, 72, 32);
                _open.SetBounds(14, actionsY, (_o.SteamAppId > 0 ? _follow.Left : _favorite.Left) - 22, 32);
                if (_instantGaming != null) _instantGaming.SetBounds(14, actionsY + 36, Width - 28, 18);
            }
            private void Ready()
            {
                try { if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)(() => { if (!IsDisposed) Invalidate(); })); } catch { }
            }
            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    foreach (Control c in new Control[] { this, _open, _compare, _favorite, _follow, _more, _instantGaming })
                        if (c != null) _tips.SetToolTip(c, null);
                base.Dispose(disposing);
            }
            private static void Cover(Graphics g, Image image, Rectangle target)
            {
                Rectangle destination = DealArtwork.Fit(image.Size, target);
                OnyxDesign.PaintCard(g, target, FpsUi.RailBg, FpsUi.Border, 8);
                var state = g.Save();
                using (var path = FpsUi.Round(target, 8))
                {
                    g.SetClip(path);
                    g.DrawImage(image, destination, new Rectangle(Point.Empty, image.Size), GraphicsUnit.Pixel);
                }
                g.Restore(state);
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e); var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                OnyxDesign.PaintCard(g, ClientRectangle, FpsUi.Card, _hover || ContainsFocus ? FpsUi.GoldDim : FpsUi.Border, 12);
                var art = Artwork; Image image = null;
                try
                {
                    image = GameArt.ForDeal(_o, Ready);
                }
                catch { }
                if (image != null) { try { Cover(g, image, art); } catch { } }
                else
                {
                    OnyxDesign.PaintCard(g, art, FpsUi.BlendGold(FpsUi.RailBg, .06f), OnyxDesign.Edge, 8);
                    NavIcons.Draw(g, NavIcons.Jeux, new RectangleF(art.X + (art.Width - 50) / 2, art.Y + (art.Height - 40) / 2, 50, 40), FpsUi.GoldDim);
                }
                string store = OffresSources.NomBoutique(_o.Source);
                int storeWidth = Math.Min(Width - 120, TextRenderer.MeasureText(store, FpsUi.Small).Width + 20);
                Badge(g, store, new Rectangle(18, 18, storeWidth, 25), FpsUi.RailBg, CouleurStore(_o.Source));
                if (_o.Remise > 0) Badge(g, "−" + _o.Remise + " %", new Rectangle(Width - 88, 18, 70, 25), FpsUi.Gold, FpsUi.BgMain);
                OnyxDesign.Text(g, _o.Nom ?? "Jeu", FpsUi.H3, FpsUi.Ink, new Rectangle(14, TitleY, Width - (_more.Visible ? 60 : 28), 23));
                string price = _o.PrixCts < 0 ? (_o.AVenir ? "À venir" : "Prix à consulter") : Offres.PrixDe(_o) + (_o.Devise == "USD" && _o.PrixCts > 0 ? " US" : "");
                OnyxDesign.Text(g, price, FpsUi.H2, FpsUi.Gold, new Rectangle(14, PriceY, Width - 116, 25));
                if (_o.Remise > 0 && _o.PrixOrigineCts > 0)
                    OnyxDesign.Text(g, Offres.PrixOrigineDe(_o), Barre, FpsUi.Dim2, new Rectangle(14, PriceY + 27, 82, 18));
                string detail = _o.Plateformes != null ? string.Join(", ", _o.Plateformes.Take(2)) : "";
                bool urgent = false;
                if (_o.Joueurs > 0) detail = Populaires.TexteJoueurs(_o.Joueurs);
                else if (_o.Fin.HasValue)
                {
                    var remaining = _o.Fin.Value - DateTime.UtcNow;
                    urgent = remaining > TimeSpan.Zero && remaining < TimeSpan.FromHours(48);
                    detail = remaining <= TimeSpan.Zero ? "Offre terminée" : urgent ? "Encore " + Math.Max(1, (int)Math.Ceiling(remaining.TotalHours)) + " h" : "Jusqu’au " + _o.Fin.Value.ToLocalTime().ToString("dd/MM");
                }
                else if (_o.Sortie.HasValue) detail = (_o.AVenir ? "À venir · sortie le " : "Sortie le ") + _o.Sortie.Value.ToLocalTime().ToString("dd/MM/yy");
                int detailX = _o.Remise > 0 && _o.PrixOrigineCts > 0 ? 100 : 14;
                OnyxDesign.Text(g, detail, FpsUi.Tiny, urgent ? FpsUi.Warn : FpsUi.Dim, new Rectangle(detailX, PriceY + 27, Width - detailX - 14, 18));
            }
            private static void Badge(Graphics g, string text, Rectangle rect, Color fill, Color ink)
            {
                using (var path = FpsUi.Round(rect, 6)) using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
                TextRenderer.DrawText(g, text, FpsUi.Small, rect, ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
        }
    }
}
