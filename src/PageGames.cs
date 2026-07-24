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

        // Mode « Espace » : retrie la bibliothèque par espace DORMANT (taille × temps sans jouer)
        // et affiche taille + dernière partie sur chaque tuile, au lieu du simple « DÉTECTÉ ».
        private bool _spaceMode, _spaceLoading;
        private Dictionary<string, DormantGames.Entry> _spaceData;
        private Dictionary<string, int> _spaceRank;
        private string _query = "";
        private string _subtitle = "Analyse des jeux installés…";
        private List<GameScan.GameInfo> _all;
        private bool _loaded;
        private ScrollWheelFilter _wheel;

        public PageGames(DashboardForm host) : base(host)
        {
            Build();
        }

        private void Build()
        {
            _mode = FpsUi.NeonButton("▶  MODE JEU");
            _mode.Width = 150; _mode.Height = 34;
            _mode.Click += (s, e) => ToggleBoost();
            Controls.Add(_mode);

            _prio = FpsUi.GhostButton("⚙  Priorité par jeu");
            _prio.Width = 160; _prio.Height = 34;
            _prio.Click += (s, e) => Host.OpenDialog(new GameProfileForm(Host.Log));
            Controls.Add(_prio);

            _space = FpsUi.GhostButton("🧹  Espace");
            _space.Width = 128; _space.Height = 34;
            _space.Click += (s, e) => ToggleSpace();
            Controls.Add(_space);

            _search = new TextBox();
            try { _search.PlaceholderText = "Rechercher un jeu…"; } catch { }
            _search.BackColor = FpsUi.Card; _search.ForeColor = FpsUi.Ink;
            _search.BorderStyle = BorderStyle.FixedSingle; _search.Font = FpsUi.Small;
            _search.SetBounds(400, 26, 200, 26);
            _search.TextChanged += (s, e) => { _query = _search.Text.Trim().ToLowerInvariant(); if (_all != null) Render(); };
            Controls.Add(_search);

            _flow = new BufferedFlow();      // double-bufferé → défilement fluide, sans scintillement
            _flow.AutoScroll = true;
            _flow.BackColor = Color.Transparent;
            _flow.Padding = new Padding(28, 6, 20, 20);
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
            if (_loaded) { Render(); return; }
            _loaded = true;
            Task.Run(() =>
            {
                List<GameScan.GameInfo> games = new List<GameScan.GameInfo>();
                try { games = GameScan.Known(); GameScan.Detect(games); } catch { }
                try { MergeScanned(games); } catch { }   // + TOUS les jeux installés (scanner générique multi-plateforme)
                try { BeginInvoke((Action)(() => { _all = games; Render(); PrefetchArt(); })); } catch { }
            });
        }

        // Fusionne les jeux RÉELLEMENT installés (toutes plateformes, connus ou pas) détectés par
        // le scanner générique, en plus du catalogue connu. Un jeu Steam hors-catalogue garde son
        // AppID → jaquette officielle quand même. Dédoublonnage par nom normalisé.
        private static void MergeScanned(List<GameScan.GameInfo> games)
        {
            List<GameLibrary.InstalledGame> scanned;
            try { scanned = GameLibrary.ScanAll(); } catch { return; }
            if (scanned == null || scanned.Count == 0) return;

            var byName = new System.Collections.Generic.Dictionary<string, GameScan.GameInfo>();
            foreach (GameScan.GameInfo g in games)
            {
                string k = NormName(g.Name);
                if (k.Length > 0 && !byName.ContainsKey(k)) byName[k] = g;
            }

            foreach (GameLibrary.InstalledGame s in scanned)
            {
                string k = NormName(s.Name);
                if (k.Length == 0) continue;

                GameScan.GameInfo known;
                if (byName.TryGetValue(k, out known))
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
                    continue;
                }

                var added = new GameScan.GameInfo
                {
                    Name = s.Name, Detected = true, SteamId = s.SteamAppId,
                    InstallPath = s.InstallDir, Store = s.Launcher
                };
                games.Add(added);
                byName[k] = added;
            }
        }

        private static string Go(long mb)
        {
            return mb >= 1024 ? (mb / 1024.0).ToString("0.0") + " Go" : mb + " Mo";
        }

        // Texte centré avec ombre portée : garantit la lisibilité par-dessus n'importe quelle
        // jaquette (claire comme sombre), sans dépendre du seul dégradé.
        private static void Shadowed(Graphics gr, string text, Font font, Rectangle rect, Color color)
        {
            var sh = new Rectangle(rect.X + 1, rect.Y + 1, rect.Width, rect.Height);
            TextRenderer.DrawText(gr, text, font, sh, Color.FromArgb(205, 0, 0, 0),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(gr, text, font, rect, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
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
                if (!g.Detected || g.SteamId > 0 || string.IsNullOrEmpty(g.Name)) continue;
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
            if (_space != null) _space.Text = _spaceMode ? "🧹  Espace  ✓" : "🧹  Espace";

            if (_spaceMode && _spaceData == null && !_spaceLoading)
            {
                _spaceLoading = true;
                _subtitle = "Calcul de l'espace occupé… (le premier passage est le plus long, ensuite c'est en cache)";
                Invalidate();
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

        private bool Match(GameScan.GameInfo g)
        {
            if (_query.Length == 0) return true;
            return g.Name != null && g.Name.ToLowerInvariant().Contains(_query);
        }

        private void Render()
        {
            if (_all == null || _flow == null) return;
            _flow.SuspendLayout();
            var old = new Control[_flow.Controls.Count];
            _flow.Controls.CopyTo(old, 0);
            _flow.Controls.Clear();

            var det = new List<GameScan.GameInfo>();
            var other = new List<GameScan.GameInfo>();
            foreach (var g in _all)
            {
                if (!Match(g)) continue;
                if (GameHidden.IsHidden(g.Name)) continue;   // masqué par l'utilisateur (réversible)
                if (g.Detected) det.Add(g); else other.Add(g);
            }

            // Mode Espace : les plus gros dormants en premier (classement calculé par DormantGames).
            if (_spaceMode && _spaceRank != null)
                det.Sort((a, b) => SpaceRank(a).CompareTo(SpaceRank(b)));

            Control last = null;
            if (det.Count > 0)
            {
                AddHeader("DÉTECTÉS SUR CE PC", det.Count, FpsUi.Neon);
                foreach (var g in det) { last = Card(g, true); _flow.Controls.Add(last); }
            }
            if (other.Count > 0)
            {
                if (last != null) _flow.SetFlowBreak(last, true);
                AddHeader(_query.Length > 0 ? "AUTRES RÉSULTATS" : "BIBLIOTHÈQUE", other.Count, FpsUi.Dim);
                foreach (var g in other) _flow.Controls.Add(Card(g, false));
            }

            _flow.ResumeLayout();
            foreach (Control c in old) { try { c.Dispose(); } catch { } }  // libère les handles GDI

            // Démarre tout de suite le chargement des jaquettes des jeux détectés (priorité à ce
            // qui est visible en haut) ; les autres se chargeront à la demande au défilement.
            foreach (var g in det) GameArt.ForGame(g.Name, g.SteamId, null);   // AppID connu OU résolu par le nom

            int shownDet = det.Count;
            if (_query.Length > 0)
                _subtitle = (det.Count + other.Count) + " résultat(s) pour « " + _search.Text.Trim() + " »   ·   " + shownDet + " détecté(s)";
            else if (_spaceMode && _spaceData != null)
            {
                long tot = 0, dorm = 0;
                foreach (GameScan.GameInfo g in det)
                {
                    DormantGames.Entry e = SpaceInfo(g);
                    if (e == null) continue;
                    tot += e.SizeMB;
                    if (e.IsDormant) dorm += e.SizeMB;
                }
                _subtitle = shownDet + " jeu(x) · " + Go(tot) + " au total · " + Go(dorm)
                          + " dormants (3 mois sans y jouer) — triés par espace récupérable";
            }
            else
                _subtitle = shownDet + " jeu(x) détecté(s) sur ton PC (toutes plateformes)";
            Invalidate();
        }

        private void AddHeader(string text, int count, Color col)
        {
            var h = new Label();
            h.Text = text + "   (" + count + ")";
            h.Font = FpsUi.Small; h.ForeColor = col; h.BackColor = Color.Transparent;
            h.AutoSize = false; h.Height = 30;
            h.Width = Math.Max(200, _flow.ClientSize.Width - 60);
            h.TextAlign = ContentAlignment.MiddleLeft;
            h.Margin = new Padding(10, 12, 10, 2);
            _flow.Controls.Add(h);
            _flow.SetFlowBreak(h, true);
        }

        private Control Card(GameScan.GameInfo g, bool detected)
        {
            // Tuile « affiche » verticale (comme la bibliothèque Steam) : jaquette officielle 600x900.
            // (Le flux parent est composité WS_EX_COMPOSITED → pas besoin de double-buffer par carte.)
            var card = new Panel();
            card.Size = new Size(170, 252);
            card.Margin = new Padding(9);
            card.BackColor = Color.Transparent;
            card.Paint += (s, e) =>
            {
                Graphics gr = e.Graphics;
                gr.SmoothingMode = SmoothingMode.AntiAlias;
                gr.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                var rr = new Rectangle(0, 0, ((Panel)s).ClientRectangle.Width - 1, ((Panel)s).ClientRectangle.Height - 1);

                // Jeu sans AppID (Battle.net, EA app, GOG, hors launcher) : on retrouve son AppID
                // Steam par le NOM afin d'afficher la VRAIE jaquette — la plupart des jeux PC
                // existent sur Steam même installés ailleurs. L'index se charge en tâche de fond ;
                // en attendant, la tuile retombe proprement sur l'icône du jeu.
                if (g.SteamId <= 0 && detected)
                {
                    int rid = SteamAppIndex.Resolve(g.Name,
                        () => { try { if (card.IsHandleCreated) card.BeginInvoke((Action)card.Invalidate); } catch { } });
                    if (rid > 0) g.SteamId = rid;
                }

                Image img = g.SteamId > 0
                    ? GameArt.Get(g.SteamId, () => { try { if (card.IsHandleCreated) card.BeginInvoke((Action)card.Invalidate); } catch { } })
                    : null;

                using (var clip = Round(rr, 12))
                {
                    var save = gr.Clip; gr.SetClip(clip);
                    if (img != null)
                    {
                        DrawCover(gr, img, rr);
                        if (!detected) using (var veil = new SolidBrush(Color.FromArgb(155, 9, 11, 10))) gr.FillRectangle(veil, rr);
                        // En mode Espace on affiche 2 lignes (taille + ancienneté) : dégradé plus HAUT
                        // et plus OPAQUE pour rester lisible même sur une jaquette claire (Borderlands…).
                        int sh = _spaceMode ? 74 : 54;
                        int sa = _spaceMode ? 238 : 210;
                        var scrim = new Rectangle(0, rr.Height - sh, rr.Width + 1, sh + 1);
                        using (var lg = new LinearGradientBrush(scrim, Color.FromArgb(0, 9, 11, 10), Color.FromArgb(sa, 9, 11, 10), 90f))
                            gr.FillRectangle(lg, scrim);
                    }
                    else
                    {
                        // Placeholder soigné pour les jeux hors Steam (Riot/Epic/Blizzard…) : dégradé +
                        // pastille ronde à la manette + nom bien lisible (au lieu d'une boîte vide).
                        using (var lg = new LinearGradientBrush(rr, Color.FromArgb(26, 31, 28), Color.FromArgb(12, 15, 13), 90f)) gr.FillRectangle(lg, rr);
                        Color ac = detected ? FpsUi.Neon : FpsUi.Dim2;

                        // Aucune jaquette Steam possible (Battle.net, EA app, jeux hors launcher) :
                        // on affiche l'icône HAUTE RÉSOLUTION du jeu, extraite de son exécutable.
                        // Extraction en tâche de fond → la carte se redessine dès qu'elle est prête.
                        Image ico = detected
                            ? GameIcon.For(g.InstallPath, () => { try { if (card.IsHandleCreated) card.BeginInvoke((Action)card.Invalidate); } catch { } })
                            : null;

                        if (ico != null && ico.Width >= 64)
                        {
                            // Vraie icône 128/256 px : on la montre EN GRAND, comme une vignette.
                            // (Pas de pastille ronde : elle rapetisserait l'illustration pour rien.)
                            int isz = 112;
                            var ir = new Rectangle((rr.Width - isz) / 2, 24, isz, isz);
                            InterpolationMode oldIm = gr.InterpolationMode;
                            gr.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            gr.DrawImage(ico, ir);
                            gr.InterpolationMode = oldIm;
                        }
                        else
                        {
                            // Rien de mieux qu'une petite icône (ou rien) : pastille ronde classique.
                            int d = 78; var circ = new Rectangle((rr.Width - d) / 2, 40, d, d);
                            using (var cb = new SolidBrush(Color.FromArgb(detected ? 34 : 22, ac.R, ac.G, ac.B))) gr.FillEllipse(cb, circ);
                            using (var cp = new Pen(Color.FromArgb(detected ? 130 : 60, ac.R, ac.G, ac.B), 1.5f)) gr.DrawEllipse(cp, circ);

                            if (ico != null)
                            {
                                int isz = Math.Min(48, Math.Max(32, ico.Width));
                                var ir = new Rectangle(circ.X + (circ.Width - isz) / 2, circ.Y + (circ.Height - isz) / 2, isz, isz);
                                InterpolationMode oldIm = gr.InterpolationMode;
                                gr.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                gr.DrawImage(ico, ir);
                                gr.InterpolationMode = oldIm;
                            }
                            else
                            {
                                // GDI (TextRenderer) centre mal les emoji : leurs métriques débordent
                                // de la cellule → glyphe décalé. GDI+ + StringFormat centré = au milieu.
                                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                                using (var gb = new SolidBrush(ac))
                                    gr.DrawString("🎮", FpsUi.GlyphL, gb, (RectangleF)circ, sf);
                            }
                        }
                        TextRenderer.DrawText(gr, g.Name, FpsUi.H3, new Rectangle(10, 138, rr.Width - 20, 68), detected ? FpsUi.Ink : FpsUi.Dim,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                    }
                    gr.Clip = save; save.Dispose();
                }

                // Statut en bas (sur le dégradé).
                DormantGames.Entry sp = (detected && _spaceMode) ? SpaceInfo(g) : null;
                if (sp != null)
                {
                    // Ligne 1 : taille, en gros et blanc. Ligne 2 : ancienneté, colorée par dormance.
                    Shadowed(gr, sp.SizeText, FpsUi.H3, new Rectangle(0, rr.Height - 46, rr.Width, 24), Color.White);
                    Color ic = sp.Never || sp.DaysIdle >= 180 ? Color.FromArgb(255, 120, 120)   // rouge : à libérer
                             : sp.DaysIdle >= 90 ? Color.FromArgb(255, 190, 90)                  // orange : peu joué
                             : sp.DaysIdle < 0 ? Color.FromArgb(170, 176, 186)                   // gris : inconnu
                             : Color.FromArgb(120, 230, 160);                                     // vert : récent
                    Shadowed(gr, "● " + sp.IdleText, FpsUi.Tiny, new Rectangle(0, rr.Height - 22, rr.Width, 16), ic);
                }
                else
                {
                    string tag = detected ? "● DÉTECTÉ" : "non installé";
                    Color tc = detected ? FpsUi.Neon : FpsUi.Dim2;
                    TextRenderer.DrawText(gr, tag, FpsUi.Small, new Rectangle(0, rr.Height - 26, rr.Width, 18), tc,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
                }

                // Pastille du launcher (coin haut-gauche).
                if (detected && !string.IsNullOrEmpty(g.Store))
                {
                    Size ts = TextRenderer.MeasureText(g.Store, FpsUi.Tiny);
                    var chip = new Rectangle(8, 8, ts.Width + 12, 17);
                    using (var b = new SolidBrush(Color.FromArgb(200, 0, 0, 0))) gr.FillRectangle(b, chip);
                    TextRenderer.DrawText(gr, g.Store, FpsUi.Tiny, chip, FpsUi.Neon,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }

                using (var pen = new Pen(detected ? FpsUi.Border : Color.FromArgb(26, 28, 27), 1f))
                using (var bp = Round(rr, 12)) gr.DrawPath(pen, bp);
            };
            var tt = new ToolTip(); tt.SetToolTip(card, g.Name + " — clic pour la fiche détaillée");
            card.Cursor = Cursors.Hand;
            card.Click += (s, e) => OpenDetail(g);
            return card;
        }

        // ------------------------------------------------------------------
        //  Fiche d'un jeu INTÉGRÉE dans la page (pas de fenêtre séparée) : elle
        //  remplace la grille, et son onglet « ‹ BIBLIOTHÈQUE » la ramène.
        //  Astuce WinForms : une Form avec TopLevel = false s'héberge dans un
        //  conteneur — on réutilise toute la fiche sans dupliquer une ligne.
        // ------------------------------------------------------------------
        private Form _detail;

        private void OpenDetail(GameScan.GameInfo g)
        {
            CloseDetail();
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
        }

        private void ShowLibraryChrome(bool on)
        {
            if (_flow != null) _flow.Visible = on;
            if (_search != null) _search.Visible = on;
            if (_mode != null) _mode.Visible = on;
            if (_prio != null) _prio.Visible = on;
            Invalidate();
        }

        // Rectangle à coins arrondis (les 4).
        private static GraphicsPath Round(Rectangle r, int rad)
        {
            var p = new GraphicsPath();
            int d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
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
                _mode.Text = "■  MODE JEU ACTIF";
                _mode.ForeColor = FpsUi.Err;
                _mode.FlatAppearance.BorderColor = FpsUi.Err;
            }
            else
            {
                _mode.Text = "▶  MODE JEU";
                _mode.ForeColor = FpsUi.Neon;
                _mode.FlatAppearance.BorderColor = FpsUi.Neon;
            }
        }

        private void DoLayout()
        {
            if (_flow == null) return;
            int ft = Host != null ? Host.ContentTop(112) : 112;
            _flow.SetBounds(20, ft, ClientSize.Width - 40, ClientSize.Height - ft);
            if (_mode != null) _mode.Location = new Point(ClientSize.Width - 34 - _mode.Width, 22);
            if (_prio != null && _mode != null) _prio.Location = new Point(_mode.Left - 12 - _prio.Width, 22);
            if (_search != null && _prio != null) _search.SetBounds(Math.Max(180, _prio.Left - 12 - 200), 26, 200, 26);
            if (_space != null && _search != null) _space.Location = new Point(Math.Max(20, _search.Left - 12 - _space.Width), 22);
            // Les en-têtes de section suivent la largeur du flux.
            foreach (Control c in _flow.Controls) if (c is Label) c.Width = Math.Max(200, _flow.ClientSize.Width - 60);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_detail != null) return;   // la fiche intégrée dessine son propre en-tête
            PaintTitle(e.Graphics, "BIBLIOTHÈQUE", _subtitle);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _wheel != null) { try { Application.RemoveMessageFilter(_wheel); } catch { } _wheel = null; }
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
