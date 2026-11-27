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
        private Button _clips, _bilan;
        private Button _space;
        private TextBox _search;
        private BufferedPanel _searchBox;
        private Button _installed, _catalogue;
        private Button _platform;
        private readonly ContextMenuStrip _platformMenu = new ContextMenuStrip();
        // Grille ou liste, et regroupement de la liste (choix gardé dans bt-jeux-vue.txt).
        private Button _vue;
        private readonly ContextMenuStrip _vueMenu = new ContextMenuStrip();
        private VueJeux.Choix _choixVue = ChargeVue();
        private const int LargeurVue = 200;
        private Label _results;
        private bool _catalogueMode;
        private string _storeFilter = "Toutes les plateformes";
        private string _tagFilter;   // étiquette choisie dans le même menu (null : aucune)
        private readonly ToolTip _tips = new ToolTip();

        // Mode « Espace » : retrie la bibliothèque par espace DORMANT (taille × temps sans jouer)
        // et affiche taille + dernière partie sur chaque tuile, au lieu du simple « DÉTECTÉ ».
        private bool _spaceMode, _spaceLoading;
        // Mode « Joués récemment » : dernière partie du journal en tête (favoris ★ toujours avant).
        private Button _recents;
        private bool _recentMode;
        private readonly ContextMenuStrip _cardMenu = new ContextMenuStrip();
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
            JournalJeu.Change += JournalChange;
            Disposed += (s, e) => JournalJeu.Change -= JournalChange;
        }

        // Temps de jeu des tuiles : copie du journal, et texte déjà calculé par carte.
        private List<JournalJeu.Partie> _journal;
        private readonly Dictionary<GameScan.GameInfo, string> _tempsJeu = new Dictionary<GameScan.GameInfo, string>();
        private readonly Dictionary<GameScan.GameInfo, DateTime?> _derniere = new Dictionary<GameScan.GameInfo, DateTime?>();

        /// <summary>Une partie vient d'être close (fil de fond) : on relit le journal et on repeint.</summary>
        private void JournalChange()
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke((Action)(() =>
                {
                    _journal = null; _tempsJeu.Clear(); _derniere.Clear();
                    bool parPartie = _choixVue.Liste && _choixVue.Par == VueJeux.Groupe.DernierePartie;
                    if (parPartie) _signatureRendu = null;   // un jeu peut changer de tranche
                    if (_recentMode || parPartie) Render();
                    if (_flow != null) foreach (Control c in _flow.Controls) c.Invalidate();
                }));
            }
            catch { }
        }

        /// <summary>Parties du journal qui désignent ce jeu (nom du launcher ou du catalogue).</summary>
        internal static List<JournalJeu.Partie> PartiesDe(List<JournalJeu.Partie> toutes, GameScan.GameInfo g)
        {
            return JournalJeu.Filtre(toutes, n => BibliothequeJeux.MemeJeu(g.Name, g.SteamId, g.Keywords, n, 0));
        }

        private DateTime? Derniere(GameScan.GameInfo g)
        {
            DateTime? d;
            if (_derniere.TryGetValue(g, out d)) return d;
            if (_journal == null) _journal = JournalJeu.Parties;
            d = JournalJeu.DernierePartie(PartiesDe(_journal, g), null);
            _derniere[g] = d;
            return d;
        }

        private string TempsJeu(GameScan.GameInfo g)
        {
            string t;
            if (_tempsJeu.TryGetValue(g, out t)) return t;
            if (_journal == null) _journal = JournalJeu.Parties;
            List<JournalJeu.Partie> p = PartiesDe(_journal, g);
            t = JournalJeu.Tuile(JournalJeu.Secondes(p, null, DateTime.MinValue), JournalJeu.DernierePartie(p, null), DateTime.Now);
            _tempsJeu[g] = t;
            return t;
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

            _clips = OnyxDesign.Button("Clips");
            _clips.Name = "games-clips";
            _clips.AccessibleName = "Bibliothèque des clips";
            _clips.Click += (s, e) => Host.OpenDialog(new ClipsForm());
            Controls.Add(_clips);

            _bilan = OnyxDesign.Button("Bilan");
            _bilan.Name = "games-bilan";
            _bilan.AccessibleName = "Bilan de jeu : temps joué et jeux les plus joués";
            _bilan.Click += (s, e) => Host.OpenDialog(new BilanJeuForm(JournalJeu.Parties, DateTime.Now));
            Controls.Add(_bilan);

            _space = OnyxDesign.Button("Trier par espace");
            _space.Width = 128; _space.Height = 34;
            _space.Click += (s, e) => ToggleSpace();
            Controls.Add(_space);

            _recents = OnyxDesign.Button("Joués récemment");
            _recents.Name = "games-recents";
            _recents.AccessibleName = "Trier par dernière partie";
            _recents.Click += (s, e) => { _recentMode = !_recentMode; if (_recentMode) { _spaceMode = false; _space.Text = "Trier par espace"; } Render(); };
            Controls.Add(_recents);
            _cardMenu.BackColor = FpsUi.Card; _cardMenu.ForeColor = FpsUi.Ink;
            _cardMenu.Font = FpsUi.Body; _cardMenu.ShowImageMargin = false;

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

            _vue = OnyxDesign.Button(VueJeux.Bouton(_choixVue) + "  ⌄");
            _vue.Name = "games-vue";
            _vue.AccessibleName = "Affichage : grille ou liste, et regroupement";
            _vueMenu.BackColor = FpsUi.Card; _vueMenu.ForeColor = FpsUi.Ink;
            _vueMenu.Font = FpsUi.Body; _vueMenu.ShowImageMargin = false;
            _vue.Click += (s, e) => MenuVue();
            Controls.Add(_vue);

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

            ManetteVeille.Geste += GesteManette;
            VisibleChanged += (s, e) => MajEcouteManette();
            HandleCreated += (s, e) =>
            {
                Form f = FindForm();
                if (f == null) return;
                f.Activated += (s2, e2) => { _fenetreActive = true; MajEcouteManette(); };
                f.Deactivate += (s2, e2) => { _fenetreActive = false; MajEcouteManette(); };
                _fenetreActive = Form.ActiveForm == f;
                MajEcouteManette();
            };
        }

        // ------------------------------------------------------------------ mode manette

        private int _manetteIndex = -1;
        private bool _fenetreActive;

        /// <summary>La manette n'est lue que si ONYX est au premier plan sur Mes jeux : jamais en jeu.</summary>
        private void MajEcouteManette()
        {
            Form f = FindForm();
            ManetteVeille.Ecoute = Visible && _fenetreActive && f != null && f.Visible && f.WindowState != FormWindowState.Minimized;
        }

        private void GesteManette(Manette.Geste g)
        {
            try { if (IsHandleCreated && !IsDisposed) BeginInvoke((Action)(() => AppliqueManette(g))); } catch { }
        }

        private List<GameTile> Tuiles()
        {
            var l = new List<GameTile>();
            if (_flow != null) foreach (Control c in _flow.Controls) if (c is GameTile t) l.Add(t);
            return l;
        }

        private void MarqueManette()
        {
            List<GameTile> t = Tuiles();
            for (int i = 0; i < t.Count; i++)
            {
                bool c = i == _manetteIndex;
                if (t[i].Choisie != c) { t[i].Choisie = c; t[i].Invalidate(); }
            }
            if (_manetteIndex >= 0 && _manetteIndex < t.Count) _flow.ScrollControlIntoView(t[_manetteIndex]);
        }

        /// <summary>Croix ou stick : se déplacer ; A : ouvrir la fiche, puis lancer le jeu depuis la fiche ;
        /// B : revenir (fiche → grille, recherche effacée, sélection retirée) ; LB / RB : Sur mon PC / Catalogue.</summary>
        private void AppliqueManette(Manette.Geste g)
        {
            if (!Visible || IsDisposed) return;
            if (_detail != null)
            {
                if (g == Manette.Geste.Retour) { CloseDetail(); MarqueManette(); }
                else if (g == Manette.Geste.Valider)
                {
                    Button lancer = BoutonLancer(_detail);
                    if (lancer != null && lancer.Enabled && lancer.Visible) lancer.PerformClick();
                }
                return;
            }
            List<GameTile> t = Tuiles();
            switch (g)
            {
                case Manette.Geste.Precedent:
                case Manette.Geste.Suivant:
                    if (g == Manette.Geste.Precedent) _installed.PerformClick(); else _catalogue.PerformClick();
                    _manetteIndex = Tuiles().Count > 0 ? 0 : -1;
                    MarqueManette();
                    return;
                case Manette.Geste.Valider:
                    if (_manetteIndex >= 0 && _manetteIndex < t.Count && t[_manetteIndex].Tag is GameScan.GameInfo jeu) OpenDetail(jeu);
                    else { _manetteIndex = Manette.Deplace(-1, t.Count, Colonnes, g); MarqueManette(); }
                    return;
                case Manette.Geste.Retour:
                    if (_search.Text.Length > 0) _search.Text = "";
                    else _manetteIndex = -1;
                    MarqueManette();
                    return;
                default:
                    _manetteIndex = Manette.Deplace(_manetteIndex, t.Count, Colonnes, g);
                    MarqueManette();
                    return;
            }
        }

        /// <summary>Le bouton « Lancer le jeu » de la fiche, cherché par son texte.</summary>
        private static Button BoutonLancer(Control racine)
        {
            foreach (Control c in racine.Controls)
            {
                if (c is Button b && b.Text.StartsWith("Lancer le jeu", StringComparison.Ordinal)) return b;
                Button d = BoutonLancer(c);
                if (d != null) return d;
            }
            return null;
        }

        public override void OnShown()
        {
            DoLayout();
            UpdateModeButton();
            _journal = null; _tempsJeu.Clear(); _derniere.Clear();   // « il y a N j » bouge avec la date
            // Deja charge : on redessine quand meme, pour repercuter un masquage fait depuis la
            // fiche d'un jeu (sinon la tuile resterait visible jusqu'au prochain lancement).
            if (_loaded) { Render(); if (_detail != null) Host.SetPageToolsVisible(false); return; }
            _loaded = true;
            Task.Run(() =>
            {
                List<GameScan.GameInfo> games = new List<GameScan.GameInfo>();
                try { games = GameScan.Known(); GameScan.Detect(games); } catch { }
                try { MergeScanned(games); } catch { }   // + TOUS les jeux installés (scanner générique multi-plateforme)
                var installes = new HashSet<int>();
                foreach (GameScan.GameInfo g in games) if (g.Detected && g.SteamId > 0) installes.Add(g.SteamId);
                try { BeginInvoke((Action)(() => { _all = games; UpdatePlatforms(); Render(); PrefetchArt(); })); } catch { }
                DropSteamSoftware(games);
                AddOwnedSteam(installes);
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
            foreach (GameScan.GameInfo g in games) if (g.Detected && g.SteamId > 0) ids.Add(g.SteamId);
            HashSet<int> software = LogicielsSteam(ids);
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

        /// <summary>
        /// Jeux lancés sur le compte Steam de ce PC mais pas installés : ajoutés au catalogue (en tête),
        /// ou fusionnés avec la fiche du catalogue qui désigne le même jeu.
        /// </summary>
        private void AddOwnedSteam(HashSet<int> installes)
        {
            List<GameScan.GameInfo> owned;
            try { owned = JeuxCompteSteam.Charge(installes); } catch { return; }
            if (owned.Count == 0) return;
            try
            {
                BeginInvoke((Action)(() =>
                {
                    if (_all == null) return;
                    foreach (GameScan.GameInfo o in owned)
                    {
                        GameScan.GameInfo same = _all.Find(g => (g.SteamId > 0 && g.SteamId == o.SteamId) || BibliothequeJeux.MemeJeu(g.Name, g.SteamId, g.Keywords, o.Name, o.SteamId));
                        if (same == null) { _all.Add(o); continue; }
                        if (same.Detected) continue;   // installé par un autre magasin
                        same.SurCompte = true; same.MinutesSteam = o.MinutesSteam;
                        if (same.SteamId <= 0) same.SteamId = o.SteamId;
                        if (string.IsNullOrEmpty(same.Store)) same.Store = "STEAM";
                    }
                    _signatureRendu = null;
                    UpdatePlatforms(); Render();
                }));
            }
            catch { }
        }

        /// <summary>AppID classés « Software » par Steam (réseau, réponse en cache 7 jours ; hors ligne :
        /// ensemble vide). Bloquant : à appeler hors du fil d'interface.</summary>
        internal static HashSet<int> LogicielsSteam(List<int> appIds)
        {
            var ids = new List<int>();
            foreach (int id in appIds) if (id > 0 && !ids.Contains(id)) ids.Add(id);
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
            return software;
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
            if (_spaceMode) _recentMode = false;
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
            List<string> tags = EtiquettesJeux.Toutes();
            if (_tagFilter != null && !EtiquettesJeux.Porte(tags, _tagFilter))
            {
                _tagFilter = null; _storeFilter = "Toutes les plateformes"; _platform.Text = _storeFilter + "  ⌄";
                foreach (ToolStripItem choice in _platformMenu.Items)
                    if (choice is ToolStripMenuItem m) m.Checked = m.Text == _storeFilter;
            }
            if (tags.Count == 0) return;
            _platformMenu.Items.Add(new ToolStripSeparator());
            _platformMenu.Items.Add(new ToolStripMenuItem("Étiquettes") { Enabled = false });
            foreach (string tag in tags) AddTag(tag);
        }

        private void AddTag(string tag)
        {
            var item = new ToolStripMenuItem("# " + tag) { Checked = string.Equals(tag, _tagFilter, StringComparison.OrdinalIgnoreCase) };
            item.Click += (s, e) =>
            {
                _tagFilter = tag; _storeFilter = "Toutes les plateformes"; _platform.Text = "# " + tag + "  ⌄";
                foreach (ToolStripItem choice in _platformMenu.Items)
                    if (choice is ToolStripMenuItem m) m.Checked = m == item;
                Render();
            };
            _platformMenu.Items.Add(item);
        }

        private void AddPlatform(string name)
        {
            var item = new ToolStripMenuItem(name) { Checked = name == _storeFilter };
            item.Click += (s, e) =>
            {
                _storeFilter = name; _tagFilter = null; _platform.Text = name + "  ⌄";
                foreach (ToolStripItem choice in _platformMenu.Items)
                    if (choice is ToolStripMenuItem m) m.Checked = m == item;
                Render();
            };
            _platformMenu.Items.Add(item);
        }

        private bool Match(GameScan.GameInfo game)
        {
            List<string> tags = _query.Length > 0 || _tagFilter != null ? EtiquettesJeux.Pour(game.Name) : null;
            return (_query.Length == 0 || (game.Name ?? "").ToLowerInvariant().Contains(_query) || OptionsJeu.Nom(game.Name).ToLowerInvariant().Contains(_query)
                    || EtiquettesJeux.Contient(tags, _query))
                && (_tagFilter == null || EtiquettesJeux.Porte(tags, _tagFilter))
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
            bool recents = _recentMode && !_catalogueMode;
            var cles = new Dictionary<GameScan.GameInfo, TriJeux.Entree>();
            foreach (var game in visible)
                cles[game] = new TriJeux.Entree { Nom = OptionsJeu.Nom(game.Name), Favori = JeuxFavoris.Est(game.Name), Derniere = recents && game.Detected ? Derniere(game) : null };
            visible.Sort((a, b) =>
            {
                int rank = _catalogueMode ? b.SurCompte.CompareTo(a.SurCompte)
                    : _spaceMode ? SpaceRank(a).CompareTo(SpaceRank(b)) : 0;
                return rank != 0 ? rank : TriJeux.Compare(cles[a], cles[b], recents);
            });
            _installed.Text = "Sur mon PC · " + installed; _catalogue.Text = "Catalogue · " + catalogue;
            StyleFilter(_installed, !_catalogueMode); StyleFilter(_catalogue, _catalogueMode);
            StyleFilter(_space, _spaceMode && !_catalogueMode);
            _space.Enabled = !_catalogueMode;
            StyleFilter(_recents, recents);
            _recents.Enabled = !_catalogueMode;
            _subtitle = installed + (installed == 1 ? " jeu détecté" : " jeux détectés") + " · Tes jeux, leurs réglages et leurs performances.";
            int surCompte = _catalogueMode ? visible.FindAll(g => g.SurCompte).Count : 0;
            _results.Text = visible.Count + (visible.Count == 1 ? " jeu" : " jeux")
                + (_catalogueMode ? " dans le catalogue · Non détectés sur ce PC" + (surCompte > 0 ? " · dont " + surCompte + " sur ton compte Steam" : "") : " sur ton PC")
                + (_spaceMode && !_catalogueMode ? (_spaceLoading ? " · Analyse de l’espace…" : " · Tri par espace récupérable")
                   : recents ? " · Joués récemment (journal d’ONYX)" : " · Par ordre alphabétique")
                + " · Clic droit sur un jeu : ★ favori";
            if (_spaceMode && !_catalogueMode && !_spaceLoading && _spaceData != null)
            {
                long total = 0;
                foreach (var game in visible) { var info = SpaceInfo(game); if (info != null) total += info.SizeMB; }
                _results.Text += " · " + Go(total) + " mesurés";
            }
            // Rien de ce que montrent les tuiles n'a changé (retour sur la page, filtre sans effet) :
            // on garde les cartes, leurs jaquettes et la position de défilement.
            var sig = new System.Text.StringBuilder(_catalogueMode ? "c" : "i").Append(_spaceMode ? "s" : "-").Append(_spaceLoading ? "l" : "-").Append(recents ? "r" : "-")
                .Append(_choixVue.Liste ? "L" + (int)_choixVue.Par : "G");
            foreach (var game in visible) sig.Append('|').Append(game.Name).Append('=').Append(cles[game].Nom).Append(cles[game].Favori ? "*" : "");
            string signature = sig.ToString();
            if (signature == _signatureRendu && ReferenceEquals(_all, _allRendu) && ReferenceEquals(_spaceData, _spaceRendu) && _flow.Controls.Count > 0)
            { Invalidate(); return; }
            _signatureRendu = signature; _allRendu = _all; _spaceRendu = _spaceData;
            RendusPourTest++;
            _flow.SuspendLayout();
            var old = new Control[_flow.Controls.Count]; _flow.Controls.CopyTo(old, 0);
            _flow.Controls.Clear(); _flow.AutoScrollPosition = Point.Empty;
            if (_choixVue.Liste && _choixVue.Par != VueJeux.Groupe.Aucun && visible.Count > 0)
            {
                // Liste regroupée : un en-tête par bloc, puis ses jeux dans l'ordre de la bibliothèque.
                var infos = new List<VueJeux.Jeu>();
                foreach (var game in visible)
                    infos.Add(new VueJeux.Jeu
                    {
                        Plateforme = StoreName(game),
                        Etiquettes = _choixVue.Par == VueJeux.Groupe.Etiquette ? EtiquettesJeux.Pour(game.Name) : null,
                        Derniere = _choixVue.Par == VueJeux.Groupe.DernierePartie && game.Detected ? Derniere(game) : null
                    });
                foreach (VueJeux.Bloc bloc in VueJeux.Range(infos, _choixVue.Par, DateTime.Now))
                {
                    _flow.Controls.Add(new EnTeteGroupe(bloc.Titre));
                    foreach (int i in bloc.Jeux) _flow.Controls.Add(Card(visible[i]));
                }
            }
            else foreach (var game in visible) _flow.Controls.Add(Card(game));
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
            if (_manetteIndex >= 0) { _manetteIndex = Math.Min(_manetteIndex, Tuiles().Count - 1); MarqueManette(); }
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

        private int Disponible { get { return Math.Max(250, _flow.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2); } }
        private int Colonnes { get { return _choixVue.Liste ? 1 : Math.Max(1, Disponible / 205); } }

        private const int HauteurLigne = 64;

        private void LayoutCards()
        {
            int available = Disponible;
            if (_choixVue.Liste)
            {
                foreach (Control c in _flow.Controls)
                {
                    if (c is EnTeteGroupe) { c.Size = new Size(available, 34); c.Margin = new Padding(0, 8, 0, 6); }
                    else if (c.Tag is string) c.Size = new Size(available, 200);
                    else { c.Size = new Size(available, HauteurLigne); c.Margin = new Padding(0, 0, 0, 8); }
                }
                return;
            }
            int columns = Colonnes;
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
            string affiche = OptionsJeu.Nom(game.Name);
            var card = new GameTile { Tag = game, Text = affiche, AccessibleName = affiche,
                AccessibleDescription = "Ouvrir la fiche du jeu", BackColor = FpsUi.BgMain };
            Action ready = () => { try { if (card.IsHandleCreated && !card.IsDisposed) card.BeginInvoke((Action)card.Invalidate); } catch { } };
            List<string> etiquettes = EtiquettesJeux.Pour(game.Name);
            string sousTitre = StoreName(game) + (etiquettes.Count > 0 ? "   ·   # " + string.Join("   # ", etiquettes) : "");
            card.Paint += (sender, e) =>
            {
                Graphics gr = e.Graphics;
                gr.SmoothingMode = SmoothingMode.AntiAlias;
                if (_choixVue.Liste) { DessineLigne(gr, card, game, affiche, sousTitre, ready); return; }
                int artHeight = card.Height - 90;
                var art = new Rectangle(1, 1, card.Width - 2, artHeight);
                OnyxDesign.PaintCard(gr, card.ClientRectangle, FpsUi.Card, card.Hot || card.Focused || card.Choisie ? FpsUi.GoldDim : FpsUi.Border, 12);
                // Hors Steam, la jaquette du catalogue local d'Epic ; à défaut, recherche du nom sur Steam.
                bool epicArt = game.SteamId <= 0 && game.ImageUrl != null && !GameArt.EchecUrl(game.ImageUrl);
                Image cover = JaquettesPerso.Pour(game.Name) ?? (epicArt ? GameArt.GetUrl(game.ImageUrl, ready) : GameArt.ForGame(game.Name, game.SteamId, ready));
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
                if (JeuxFavoris.Est(game.Name))
                {
                    var star = new Rectangle(card.Width - 40, 12, 28, 23);
                    using (var path = FpsUi.Round(star, 6))
                    using (var fill = new SolidBrush(Color.FromArgb(225, FpsUi.RailBg))) gr.FillPath(fill, path);
                    TextRenderer.DrawText(gr, "★", FpsUi.Small, star, FpsUi.Gold, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
                if (card.Hot || card.Focused || card.Choisie)
                {
                    var action = new Rectangle(12, artHeight - 40, card.Width - 24, 30);
                    OnyxDesign.PaintCard(gr, action, FpsUi.Gold, FpsUi.Gold, 7);
                    TextRenderer.DrawText(gr, "Voir le jeu  →", FpsUi.H3, action, FpsUi.BgMain, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                OnyxDesign.Text(gr, affiche, FpsUi.H3, FpsUi.Ink, new Rectangle(14, artHeight + 12, card.Width - 28, 40), true);
                bool dormant;
                string status = Statut(game, out dormant);
                OnyxDesign.Text(gr, status, FpsUi.Tiny, dormant ? FpsUi.Warn : FpsUi.Dim,
                    new Rectangle(14, card.Height - 26, card.Width - 28, 18));
            };
            _tips.SetToolTip(card, affiche + " — ouvrir la fiche et les réglages" + (etiquettes.Count > 0 ? "\n# " + string.Join("  # ", etiquettes) : ""));
            card.Click += (s, e) => OpenDetail(game);
            card.MouseEnter += (s, e) => { if (_manetteIndex >= 0) { _manetteIndex = -1; MarqueManette(); } };   // la souris reprend la main
            card.MouseUp +=(s, e) => { if (e.Button == MouseButtons.Right) MenuCarte(card, game, e.Location); };
            return card;
        }

        /// <summary>Ligne d'état d'un jeu : espace et inactivité (tri par espace), temps de jeu, ou statut.</summary>
        private string Statut(GameScan.GameInfo game, out bool dormant)
        {
            var info = _spaceMode && game.Detected ? SpaceInfo(game) : null;
            dormant = info != null && info.IsDormant;
            string joue = info == null && game.Detected ? TempsJeu(game) : null;
            return info != null ? info.SizeText + " · " + info.IdleText
                : joue ?? (game.Detected ? "Détecté sur ce PC" : game.SurCompte ? JeuxCompteSteam.Statut(game.MinutesSteam) : "Fiche & réglages");
        }

        /// <summary>Un jeu en vue liste : petite jaquette, nom, plateforme et étiquettes, état à droite.</summary>
        private void DessineLigne(Graphics gr, GameTile card, GameScan.GameInfo game, string affiche, string sousTitre, Action ready)
        {
            bool vif = card.Hot || card.Focused || card.Choisie;
            OnyxDesign.PaintCard(gr, card.ClientRectangle, vif ? FpsUi.BlendGold(FpsUi.Card, .06f) : FpsUi.Card, vif ? FpsUi.GoldDim : FpsUi.Border, 10);
            var vignette = new Rectangle(8, 6, 46, card.Height - 12);
            bool epicArt = game.SteamId <= 0 && game.ImageUrl != null && !GameArt.EchecUrl(game.ImageUrl);
            Image cover = JaquettesPerso.Pour(game.Name) ?? (epicArt ? GameArt.GetUrl(game.ImageUrl, ready) : GameArt.ForGame(game.Name, game.SteamId, ready));
            var state = gr.Save();
            using (var clip = FpsUi.Round(vignette, 6)) gr.SetClip(clip);
            if (cover != null) DrawCover(gr, cover, vignette);
            else
            {
                using (var fill = new SolidBrush(FpsUi.RailBg)) gr.FillRectangle(fill, vignette);
                Image icon = game.Detected && !epicArt ? GameIcon.For(game.InstallPath, ready) : null;
                if (icon != null) gr.DrawImage(icon, new Rectangle(vignette.X + 7, vignette.Y + (vignette.Height - 32) / 2, 32, 32));
                else OnyxDesign.IconWell(gr, NavIcons.Jeux, new Rectangle(vignette.X + 5, vignette.Y + (vignette.Height - 36) / 2, 36, 36));
            }
            gr.Restore(state);

            int droite = Math.Max(140, Math.Min(280, card.Width / 3));
            int x = vignette.Right + 16, largeur = card.Width - x - droite - 24;
            if (JeuxFavoris.Est(game.Name))
            {
                TextRenderer.DrawText(gr, "★", FpsUi.Small, new Rectangle(x, 9, 20, 24), FpsUi.Gold, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                x += 22; largeur -= 22;
            }
            TextRenderer.DrawText(gr, affiche, FpsUi.H3, new Rectangle(x, 9, largeur, 24), FpsUi.Ink,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(gr, sousTitre, FpsUi.Tiny, new Rectangle(vignette.Right + 16, 36, card.Width - vignette.Right - droite - 40, 18), FpsUi.Dim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            bool dormant;
            string status = Statut(game, out dormant);
            TextRenderer.DrawText(gr, vif ? "Voir le jeu  →" : status, vif ? FpsUi.H3 : FpsUi.Small, new Rectangle(card.Width - droite - 16, 0, droite, card.Height),
                vif ? FpsUi.Gold : dormant ? FpsUi.Warn : FpsUi.Dim,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        /// <summary>Titre d'un bloc de la liste regroupée (« Steam · 12 »).</summary>
        private sealed class EnTeteGroupe : Control
        {
            public EnTeteGroupe(string titre)
            {
                Text = titre; Tag = "groupe"; AccessibleName = titre; AccessibleRole = AccessibleRole.StaticText;
                SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
                BackColor = Color.Transparent;
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                TextRenderer.DrawText(e.Graphics, Text, FpsUi.H3, new Rectangle(2, 0, Width - 4, Height - 4), FpsUi.Gold,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                using (var pen = new Pen(FpsUi.Border)) e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
            }
        }

        private static VueJeux.Choix ChargeVue()
        {
            try
            {
                string p = AppPaths.File("bt-jeux-vue.txt");
                return VueJeux.Lit(System.IO.File.Exists(p) ? System.IO.File.ReadAllText(p) : "");
            }
            catch { return new VueJeux.Choix(); }
        }

        private void MenuVue()
        {
            _vueMenu.Items.Clear();
            var grille = new ToolStripMenuItem("Grille de jaquettes") { Checked = !_choixVue.Liste };
            grille.Click += (s, e) => ChangeVue(false, _choixVue.Par);
            var liste = new ToolStripMenuItem("Liste") { Checked = _choixVue.Liste };
            liste.Click += (s, e) => ChangeVue(true, _choixVue.Par);
            _vueMenu.Items.Add(grille); _vueMenu.Items.Add(liste);
            _vueMenu.Items.Add(new ToolStripSeparator());
            _vueMenu.Items.Add(new ToolStripMenuItem("Regrouper la liste par") { Enabled = false });
            foreach (VueJeux.Groupe g in Enum.GetValues(typeof(VueJeux.Groupe)))
            {
                VueJeux.Groupe par = g;
                var it = new ToolStripMenuItem("   " + VueJeux.Libelles[(int)g]) { Checked = _choixVue.Liste && _choixVue.Par == g };
                it.Click += (s, e) => ChangeVue(true, par);
                _vueMenu.Items.Add(it);
            }
            _vueMenu.Show(_vue, new Point(0, _vue.Height + 4));
        }

        /// <summary>Change l'affichage ; <paramref name="garde"/> faux = essai sans écrire le choix (test d'interface).</summary>
        private void ChangeVue(bool liste, VueJeux.Groupe par, bool garde = true)
        {
            _choixVue = new VueJeux.Choix { Liste = liste, Par = par };
            if (garde)
                try { System.IO.File.WriteAllText(AppPaths.File("bt-jeux-vue.txt"), VueJeux.Ecrit(_choixVue)); } catch { }
            _vue.Text = VueJeux.Bouton(_choixVue) + "  ⌄";
            if (_manetteIndex >= 0) _manetteIndex = 0;
            Render();
        }

        /// <summary>Clic droit sur une tuile : favori ★ (en tête de la bibliothèque).</summary>
        private void MenuCarte(Control card, GameScan.GameInfo game, Point ou)
        {
            bool fav = JeuxFavoris.Est(game.Name);
            _cardMenu.Items.Clear();
            var it = new ToolStripMenuItem(fav ? "Retirer des favoris" : "★ Ajouter aux favoris");
            it.Click += (s, e) => { JeuxFavoris.Bascule(game.Name); Render(); };
            _cardMenu.Items.Add(it);
            var open = new ToolStripMenuItem("Ouvrir la fiche");
            open.Click += (s, e) => OpenDetail(game);
            _cardMenu.Items.Add(open);
            if (!game.Detected && game.SteamId > 0)
            {
                var install = new ToolStripMenuItem("Installer avec Steam");
                int id = game.SteamId;
                install.Click += (s, e) =>
                {
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("steam://install/" + id) { UseShellExecute = true }); }
                    catch (Exception ex) { _results.Text = "Steam n'a pas pu s'ouvrir : " + ex.Message; }
                };
                _cardMenu.Items.Add(install);
            }
            var opts = new ToolStripMenuItem("Options, étiquettes et images…");
            opts.Click += (s, e) =>
            {
                using (var f = new OptionsJeuForm(game))
                {
                    f.ShowDialog(FindForm());
                    if (!f.Change) return;
                    UpdatePlatforms();
                    _signatureRendu = null;   // étiquettes et images ne changent pas la signature
                    Render();
                    if (_flow != null) foreach (Control c in _flow.Controls) c.Invalidate();
                }
            };
            _cardMenu.Items.Add(opts);
            _cardMenu.Show(card, ou);
        }

        private sealed class GameTile : Button
        {
            public bool Hot;
            /// <summary>Case choisie à la manette (dessinée comme au survol).</summary>
            public bool Choisie;
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
        /// <summary>Mode manette sans manette : gestes injectés. « Valider » dans la fiche n'est JAMAIS
        /// testé (il lancerait le jeu).</summary>
        internal void ValidateManette()
        {
            VuePourTest(false, VueJeux.Groupe.Aucun);
            _installed.PerformClick(); _search.Text = ""; _manetteIndex = -1;
            List<GameTile> t = Tuiles();
            int cols = Colonnes;
            if (t.Count < cols + 2) throw new InvalidOperationException("Manette : trop peu de jeux pour tester la grille.");
            AppliqueManette(Manette.Geste.Droite);
            AppliqueManette(Manette.Geste.Droite);
            AppliqueManette(Manette.Geste.Bas);
            if (_manetteIndex != 1 + cols || !Tuiles()[1 + cols].Choisie || Tuiles()[0].Choisie)
                throw new InvalidOperationException("Manette : déplacement incorrect (" + _manetteIndex + ", " + cols + " colonnes).");
            AppliqueManette(Manette.Geste.Valider);
            if (_detail == null) throw new InvalidOperationException("Manette : A n'ouvre pas la fiche.");
            if (BoutonLancer(_detail) == null) throw new InvalidOperationException("Manette : bouton « Lancer le jeu » introuvable dans la fiche.");
            AppliqueManette(Manette.Geste.Retour);
            if (_detail != null || !Tuiles()[_manetteIndex].Choisie) throw new InvalidOperationException("Manette : B ne ramène pas à la grille sélectionnée.");
            AppliqueManette(Manette.Geste.Suivant);
            if (!_catalogueMode || _manetteIndex != 0) throw new InvalidOperationException("Manette : RB ne passe pas au catalogue.");
            AppliqueManette(Manette.Geste.Precedent);
            AppliqueManette(Manette.Geste.Retour);
            if (_catalogueMode || _manetteIndex != -1) throw new InvalidOperationException("Manette : LB ou B incorrect.");
        }

        internal void ApercuManette()
        {
            _manetteIndex = -1;
            AppliqueManette(Manette.Geste.Droite);
            AppliqueManette(Manette.Geste.Droite);
        }

        /// <summary>Change l'affichage sans écrire bt-jeux-vue.txt.</summary>
        internal void VuePourTest(bool liste, VueJeux.Groupe par) { ChangeVue(liste, par, false); }

        /// <summary>Vue liste : lignes pleine largeur, en-têtes en mode groupé, bouton sans chevauchement,
        /// manette sur une seule colonne. Le choix d'origine est remis à la fin.</summary>
        internal void ValidateListe()
        {
            var avant = _choixVue;
            _installed.PerformClick(); _search.Text = "";
            foreach (VueJeux.Groupe g in Enum.GetValues(typeof(VueJeux.Groupe)))
            {
                VuePourTest(true, g);
                int lignes = 0, entetes = 0;
                foreach (Control c in _flow.Controls)
                {
                    if (c is EnTeteGroupe) { entetes++; continue; }
                    if (!(c is GameTile)) continue;
                    lignes++;
                    if (c.Height != HauteurLigne || c.Width != Disponible || c.Right > _flow.ClientSize.Width)
                        throw new InvalidOperationException("Liste : ligne mal dimensionnée (" + c.Width + "x" + c.Height + ").");
                }
                if (lignes == 0) throw new InvalidOperationException("Liste : aucun jeu.");
                if ((g == VueJeux.Groupe.Aucun) != (entetes == 0)) throw new InvalidOperationException("Liste : en-têtes incorrects pour " + g + ".");
                if (_vue.Text.IndexOf(VueJeux.Bouton(_choixVue), StringComparison.Ordinal) < 0) throw new InvalidOperationException("Liste : texte du bouton.");
            }
            if (Colonnes != 1) throw new InvalidOperationException("Liste : la manette doit avancer d'une ligne.");
            if (_vue.Bounds.IntersectsWith(_platform.Bounds) || _vue.Bounds.IntersectsWith(_searchBox.Bounds)
                || _vue.Bounds.IntersectsWith(_results.Bounds) || _vue.Left < 0 || _vue.Bottom > _flow.Top)
                throw new InvalidOperationException("Bouton d'affichage superposé.");
            VuePourTest(avant.Liste, avant.Par);
        }

        internal void ValidateLibraryUi()
        {
            VuePourTest(false, VueJeux.Groupe.Aucun);
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
            if (_platform.Bounds.IntersectsWith(_searchBox.Bounds) || _results.Bounds.IntersectsWith(_searchBox.Bounds)
                || _searchBox.Bottom > _flow.Top || _platform.Right > ClientSize.Width)
                throw new InvalidOperationException("Filtres superposés.");
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
            if (_recents != null) _recents.Visible = on;
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
            _clips.SetBounds(_prio.Left - 112, 20, 100, 36);
            _bilan.SetBounds(_clips.Left - 112, 20, 100, 36);
            _installed.SetBounds(34, row, 142, 36); _catalogue.SetBounds(184, row, 136, 36);
            _space.SetBounds(332, row, 132, 36);
            _recents.SetBounds(472, row, 150, 36);
            // Fenêtre étroite : plateformes et recherche ne tiennent plus sur la rangée des filtres,
            // la recherche descend à droite de la ligne des résultats.
            bool etroit = right - 630 < 150 + 12 + LargeurVue + 12 + 230;
            _searchBox.SetBounds(right - 230, etroit ? row + 46 : row, 230, 36);
            _search.SetBounds(12, 9, 206, 20);
            _vue.SetBounds(_searchBox.Left - 12 - LargeurVue, _searchBox.Top, LargeurVue, 36);
            _platform.SetBounds(630, row, etroit ? Math.Max(120, right - 630) : _vue.Left - 642, 36);
            _results.SetBounds(34, row + 52, (etroit ? _vue.Left - 12 : right) - 34, 24);
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
            if (disposing) { ManetteVeille.Geste -= GesteManette; ManetteVeille.Ecoute = false; _tips.Dispose(); _platformMenu.Dispose(); _cardMenu.Dispose(); _vueMenu.Dispose(); }
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
