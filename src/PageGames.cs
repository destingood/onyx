using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    // Page Jeux : bibliotheque des jeux detectes (grand catalogue) + recherche + Mode Jeu (boost).
    internal class PageGames : FpsPage
    {
        private FlowLayoutPanel _flow;
        private Button _mode;
        private Button _prio;
        private Button _space;
        private TextBox _search;
        private BufferedPanel _searchBox;
        private Button _installed, _catalogue;
        private Button _platform;
        private readonly ContextMenuStrip _platformMenu = new ContextMenuStrip();
        private Label _results;
        private bool _catalogueMode;
        private string _storeFilter = "Toutes les plateformes";
        private readonly ToolTip _tips = new ToolTip();

        // Mode « Espace » : retrie la bibliothèque par espace DORMANT (taille × temps sans jouer)
        // et affiche taille + dernière partie sur chaque tuile, au lieu du simple « DÉTECTÉ ».
        private bool _spaceMode, _spaceLoading;
        private Dictionary<string, DormantGames.Entry> _spaceData;
        private Dictionary<string, int> _spaceRank;
        private string _query = "";
        private string _subtitle = "Analyse des jeux installés…";
        private List<GameScan.GameInfo> _all;
        private bool _loaded;
        internal int RendusPourTest;
        private string _signatureRendu;
        private object _allRendu, _spaceRendu;
        private ScrollWheelFilter _wheel;

        public PageGames(DashboardForm host) : base(host)
        {
            Build();
        }

        private void Build()
        {
            _mode = OnyxDesign.AccentButton("Activer le mode jeu");
            _mode.Width = 190; _mode.Height = 36;
            _mode.Click += (s, e) => ToggleBoost();
            Controls.Add(_mode);

            _prio = OnyxDesign.Button("Priorité par jeu");
            _prio.Width = 160; _prio.Height = 34;
            _prio.Click += (s, e) => Host.OpenDialog(new GameProfileForm(Host.Log));
            Controls.Add(_prio);

            _space = OnyxDesign.Button("Trier par espace");
            _space.Width = 128; _space.Height = 34;
            _space.Click += (s, e) => ToggleSpace();
            Controls.Add(_space);

            _installed = OnyxDesign.Button("Sur mon PC");
            _catalogue = OnyxDesign.Button("Catalogue");
            _installed.Name = "games-installed"; _catalogue.Name = "games-catalogue";
            _installed.Click += (s, e) => { _catalogueMode = false; Render(); };
            _catalogue.Click += (s, e) => { _catalogueMode = true; Render(); };
            Controls.Add(_installed); Controls.Add(_catalogue);

            _platform = OnyxDesign.Button("Toutes les plateformes  ⌄");
            _platform.Name = "games-platform";
            _platform.AccessibleName = "Filtrer par plateforme";
            _platformMenu.BackColor = FpsUi.Card; _platformMenu.ForeColor = FpsUi.Ink;
            _platformMenu.Font = FpsUi.Body; _platformMenu.ShowImageMargin = false;
            _platform.Click += (s, e) => _platformMenu.Show(_platform, new Point(0, _platform.Height + 4));
            Controls.Add(_platform);

            _searchBox = new BufferedPanel { BackColor = Color.Transparent };
            _searchBox.Paint += (s, e) => OnyxDesign.PaintCard(e.Graphics, _searchBox.ClientRectangle, FpsUi.Card,
                _search.Focused ? FpsUi.GoldDim : FpsUi.Border, 8);
            _search = new TextBox { Name = "games-search", PlaceholderText = "Rechercher un jeu…",
                BackColor = FpsUi.Card, ForeColor = FpsUi.Ink, BorderStyle = BorderStyle.None, Font = FpsUi.Body };
            _search.AccessibleName = "Rechercher dans les jeux";
            _search.TextChanged += (s, e) => { _query = _search.Text.Trim().ToLowerInvariant(); Render(); };
            _search.GotFocus += (s, e) => _searchBox.Invalidate();
            _search.LostFocus += (s, e) => _searchBox.Invalidate();
            _searchBox.Controls.Add(_search); Controls.Add(_searchBox);
            _results = FpsUi.Text("Chargement de la bibliothèque…", FpsUi.Small, FpsUi.Dim);
            _results.AutoSize = false; Controls.Add(_results);

            _flow = new BufferedFlow();      // double-bufferé → défilement fluide, sans scintillement
            _flow.AutoScroll = true;
            _flow.BackColor = Color.Transparent;
            _flow.Padding = new Padding(0, 0, 0, 16);
            _flow.Name = "games-grid";
            Controls.Add(_flow);

            // La molette défile la grille même quand le curseur est sur une carte (le message va
            // normalement au contrôle qui a le focus, pas à celui sous le curseur).
            _wheel = new ScrollWheelFilter(_flow);
            try { Application.AddMessageFilter(_wheel); } catch { }

            Resize += (s, e) => DoLayout();
        }

        public override void OnShown()
        {
            DoLayout();
            UpdateModeButton();
            // Deja charge : on redessine quand meme, pour repercuter un masquage fait depuis la
            // fiche d'un jeu (sinon la tuile resterait visible jusqu'au prochain lancement).
            if (_loaded) { Render(); if (_detail != null) Host.SetPageToolsVisible(false); return; }
            _loaded = true;
            Task.Run(() =>
            {
                List<GameScan.GameInfo> games = new List<GameScan.GameInfo>();
                try { games = GameScan.Known(); GameScan.Detect(games); } catch { }
                try { MergeScanned(games); } catch { }   // + TOUS les jeux installés (scanner générique multi-plateforme)
                try { BeginInvoke((Action)(() => { _all = games; UpdatePlatforms(); Render(); PrefetchArt(); })); } catch { }
                DropSteamSoftware(games);
            });
        }

        /// <summary>
        /// Steam vend aussi des logiciels (Wallpaper Engine, 3DMark, Lossless Scaling) que rien ne
        /// distingue d'un jeu sur le disque : le store les classe « Software ». On les retire de la
        /// bibliothèque dès que la réponse arrive (mise en cache 7 jours ; hors ligne, ils restent).
        /// </summary>
        private void DropSteamSoftware(List<GameScan.GameInfo> games)
        {
            var ids = new List<int>();
            foreach (GameScan.GameInfo g in games) if (g.Detected && g.SteamId > 0 && !ids.Contains(g.SteamId)) ids.Add(g.SteamId);
            var software = new HashSet<int>();
            for (int i = 0; i < ids.Count; i += OffresSources.LotSteam)
            {
                try
                {
                    string url = OffresSources.UrlFichesSteam(ids.GetRange(i, Math.Min(OffresSources.LotSteam, ids.Count - i)));
                    software.UnionWith(BibliothequeJeux.LogicielsSteam(OffresReseau.Texte(url, 7 * 24 * 60).GetAwaiter().GetResult()));
                }
                catch { }
            }
            if (software.Count == 0) return;
            try
            {
                BeginInvoke((Action)(() =>
                {
                    if (_all == null) return;
                    _all.RemoveAll(g => g.Detected && software.Contains(g.SteamId));
                    UpdatePlatforms(); Render();
                }));
            }
            catch { }
        }

        // Fusionne les jeux RÉELLEMENT installés (toutes plateformes, connus ou pas) détectés par
        // le scanner générique, en plus du catalogue connu. Un jeu Steam hors-catalogue garde son
        // AppID → jaquette officielle quand même. Dédoublonnage par nom normalisé.
        private static void MergeScanned(List<GameScan.GameInfo> games)
        {
            List<GameLibrary.InstalledGame> scanned;
            try { scanned = GameLibrary.ScanAll(); } catch { return; }
            if (scanned == null || scanned.Count == 0) return;

            foreach (GameScan.GameInfo g in games) if (g.Detected) BibliothequeJeux.AjouteStore(g.Stores, g.Store);

            foreach (GameLibrary.InstalledGame s in scanned)
            {
                if (BibliothequeJeux.Cle(s.Name).Length == 0) continue;

                // Même jeu sur une autre plateforme (même AppID, même nom à l'édition près, ou nom
                // court du launcher = mot-clé du catalogue) : une seule carte, toutes les plateformes.
                GameScan.GameInfo known = null;
                foreach (GameScan.GameInfo g in games)
                    if (BibliothequeJeux.MemeJeu(g.Name, g.SteamId, g.Keywords, s.Name, s.SteamAppId)) { known = g; break; }
                if (known != null)
                {
                    // Le catalogue connaît déjà ce jeu — mais souvent SANS chemin d'installation :
                    // GameScan.Detect() peut le marquer « détecté » sur un simple mot-clé de
                    // désinstallation, en laissant InstallPath à null (cas de World of Warcraft et
                    // Hearthstone). On ENRICHIT donc l'entrée existante au lieu de jeter la nôtre,
                    // sinon : pas de dossier → pas d'icône extraite et bouton « Lancer » inerte.
                    known.Detected = true;
                    if (string.IsNullOrEmpty(known.InstallPath)) known.InstallPath = s.InstallDir;
                    if (string.IsNullOrEmpty(known.Store)) known.Store = s.Launcher;
                    if (known.SteamId <= 0 && s.SteamAppId > 0) known.SteamId = s.SteamAppId;
                    if (known.ImageUrl == null) known.ImageUrl = s.ImageUrl;
                    BibliothequeJeux.AjouteStore(known.Stores, known.Store);
                    BibliothequeJeux.AjouteStore(known.Stores, s.Launcher);
                    continue;
                }

                var added = new GameScan.GameInfo
                {
                    Name = s.Name, Detected = true, SteamId = s.SteamAppId,
                    InstallPath = s.InstallDir, Store = s.Launcher, ImageUrl = s.ImageUrl
                };
                BibliothequeJeux.AjouteStore(added.Stores, s.Launcher);
                games.Add(added);
            }
        }

        private static string Go(long mb)
        {
            return mb >= 1024 ? (mb / 1024.0).ToString("0.0") + " Go" : mb + " Mo";
        }

        private static string NormName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s.ToLowerInvariant()) if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }

        /// <summary>
        /// Lance la résolution « nom → AppID Steam » pour TOUS les jeux détectés sans AppID, dès
        /// le chargement de la bibliothèque. Indispensable : déclencher ça depuis le Paint d'une
        /// tuile ne marche que pour les cartes VISIBLES (WinForms ne peint pas hors écran), donc
        /// les jaquettes des jeux plus bas dans la liste n'arrivaient jamais.
        /// </summary>
        private void PrefetchArt()
        {
            if (_all == null) return;
            foreach (GameScan.GameInfo g in _all)
            {
                if (!g.Detected || g.SteamId > 0 || g.ImageUrl != null || string.IsNullOrEmpty(g.Name)) continue;
                GameScan.GameInfo gg = g;
                int id = SteamAppIndex.Resolve(gg.Name, () =>
                {
                    // Résolution arrivée : on relit (désormais en cache) et on redessine.
                    try
                    {
                        if (!IsHandleCreated) return;
                        BeginInvoke((Action)(() =>
                        {
                            if (gg.SteamId <= 0) gg.SteamId = SteamAppIndex.Resolve(gg.Name, null);
                            if (_flow != null) _flow.Invalidate(true);
                        }));
                    }
                    catch { }
                });
                if (id > 0) gg.SteamId = id;   // déjà en cache : immédiat
            }
        }

        /// <summary>
        /// Bascule le mode « Espace ». Le calcul des tailles est lourd (des milliers de fichiers)
        /// : il tourne en tâche de fond, une seule fois, et DormantGames le met en cache disque.
        /// </summary>
        private void ToggleSpace()
        {
            _spaceMode = !_spaceMode;
            if (_space != null) _space.Text = _spaceMode ? "Tri : espace ↓" : "Trier par espace";

            if (_spaceMode && _spaceData == null && !_spaceLoading)
            {
                _spaceLoading = true;
                _results.Text = "Analyse de l’espace occupé en cours…";
                Render();
                Task.Run(() =>
                {
                    List<DormantGames.Entry> res;
                    try { res = DormantGames.Scan(null); }
                    catch { res = new List<DormantGames.Entry>(); }

                    var data = new Dictionary<string, DormantGames.Entry>(StringComparer.Ordinal);
                    var rank = new Dictionary<string, int>(StringComparer.Ordinal);
                    for (int i = 0; i < res.Count; i++)
                    {
                        string k = NormName(res[i].Name);
                        if (k.Length == 0 || data.ContainsKey(k)) continue;
                        data[k] = res[i]; rank[k] = i;      // res est déjà trié : rang 0 = plus dormant
                    }
                    try { BeginInvoke((Action)(() => { _spaceData = data; _spaceRank = rank; _spaceLoading = false; Render(); })); }
                    catch { }
                });
                return;
            }
            Render();
        }

        // Rang « espace dormant » du jeu (0 = le plus gros gain). Inconnu → renvoyé en fin de liste.
        private int SpaceRank(GameScan.GameInfo g)
        {
            int r;
            return _spaceRank != null && _spaceRank.TryGetValue(NormName(g.Name), out r) ? r : int.MaxValue;
        }

        private DormantGames.Entry SpaceInfo(GameScan.GameInfo g)
        {
            DormantGames.Entry e;
            return _spaceData != null && _spaceData.TryGetValue(NormName(g.Name), out e) ? e : null;
        }

        /// <summary>Plateformes du jeu : toutes celles où il est installé, sinon son launcher.</summary>
        private static List<string> Stores(GameScan.GameInfo game)
        {
            if (game.Stores.Count > 0) return game.Stores;
            var one = new List<string>();
            BibliothequeJeux.AjouteStore(one, game.Store);
            return one;
        }

        /// <summary>Texte de la pastille : « STEAM + EPIC », ou « Autres ».</summary>
        private static string StoreName(GameScan.GameInfo game)
        {
            return BibliothequeJeux.Pastille(Stores(game));
        }

        private static bool OnStore(GameScan.GameInfo game, string store)
        {
            List<string> all = Stores(game);
            return all.Count == 0 ? store == "Autres" : all.Contains(store);
        }

        private void UpdatePlatforms()
        {
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var game in _all)
            {
                if (GameHidden.IsHidden(game.Name)) continue;
                List<string> all = Stores(game);
                if (all.Count == 0) names.Add("Autres");
                foreach (string s in all) names.Add(s);
            }
            _platformMenu.Items.Clear();
            AddPlatform("Toutes les plateformes");
            foreach (string name in names) AddPlatform(name);
        }

        private void AddPlatform(string name)
        {
            var item = new ToolStripMenuItem(name) { Checked = name == _storeFilter };
            item.Click += (s, e) =>
            {
                _storeFilter = name; _platform.Text = name + "  ⌄";
                foreach (ToolStripMenuItem choice in _platformMenu.Items) choice.Checked = choice == item;
                Render();
            };
            _platformMenu.Items.Add(item);
        }

        private bool Match(GameScan.GameInfo game)
        {
            return (_query.Length == 0 || (game.Name ?? "").ToLowerInvariant().Contains(_query))
                && (_storeFilter == "Toutes les plateformes" || OnStore(game, _storeFilter));
        }

        private void Render()
        {
            if (_all == null || _flow == null) return;
            var visible = new List<GameScan.GameInfo>();
            int installed = 0, catalogue = 0;
            foreach (var game in _all)
            {
                if (GameHidden.IsHidden(game.Name)) continue;
                if (game.Detected) installed++; else catalogue++;
                if (game.Detected == !_catalogueMode && Match(game)) visible.Add(game);
            }
            visible.Sort((a, b) =>
            {
                int rank = _spaceMode && !_catalogueMode ? SpaceRank(a).CompareTo(SpaceRank(b)) : 0;
                return rank != 0 ? rank : StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name);
            });
            _installed.Text = "Sur mon PC · " + installed; _catalogue.Text = "Catalogue · " + catalogue;
            StyleFilter(_installed, !_catalogueMode); StyleFilter(_catalogue, _catalogueMode);
            StyleFilter(_space, _spaceMode && !_catalogueMode);
            _space.Enabled = !_catalogueMode;
            _subtitle = installed + (installed == 1 ? " jeu détecté" : " jeux détectés") + " · Tes jeux, leurs réglages et leurs performances.";
            _results.Text = visible.Count + (visible.Count == 1 ? " jeu" : " jeux")
                + (_catalogueMode ? " dans le catalogue · Non détectés sur ce PC" : " sur ton PC")
                + (_spaceMode && !_catalogueMode ? (_spaceLoading ? " · Analyse de l’espace…" : " · Tri par espace récupérable") : " · Par ordre alphabétique");
            if (_spaceMode && !_catalogueMode && !_spaceLoading && _spaceData != null)
            {
                long total = 0;
                foreach (var game in visible) { var info = SpaceInfo(game); if (info != null) total += info.SizeMB; }
                _results.Text += " · " + Go(total) + " mesurés";
            }
            // Rien de ce que montrent les tuiles n'a changé (retour sur la page, filtre sans effet) :
            // on garde les cartes, leurs jaquettes et la position de défilement.
            var sig = new System.Text.StringBuilder(_catalogueMode ? "c" : "i").Append(_spaceMode ? "s" : "-").Append(_spaceLoading ? "l" : "-");
            foreach (var game in visible) sig.Append('|').Append(game.Name);
            string signature = sig.ToString();
            if (signature == _signatureRendu && ReferenceEquals(_all, _allRendu) && ReferenceEquals(_spaceData, _spaceRendu) && _flow.Controls.Count > 0)
            { Invalidate(); return; }
            _signatureRendu = signature; _allRendu = _all; _spaceRendu = _spaceData;
            RendusPourTest++;
            _flow.SuspendLayout();
            var old = new Control[_flow.Controls.Count]; _flow.Controls.CopyTo(old, 0);
            _flow.Controls.Clear(); _flow.AutoScrollPosition = Point.Empty;
            foreach (var game in visible) _flow.Controls.Add(Card(game));
            if (visible.Count == 0)
            {
                var empty = new BufferedPanel { Height = 200, Tag = "empty", BackColor = Color.Transparent };
                empty.Paint += (s, e) =>
                {
                    OnyxDesign.PaintCard(e.Graphics, empty.ClientRectangle, FpsUi.Card, FpsUi.Border, 12);
                    OnyxDesign.IconWell(e.Graphics, NavIcons.Jeux, new Rectangle(24, 26, 48, 48));
                    OnyxDesign.Text(e.Graphics, "Aucun jeu dans cette vue", FpsUi.H2, FpsUi.Ink, new Rectangle(90, 28, empty.Width - 114, 28));
                    OnyxDesign.Text(e.Graphics, "Essaie un autre nom ou sélectionne toutes les plateformes.", FpsUi.Body, FpsUi.Dim, new Rectangle(90, 65, empty.Width - 114, 52), true);
                };
                _flow.Controls.Add(empty);
            }
            LayoutCards(); _flow.ResumeLayout();
            foreach (Control control in old) { _tips.SetToolTip(control, null); control.Dispose(); }
            Invalidate();
        }

        private static void StyleFilter(Button button, bool active)
        {
            button.ForeColor = active ? FpsUi.Gold : FpsUi.Dim;
            button.BackColor = active ? FpsUi.BlendGold(FpsUi.Card, .1f) : FpsUi.Card;
            button.FlatAppearance.BorderSize = active ? 1 : 0;
            button.FlatAppearance.BorderColor = active ? FpsUi.GoldDim : FpsUi.Border;
            button.Invalidate();
        }

        private void LayoutCards()
        {
            int available = Math.Max(250, _flow.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2);
            int columns = Math.Max(1, available / 205);
            int width = (available - (columns - 1) * 16) / columns;
            int index = 0;
            foreach (Control card in _flow.Controls)
            {
                if (card.Tag is string) { card.Size = new Size(available, 200); continue; }
                card.Size = new Size(width, (int)(width * 1.12f) + 90);
                card.Margin = new Padding(0, 0, ++index % columns == 0 ? 0 : 16, 18);
            }
        }

        private Control Card(GameScan.GameInfo game)
        {
            var card = new GameTile { Tag = game, Text = game.Name, AccessibleName = game.Name,
                AccessibleDescription = "Ouvrir la fiche du jeu", BackColor = FpsUi.BgMain };
            Action ready = () => { try { if (card.IsHandleCreated && !card.IsDisposed) card.BeginInvoke((Action)card.Invalidate); } catch { } };
            card.Paint += (sender, e) =>
            {
                Graphics gr = e.Graphics;
                gr.SmoothingMode = SmoothingMode.AntiAlias;
                int artHeight = card.Height - 90;
                var art = new Rectangle(1, 1, card.Width - 2, artHeight);
                OnyxDesign.PaintCard(gr, card.ClientRectangle, FpsUi.Card, card.Hot || card.Focused ? FpsUi.GoldDim : FpsUi.Border, 12);
                // Hors Steam, la jaquette du catalogue local d'Epic ; à défaut, recherche du nom sur Steam.
                bool epicArt = game.SteamId <= 0 && game.ImageUrl != null && !GameArt.EchecUrl(game.ImageUrl);
                Image cover = epicArt ? GameArt.GetUrl(game.ImageUrl, ready) : GameArt.ForGame(game.Name, game.SteamId, ready);
                var state = gr.Save();
                using (var clip = FpsUi.Round(new RectangleF(1, 1, card.Width - 2, card.Height - 2), 11)) gr.SetClip(clip);
                gr.SetClip(art, CombineMode.Intersect);
                if (cover != null) DrawCover(gr, cover, art);
                else
                {
                    using (var fill = new LinearGradientBrush(art, Color.FromArgb(43, 39, 29), FpsUi.RailBg, 60f)) gr.FillRectangle(fill, art);
                    using (var line = new Pen(Color.FromArgb(14, FpsUi.Gold)))
                        for (int x = -artHeight; x < card.Width; x += 22) gr.DrawLine(line, x, art.Bottom, x + artHeight, art.Top);
                    Image icon = game.Detected && !epicArt ? GameIcon.For(game.InstallPath, ready) : null;
                    int size = Math.Min(icon != null ? 132 : 100, artHeight - 64);
                    var iconRect = new Rectangle((card.Width - size) / 2, (artHeight - size) / 2, size, size);
                    if (icon != null) DrawIconCover(gr, icon, art, iconRect);
                    else OnyxDesign.IconWell(gr, NavIcons.Jeux, new Rectangle((card.Width - 64) / 2, (artHeight - 64) / 2, 64, 64));
                }
                using (var fade = new LinearGradientBrush(art, Color.FromArgb(30, 0, 0, 0), Color.FromArgb(100, 0, 0, 0), 90f)) gr.FillRectangle(fade, art);
                gr.Restore(state);
                string store = StoreName(game);
                int chipWidth = Math.Min(card.Width - 24, TextRenderer.MeasureText(store, FpsUi.Tiny).Width + 18);
                var chip = new Rectangle(12, 12, chipWidth, 23);
                using (var path = FpsUi.Round(chip, 6))
                using (var fill = new SolidBrush(Color.FromArgb(225, FpsUi.RailBg))) gr.FillPath(fill, path);
                TextRenderer.DrawText(gr, store, FpsUi.Tiny, chip, FpsUi.Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                if (card.Hot || card.Focused)
                {
                    var action = new Rectangle(12, artHeight - 40, card.Width - 24, 30);
                    OnyxDesign.PaintCard(gr, action, FpsUi.Gold, FpsUi.Gold, 7);
                    TextRenderer.DrawText(gr, "Voir le jeu  →", FpsUi.H3, action, FpsUi.BgMain, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                OnyxDesign.Text(gr, game.Name, FpsUi.H3, FpsUi.Ink, new Rectangle(14, artHeight + 12, card.Width - 28, 40), true);
                var info = _spaceMode && game.Detected ? SpaceInfo(game) : null;
                string status = info != null ? info.SizeText + " · " + info.IdleText : game.Detected ? "Détecté sur ce PC" : "Fiche & réglages";
                OnyxDesign.Text(gr, status, FpsUi.Tiny, info != null && info.IsDormant ? FpsUi.Warn : FpsUi.Dim,
                    new Rectangle(14, card.Height - 26, card.Width - 28, 18));
            };
            _tips.SetToolTip(card, game.Name + " — ouvrir la fiche et les réglages");
            card.Click += (s, e) => OpenDetail(game);
            return card;
        }

        private sealed class GameTile : Button
        {
            public bool Hot;
            public GameTile()
            {
                FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand;
                SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            }
            protected override void OnPaint(PaintEventArgs e) { e.Graphics.Clear(BackColor); base.OnPaint(e); }
            protected override void OnMouseEnter(EventArgs e) { Hot = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { Hot = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
            protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        }

        // ------------------------------------------------------------------
        //  Fiche d'un jeu INTÉGRÉE dans la page (pas de fenêtre séparée) : elle
        //  remplace la grille, et son onglet « ‹ BIBLIOTHÈQUE » la ramène.
        //  Astuce WinForms : une Form avec TopLevel = false s'héberge dans un
        //  conteneur — on réutilise toute la fiche sans dupliquer une ligne.
        // ------------------------------------------------------------------
        private Form _detail;
        private Point _libraryScroll;

#if BTTEST
        internal bool PreviewReady { get { return _all != null; } }
        internal void PreviewDetail(bool open)
        {
            if (!open) { CloseDetail(); return; }
            var game = _all.Find(g => g.Detected && g.SteamId == 730) ?? _all.Find(g => g.Detected);
            if (game == null) throw new InvalidOperationException("Aucun jeu détecté pour vérifier la fiche.");
            OpenDetail(game);
            if (_detail == null || _flow.Visible || _searchBox.Visible || _space.Visible)
                throw new InvalidOperationException("La fiche ne remplace pas correctement la bibliothèque.");
        }
        internal void ValidateLibraryUi()
        {
            _catalogue.PerformClick();
            foreach (Control tile in _flow.Controls)
                if (tile.Tag is GameScan.GameInfo game && game.Detected) throw new InvalidOperationException("Jeu détecté dans le catalogue.");
            _installed.PerformClick();
            foreach (Control tile in _flow.Controls)
                if (tile.Tag is GameScan.GameInfo game && !game.Detected) throw new InvalidOperationException("Jeu du catalogue parmi les jeux détectés.");
            if (_platformMenu.Items.Count > 1)
            {
                ((ToolStripMenuItem)_platformMenu.Items[1]).PerformClick();
                foreach (Control tile in _flow.Controls)
                    if (tile.Tag is GameScan.GameInfo game && !OnStore(game, _storeFilter)) throw new InvalidOperationException("Filtre de plateforme incorrect.");
                ((ToolStripMenuItem)_platformMenu.Items[0]).PerformClick();
            }
            _search.Text = "counter";
            foreach (Control tile in _flow.Controls)
                if (tile.Tag is GameScan.GameInfo game && !(game.Name ?? "").ToLowerInvariant().Contains("counter"))
                    throw new InvalidOperationException("Recherche incorrecte.");
            _search.Text = "__onyx_no_game_matches_734981__";
            if (_flow.Controls.Count != 1 || !(_flow.Controls[0].Tag is string)) throw new InvalidOperationException("État vide absent.");
            _search.Text = "";
            DoLayout();
            foreach (Control tile in _flow.Controls)
                if (tile.Right > _flow.ClientSize.Width || tile.Width < 180) throw new InvalidOperationException("Grille hors cadre.");
            if (_platform.Right + 8 > _searchBox.Left) throw new InvalidOperationException("Filtres superposés.");
        }
#endif

        private void OpenDetail(GameScan.GameInfo g)
        {
            CloseDetail();
            _libraryScroll = _flow.AutoScrollPosition;
            try
            {
                var f = new GameDetailForm(g, Host.Log);
                f.TopLevel = false;
                f.FormBorderStyle = FormBorderStyle.None;
                f.StartPosition = FormStartPosition.Manual;   // CenterParent n'a aucun sens intégré
                f.Dock = DockStyle.Fill;
                // « Fermer » et « ‹ BIBLIOTHÈQUE » appellent Close() : on l'intercepte pour
                // revenir à la grille. Le retrait/Dispose est DIFFÉRÉ (BeginInvoke) : détruire
                // une Form pendant son propre FormClosing laisse une fenêtre fantôme.
                f.FormClosing += (s, e) => { e.Cancel = true; try { BeginInvoke((Action)CloseDetail); } catch { } };
                _detail = f;
                // Échange en une seule passe de layout, fiche invisible tant qu'elle n'est
                // pas positionnée : sinon on voit la page se recomposer morceau par morceau.
                SuspendLayout();
                f.Visible = false;
                ShowLibraryChrome(false);
                Controls.Add(f);
                f.BringToFront();
                ResumeLayout(true);
                f.Show();
            }
            catch { CloseDetail(); }
        }

        private void CloseDetail()
        {
            if (_detail == null) return;
            var f = _detail; _detail = null;
            SuspendLayout();
            try { f.Visible = false; Controls.Remove(f); } catch { }
            ShowLibraryChrome(true);
            try { if (_flow != null) _flow.BringToFront(); } catch { }
            ResumeLayout(true);
            try { f.Dispose(); } catch { }
            Render();
            _flow.AutoScrollPosition = new Point(-_libraryScroll.X, -_libraryScroll.Y);
        }

        private void ShowLibraryChrome(bool on)
        {
            if (_flow != null) _flow.Visible = on;
            if (_searchBox != null) _searchBox.Visible = on;
            _installed.Visible = _catalogue.Visible = _platform.Visible = _results.Visible = _space.Visible = on;
            Host.SetPageToolsVisible(on);
            if (_mode != null) _mode.Visible = on;
            if (_prio != null) _prio.Visible = on;
            Invalidate();
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Image, Bitmap> _blur =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Image, Bitmap>();

        /// <summary>
        /// Jeu sans jaquette publiée (World of Warcraft, Hearthstone : ni sur Steam ni sur Epic) :
        /// son icône officielle en grand, sur un fond tiré de ses propres couleurs (icône réduite
        /// à 6×6 puis agrandie, donc floue), assombri pour garder le nom lisible.
        /// </summary>
        private static void DrawIconCover(Graphics g, Image icon, Rectangle art, Rectangle iconRect)
        {
            try
            {
                Bitmap small;
                if (!_blur.TryGetValue(icon, out small))
                {
                    small = new Bitmap(6, 6);
                    using (Graphics sg = Graphics.FromImage(small))
                    {
                        sg.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        sg.DrawImage(icon, new Rectangle(0, 0, 6, 6));
                    }
                    try { _blur.Add(icon, small); } catch { }
                }
                var old = g.InterpolationMode; var oldOffset = g.PixelOffsetMode;
                g.InterpolationMode = InterpolationMode.HighQualityBilinear; g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(small, art, new RectangleF(0.5f, 0.5f, 5f, 5f), GraphicsUnit.Pixel);
                g.PixelOffsetMode = oldOffset;
                using (var veil = new SolidBrush(Color.FromArgb(120, 10, 10, 12))) g.FillRectangle(veil, art);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(icon, iconRect);
                g.InterpolationMode = old;
            }
            catch { }
        }

        // Dessine l'image en « cover » (remplit dst, recadrage centré).
        private static void DrawCover(Graphics g, Image img, Rectangle dst)
        {
            try
            {
                float sc = Math.Max((float)dst.Width / img.Width, (float)dst.Height / img.Height);
                int w = (int)Math.Ceiling(img.Width * sc), h = (int)Math.Ceiling(img.Height * sc);
                int x = dst.X + (dst.Width - w) / 2, y = dst.Y + (dst.Height - h) / 2;
                var old = g.InterpolationMode; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(img, new Rectangle(x, y, w, h));
                g.InterpolationMode = old;
            }
            catch { }
        }

        private void ToggleBoost()
        {
            _mode.Enabled = false;
            // Bascule MANUELLE : l'automatique n'en est plus propriétaire (AutoJeu.Desapproprie).
            try { AutoJeu.Desapproprie(); } catch { }
            bool activating = !GameBoost.IsActive;
            Task.Run(() =>
            {
                try { if (activating) GameBoost.Activate(Host.Log); else GameBoost.Deactivate(Host.Log); } catch { }
                try { BeginInvoke((Action)(() => { _mode.Enabled = true; UpdateModeButton(); })); } catch { }
            });
        }

        private void UpdateModeButton()
        {
            if (GameBoost.IsActive)
            {
                _mode.Text = "Mode jeu actif · Arrêter";
                _mode.ForeColor = FpsUi.Gold;
                _mode.FlatAppearance.BorderColor = FpsUi.Gold;
            }
            else
            {
                _mode.Text = "Activer le mode jeu";
                _mode.ForeColor = FpsUi.Gold;
                _mode.FlatAppearance.BorderColor = FpsUi.Gold;
            }
        }

        private void DoLayout()
        {
            if (_flow == null) return;
            int row = Host != null ? Host.ContentTop(96) : 96;
            int right = ClientSize.Width - 34;
            _mode.SetBounds(right - 190, 20, 190, 36);
            _prio.SetBounds(_mode.Left - 172, 20, 160, 36);
            _installed.SetBounds(34, row, 142, 36); _catalogue.SetBounds(184, row, 136, 36);
            _space.SetBounds(332, row, 132, 36);
            _searchBox.SetBounds(right - 230, row, 230, 36);
            _search.SetBounds(12, 9, 206, 20);
            _platform.SetBounds(478, row, Math.Max(120, _searchBox.Left - 490), 36);
            _results.SetBounds(34, row + 52, right - 34, 24);
            _flow.SetBounds(34, row + 86, ClientSize.Width - 52, Math.Max(100, ClientSize.Height - row - 102));
            _flow.SuspendLayout(); LayoutCards(); _flow.ResumeLayout();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_detail != null) return;   // la fiche intégrée dessine son propre en-tête
            PaintPremiumTitle(e.Graphics, "MES JEUX", _subtitle);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _wheel != null) { try { Application.RemoveMessageFilter(_wheel); } catch { } _wheel = null; }
            if (disposing) { _tips.Dispose(); _platformMenu.Dispose(); }
            base.Dispose(disposing);
        }
    }

    // FlowLayoutPanel composité (WS_EX_COMPOSITED) : rend toute la grille + ses cartes enfants dans
    // un seul back-buffer → défilement fluide sans scintillement, tout en gardant la transparence.
    internal class BufferedFlow : FlowLayoutPanel
    {
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x02000000; return cp; }  // WS_EX_COMPOSITED
        }
    }
}
