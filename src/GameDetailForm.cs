using System;
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
    /// lancer / ouvrir le dossier. Le boost léger reste gratuit ; le complet (auto-tune matériel)
    /// est une fonction Pro. Tout est confirmé, sauvegardé et réversible.
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
            Text = "Fluide — " + g.Name;
            // Mise en page « fiche pleine largeur » : onglets en haut, optimisations à gauche,
            // grande jaquette à droite, appel Pro en bas — comme une page, pas une boîte.
            ClientSize = new Size(1000, 620);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = FpsUi.BgMain; Font = FpsUi.Body; DoubleBuffered = true;
            try { Icon = Logo.MakeIcon(32, FpsUi.Neon); } catch { }

            if (g.SteamId > 0) { try { GameArt.Get(g.SteamId, null); } catch { } }   // démarre le chargement de la jaquette tôt
            BuildActions();
            RefreshPrio();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int v = 1; DwmSetWindowAttribute(Handle, 20, ref v, 4); } catch { }   // barre de titre sombre
        }

        private const int Pad = 40;        // marge de page
        private const int CoverW = 320;    // largeur de la jaquette (colonne droite)

        private int LeftW { get { return ClientSize.Width - Pad * 2 - CoverW - 32; } }

        private Button _back, _mode, _launch, _folder, _pro, _close;

        private void BuildActions()
        {
            // Onglet « ‹ BIBLIOTHÈQUE » : ramène à la liste.
            _back = FpsUi.GhostButton("‹  BIBLIOTHÈQUE");
            _back.FlatAppearance.BorderSize = 0;
            _back.BackColor = FpsUi.BgMain; _back.ForeColor = FpsUi.Dim;
            _back.Click += (s, e) => Close();
            Controls.Add(_back);

            _mode = FpsUi.NeonButton("▶  MODE JEU");
            _mode.Click += (s, e) => { try { Close(); } catch { } };
            Controls.Add(_mode);

            if (_exes != null)
            {
                _prio = FpsUi.GhostButton("Priorité CPU : —");
                _prio.Click += (s, e) => TogglePrio();
                Controls.Add(_prio);
            }

            _launch = FpsUi.NeonButton("▶  Lancer");
            _launch.Enabled = _g.SteamId > 0 || _g.InstallPath != null;
            _launch.Click += (s, e) => Launch();
            Controls.Add(_launch);

            _folder = FpsUi.GhostButton("Ouvrir le dossier");
            _folder.Enabled = _g.InstallPath != null;
            _folder.Click += (s, e) => OpenFolder();
            Controls.Add(_folder);

            BuildBoosts();

            if (!License.ProUnlocked)
            {
                _pro = FpsUi.NeonButton("◆  PASSER PRO");
                _pro.Click += (s, e) => { using (var f = new LicenseKeyForm("Boost complet")) f.ShowDialog(this); };
                Controls.Add(_pro);
            }

            _close = FpsUi.GhostButton("Fermer");
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
            if (_mode != null) _mode.SetBounds(W - Pad - 150, 20, 150, 34);

            int cardY = 146, cardW = (lw - 16) / 2;
            if (_cardLight != null) _cardLight.SetBounds(Pad, cardY, cardW, 166);
            if (_cardFull != null) _cardFull.SetBounds(Pad + cardW + 16, cardY, cardW, 166);

            int ay = 448;
            if (_prio != null) _prio.SetBounds(Pad, ay, lw, 34);
            int half = (lw - 12) / 2;
            if (_launch != null) _launch.SetBounds(Pad, ay + 42, half, 34);
            if (_folder != null) _folder.SetBounds(Pad + half + 12, ay + 42, half, 34);

            if (_pro != null) _pro.SetBounds(W - Pad - CoverW, H - 62, CoverW, 40);
            if (_close != null) _close.SetBounds(Pad, H - 62, 120, 40);
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
            _prio.ForeColor = hi ? FpsUi.Neon : FpsUi.Ink;
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
                        if (File.Exists(p)) { Process.Start(new ProcessStartInfo(p) { UseShellExecute = true, WorkingDirectory = _g.InstallPath }); return; }
                    }

                // Jeu hors catalogue (repéré par le scanner) : on devine l'exécutable principal.
                string guess = GameLibrary.GuessMainExe(_g.InstallPath);
                if (guess != null)
                {
                    Process.Start(new ProcessStartInfo(guess) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(guess) });
                    return;
                }
                OpenFolder();
            }
            catch (Exception ex) { MessageBox.Show(this, "Impossible de lancer le jeu : " + ex.Message, "Fluide", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
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
            card.Paint += (s, e) => FpsUi.PaintCard(e.Graphics, ((Panel)s).ClientRectangle,
                pro ? Color.FromArgb(24, 23, 46) : FpsUi.Card, pro ? FpsUi.NeonDim : FpsUi.Border, 12f);

            // Grand nombre (le compte réel), puis le libellé et l'explication.
            num = FpsUi.Text("…", FpsUi.Garet, FpsUi.Ink);
            num.AutoSize = false; num.SetBounds(20, 16, w - 40, 46);
            var lab = FpsUi.Text("Optimisations", FpsUi.Small, FpsUi.Dim2);
            lab.AutoSize = false; lab.SetBounds(22, 62, w - 44, 18);
            var sub = FpsUi.Text("", FpsUi.Small, pro ? FpsUi.Neon : FpsUi.Dim);
            sub.AutoSize = false; sub.SetBounds(22, 84, w - 44, 34);
            sub.Text = pro ? "Adapté à TON matériel (eSport)." : "Réglages sûrs, tous jeux.";

            btn = pro ? FpsUi.NeonButton("BOOST COMPLET") : FpsUi.GhostButton("BOOST LÉGER");
            btn.SetBounds(20, 122, w - 40, 34);
            card.Controls.Add(num); card.Controls.Add(lab); card.Controls.Add(sub); card.Controls.Add(btn);

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
                    if (_btnFull != null && !License.ProUnlocked) _btnFull.Text = "🔒  BOOST COMPLET";
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
            // Le boost complet, c'est l'auto-tune matériel : fonction Pro, comme ailleurs dans l'app.
            if (full && !License.ProUnlocked)
            {
                using (var f = new LicenseKeyForm("Boost complet")) f.ShowDialog(this);
                if (!License.ProUnlocked) return;
                if (_btnFull != null) _btnFull.Text = "BOOST COMPLET";
            }

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
                "Fluide — " + _g.Name, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

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
                        "Fluide", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

            // --- Onglets : « BIBLIOTHÈQUE » (bouton) puis le jeu, actif et souligné ---
            int tabX = Pad + 182;
            TextRenderer.DrawText(g, _g.Name.ToUpperInvariant(), FpsUi.H3, new Rectangle(tabX, 26, W - tabX - 210, 22),
                FpsUi.Ink, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            int tabW = Math.Min(W - tabX - 210, TextRenderer.MeasureText(_g.Name.ToUpperInvariant(), FpsUi.H3).Width);
            using (var pen = new Pen(FpsUi.Neon, 2f)) g.DrawLine(pen, tabX, 52, tabX + tabW, 52);
            using (var pen = new Pen(FpsUi.Border)) g.DrawLine(pen, Pad, 52, W - Pad, 52);

            // --- Colonne droite : grande jaquette ---
            var cover = new Rectangle(W - Pad - CoverW, 84, CoverW, 440);
            Image big = _g.SteamId > 0
                ? GameArt.Get(_g.SteamId, () => { try { if (IsHandleCreated) BeginInvoke((Action)Invalidate); } catch { } })
                : null;
            using (var clip = Round(cover, 14))
            {
                var save = g.Clip; g.SetClip(clip);
                if (big != null) DrawCover(g, big, cover);
                else
                {
                    using (var b = new SolidBrush(FpsUi.Card)) g.FillRectangle(b, cover);
                    TextRenderer.DrawText(g, "🎮", FpsUi.GlyphXL, cover, FpsUi.Dim2,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                g.Clip = save; save.Dispose();
            }
            using (var pen = new Pen(FpsUi.Border)) using (var bp = Round(cover, 14)) g.DrawPath(pen, bp);

            // --- Colonne gauche : titre du bloc + réglages, puis les cartes de boost ---
            TextRenderer.DrawText(g, "OPTIMISATIONS DE JEU", FpsUi.H2, new Rectangle(Pad, 84, LeftW, 26),
                FpsUi.Ink, TextFormatFlags.NoPrefix);
            string st2 = _g.Detected ? ("● DÉTECTÉ" + (string.IsNullOrEmpty(_g.Store) ? "" : "   ·   " + _g.Store)) : "non installé sur ce PC";
            TextRenderer.DrawText(g, st2, FpsUi.Small, new Rectangle(Pad, 114, LeftW, 18),
                _g.Detected ? FpsUi.Neon : FpsUi.Dim, TextFormatFlags.NoPrefix);

            // Conseils FPS SOUS les cartes : l'action passe devant l'explication.
            Section(g, "RÉGLAGES À FAIRE DANS LE JEU", 336, Pad, LeftW);
            string tip = _g.Uncap ?? "Règle la limite d'images sur Illimitée (ou 500) et coupe la V-Sync dans les options du jeu.";
            TextRenderer.DrawText(g, tip, FpsUi.Body, new Rectangle(Pad, 364, LeftW, 64), FpsUi.Ink,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);

            if (!string.IsNullOrEmpty(_g.InstallPath))
                TextRenderer.DrawText(g, _g.InstallPath, FpsUi.Tiny, new Rectangle(Pad, ClientSize.Height - 84, LeftW, 16),
                    FpsUi.Dim2, TextFormatFlags.NoPrefix | TextFormatFlags.PathEllipsis);
        }

        private static void Section(Graphics g, string title, int y, int x, int w)
        {
            TextRenderer.DrawText(g, title, FpsUi.Small, new Rectangle(x, y, w, 18), FpsUi.NeonDim, TextFormatFlags.NoPrefix);
            using (var pen = new Pen(FpsUi.Border)) g.DrawLine(pen, x, y + 20, x + w, y + 20);
        }

        private static GraphicsPath Round(Rectangle r, int rad)
        {
            var p = new GraphicsPath(); int d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
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
