using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// 💽 Page STOCKAGE — le centre de gestion de l'espace disque.
    ///
    /// Trois zones, dans l'ordre où la question se pose :
    ///   1. où j'en suis          → jauge par disque, avec la part récupérable montrée dans la barre
    ///   2. ce que je peux libérer → une carte par module, chacune avec son interrupteur
    ///   3. qui prend la place     → applications, jeux dormants, dossiers persos (montrés, jamais touchés)
    ///
    /// Le bouton « TOUT LIBÉRER » ne dépasse JAMAIS le cran de sûreté configuré, et les modules de
    /// niveau « données perso » n'ont pas d'interrupteur du tout : c'est la garantie visible que
    /// rien de personnel ne part dans un lot.
    /// </summary>
    internal class PageStorage : FpsPage
    {
        [DllImport("user32.dll")]
        private static extern bool ShowScrollBar(IntPtr window, int bar, bool show);
        private Panel _scroll;
        private BufferedPanel _drives, _content, _overview, _selectionBar;
        private readonly Button[] _views = new Button[3];
        private Label _empty;
        private ScrollWheelFilter _wheel;
        private int _view;
        private Button _btnFree, _btnConfig, _btnRescan;
        // Le sous-titre de la page EST la ligne d'état : une seule zone de texte, jamais deux
        // messages concurrents sous le titre.
        private string _statusText;

        private StorageSettings _st;
        private List<Storage.Module> _modules = new List<Storage.Module>();
        private List<Storage.DriveView> _drivesData = new List<Storage.DriveView>();
        private readonly HashSet<string> _selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<Control> _cards = new List<Control>();

        private bool _scanned, _busy;
        private bool _rescan;   // une analyse demandée pendant qu'une autre tourne : relancée à la fin

        private const int CardCleanH = 244, CardBrowseH = 272, Gap = 16, PadX = 34;

        public PageStorage(DashboardForm host) : base(host)
        {
            _st = StorageSettings.Load();
            Build();
        }

        public override void OnShown()
        {
            DoLayout();
            if (!_scanned) Scan();
        }

        // ------------------------------------------------------------------
        //  Construction
        // ------------------------------------------------------------------
        private void Build()
        {
            _scroll = new BufferedPanel();
            _scroll.Name = "storage-scroll";
            _scroll.AutoScroll = true;
            _scroll.BackColor = FpsUi.BgMain;
            Controls.Add(_scroll);
            _wheel = new ScrollWheelFilter(_scroll); Application.AddMessageFilter(_wheel);
            _content = new BufferedPanel { BackColor = FpsUi.BgMain }; _scroll.Controls.Add(_content);
            _overview = new BufferedPanel { BackColor = Color.Transparent };
            _overview.Paint += PaintOverview; _content.Controls.Add(_overview);
            _selectionBar = new BufferedPanel { Name = "storage-selection", BackColor = Color.Transparent };
            _selectionBar.Paint += PaintSelection; Controls.Add(_selectionBar);
            string[] views = { "Vue d’ensemble", "Nettoyer", "Explorer" };
            for (int i = 0; i < views.Length; i++)
            {
                int view = i; _views[i] = OnyxDesign.Button(views[i]); _views[i].Name = "storage-view-" + i;
                _views[i].Click += (s, e) => { _view = view; _scroll.AutoScrollPosition = Point.Empty; LayoutCards(); };
                Controls.Add(_views[i]);
            }
            _empty = FpsUi.Text("Analyse en cours…", FpsUi.Body, FpsUi.Dim);
            _empty.AutoSize = false; _content.Controls.Add(_empty);

            _drives = new BufferedPanel();
            _drives.BackColor = Color.Transparent;
            _drives.Paint += PaintDrives;
            _content.Controls.Add(_drives);

            // Pas d'emoji ici : les boutons sont en Ubuntu, qui n'a pas de repli emoji (carré tofu).
            _btnFree = OnyxDesign.PrimaryButton("Vérifier et nettoyer  →");
            _btnFree.Height = 40; _btnFree.Width = 230;
            _btnFree.Click += (s, e) => FreeSelected();
            _selectionBar.Controls.Add(_btnFree);

            _btnConfig = OnyxDesign.Button("Configurer…");
            _btnConfig.Height = 36; _btnConfig.Width = 130;
            _btnConfig.Click += (s, e) => OpenConfig();
            Controls.Add(_btnConfig);

            _btnRescan = OnyxDesign.Button("Analyser à nouveau");
            _btnRescan.Height = 36; _btnRescan.Width = 160;
            _btnRescan.Click += (s, e) => Scan();
            Controls.Add(_btnRescan);

            Resize += (s, e) => DoLayout();
            DoLayout();
        }

        private void DoLayout()
        {
            if (_scroll == null || _selectionBar == null || _btnFree == null) return;
            int top = Host != null ? Host.ContentTop(96) : 96;
            for (int i = 0; i < _views.Length; i++) _views[i].SetBounds(PadX + i * 170, top, 158, 36);
            _selectionBar.SetBounds(PadX, ClientSize.Height - 90, ClientSize.Width - PadX * 2, 72);
            _btnFree.SetBounds(_selectionBar.Width - 248, 16, 230, 40);
            _scroll.SetBounds(PadX, top + 52, ClientSize.Width - PadX - 18, Math.Max(120, _selectionBar.Top - top - 68));
            _btnRescan.SetBounds(ClientSize.Width - PadX - 160, 20, 160, 36);
            _btnConfig.SetBounds(_btnRescan.Left - 142, 20, 130, 36);
            LayoutCards();
        }

        private void LayoutCards()
        {
            if (_scroll == null || _drives == null || _content == null) return;
            int available = Math.Max(280, _scroll.Width - SystemInformation.VerticalScrollBarWidth - 2);
            int columns = available >= 1040 ? 3 : available >= 640 ? 2 : 1;
            int width = (available - (columns - 1) * Gap) / columns;
            _content.SuspendLayout();
            _overview.Visible = _drives.Visible = _view == 0;
            _overview.SetBounds(0, 0, available, 108);
            _drives.SetBounds(0, 128, available, DrivesHeight(available));
            int y = _view == 0 ? _drives.Bottom + 26 : 4;
            foreach (var header in _headers.Values) header.Visible = false;
            int col = 0, shown = 0; string section = null;
            foreach (Control card in _cards)
            {
                var tag = card.Tag as CardTag; if (tag == null) continue;
                bool visible = _view == 0 || (_view == 1 ? tag.Section == "clean" : tag.Section == "browse");
                card.Visible = visible;
                if (!visible) continue;
                if (tag.Section != section)
                {
                    if (section != null) { y += col > 0 ? RowH(section) + Gap : 0; col = 0; }
                    section = tag.Section;
                    var header = HeaderFor(section);
                    if (header != null) { header.Visible = true; header.SetBounds(0, y, available, 24); y += 36; }
                }
                card.SetBounds(col * (width + Gap), y, width, tag.Height);
                shown++; col++;
                if (col == columns) { col = 0; y += tag.Height + Gap; }
            }
            if (col > 0 && section != null) y += RowH(section) + Gap;
            _empty.Visible = shown == 0;
            _empty.Text = _busy ? "L’analyse est en cours. Les catégories apparaissent au fur et à mesure."
                : _view == 1 ? "Aucun élément à nettoyer dans les catégories activées." : "Aucun élément à afficher dans cette vue.";
            _empty.SetBounds(18, y + 16, available - 36, 60);
            _content.Size = new Size(available, y + (shown == 0 ? 100 : 20));
            _content.ResumeLayout();
            int clean = 0, browse = 0;
            foreach (var card in _cards) { var tag = card.Tag as CardTag; if (tag.Section == "clean") clean++; else browse++; }
            _views[1].Text = "Nettoyer · " + clean; _views[2].Text = "Explorer · " + browse;
            for (int i = 0; i < _views.Length; i++)
            {
                _views[i].ForeColor = i == _view ? FpsUi.Gold : FpsUi.Dim;
                _views[i].BackColor = i == _view ? FpsUi.BlendGold(FpsUi.Card, .1f) : FpsUi.Card;
                _views[i].FlatAppearance.BorderSize = i == _view ? 1 : 0;
                _views[i].FlatAppearance.BorderColor = i == _view ? FpsUi.GoldDim : FpsUi.Border;
                _views[i].Invalidate();
            }
            UpdateSelection();
            // La grille est dimensionnée pour la largeur utile : seul le défilement vertical
            // est nécessaire. Retire aussi la barre native résiduelle après changement de vue.
            if (_scroll.IsHandleCreated) ShowScrollBar(_scroll.Handle, 0, false);
        }

        private int RowH(string section) { return section == "clean" ? CardCleanH : CardBrowseH; }

        /// <summary>Fait défiler le panneau (harnais de test visuel : vérifie que les cartes du bas
        /// restent solidaires de leur contenu en défilant).</summary>
        internal int ScrollY { get { return _scroll == null ? 0 : -_scroll.AutoScrollPosition.Y; } }
        internal int ModulesAffiches { get { return _modules.Count; } }
        internal bool Occupe { get { return _busy; } }

        internal void ScrollTo(int y)
        {
            try { if (_scroll != null) _scroll.AutoScrollPosition = new Point(0, Math.Max(0, y)); } catch { }
        }

#if BTTEST
        internal void PreviewView(int view) { _views[view].PerformClick(); }
        internal void ValidateViewportUi()
        {
            if (_scroll.HorizontalScroll.Visible)
                throw new InvalidOperationException("Vue " + _view + " : client=" + _scroll.ClientSize + ", contenu=" + _content.Bounds + ", zone=" + _scroll.DisplayRectangle);
        }
        internal void ValidateStorageUi()
        {
            var selection = new HashSet<string>(_selected);
            long estimate = RecoverableMB(), sum = 0;
            foreach (var item in SelectedItems()) sum += item.SizeMB;
            if (estimate != sum) throw new InvalidOperationException("Estimation différente de la sélection réelle.");
            for (int view = 0; view < 3; view++)
            {
                PreviewView(view);
                foreach (Control card in _cards)
                {
                    var tag = (CardTag)card.Tag;
                    bool expected = view == 0 || (view == 1 ? tag.Section == "clean" : tag.Section == "browse");
                    if (card.Visible != expected) throw new InvalidOperationException("Filtre de stockage incorrect.");
                    if (card.Visible && card.Right > _content.Width) throw new InvalidOperationException("Carte hors de la grille.");
                    if (tag.Section == "browse") foreach (Control child in card.Controls)
                        if (child is ToggleSwitch) throw new InvalidOperationException("Sélection de nettoyage sur une catégorie à explorer.");
                }
                if (!selection.SetEquals(_selected)) throw new InvalidOperationException("La navigation a modifié la sélection.");
            }
            PreviewView(1);
            foreach (Control card in _cards)
            {
                if (!card.Visible) continue;
                foreach (Control child in card.Controls)
                {
                    var toggle = child as ToggleSwitch;
                    if (toggle == null || toggle.Locked) continue;
                    var click = typeof(ToggleSwitch).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    click.Invoke(toggle, new object[] { EventArgs.Empty });
                    if (selection.SetEquals(_selected)) throw new InvalidOperationException("Interrupteur sans effet sur la sélection.");
                    if (_btnFree.Enabled != (SelectedItems().Count > 0)) throw new InvalidOperationException("État du bouton de nettoyage incorrect.");
                    click.Invoke(toggle, new object[] { EventArgs.Empty });
                    if (!selection.SetEquals(_selected) || RecoverableMB() != estimate) throw new InvalidOperationException("Sélection non restaurée.");
                    goto checkedToggle;
                }
            }
            checkedToggle:
            PreviewView(0); ScrollTo(200); int before = ScrollY;
            ShowModules(new List<Storage.Module>(_modules));
            if (ScrollY != before) throw new InvalidOperationException("Défilement perdu après actualisation des cartes.");
            ScrollTo(0);
            if (_scroll.HorizontalScroll.Visible)
                throw new InvalidOperationException("Défilement horizontal : client=" + _scroll.ClientSize + ", contenu=" + _content.Bounds + ", zone=" + _scroll.DisplayRectangle);
            if (_scroll.Bottom > _selectionBar.Top || _btnFree.Right > _selectionBar.Width)
                throw new InvalidOperationException("Barre de sélection hors cadre.");
        }
#endif

        private readonly Dictionary<string, Label> _headers = new Dictionary<string, Label>();

        private Label HeaderFor(string section)
        {
            Label l;
            if (_headers.TryGetValue(section, out l)) return l;
            return null;
        }

        private Label MakeHeader(string section, string text)
        {
            var l = FpsUi.Text(text, FpsUi.H3, FpsUi.Gold);
            l.AutoSize = false;
            _content.Controls.Add(l);
            _headers[section] = l;
            return l;
        }

        private class CardTag
        {
            public string Section;   // « clean » ou « browse »
            public int Height;
        }

        private int DrivesHeight(int width)
        {
            int columns = width >= 1040 ? 4 : width >= 640 ? 2 : 1;
            return 32 + Math.Max(1, (_drivesData.Count + columns - 1) / columns) * 120;
        }

        // ------------------------------------------------------------------
        //  Analyse
        // ------------------------------------------------------------------
        private void Scan()
        {
            // Une fenêtre (désinstallation, corbeille…) peut se fermer pendant l'analyse : ses
            // changements seront pris en compte par une nouvelle analyse, juste après celle-ci.
            if (_busy) { _rescan = true; return; }
            _rescan = false;
            SetBusy(true);
            SetStatus("Analyse du stockage…");
            _scanned = true;
            StorageSettings st = _st;

            // Les cartes arrivent module par module : on repart d'une page vide.
            _modules = new List<Storage.Module>();
            _selected.Clear();
            RebuildCards();

            Task.Run(delegate
            {
                List<Storage.DriveView> drives = new List<Storage.DriveView>();
                try { drives = Storage.Drives(); } catch { }
                try { BeginInvoke((Action)(() => { _drivesData = drives; if (_drives != null) { _drives.Invalidate(); LayoutCards(); } })); }
                catch { }

                List<Storage.Module> mods = new List<Storage.Module>();
                try
                {
                    mods = Storage.ScanAll(st, delegate (string label, int done, int total)
                    {
                        try
                        {
                            if (!IsHandleCreated || label == null) return;
                            BeginInvoke((Action)(() => SetStatus("Analyse… " + label + "  (" + (done + 1) + " / " + total + ")")));
                        }
                        catch { }
                    }, delegate (List<Storage.Module> prets)
                    {
                        // Chaque module prêt s'affiche aussitôt, sans attendre les plus lents.
                        try
                        {
                            if (!IsHandleCreated) return;
                            BeginInvoke((Action)(() => ShowModules(prets)));
                        }
                        catch { }
                    });
                }
                catch { }
                try { BeginInvoke((Action)(() => Populate(mods))); } catch { }
            });
        }

        private void Populate(List<Storage.Module> mods)
        {
            ShowModules(mods ?? new List<Storage.Module>());
            SetBusy(false);
            SetStatus(null);
            Invalidate();
            if (_rescan) Scan();
        }

        /// <summary>Affiche les modules prêts (ordre d'affichage). Un module qui apparaît reçoit la
        /// sélection par défaut ; ceux déjà là gardent ce que l'utilisateur a pu basculer entre-temps.</summary>
        private void ShowModules(List<Storage.Module> prets)
        {
            var deja = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Storage.Module m in _modules) deja.Add(m.Id);

            // Sélection par défaut : tout ce que le cran de sûreté configuré autorise.
            foreach (Storage.Module m in prets)
                if (!deja.Contains(m.Id) && !m.Browse && m.Safety <= _st.MaxSafety && m.HasActions)
                    _selected.Add(m.Id);

            _modules = new List<Storage.Module>(prets);

            // La position de défilement survit à la reconstruction : une carte qui arrive ne fait
            // pas sauter la page sous les yeux de l'utilisateur.
            Point pos = _scroll != null ? _scroll.AutoScrollPosition : Point.Empty;
            if (_scroll != null) _scroll.AutoScrollPosition = Point.Empty;
            RebuildCards();
            if (_scroll != null) _scroll.AutoScrollPosition = new Point(-pos.X, -pos.Y);
            if (_scroll.IsHandleCreated) ShowScrollBar(_scroll.Handle, 0, false);
            Invalidate();
        }

        private void RebuildCards()
        {
            _scroll.SuspendLayout();
            foreach (Control c in _cards) { try { _content.Controls.Remove(c); c.Dispose(); } catch { } }
            _cards.Clear();
            foreach (KeyValuePair<string, Label> kv in _headers) { try { _content.Controls.Remove(kv.Value); kv.Value.Dispose(); } catch { } }
            _headers.Clear();

            var clean = new List<Storage.Module>();
            var browse = new List<Storage.Module>();
            foreach (Storage.Module m in _modules)
            {
                if (!_st.IsEnabled(m.Id)) continue;
                if (m.Browse) { browse.Add(m); continue; }
                if (m.Items.Count == 0) continue;   // rien à récupérer : on n'encombre pas
                clean.Add(m);
            }

            // Les en-têtes ne sont créés que si leur section a du contenu (sinon ils flotteraient
            // en haut du panneau, sans rien sous eux).
            if (clean.Count > 0) MakeHeader("clean", "À NETTOYER  /  " + clean.Count + " catégories");
            foreach (Storage.Module m in clean)
            {
                Control card = MakeCleanCard(m);
                _cards.Add(card);
                _content.Controls.Add(card);
            }

            if (browse.Count > 0) MakeHeader("browse", "À EXPLORER  /  " + browse.Count + " catégories");
            foreach (Storage.Module m in browse)
            {
                Control card = MakeBrowseCard(m);
                _cards.Add(card);
                _content.Controls.Add(card);
            }

            _scroll.ResumeLayout();
            LayoutCards();
        }

        // --- carte « module nettoyable » ---
        private Control MakeCleanCard(Storage.Module module)
        {
            var card = new BufferedPanel { BackColor = Color.Transparent, Tag = new CardTag { Section = "clean", Height = CardCleanH } };
            card.Paint += (s, e) =>
            {
                bool selected = _selected.Contains(module.Id);
                OnyxDesign.PaintCard(e.Graphics, card.ClientRectangle, FpsUi.Card, selected ? OnyxDesign.Edge : FpsUi.Border, 12);
                OnyxDesign.IconWell(e.Graphics, StorageIcon(module.Id), new Rectangle(18, 18, 36, 36), selected);
                OnyxDesign.Text(e.Graphics, module.Name, FpsUi.H3, FpsUi.Ink, new Rectangle(68, 18, card.Width - 144, 44), true);
                OnyxDesign.Text(e.Graphics, "ESTIMATION RÉCUPÉRABLE", FpsUi.Tiny, FpsUi.Dim2, new Rectangle(18, 74, card.Width - 36, 18));
                string size = module.FreeableMB > 0 ? Storage.Human(module.FreeableMB) : module.HasActions ? "Mesuré après nettoyage" : "Rien à récupérer";
                OnyxDesign.Text(e.Graphics, size, module.FreeableMB > 0 ? OnyxDesign.Number : FpsUi.H3, selected ? FpsUi.Gold : FpsUi.Ink, new Rectangle(16, 94, card.Width - 32, 42));
                OnyxDesign.Text(e.Graphics, module.Desc, FpsUi.Body, FpsUi.Dim, new Rectangle(18, 145, card.Width - 36, 60), true);
                using (var pen = new Pen(FpsUi.Border)) e.Graphics.DrawLine(pen, 18, 211, card.Width - 18, 211);
                OnyxDesign.Text(e.Graphics, module.Safety >= Storage.SafeVerifier ? "À vérifier avant nettoyage" : "Fichiers temporaires et caches",
                    FpsUi.Small, module.Safety >= Storage.SafeVerifier ? FpsUi.Warn : FpsUi.Dim2, new Rectangle(18, 220, card.Width - 36, 18));
            };
            var toggle = new ToggleSwitch { Name = "storage-toggle-" + module.Id, AccessibleName = module.Name, Locked = !module.HasActions, On = _selected.Contains(module.Id) };
            card.Controls.Add(toggle);
            card.SizeChanged += (s, e) => toggle.Location = new Point(card.Width - toggle.Width - 18, 20);
            toggle.Toggled += (s, e) =>
            {
                if (toggle.On) _selected.Add(module.Id); else _selected.Remove(module.Id);
                card.Invalidate(); SetStatus(null);
            };
            OnyxDesign.HoverCard(card);
            return card;
        }

        private Control MakeBrowseCard(Storage.Module module)
        {
            var card = new BufferedPanel { BackColor = Color.Transparent, Tag = new CardTag { Section = "browse", Height = CardBrowseH } };
            card.Paint += (s, e) =>
            {
                var g = e.Graphics;
                OnyxDesign.PaintCard(g, card.ClientRectangle, FpsUi.Card, FpsUi.Border, 12);
                OnyxDesign.IconWell(g, StorageIcon(module.Id), new Rectangle(18, 18, 36, 36));
                OnyxDesign.Text(g, module.Name, FpsUi.H3, FpsUi.Ink, new Rectangle(68, 18, card.Width - 86, 44), true);
                OnyxDesign.Text(g, module.Items.Count == 0 ? "—" : Storage.Human(module.SizeMB), OnyxDesign.Number, FpsUi.Ink, new Rectangle(16, 72, card.Width - 32, 40));
                OnyxDesign.Text(g, module.Items.Count + (module.Items.Count == 1 ? " élément repéré" : " éléments repérés"), FpsUi.Small, FpsUi.Dim, new Rectangle(18, 113, card.Width - 36, 20));
                int y = 142, shown = 0;
                foreach (var item in module.Items)
                {
                    if (shown++ == 3) break;
                    OnyxDesign.Text(g, item.Name, FpsUi.Small, FpsUi.Ink, new Rectangle(18, y, card.Width - 122, 20));
                    TextRenderer.DrawText(g, item.SizeText, FpsUi.Small, new Rectangle(card.Width - 96, y, 78, 20), FpsUi.Dim,
                        TextFormatFlags.Right | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                    y += 22;
                }
                if (module.Items.Count == 0) OnyxDesign.Text(g, "Aucun élément au-dessus du seuil.", FpsUi.Small, FpsUi.Dim2, new Rectangle(18, 148, card.Width - 36, 40), true);
            };
            var button = OnyxDesign.Button(BrowseLabel(module)); button.ForeColor = FpsUi.Gold;
            button.Click += (s, e) => OpenBrowse(module); card.Controls.Add(button);
            card.SizeChanged += (s, e) => button.SetBounds(18, card.Height - 50, card.Width - 36, 32);
            OnyxDesign.HoverCard(card);
            return card;
        }

        private static int StorageIcon(string id)
        {
            if (id == "games" || id == "gpu") return NavIcons.Jeux;
            if (id == "apps") return NavIcons.Collection;
            if (id == "winsys" || id == "history") return NavIcons.Systeme;
            return NavIcons.Stockage;
        }

        private static string BrowseLabel(Storage.Module m)
        {
            int n = m.Items.Count;
            switch (m.Id)
            {
                case "apps": return n > 0 ? "Voir les " + n + " applications…" : "Voir les applications…";
                case "games": return "Ouvrir les jeux dormants…";
                case "doublons": return n > 0 ? "Trier les " + n + " doublons…" : "Aucun doublon au-dessus du seuil";
                case "videos": return n > 0 ? "Gérer les " + n + " plus grosses vidéos…" : "Aucune vidéo au-dessus du seuil";
                case "audio": return n > 0 ? "Gérer les " + n + " fichiers audio…" : "Aucun fichier audio au-dessus du seuil";
                case "images": return n > 0 ? "Gérer les " + n + " images…" : "Aucune image au-dessus du seuil";
                case "archives": return n > 0 ? "Gérer les " + n + " archives…" : "Aucune archive au-dessus du seuil";
                case "installers": return n > 0 ? "Gérer les " + n + " installeurs…" : "Aucun installeur au-dessus du seuil";
                case "bigfiles": return n > 0 ? "Gérer les " + n + " gros fichiers…" : "Rien au-dessus du seuil";
                default: return n > 0 ? "Ouvrir « " + m.Items[0].Name + " »…" : "Ouvrir mes dossiers…";
            }
        }

        private void OpenBrowse(Storage.Module m)
        {
            if (m.Id == "apps")
            {
                var apps = new List<Storage.AppEntry>();
                foreach (Storage.StorageItem it in m.Items) { var a = it.Tag as Storage.AppEntry; if (a != null) apps.Add(a); }
                Host.OpenDialog(new StorageAppsForm(Host.Log, apps));
                Scan();   // une désinstallation a pu changer la donne
                return;
            }
            if (m.Id == "games") { Host.OpenDialog(new DormantGamesForm(Host.Log)); Scan(); return; }
            if (m.Id == "doublons")
            {
                if (m.Items.Count == 0) return;
                Host.OpenDialog(new StorageDoublonsForm(Host.Log, new List<Storage.StorageItem>(m.Items)));
                Scan();   // des copies ont pu partir à la corbeille
                return;
            }
            if (IsFileModule(m.Id))
            {
                // La fenêtre reçoit les éléments déjà balayés (pas de re-balayage), puis la page
                // se remet à jour : des fichiers ont pu partir à la corbeille.
                Host.OpenDialog(new StorageFilesForm(Host.Log, new List<Storage.StorageItem>(m.Items), m.Id));
                Scan();
                return;
            }
            if (m.Items.Count > 0 && m.Items[0].Open != null) m.Items[0].Open();
        }

        private static bool IsFileModule(string id)
        {
            return id == "videos" || id == "audio" || id == "images" || id == "archives" || id == "installers" || id == "bigfiles";
        }

        private static string Trim(string s, int max)
        {
            s = s ?? "";
            return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
        }

        // ------------------------------------------------------------------
        //  Libération
        // ------------------------------------------------------------------
        private List<Storage.StorageItem> SelectedItems()
        {
            var list = new List<Storage.StorageItem>();
            foreach (Storage.Module m in _modules)
            {
                if (m.Browse || !_selected.Contains(m.Id)) continue;
                foreach (Storage.StorageItem it in m.Items)
                    if (it.Free != null && it.Safety <= Storage.SafeVerifier) list.Add(it);
            }
            return list;
        }

        private void FreeSelected()
        {
            if (_busy) return;
            List<Storage.StorageItem> items = SelectedItems();
            if (items.Count == 0)
            {
                MessageBox.Show(FindForm(), "Aucun module sélectionné — active au moins un interrupteur.",
                    "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            long est = 0; int deep = 0;
            foreach (Storage.StorageItem it in items) { est += it.SizeMB; if (it.Safety >= Storage.SafeVerifier) deep++; }

            var msg = new System.Text.StringBuilder();
            msg.Append("Libérer ~").Append(Storage.Human(est)).Append(" ?\n\n");
            msg.Append(items.Count).Append(" élément(s) :\n");
            int n = 0;
            foreach (Storage.StorageItem it in items)
            {
                if (n++ >= 8) { msg.Append("  • … et ").Append(items.Count - 8).Append(" autre(s)\n"); break; }
                msg.Append("  • ").Append(it.Name).Append("  (").Append(it.SizeText).Append(")\n");
            }
            if (deep > 0) msg.Append("\n⚠ ").Append(deep).Append(" élément(s) de niveau « à vérifier » : historique, corbeille ou réglage Windows.");
            msg.Append("\n\nTes fichiers personnels, tes jeux et tes sauvegardes ne sont pas touchés.");

            if (MessageBox.Show(FindForm(), msg.ToString(), "ONYX — libérer de l'espace",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            SetBusy(true);
            SetStatus("Libération en cours…");
            int maxSafety = _st.MaxSafety;
            foreach (Storage.StorageItem it in items) if (it.Safety > maxSafety) maxSafety = it.Safety;   // choix explicite de l'utilisateur

            Task.Run(delegate
            {
                long freed = 0;
                try { freed = Storage.FreeAll(items, Math.Min(maxSafety, Storage.SafeVerifier), Host.Log); } catch { }
                long done = freed;
                try { BeginInvoke((Action)(() => { AfterFree(done); })); } catch { }
            });
        }

        private void AfterFree(long freedMB)
        {
            SetBusy(false);
            MessageBox.Show(FindForm(),
                freedMB > 0
                    ? "✨ " + Storage.Human(freedMB) + " libérés.\n\nJ'ai remesuré après coup : c'est le gain réel, pas une estimation."
                    : "Le nettoyage est passé, mais le gain mesuré est négligeable.\nCertains fichiers étaient verrouillés par Windows — réessaie après un redémarrage.",
                "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Scan();
        }

        private void OpenConfig()
        {
            using (var f = new StorageConfigForm(_st))
            {
                AnimFx.HookDialog(f);
                if (f.ShowDialog(FindForm()) != DialogResult.OK) return;
            }
            _st = StorageSettings.Load();
            Scan();
        }


        private void SetBusy(bool busy)
        {
            _busy = busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            UpdateSelection();
            if (_btnRescan != null) _btnRescan.Enabled = !busy;
            if (_btnConfig != null) _btnConfig.Enabled = !busy;
        }

        // ------------------------------------------------------------------
        //  Peinture
        // ------------------------------------------------------------------
        private void PaintOverview(object sender, PaintEventArgs e)
        {
            var g = e.Graphics; int cell = _overview.Width / 3;
            OnyxDesign.PaintCard(g, _overview.ClientRectangle, Color.FromArgb(27, 25, 20), OnyxDesign.Edge, 12);
            long free = 0, total = 0;
            foreach (var drive in _drivesData) { free += drive.FreeMB; total += drive.TotalMB; }
            string[] labels = { "ESPACE DISPONIBLE", "ESPACE OCCUPÉ", "NETTOYAGE SÉLECTIONNÉ" };
            string[] values = { _drivesData.Count == 0 ? "—" : Storage.Human(free), _drivesData.Count == 0 ? "—" : Storage.Human(total - free), _modules.Count == 0 && _busy ? "…" : Storage.Human(RecoverableMB()) };
            string[] notes = { _drivesData.Count + (_drivesData.Count == 1 ? " volume détecté" : " volumes détectés"), "sur " + Storage.Human(total) + " de capacité", "Estimation avant nettoyage" };
            for (int i = 0; i < 3; i++)
            {
                int x = cell * i + 20;
                OnyxDesign.Text(g, labels[i], FpsUi.Tiny, FpsUi.Dim, new Rectangle(x, 16, cell - 40, 18));
                OnyxDesign.Text(g, values[i], OnyxDesign.Number, i == 2 ? FpsUi.Gold : FpsUi.Ink, new Rectangle(x - 2, 35, cell - 36, 42));
                OnyxDesign.Text(g, notes[i], FpsUi.Small, FpsUi.Dim2, new Rectangle(x, 82, cell - 40, 18));
                if (i > 0) using (var pen = new Pen(OnyxDesign.Edge)) g.DrawLine(pen, cell * i, 20, cell * i, 88);
            }
        }

        private void PaintDrives(object sender, PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            OnyxDesign.Text(g, "TES VOLUMES", FpsUi.H3, FpsUi.Ink, new Rectangle(0, 0, _drives.Width, 24));
            if (_drivesData.Count == 0)
            {
                OnyxDesign.Text(g, _busy ? "Lecture des disques…" : "Aucun disque fixe lisible.", FpsUi.Body, FpsUi.Dim, new Rectangle(18, 52, _drives.Width - 36, 30)); return;
            }
            int columns = _drives.Width >= 1040 ? 4 : _drives.Width >= 640 ? 2 : 1;
            int width = (_drives.Width - (columns - 1) * 12) / columns;
            for (int i = 0; i < _drivesData.Count; i++)
            {
                var drive = _drivesData[i]; int x = (i % columns) * (width + 12), y = 32 + (i / columns) * 120;
                Color level = drive.Level == 2 ? FpsUi.Err : drive.Level == 1 ? FpsUi.Warn : FpsUi.Gold;
                OnyxDesign.PaintCard(g, new Rectangle(x, y, width, 108), FpsUi.Card, drive.IsSystem ? OnyxDesign.Edge : FpsUi.Border, 12);
                var ring = new Rectangle(x + 16, y + 19, 48, 48);
                using (var pen = new Pen(FpsUi.Border, 4)) g.DrawEllipse(pen, ring);
                float used = drive.TotalMB > 0 ? (float)drive.UsedMB / drive.TotalMB : 0;
                using (var pen = new Pen(level, 4) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    if (used > 0) g.DrawArc(pen, ring, -90, Math.Min(1, used) * 360);
                TextRenderer.DrawText(g, (100 - drive.FreePct) + "%", FpsUi.Small, ring, FpsUi.Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                OnyxDesign.Text(g, "occupé", FpsUi.Tiny, FpsUi.Dim2, new Rectangle(x + 18, y + 78, 50, 18));
                OnyxDesign.Text(g, drive.Letter + (drive.IsSystem ? " · Système" : " · " + drive.Label), FpsUi.H3, FpsUi.Ink, new Rectangle(x + 82, y + 18, width - 98, 24));
                OnyxDesign.Text(g, Storage.Human(drive.FreeMB) + " libres", FpsUi.H3, level, new Rectangle(x + 82, y + 47, width - 98, 22));
                OnyxDesign.Text(g, "sur " + Storage.Human(drive.TotalMB), FpsUi.Small, FpsUi.Dim, new Rectangle(x + 82, y + 77, width - 98, 18));
            }
        }

        private void PaintSelection(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            OnyxDesign.PaintCard(g, _selectionBar.ClientRectangle, FpsUi.Card, OnyxDesign.Edge, 12);
            int count = SelectedItems().Count;
            string title = _busy ? "Analyse ou nettoyage en cours…" : count == 0 ? "Choisis les catégories à nettoyer" : Storage.Human(RecoverableMB()) + " estimés · " + count + (count == 1 ? " élément sélectionné" : " éléments sélectionnés");
            OnyxDesign.Text(g, title, FpsUi.H3, FpsUi.Ink, new Rectangle(20, 14, _btnFree.Left - 36, 24));
            OnyxDesign.Text(g, _busy ? _statusText : "Le détail de la sélection est présenté avant le nettoyage.", FpsUi.Small, FpsUi.Dim, new Rectangle(20, 43, _btnFree.Left - 36, 20));
        }

        private void UpdateSelection()
        {
            if (_btnFree != null) _btnFree.Enabled = !_busy && SelectedItems().Count > 0;
            if (_overview != null) _overview.Invalidate();
            if (_selectionBar != null) _selectionBar.Invalidate();
        }

        private long RecoverableMB()
        {
            long mb = 0;
            foreach (Storage.Module m in _modules)
                if (!m.Browse && _selected.Contains(m.Id)) mb += m.FreeableMB;
            return mb;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            PaintPremiumTitle(e.Graphics, "STOCKAGE", Subtitle());
        }

        private void SetStatus(string s)
        {
            _statusText = s;
            UpdateSelection();
            Invalidate();   // le sous-titre est peint, pas posé dans un Label
        }

        private string Subtitle()
        {
            if (_busy) return _statusText;
            return "Tes disques, l’espace récupérable et les fichiers qui prennent de la place.";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _wheel != null) { Application.RemoveMessageFilter(_wheel); _wheel = null; }
            base.Dispose(disposing);
        }
    }
}
