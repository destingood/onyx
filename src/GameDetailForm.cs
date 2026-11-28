using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BTOptimizer
{
    /// <summary>
    /// Fiche détaillée d'un jeu (au clic depuis la page Jeux) : onglets, grande jaquette à droite,
    /// deux niveaux de boost avec leur compte RÉEL à gauche, priorité CPU dédiée (IFEO),
    /// lancer / ouvrir le dossier. Boost léger (réglages recommandés) ou complet (auto-tune matériel).
    /// Tout est confirmé, sauvegardé et réversible.
    /// </summary>
    internal class GameDetailForm : Form
    {
        private readonly GameScan.GameInfo _g;
        private readonly Action<string, int> _log;
        private readonly string[] _exes;
        private Button _prio;
        private bool _hiPrio;

        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int sz);

        public GameDetailForm(GameScan.GameInfo g, Action<string, int> log)
        {
            _g = g; _log = log; _exes = GameScan.ExesFor(g.Name);
            Text = "ONYX — " + g.Name;
            // Mise en page « fiche pleine largeur » : onglets en haut, optimisations à gauche,
            // grande jaquette à droite — comme une page, pas une boîte.
            ClientSize = new Size(1000, 620);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = FpsUi.BgMain; Font = FpsUi.Body; DoubleBuffered = true;
            Disposed += (s, e) => { if (_fondRendu != null) _fondRendu.Dispose(); };
            try { Icon = Logo.MakeIcon(32, FpsUi.Gold); } catch { }

            // Démarre tôt le chargement de la jaquette — y compris pour les jeux SANS AppID
            // (EA, Battle.net…) : ForGame résout d'abord le nom via l'index Steam.
            try { GameArt.ForGame(g.Name, g.SteamId, null); } catch { }
            try { _parties = PageGames.PartiesDe(JournalJeu.Parties, g); } catch { _parties = new List<JournalJeu.Partie>(); }
            _pointsFps = JournalJeu.PointsFps(_parties, 20);
            BuildActions();
            RefreshPrio();
            ChargeFicheSteam();
        }

        /// <summary>Fiche Steam pas encore lue (jeu ouvert avant le scan) : chargée hors du fil
        /// d'interface (réponse en cache 7 jours), puis la fiche se redessine.</summary>
        private void ChargeFicheSteam()
        {
            int id = _g.SteamId;
            if (id <= 0 || FichesSteam.Connue(id)) return;
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    string url = FicheSteam.Url(new[] { id });
                    if (url == null) return;
                    string json = OffresReseau.Texte(url, 7 * 24 * 60).GetAwaiter().GetResult();
                    if (FichesSteam.Memorise(json) > 0 && IsHandleCreated && !IsDisposed) BeginInvoke((Action)Invalidate);
                }
                catch { }
            });
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int v = 1; DwmSetWindowAttribute(Handle, 20, ref v, 4); } catch { }   // barre de titre sombre
        }

        /// <summary>Composite (WS_EX_COMPOSITED) : la fiche ET tous ses enfants sont rendus dans
        /// un seul back-buffer. Sans ça, l'intégration dans la page repeint par morceaux et
        /// laisse des carrés noirs le temps de la transition (même idiome que BufferedFlow).</summary>
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x02000000; return cp; }
        }

        private const int Pad = 40;        // marge de page
        private int CoverW { get { return Math.Max(240, Math.Min(340, ClientSize.Width / 3)); } } //    // largeur de la jaquette (colonne droite)

        private int LeftW { get { return ClientSize.Width - Pad * 2 - CoverW - 32; } }

        private Button _back, _launch, _folder, _close, _hide, _uninstall, _presets, _clips, _succes;
        private CheckBox _fermeSteam;

        private void BuildActions()
        {
            // Onglet « ‹ BIBLIOTHÈQUE » : ramène à la liste.
            _back = OnyxDesign.Button("←  Mes jeux");
            _back.FlatAppearance.BorderSize = 0;
            _back.BackColor = FpsUi.BgMain; _back.ForeColor = FpsUi.Dim;
            _back.Click += (s, e) => Close();
            Controls.Add(_back);


            if (_exes != null)
            {
                _prio = OnyxDesign.Button("Priorité CPU : —");
                _prio.Click += (s, e) => TogglePrio();
                Controls.Add(_prio);
            }

            _launch = OnyxDesign.PrimaryButton("Lancer le jeu  →");
            _launch.Enabled = _g.SteamId > 0 || _g.InstallPath != null;
            _launch.Click += (s, e) => Launch();
            Controls.Add(_launch);

            _folder = OnyxDesign.Button("Ouvrir le dossier");
            _folder.Enabled = _g.InstallPath != null;
            _folder.Click += (s, e) => OpenFolder();
            Controls.Add(_folder);

            // Réglage commun à tous les jeux Steam, coupé par défaut (voir FermeLanceur).
            if (_g.SteamId > 0)
            {
                _fermeSteam = new CheckBox
                {
                    Text = "Fermer Steam 2 min après la fin de la partie (seulement si ONYX l'a ouvert en lançant le jeu)",
                    Checked = FermeLanceur.Actif, ForeColor = FpsUi.Dim, Font = FpsUi.Small, BackColor = Color.Transparent
                };
                _fermeSteam.CheckedChanged += (s, e) => FermeLanceur.Actif = _fermeSteam.Checked;
                Controls.Add(_fermeSteam);
            }

            BuildBoosts();

            IReglagesJeu reglages = string.Equals(_g.Name, "Fortnite", StringComparison.OrdinalIgnoreCase) ? ReglagesFortnite.Pilote
                                  : ReglagesCyberpunk.EstLeJeu(_g.SteamId, _g.Name) ? ReglagesCyberpunk.Pilote
                                  : ReglagesCod.EstLeJeu(_g.SteamId, _g.Name) ? ReglagesCod.Pilote : null;
            if (reglages != null)
            {
                _presets = OnyxDesign.Button("Préréglages graphiques…");
                _presets.Click += (s, e) => { using (var f = new ReglagesJeuForm(reglages)) f.ShowDialog(FindForm()); };
                Controls.Add(_presets);
            }

            _clips = OnyxDesign.Button("Clips du jeu…");
            _clips.Click += (s, e) => { using (var f = new ClipsForm(_g.Name)) f.ShowDialog(FindForm()); };
            Controls.Add(_clips);

            // Succès Steam : lus dans le cache de Steam (aucun réseau). Le bouton n'apparaît
            // que si Steam connaît la liste des succès du jeu ; le compte vient d'un fil à part.
            if (_g.SteamId > 0)
            {
                _succes = OnyxDesign.Button("Succès…");
                _succes.Visible = false;
                _succes.AccessibleName = "Succès Steam du jeu";
                _succes.Click += (s, e) => { using (var f = new SuccesForm(_g.SteamId, _g.Name)) f.ShowDialog(FindForm()); };
                Controls.Add(_succes);
                int app = _g.SteamId;
                // Démarré une fois la fenêtre créée : BeginInvoke échoue avant.
                EventHandler lance = null;
                lance = (s0, e0) => { HandleCreated -= lance; Task.Run(() =>
                {
                    string raison; DateTime? releve;
                    SuccesSteam.Bilan b = SuccesSteam.Charge(app, out raison, out releve);
                    if (b == null) return;
                    try { BeginInvoke((Action)(() => { if (IsDisposed) return; _succes.Text = "Succès " + b.Obtenus + "/" + b.Total + "…"; _succes.Visible = true; })); } catch { }
                }); };
                if (IsHandleCreated) lance(this, EventArgs.Empty); else HandleCreated += lance;
            }

            // Deux actions VOLONTAIREMENT distinctes : masquer ne touche à rien sur le disque,
            // désinstaller délègue au désinstalleur officiel. Ne jamais confondre les deux.
            _hide = OnyxDesign.Button(GameHidden.IsHidden(_g.Name) ? "Réafficher" : "Masquer");
            _hide.Click += (s, e) => ToggleHide();
            Controls.Add(_hide);

            _uninstall = OnyxDesign.Button("Désinstaller…");
            _uninstall.Click += (s, e) => Uninstall();
            Controls.Add(_uninstall);

            _options = OnyxDesign.Button("Options…");
            _options.AccessibleName = "Options du jeu : nom, exécutable, arguments, jaquette";
            _options.Click += (s, e) =>
            {
                using (var f = new OptionsJeuForm(_g)) { f.ShowDialog(FindForm()); if (f.Change) { OptionsChangees = true; Invalidate(true); } }
            };
            Controls.Add(_options);

            // Note personnelle et liens web du jeu : le bouton annonce combien il y en a.
            _notes = OnyxDesign.Button(TexteNotes());
            _notes.AccessibleName = "Notes et liens du jeu";
            _notes.Click += (s, e) =>
            {
                using (var f = new NotesJeuForm(_g)) { f.ShowDialog(FindForm()); if (f.Change) { _notes.Text = TexteNotes(); Invalidate(); } }
            };
            Controls.Add(_notes);

            _close =OnyxDesign.Button("Fermer");
            _close.Click += (s, e) => Close();
            Controls.Add(_close);

            LayoutControls();
            Resize += (s, e) => { LayoutControls(); Invalidate(); };
        }

        /// <summary>Positionne tout d'après la taille COURANTE : la fiche peut ainsi être
        /// affichée en fenêtre ou intégrée dans la page Jeux, à n'importe quelle taille.</summary>
        private void LayoutControls()
        {
            int W = ClientSize.Width, H = ClientSize.Height, lw = LeftW;
            if (lw < 200) return;                       // trop étroit : on ne repositionne pas

            if (_back != null) _back.SetBounds(Pad, 22, 170, 30);

            int cardY = 146, cardW = (lw - 16) / 2;
            if (_cardLight != null) _cardLight.SetBounds(Pad, cardY, cardW, 166);
            if (_cardFull != null) _cardFull.SetBounds(Pad + cardW + 16, cardY, cardW, 166);

            int ay = 448;
            if (_prio != null) _prio.SetBounds(Pad, ay, lw, 34);
            if (_presets != null) _presets.SetBounds(Pad, 402, Math.Min(lw, 240), 32);
            int cx = _presets != null ? Pad + Math.Min(lw, 240) + 12 : Pad;
            int clipsW = _succes != null ? 150 : 180;
            if (_clips != null) _clips.SetBounds(cx, 402, Math.Max(120, Math.Min(clipsW, Pad + lw - cx)), 32);
            if (_succes != null && _clips != null) { int sx = _clips.Right + 12; _succes.SetBounds(sx, 402, Math.Max(100, Pad + lw - sx), 32); }
            int half = (lw - 12) / 2;
            if (_launch != null) _launch.SetBounds(Pad, ay + 42, half, 34);
            if (_folder != null) _folder.SetBounds(Pad + half + 12, ay + 42, half, 34);
            if (_fermeSteam != null)
            {
                int fy = ay + 42 + 34 + 6;
                _fermeSteam.SetBounds(Pad, fy, lw, 24);
                _fermeSteam.Visible = fy + 24 <= H - 66;   // jamais sur la rangée du bas
            }

            // Rangée du bas : cinq boutons qui se partagent la colonne gauche (jamais sous la jaquette).
            int[] parts = { 90, 106, 126, 96, 105 };   // Fermer, Masquer, Désinstaller, Options, Notes
            Button[] bas = { _close, _hide, _uninstall, _options, _notes };
            const int ecart = 8;
            int somme = 0; foreach (int p in parts) somme += p;
            double k = Math.Min(1.25, (lw - ecart * (parts.Length - 1)) / (double)somme);
            int bx = Pad;
            for (int i = 0; i < bas.Length; i++)
            {
                int bw = (int)(parts[i] * k);
                if (bas[i] != null) bas[i].SetBounds(bx, H - 62, bw, 40);
                bx += bw + ecart;
            }
        }

        /// <summary>« Notes · 3 » quand le jeu a une note ou des liens, « Notes… » sinon.</summary>
        private string TexteNotes()
        {
            int n = 0;
            try { n = NotesJeu.De(_g.Name).Nombre; } catch { }
            return n > 0 ? "Notes · " + n : "Notes…";
        }

        // ---- Priorité CPU dédiée (IFEO, comme la page « Priorité par jeu ») ----
        private static bool ExeHi(string exe)
        {
            return Sys.IntEquals(Sys.GetMachine(GameScan.IfeoKey + "\\" + exe + "\\PerfOptions", "CpuPriorityClass"), 3);
        }

        private void RefreshPrio()
        {
            if (_exes == null || _prio == null) return;
            bool hi = true; foreach (var exe in _exes) if (!ExeHi(exe)) { hi = false; break; }
            _hiPrio = hi;
            _prio.Text = hi ? "Priorité CPU : HAUTE ✓  (rétablir)" : "Priorité CPU normale  →  passer en HAUTE";
            _prio.ForeColor = hi ? FpsUi.Gold : FpsUi.Ink;
        }

        private void TogglePrio()
        {
            if (_exes == null) return;
            bool want = !_hiPrio;
            Cursor = Cursors.WaitCursor; _prio.Enabled = false;
            Task.Run(() =>
            {
                foreach (var exe in _exes)
                {
                    string key = GameScan.IfeoKey + "\\" + exe + "\\PerfOptions";
                    try { if (want) Sys.SetMachine(key, "CpuPriorityClass", 3, RegistryValueKind.DWord); else Sys.DelMachine(key, "CpuPriorityClass"); }
                    catch (Exception ex) { if (_log != null) _log(_g.Name + " (" + exe + ") : " + ex.Message, 2); }
                }
                if (_log != null) _log(_g.Name + (want ? " → priorité CPU Haute." : " → priorité CPU normale (rétablie)."), 1);
                try { BeginInvoke((Action)(() => { Cursor = Cursors.Default; _prio.Enabled = true; RefreshPrio(); })); } catch { }
            });
        }

        private void Launch()
        {
            try
            {
                // Options choisies par l'utilisateur : exécutable, ou arguments d'un jeu Steam.
                OptionsJeu.Options opt = OptionsJeu.De(_g.Name);
                if (_g.SteamId > 0) FermeLanceur.NoteLancement(_g.Name);   // Steam ouvert avant ? (fermeture en fin de partie)
                if (OptionsJeu.LanceSiChoisi(_g, opt)) return;
                string args = opt != null ? opt.Arguments : "";
                if (_g.SteamId > 0) { Process.Start(new ProcessStartInfo("steam://run/" + _g.SteamId) { UseShellExecute = true }); return; }

                // Jeux Battle.net : passer par l'URI officielle du launcher est bien plus fiable
                // que de lancer l'exe à la main (Blizzard exige souvent l'agent Battle.net).
                string bnet = BattleNetUri(_g.Name);
                if (bnet != null && string.Equals(_g.Store, "BATTLE.NET", StringComparison.OrdinalIgnoreCase))
                { Process.Start(new ProcessStartInfo(bnet) { UseShellExecute = true }); return; }

                if (_g.InstallPath != null && _exes != null)
                    foreach (var exe in _exes)
                    {
                        string p = Path.Combine(_g.InstallPath, exe);
                        if (File.Exists(p)) { Process.Start(new ProcessStartInfo(p, args) { UseShellExecute = true, WorkingDirectory = opt != null && opt.Dossier.Length > 0 ? opt.Dossier : _g.InstallPath }); return; }
                    }

                // Jeu hors catalogue (repéré par le scanner) : on devine l'exécutable principal.
                string guess = GameLibrary.GuessMainExe(_g.InstallPath);
                if (guess != null)
                {
                    Process.Start(new ProcessStartInfo(guess, args) { UseShellExecute = true, WorkingDirectory = OptionsJeu.DossierDepart(opt != null ? opt.Dossier : "", guess) });
                    return;
                }
                OpenFolder();
            }
            catch (Exception ex) { MessageBox.Show(this, "Impossible de lancer le jeu : " + ex.Message, "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        // URI officielles du launcher Blizzard (codes produits Battle.net).
        private static string BattleNetUri(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            if (n.Contains("world of warcraft")) return "battlenet://WoW";
            if (n.Contains("hearthstone")) return "battlenet://WTCG";
            if (n.Contains("overwatch")) return "battlenet://Pro";
            if (n.Contains("diablo iv") || n.Contains("diablo 4")) return "battlenet://Fen";
            if (n.Contains("diablo iii") || n.Contains("diablo 3")) return "battlenet://D3";
            if (n.Contains("starcraft ii") || n.Contains("starcraft 2")) return "battlenet://S2";
            return null;
        }

        /// <summary>Masque / réaffiche le jeu dans la bibliothèque. Aucun fichier n'est touché.</summary>
        private void ToggleHide()
        {
            bool now = !GameHidden.IsHidden(_g.Name);
            GameHidden.SetHidden(_g.Name, now);
            if (_hide != null) _hide.Text = now ? "Réafficher" : "Masquer";
            if (!now) return;
            MessageBox.Show(this,
                "« " + _g.Name + " » est masqué de ta bibliothèque.\n\n"
                + "Aucun fichier n'a été touché — c'est purement visuel et réversible :\n"
                + "⋯  →  Jeux  →  « Réafficher les jeux masqués ».",
                "Masqué", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>Délègue au helper partagé : une seule implémentation de la désinstallation.</summary>
        private void Uninstall() { GameActions.Uninstall(this, _g.Name, _g.SteamId); }

        private void OpenFolder()
        {
            try { if (_g.InstallPath != null && Directory.Exists(_g.InstallPath)) Process.Start(new ProcessStartInfo(_g.InstallPath) { UseShellExecute = true }); }
            catch { }
        }

        // ------------------------------------------------------------------
        //  Deux niveaux de boost, avec le VRAI nombre d'optimisations de chacun
        //  (compté sur le catalogue de CE PC, jamais un chiffre décoratif).
        // ------------------------------------------------------------------
        private Panel _cardLight, _cardFull;
        private Label _nLight, _nFull;
        private Button _btnLight, _btnFull;

        private void BuildBoosts()
        {
            int w = (LeftW - 16) / 2;
            _cardLight = BoostCard(Pad, 146, w, "BOOST LÉGER", false, out _nLight, out _btnLight);
            _cardFull = BoostCard(Pad + w + 16, 146, w, "BOOST COMPLET", true, out _nFull, out _btnFull);
            Controls.Add(_cardLight); Controls.Add(_cardFull);
            CountBoosts();
        }

        private Panel BoostCard(int x, int y, int w, string title, bool pro, out Label num, out Button btn)
        {
            var card = new Panel { Bounds = new Rectangle(x, y, w, 166), BackColor = Color.Transparent };
            card.Paint += (s, e) => OnyxDesign.PaintCard(e.Graphics, ((Panel)s).ClientRectangle,
                pro ? FpsUi.BlendGold(FpsUi.Card, .06f) : FpsUi.Card, pro ? FpsUi.GoldDim : FpsUi.Border, 12f);

            // Grand nombre (le compte réel), puis le libellé et l'explication.
            num = FpsUi.Text("…", OnyxDesign.Number, FpsUi.Ink);
            num.AutoSize = false; num.SetBounds(20, 16, w - 40, 46);
            var lab = FpsUi.Text("Optimisations", FpsUi.Small, FpsUi.Dim2);
            lab.AutoSize = false; lab.SetBounds(22, 62, w - 44, 18);
            var sub = FpsUi.Text("", FpsUi.Small, pro ? FpsUi.Gold : FpsUi.Dim);
            sub.AutoSize = false; sub.SetBounds(22, 84, w - 44, 34);
            sub.Text = pro ? "Adapté à TON matériel (eSport)." : "Réglages sûrs, tous jeux.";

            btn = pro ? OnyxDesign.AccentButton("BOOST COMPLET") : OnyxDesign.Button("BOOST LÉGER");
            btn.SetBounds(20, 122, w - 40, 34);
            card.Controls.Add(num); card.Controls.Add(lab); card.Controls.Add(sub); card.Controls.Add(btn);
            foreach (Control child in card.Controls) child.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            OnyxDesign.HoverCard(card);

            bool isPro = pro;
            btn.Click += (s, e) => ApplyBoost(isPro);
            return card;
        }

        /// <summary>Compte réel des deux niveaux, en tâche de fond (la détection matérielle
        /// et le catalogue coûtent quelques centaines de ms).</summary>
        private void CountBoosts()
        {
            Task.Run(() =>
            {
                int light = 0, full = 0;
                try { light = Catalog.All().Where(t => t.Recommended).Count(); } catch { }
                try { full = FullBoostList().Count; } catch { }
                try { BeginInvoke((Action)(() =>
                {
                    if (_nLight != null) _nLight.Text = light.ToString();
                    if (_nFull != null) _nFull.Text = full.ToString();
                })); }
                catch { }
            });
        }

        private System.Collections.Generic.List<Tweak> FullBoostList()
        {
            var all = Catalog.All();
            var ids = Hardware.AutoTuneIds(all, Hardware.Detect(), Hardware.LevelAggressive);
            return all.Where(t => ids.Contains(t.Id)).ToList();
        }

        private void ApplyBoost(bool full)
        {
            System.Collections.Generic.List<Tweak> list;
            try { list = full ? FullBoostList() : Catalog.All().Where(t => t.Recommended).ToList(); }
            catch { return; }

            if (MessageBox.Show(this,
                (full ? "Appliquer le BOOST COMPLET" : "Appliquer le BOOST LÉGER")
                + " pour " + _g.Name + " ?\n\n"
                + list.Count + " optimisation(s) seront appliquées.\n"
                + (full ? "Réglage adapté à ton matériel (niveau eSport).\n" : "Réglages sûrs, valables pour tous les jeux.\n")
                + "\nUne sauvegarde du registre et un point de restauration sont créés avant, "
                + "et tout reste réversible depuis la page Optimisations.",
                "ONYX — " + _g.Name, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            Cursor = Cursors.WaitCursor;
            if (_btnLight != null) _btnLight.Enabled = false;
            if (_btnFull != null) _btnFull.Enabled = false;
            Task.Run(() =>
            {
                int n = list.Count;
                try { Engine.Run(list, true, true, false, _log); try { AppStats.Invalidate(); } catch { } }
                catch (Exception ex) { if (_log != null) _log("Boost " + _g.Name + " : " + ex.Message, 2); }
                try { BeginInvoke((Action)(() =>
                {
                    Cursor = Cursors.Default;
                    if (_btnLight != null) _btnLight.Enabled = true;
                    if (_btnFull != null) _btnFull.Enabled = true;
                    MessageBox.Show(this, n + " optimisation(s) appliquée(s). Bon jeu ! 🎮",
                        "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Information);
                })); }
                catch { }
            });
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            int W = ClientSize.Width;
            PeintFond(g);

            // En-tête de la fiche, indépendant de la barre d’outils de la bibliothèque.
            OnyxDesign.Text(g, OptionsJeu.Nom(_g.Name), OnyxDesign.Title, FpsUi.Ink, new Rectangle(Pad + 180, 20, W - Pad * 2 - 180, 34));
            using (var pen = new Pen(FpsUi.Border)) g.DrawLine(pen, Pad, 64, W - Pad, 64);

            // --- Colonne droite : grande jaquette ; plus courte quand le temps de jeu s'affiche dessous ---
            bool joue = _parties != null && _parties.Count > 0;
            // Courbe des FPS sous le temps de jeu : la jaquette raccourcit encore pour lui laisser la place.
            var cover = new Rectangle(W - Pad - CoverW, 84, CoverW, !joue ? 440 : _pointsFps.Count >= 2 ? 210 : 290);
            if (joue) TempsDeJeu(g, cover.X, cover.Bottom + 16, cover.Width);
            Image big = JaquettesPerso.Pour(_g.Name) ?? GameArt.ForGame(_g.Name, _g.SteamId,
                () => { try { if (IsHandleCreated) BeginInvoke((Action)Invalidate); } catch { } });
            using (var clip = Round(cover, 14))
            {
                var save = g.Clip; g.SetClip(clip);
                if (big != null) DrawCover(g, big, cover);
                else
                {
                    using (var b = new SolidBrush(FpsUi.Card)) g.FillRectangle(b, cover);
                    Image icon = GameIcon.For(_g.InstallPath, () => { try { if (IsHandleCreated) BeginInvoke((Action)Invalidate); } catch { } });
                    if (icon != null) g.DrawImage(icon, new Rectangle(cover.X + (cover.Width - 128) / 2, cover.Y + (cover.Height - 128) / 2, 128, 128));
                    else OnyxDesign.IconWell(g, NavIcons.Jeux, new Rectangle(cover.X + (cover.Width - 80) / 2, cover.Y + (cover.Height - 80) / 2, 80, 80));
                }
                g.Clip = save; save.Dispose();
            }
            using (var pen = new Pen(FpsUi.Border)) using (var bp = Round(cover, 14)) g.DrawPath(pen, bp);

            // --- Colonne gauche : titre du bloc + réglages, puis les cartes de boost ---
            TextRenderer.DrawText(g, "Préparer ta session", FpsUi.H2, new Rectangle(Pad, 84, LeftW, 26),
                FpsUi.Ink, TextFormatFlags.NoPrefix);
            string st2 = _g.Detected ? ("● DÉTECTÉ" + (string.IsNullOrEmpty(_g.Store) ? "" : "   ·   " + _g.Store)) : "non installé sur ce PC";
            // Appréciation personnelle (fenêtre « Notes ») : rappelée à côté de l'état du jeu.
            string appreciation = "";
            try { appreciation = NotesJeu.TexteEtoiles(NotesJeu.De(_g.Name).Etoiles); } catch { }
            if (appreciation.Length > 0) st2 += "   ·   ma note " + appreciation;
            try { if (NotesJeu.De(_g.Name).Recommande) st2 += "   ·   je recommande"; } catch { }
            TextRenderer.DrawText(g, st2, FpsUi.Small, new Rectangle(Pad, 114, LeftW, 18),
                _g.Detected ? FpsUi.Gold : FpsUi.Dim, TextFormatFlags.NoPrefix);

            // Conseils FPS SOUS les cartes : l'action passe devant l'explication.
            Section(g, "RÉGLAGES À FAIRE DANS LE JEU", 336, Pad, LeftW);
            string tip = _g.Uncap ?? "Règle la limite d'images sur Illimitée (ou 500) et coupe la V-Sync dans les options du jeu.";
            TextRenderer.DrawText(g, tip, FpsUi.Body, new Rectangle(Pad, 364, LeftW, 34), FpsUi.Ink,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);

            // Fiche Steam (genre, éditeur, sortie, avis) dans l'espace libre sous « Lancer le jeu ».
            List<string> fiche = FicheSteam.Lignes(FichesSteam.De(_g.SteamId));
            if (fiche.Count > 0 && _launch != null)
            {
                int fy = _launch.Bottom + 26, bas = ClientSize.Height - 92;
                if (fy + 48 <= bas)
                {
                    Section(g, "FICHE STEAM", fy, Pad, LeftW);
                    int ly = fy + 28;
                    foreach (string l in fiche)
                    {
                        if (ly + 18 > bas) break;
                        TextRenderer.DrawText(g, l, FpsUi.Small, new Rectangle(Pad, ly, LeftW, 18), FpsUi.Ink,
                            TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                        ly += 21;
                    }
                }
            }

            if (!string.IsNullOrEmpty(_g.InstallPath))
                TextRenderer.DrawText(g, _g.InstallPath, FpsUi.Tiny, new Rectangle(Pad, ClientSize.Height - 84, LeftW, 16),
                    FpsUi.Dim2, TextFormatFlags.NoPrefix | TextFormatFlags.PathEllipsis);
        }

        private readonly List<JournalJeu.Partie> _parties;
        private readonly List<JournalJeu.Partie> _pointsFps;
        private Button _options, _notes;

        /// <summary>Vrai si nom, lancement ou jaquette ont changé : la bibliothèque se redessine.</summary>
        public bool OptionsChangees { get; private set; }

        /// <summary>Temps de jeu relevé par ONYX : 7 jours, 30 jours, total, puis les 5 dernières parties.</summary>
        private void TempsDeJeu(Graphics g, int x, int y, int w)
        {
            Section(g, "TEMPS DE JEU", y, x, w);
            DateTime now = DateTime.Now;
            string semaine = JournalJeu.Duree(JournalJeu.Secondes(_parties, null, now.AddDays(-7)));
            string mois = JournalJeu.Duree(JournalJeu.Secondes(_parties, null, now.AddDays(-30)));
            string total = JournalJeu.Duree(JournalJeu.Secondes(_parties, null, DateTime.MinValue));
            var flags = TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
            TextRenderer.DrawText(g, "7 jours : " + semaine + "   ·   30 jours : " + mois, FpsUi.Small, new Rectangle(x, y + 28, w, 18), FpsUi.Ink, flags);
            TextRenderer.DrawText(g, "Au total : " + total, FpsUi.Small, new Rectangle(x, y + 48, w, 18), FpsUi.Ink, flags);
            int ly = y + 76;
            foreach (JournalJeu.Partie p in JournalJeu.Dernieres(_parties, 5))
            {
                TextRenderer.DrawText(g, JournalJeu.LignePartie(p), FpsUi.Tiny, new Rectangle(x, ly, w, 16), FpsUi.Dim, flags);
                ly += 17;
            }
            // Matériel de la dernière partie mesurée (option « Mesurer chaque partie » du Mode Jeu).
            string materiel = JournalJeu.TexteMateriel(JournalJeu.DerniereMesuree(_parties));
            if (materiel != null)
            {
                TextRenderer.DrawText(g, materiel, FpsUi.Tiny, new Rectangle(x, ly, w, 16), FpsUi.Ink, flags);
                ly += 17;
            }
            if (_pointsFps.Count >= 2) CourbeFps(g, x, ly + 8, w);
        }

        /// <summary>FPS moyens (or) et au plus bas (gris) des dernières parties mesurées, de gauche (ancienne) à droite (récente).</summary>
        private void CourbeFps(Graphics g, int x, int y, int w)
        {
            Section(g, "FPS · " + _pointsFps.Count + " dernières parties mesurées", y, x, w);
            var zone = new Rectangle(x + 34, y + 30, w - 40, Math.Max(30, Math.Min(64, ClientSize.Height - y - 50)));
            double haut = 0, bas = double.MaxValue;
            foreach (JournalJeu.Partie p in _pointsFps)
            {
                haut = Math.Max(haut, p.FpsMoyen);
                bas = Math.Min(bas, p.FpsBas > 0 ? p.FpsBas : p.FpsMoyen);
            }
            if (haut - bas < 10) { haut += 5; bas = Math.Max(0, bas - 5); }
            Func<int, double, PointF> pt = (i, v) => new PointF(
                zone.X + (float)i * zone.Width / (_pointsFps.Count - 1),
                zone.Bottom - (float)((v - bas) / (haut - bas) * zone.Height));
            var flags = TextFormatFlags.NoPrefix | TextFormatFlags.Right;
            TextRenderer.DrawText(g, ((int)haut).ToString(), FpsUi.Tiny, new Rectangle(x, zone.Y - 7, 30, 14), FpsUi.Dim, flags);
            TextRenderer.DrawText(g, ((int)bas).ToString(), FpsUi.Tiny, new Rectangle(x, zone.Bottom - 7, 30, 14), FpsUi.Dim, flags);
            var basses = new List<PointF>();
            var moyennes = new PointF[_pointsFps.Count];
            for (int i = 0; i < _pointsFps.Count; i++)
            {
                moyennes[i] = pt(i, _pointsFps[i].FpsMoyen);
                if (_pointsFps[i].FpsBas > 0) basses.Add(pt(i, _pointsFps[i].FpsBas));
            }
            if (basses.Count >= 2) using (var pen = new Pen(FpsUi.Dim2, 1.5f)) g.DrawLines(pen, basses.ToArray());
            using (var pen = new Pen(FpsUi.Gold, 2f)) g.DrawLines(pen, moyennes);
            using (var b = new SolidBrush(FpsUi.Gold)) foreach (PointF p in moyennes) g.FillEllipse(b, p.X - 2.5f, p.Y - 2.5f, 5, 5);
        }

        private static void Section(Graphics g, string title, int y, int x, int w)
        {
            TextRenderer.DrawText(g, title, FpsUi.Small, new Rectangle(x, y, w, 18), FpsUi.GoldDim, TextFormatFlags.NoPrefix);
            using (var pen = new Pen(FpsUi.Border)) g.DrawLine(pen, x, y + 20, x + w, y + 20);
        }

        private static GraphicsPath Round(Rectangle r, int rad)
        {
            var p = new GraphicsPath(); int d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }

        private Bitmap _fondRendu;
        private Image _fondSource;

        /// <summary>
        /// Fond choisi par l'utilisateur (Options du jeu), sous un voile sombre pour garder le texte
        /// lisible. Mis à l'échelle une fois par taille de fenêtre, pas à chaque peinture.
        /// </summary>
        private void PeintFond(Graphics g)
        {
            Image src = FondsPerso.Pour(_g.Name);
            if (src == null) { if (_fondRendu != null) { _fondRendu.Dispose(); _fondRendu = null; } return; }
            Size taille = ClientSize;
            if (taille.Width < 1 || taille.Height < 1) return;
            if (_fondRendu == null || !ReferenceEquals(_fondSource, src) || _fondRendu.Size != taille)
            {
                if (_fondRendu != null) _fondRendu.Dispose();
                _fondSource = src;
                _fondRendu = new Bitmap(taille.Width, taille.Height);
                var tout = new Rectangle(Point.Empty, taille);
                using (Graphics gb = Graphics.FromImage(_fondRendu))
                {
                    gb.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    DrawCover(gb, src, tout);
                    using (var voile = new LinearGradientBrush(tout, Color.FromArgb(178, FpsUi.BgMain), Color.FromArgb(240, FpsUi.BgMain), 90f))
                        gb.FillRectangle(voile, tout);
                }
            }
            g.DrawImageUnscaled(_fondRendu, 0, 0);
        }

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
    }
}
