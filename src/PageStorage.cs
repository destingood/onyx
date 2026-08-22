using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
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
        private Panel _scroll;
        private BufferedPanel _drives;
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

        private const int CardW = 356, CardCleanH = 152, CardBrowseH = 196, Gap = 16, PadX = 34;

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
            _scroll = new Panel();
            _scroll.AutoScroll = true;
            _scroll.BackColor = Color.Transparent;
            Controls.Add(_scroll);

            _drives = new BufferedPanel();
            _drives.BackColor = Color.Transparent;
            _drives.Paint += PaintDrives;
            _scroll.Controls.Add(_drives);

            // Pas d'emoji ici : les boutons sont en Ubuntu, qui n'a pas de repli emoji (carré tofu).
            _btnFree = FpsUi.GoldButton("TOUT LIBÉRER");
            _btnFree.Height = 30; _btnFree.Width = 168;
            _btnFree.Click += (s, e) => FreeSelected();
            Controls.Add(_btnFree);

            _btnConfig = FpsUi.GhostButton("⚙ Configurer…");
            _btnConfig.Height = 30; _btnConfig.Width = 118;
            _btnConfig.Click += (s, e) => OpenConfig();
            Controls.Add(_btnConfig);

            _btnRescan = FpsUi.GhostButton("↻ Analyser");
            _btnRescan.Height = 30; _btnRescan.Width = 96;
            _btnRescan.Click += (s, e) => Scan();
            Controls.Add(_btnRescan);

            Resize += (s, e) => DoLayout();
            DoLayout();
        }

        private void DoLayout()
        {
            if (_scroll == null) return;
            int top = Host != null ? Host.ContentTop(96) : 96;
            int bottom = Host != null ? Host.ContentBottom(12) : ClientSize.Height - 12;
            _scroll.SetBounds(PadX - 14, top, Math.Max(200, ClientSize.Width - (PadX - 14) - 20), Math.Max(120, bottom - top));

            if (_btnRescan != null)
            {
                int rx = ClientSize.Width - PadX;
                _btnRescan.Location = new Point(rx - _btnRescan.Width, 22); rx -= _btnRescan.Width + 8;
                _btnConfig.Location = new Point(rx - _btnConfig.Width, 22); rx -= _btnConfig.Width + 8;
                _btnFree.Location = new Point(rx - _btnFree.Width, 22);
            }
            LayoutCards();
        }

        // Grille fluide : autant de colonnes que la largeur en autorise, jamais moins d'une.
        private void LayoutCards()
        {
            if (_scroll == null || _drives == null) return;
            // La largeur de l'ascenseur vertical est réservée D'AVANCE : sinon la première mise en
            // page (sans ascenseur) déborde dès qu'il apparaît, et un ascenseur HORIZONTAL surgit.
            int avail = Math.Max(CardW, _scroll.Width - 24 - SystemInformation.VerticalScrollBarWidth);
            int cols = Math.Max(1, (avail + Gap) / (CardW + Gap));

            _drives.SetBounds(12, 4, Math.Min(avail, cols * (CardW + Gap) - Gap), DrivesHeight());
            int y = _drives.Bottom + 22;
            int col = 0;
            string section = null;

            foreach (Control c in _cards)
            {
                var tag = c.Tag as CardTag;
                if (tag == null) continue;
                if (tag.Section != section)
                {
                    if (section != null) { y += (col > 0 ? RowH(section) + Gap : 0); col = 0; }
                    section = tag.Section;
                    var hdr = HeaderFor(section);
                    if (hdr != null) { hdr.SetBounds(14, y, avail, 22); y += 30; }
                }
                c.SetBounds(12 + col * (CardW + Gap), y, CardW, tag.Height);
                col++;
                if (col >= cols) { col = 0; y += tag.Height + Gap; }
            }
            if (col > 0 && section != null) y += RowH(section) + Gap;
            // Marge de respiration en bas (sinon la dernière rangée colle au bord du défilement).
            _scroll.AutoScrollMinSize = new Size(0, y + 20);
        }

        private int RowH(string section) { return section == "clean" ? CardCleanH : CardBrowseH; }

        /// <summary>Fait défiler le panneau (harnais de test visuel : vérifie que les cartes du bas
        /// restent solidaires de leur contenu en défilant).</summary>
        internal void ScrollTo(int y)
        {
            try { if (_scroll != null) _scroll.AutoScrollPosition = new Point(0, Math.Max(0, y)); } catch { }
        }

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
            _scroll.Controls.Add(l);
            _headers[section] = l;
            return l;
        }

        private class CardTag
        {
            public string Section;   // « clean » ou « browse »
            public int Height;
        }

        private int DrivesHeight()
        {
            int n = _drivesData.Count;
            if (n == 0) n = 1;
            return 44 + n * 52 + 12;
        }

        // ------------------------------------------------------------------
        //  Analyse
        // ------------------------------------------------------------------
        private void Scan()
        {
            if (_busy) return;
            SetBusy(true);
            SetStatus("Analyse du stockage…");
            _scanned = true;
            StorageSettings st = _st;

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
                    });
                }
                catch { }
                try { BeginInvoke((Action)(() => Populate(mods))); } catch { }
            });
        }

        private void Populate(List<Storage.Module> mods)
        {
            _modules = mods ?? new List<Storage.Module>();

            // Sélection par défaut : tout ce que le cran de sûreté configuré autorise.
            _selected.Clear();
            foreach (Storage.Module m in _modules)
                if (!m.Browse && m.Safety <= _st.MaxSafety && m.HasActions && Allowed(m))
                    _selected.Add(m.Id);

            RebuildCards();
            SetBusy(false);
            SetStatus(null);
            Invalidate();
        }

        private bool Allowed(Storage.Module m) { return !m.Pro || License.ProUnlocked; }

        private void RebuildCards()
        {
            _scroll.SuspendLayout();
            foreach (Control c in _cards) { try { _scroll.Controls.Remove(c); c.Dispose(); } catch { } }
            _cards.Clear();
            foreach (KeyValuePair<string, Label> kv in _headers) { try { _scroll.Controls.Remove(kv.Value); kv.Value.Dispose(); } catch { } }
            _headers.Clear();

            var clean = new List<Storage.Module>();
            var browse = new List<Storage.Module>();
            foreach (Storage.Module m in _modules)
            {
                if (!_st.IsEnabled(m.Id)) continue;
                if (m.Browse) { browse.Add(m); continue; }
                if (m.Items.Count == 0 && Allowed(m)) continue;   // rien à récupérer : on n'encombre pas
                clean.Add(m);
            }

            // Les en-têtes ne sont créés que si leur section a du contenu (sinon ils flotteraient
            // en haut du panneau, sans rien sous eux).
            if (clean.Count > 0) MakeHeader("clean", "LIBÉRER DE L'ESPACE");
            foreach (Storage.Module m in clean)
            {
                Control card = MakeCleanCard(m);
                _cards.Add(card);
                _scroll.Controls.Add(card);
            }

            if (browse.Count > 0) MakeHeader("browse", "QUI PREND LA PLACE ?");
            foreach (Storage.Module m in browse)
            {
                Control card = MakeBrowseCard(m);
                _cards.Add(card);
                _scroll.Controls.Add(card);
            }

            _scroll.ResumeLayout();
            LayoutCards();
        }

        // --- carte « module nettoyable » ---
        private Control MakeCleanCard(Storage.Module m)
        {
            bool locked = !Allowed(m);
            var card = new Panel();
            card.BackColor = Color.Transparent;
            card.Tag = new CardTag { Section = "clean", Height = CardCleanH };
            card.Paint += (s, e) => FpsUi.PaintCard(e.Graphics, ((Panel)s).ClientRectangle, FpsUi.Card, FpsUi.Border, 12f);

            var glyph = FpsUi.Text(m.Glyph, FpsUi.GlyphL, locked ? FpsUi.Dim2 : FpsUi.Ink);
            glyph.AutoSize = false; glyph.SetBounds(14, 12, 48, 48);
            glyph.TextAlign = ContentAlignment.MiddleCenter;
            card.Controls.Add(glyph);

            var name = FpsUi.Text(m.Name, FpsUi.H3, FpsUi.Ink);
            name.AutoSize = false; name.SetBounds(66, 16, 210, 34);
            card.Controls.Add(name);

            bool big = !locked && m.FreeableMB > 0;
            string sizeTxt;
            // Pas d'emoji cadenas : la police des titres (Ubuntu) n'a pas de repli emoji (tofu).
            if (locked) sizeTxt = "réservé à Pro";
            else if (m.FreeableMB > 0) sizeTxt = Storage.Human(m.FreeableMB);
            else if (m.HasActions) sizeTxt = "gain mesuré après coup";
            else sizeTxt = "rien à récupérer";
            var size = FpsUi.Text(sizeTxt, big ? FpsUi.Num : FpsUi.H3, locked ? FpsUi.Dim2 : FpsUi.Ink);
            size.AutoSize = false; size.SetBounds(66, 50, 210, 30);
            card.Controls.Add(size);

            // Les bandes ne se CHEVAUCHENT pas : deux labels transparents superposés se masquent
            // l'un l'autre (le dernier ajouté passe derrière) — c'est ce qui rognait le badge.
            var desc = FpsUi.Text(m.Desc, FpsUi.Small, FpsUi.Dim);
            desc.AutoSize = false; desc.SetBounds(16, 84, CardW - 32, 36);
            card.Controls.Add(desc);

            if (m.Safety >= Storage.SafeVerifier)
            {
                // Bornes EXPLICITES : en AutoSize, la hauteur calculée pour Inter (police chargée
                // depuis un fichier) est trop courte d'environ 3 px et rogne le haut des lettres —
                // les accents et les hampes disparaissaient (« a verifier » au lieu d'« à vérifier »).
                // Pas de « ⚠ » non plus : l'orange suffit à alerter, et le symbole force un repli de police.
                var badge = FpsUi.Text("à vérifier — décoché par défaut", FpsUi.Small, FpsUi.Warn);
                badge.AutoSize = false;
                badge.SetBounds(16, 124, CardW - 32, 20);
                card.Controls.Add(badge);
            }

            var tog = new ToggleSwitch();
            tog.Location = new Point(CardW - 62, 18);
            tog.Locked = locked || !m.HasActions;
            tog.On = !locked && _selected.Contains(m.Id);
            Storage.Module cap = m;
            tog.Toggled += delegate
            {
                if (tog.On) _selected.Add(cap.Id); else _selected.Remove(cap.Id);
                SetStatus(null);
            };
            if (locked)
            {
                tog.Click += delegate { if (Pro("Windows en profondeur")) Scan(); };
                var lockTip = new ToolTip();
                lockTip.SetToolTip(tog, "Réservé à ONYX Pro");
            }
            card.Controls.Add(tog);
            return card;
        }

        // --- carte « qui prend la place » ---
        private Control MakeBrowseCard(Storage.Module m)
        {
            var card = new Panel();
            card.BackColor = Color.Transparent;
            card.Tag = new CardTag { Section = "browse", Height = CardBrowseH };
            card.Paint += (s, e) => FpsUi.PaintCard(e.Graphics, ((Panel)s).ClientRectangle, FpsUi.Card, FpsUi.Border, 12f);

            var glyph = FpsUi.Text(m.Glyph, FpsUi.GlyphL, FpsUi.Ink);
            glyph.AutoSize = false; glyph.SetBounds(16, 14, 44, 44);
            glyph.TextAlign = ContentAlignment.MiddleCenter;
            card.Controls.Add(glyph);

            var name = FpsUi.Text(m.Name, FpsUi.H3, FpsUi.Ink);
            name.AutoSize = false; name.SetBounds(66, 14, 220, 34);
            card.Controls.Add(name);

            var size = FpsUi.Text(m.Items.Count == 0 ? "—" : Storage.Human(m.SizeMB) + "  ·  " + m.Items.Count + " élément(s)",
                                  FpsUi.Small, FpsUi.Dim);
            size.AutoSize = false; size.SetBounds(66, 46, 260, 18);
            card.Controls.Add(size);

            // Aperçu : les trois plus gros, tout de suite lisibles, sans ouvrir quoi que ce soit.
            int y = 74;
            int shown = 0;
            foreach (Storage.StorageItem it in m.Items)
            {
                if (shown >= 3) break;
                var line = FpsUi.Text("• " + Trim(it.Name, 34), FpsUi.Small, FpsUi.Ink);
                line.AutoSize = false; line.SetBounds(16, y, CardW - 110, 18);
                card.Controls.Add(line);
                var mb = FpsUi.Text(it.SizeText, FpsUi.Small, FpsUi.Dim);
                mb.AutoSize = false; mb.SetBounds(CardW - 96, y, 80, 18);
                mb.TextAlign = ContentAlignment.MiddleRight;
                card.Controls.Add(mb);
                y += 22; shown++;
            }
            if (shown == 0)
            {
                var none = FpsUi.Text("Rien de notable ici — c'est plutôt bon signe.", FpsUi.Small, FpsUi.Dim2);
                none.AutoSize = false; none.SetBounds(16, y, CardW - 32, 18);
                card.Controls.Add(none);
            }

            var btn = FpsUi.GhostButton(BrowseLabel(m));
            btn.SetBounds(16, CardBrowseH - 44, CardW - 32, 30);
            btn.ForeColor = FpsUi.Gold;
            Storage.Module cap = m;
            btn.Click += (s, e) => OpenBrowse(cap);
            card.Controls.Add(btn);
            return card;
        }

        private static string BrowseLabel(Storage.Module m)
        {
            int n = m.Items.Count;
            switch (m.Id)
            {
                case "apps": return n > 0 ? "Voir les " + n + " applications…" : "Voir les applications…";
                case "games": return "Ouvrir les jeux dormants…";
                case "videos": return n > 0 ? "Gérer les " + n + " plus grosses vidéos…" : "Aucune vidéo au-dessus du seuil";
                case "archives": return n > 0 ? "Gérer les " + n + " archives…" : "Aucune archive au-dessus du seuil";
                case "bigfiles": return n > 0 ? "Gérer les " + n + " gros fichiers…" : "Rien au-dessus du seuil";
                default: return n > 0 ? "Ouvrir « " + m.Items[0].Name + " »…" : "Ouvrir mes dossiers…";
            }
        }

        private void OpenBrowse(Storage.Module m)
        {
            if (m.Id == "apps")
            {
                if (!Pro("Inventaire complet des applications")) return;
                var apps = new List<Storage.AppEntry>();
                foreach (Storage.StorageItem it in m.Items) { var a = it.Tag as Storage.AppEntry; if (a != null) apps.Add(a); }
                Host.OpenDialog(new StorageAppsForm(Host.Log, apps));
                Scan();   // une désinstallation a pu changer la donne
                return;
            }
            if (m.Id == "games") { Host.OpenDialog(new DormantGamesForm(Host.Log)); Scan(); return; }
            if (m.Id == "videos" || m.Id == "archives" || m.Id == "bigfiles")
            {
                // La fenêtre reçoit les éléments déjà balayés (pas de re-balayage), puis la page
                // se remet à jour : des fichiers ont pu partir à la corbeille.
                Host.OpenDialog(new StorageFilesForm(Host.Log, new List<Storage.StorageItem>(m.Items)));
                Scan();
                return;
            }
            if (m.Items.Count > 0 && m.Items[0].Open != null) m.Items[0].Open();
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
                if (m.Browse || !_selected.Contains(m.Id) || !Allowed(m)) continue;
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

        private bool Pro(string feat)
        {
            if (License.ProUnlocked) return true;
            using (var f = new LicenseKeyForm(feat)) f.ShowDialog(FindForm());
            return License.ProUnlocked;
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            if (_btnFree != null) _btnFree.Enabled = !busy;
            if (_btnRescan != null) _btnRescan.Enabled = !busy;
            if (_btnConfig != null) _btnConfig.Enabled = !busy;
        }

        // ------------------------------------------------------------------
        //  Peinture
        // ------------------------------------------------------------------
        private void PaintDrives(object sender, PaintEventArgs e)
        {
            var p = (Panel)sender;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            FpsUi.PaintCard(g, p.ClientRectangle, FpsUi.Card, FpsUi.Border, 12f);

            TextRenderer.DrawText(g, "TES DISQUES", FpsUi.H3, new Point(18, 14), FpsUi.Gold, TextFormatFlags.NoPadding);

            if (_drivesData.Count == 0)
            {
                TextRenderer.DrawText(g, _scanned ? "Aucun disque fixe lisible." : "Lecture des disques…",
                    FpsUi.Small, new Point(18, 46), FpsUi.Dim, TextFormatFlags.NoPadding);
                return;
            }

            long recoverable = RecoverableMB();
            int y = 44;
            foreach (Storage.DriveView d in _drivesData)
            {
                Color lvl = d.Level == 2 ? FpsUi.Err : (d.Level == 1 ? FpsUi.Warn : FpsUi.Ok);
                string head = d.Letter + (string.IsNullOrEmpty(d.Label) ? "" : "  " + d.Label) + (d.IsSystem ? "  · système" : "");
                TextRenderer.DrawText(g, head, FpsUi.H3, new Point(18, y), FpsUi.Ink, TextFormatFlags.NoPadding);

                string right = Storage.Human(d.UsedMB) + " / " + Storage.Human(d.TotalMB) + "   ·   " + d.FreePct + " % libres";
                Size rs = TextRenderer.MeasureText(g, right, FpsUi.Small);
                TextRenderer.DrawText(g, right, FpsUi.Small, new Point(p.Width - 20 - rs.Width, y + 2), lvl, TextFormatFlags.NoPadding);

                // Barre : occupé (couleur de verdict) + part récupérable (émeraude translucide)
                var bar = new RectangleF(18, y + 22, Math.Max(40, p.Width - 38), 10);
                using (GraphicsPath bg = FpsUi.Round(bar, 5f))
                using (var br = new SolidBrush(Color.FromArgb(46, 40, 32)))
                    g.FillPath(br, bg);

                float usedW = d.TotalMB > 0 ? (float)(bar.Width * d.UsedMB / (double)d.TotalMB) : 0;
                if (usedW > 2)
                {
                    using (GraphicsPath up = FpsUi.Round(new RectangleF(bar.X, bar.Y, usedW, bar.Height), 5f))
                    using (var br = new SolidBrush(lvl))
                        g.FillPath(br, up);
                }

                // Ce qui peut revenir : dessiné À LA FIN de la zone occupée, là où l'espace va rendre.
                if (d.IsSystem && recoverable > 0 && d.TotalMB > 0)
                {
                    float recW = (float)(bar.Width * recoverable / (double)d.TotalMB);
                    if (recW > 2 && recW < usedW)
                    {
                        using (GraphicsPath rp = FpsUi.Round(new RectangleF(bar.X + usedW - recW, bar.Y, recW, bar.Height), 5f))
                        using (var br = new SolidBrush(Color.FromArgb(190, FpsUi.Ok)))
                            g.FillPath(br, rp);
                    }
                }
                y += 52;
            }

            if (recoverable > 0)
            {
                using (var br = new SolidBrush(Color.FromArgb(190, FpsUi.Ok)))
                    g.FillEllipse(br, 18, y - 4, 8, 8);
                TextRenderer.DrawText(g, "cette part revient si tu libères maintenant  (" + Storage.Human(recoverable) + ")",
                    FpsUi.Tiny, new Point(31, y - 6), FpsUi.Dim, TextFormatFlags.NoPadding);
            }
        }

        private long RecoverableMB()
        {
            long mb = 0;
            foreach (Storage.Module m in _modules)
                if (!m.Browse && _selected.Contains(m.Id) && Allowed(m)) mb += m.FreeableMB;
            return mb;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            PaintTitle(e.Graphics, "STOCKAGE", Subtitle());
        }

        private void SetStatus(string s)
        {
            _statusText = s;
            Invalidate();   // le sous-titre est peint, pas posé dans un Label
        }

        private string Subtitle()
        {
            if (_busy) return _statusText;
            if (!_scanned) return "Analyse du disque…";
            Storage.DriveView sys = null;
            foreach (Storage.DriveView d in _drivesData) if (d.IsSystem) { sys = d; break; }
            long rec = RecoverableMB();
            int n = SelectedItems().Count;
            var sb = new System.Text.StringBuilder();
            sb.Append(rec > 0 ? Storage.Human(rec) + " récupérables" : "rien de significatif à récupérer");
            if (sys != null) sb.Append("  ·  ").Append(sys.Letter).Append(" rempli à ").Append(100 - sys.FreePct).Append(" %");
            sb.Append("  ·  ").Append(n == 0 ? "aucun module sélectionné" : n + " élément(s) prêts à partir");
            return sb.ToString();
        }
    }
}
