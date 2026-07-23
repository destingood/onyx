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
    /// Fiche détaillée d'un jeu (au clic depuis la page Jeux), façon FPS Doctor : jaquette officielle,
    /// statut + launcher, réglages FPS présentés proprement, priorité CPU DÉDIÉE à ce jeu (IFEO),
    /// lancer / ouvrir le dossier, et « Optimiser mon PC pour le jeu » en 1 clic (preset recommandé,
    /// réversible) — pensé pour un néophyte.
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
            ClientSize = new Size(640, 560);
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

        private void BuildActions()
        {
            const int rx = 200;
            if (_exes != null)
            {
                _prio = FpsUi.GhostButton("Priorité CPU : —");
                _prio.SetBounds(rx, 126, 300, 34);
                _prio.Click += (s, e) => TogglePrio();
                Controls.Add(_prio);
            }

            var launch = FpsUi.NeonButton("▶  Lancer");
            launch.SetBounds(rx, 168, 145, 34);
            launch.Enabled = _g.SteamId > 0 || _g.InstallPath != null;
            launch.Click += (s, e) => Launch();
            Controls.Add(launch);

            var folder = FpsUi.GhostButton("Ouvrir le dossier");
            folder.SetBounds(rx + 155, 168, 145, 34);
            folder.Enabled = _g.InstallPath != null;
            folder.Click += (s, e) => OpenFolder();
            Controls.Add(folder);

            BuildBoosts();

            var close = FpsUi.GhostButton("Fermer");
            close.SetBounds(ClientSize.Width - 24 - 110, ClientSize.Height - 58, 110, 40);
            close.Click += (s, e) => Close();
            Controls.Add(close);
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
                if (_g.InstallPath != null && _exes != null)
                    foreach (var exe in _exes)
                    {
                        string p = Path.Combine(_g.InstallPath, exe);
                        if (File.Exists(p)) { Process.Start(new ProcessStartInfo(p) { UseShellExecute = true, WorkingDirectory = _g.InstallPath }); return; }
                    }
                OpenFolder();
            }
            catch (Exception ex) { MessageBox.Show(this, "Impossible de lancer le jeu : " + ex.Message, "Fluide", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
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
            int y = ClientSize.Height - 190, w = (ClientSize.Width - 24 * 2 - 16) / 2;
            _cardLight = BoostCard(24, y, w, "BOOST LÉGER", false, out _nLight, out _btnLight);
            _cardFull = BoostCard(24 + w + 16, y, w, "BOOST COMPLET", true, out _nFull, out _btnFull);
            Controls.Add(_cardLight); Controls.Add(_cardFull);
            CountBoosts();
        }

        private Panel BoostCard(int x, int y, int w, string title, bool pro, out Label num, out Button btn)
        {
            var card = new Panel { Bounds = new Rectangle(x, y, w, 132), BackColor = Color.Transparent };
            card.Paint += (s, e) => FpsUi.PaintCard(e.Graphics, ((Panel)s).ClientRectangle,
                pro ? Color.FromArgb(24, 23, 46) : FpsUi.Card, pro ? FpsUi.NeonDim : FpsUi.Border, 12f);

            num = FpsUi.Text("…", FpsUi.Num, FpsUi.Ink);
            num.AutoSize = false; num.SetBounds(16, 12, 70, 34);
            var lab = FpsUi.Text(title, FpsUi.H3, pro ? FpsUi.Neon : FpsUi.Dim);
            lab.AutoSize = false; lab.SetBounds(86, 20, w - 100, 20);
            var sub = FpsUi.Text("", FpsUi.Small, FpsUi.Dim2);
            sub.AutoSize = false; sub.SetBounds(16, 50, w - 32, 32);
            sub.Text = pro ? "Réglage auto adapté à TON matériel." : "Réglages sûrs, valables pour tous les jeux.";

            btn = pro ? FpsUi.NeonButton("BOOST COMPLET") : FpsUi.GhostButton("BOOST LÉGER");
            btn.SetBounds(16, 88, w - 32, 32);
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

            // Jaquette
            var pr = new Rectangle(24, 24, 150, 224);
            Image img = _g.SteamId > 0
                ? GameArt.Get(_g.SteamId, () => { try { if (IsHandleCreated) BeginInvoke((Action)Invalidate); } catch { } })
                : null;
            using (var clip = Round(pr, 12))
            {
                var save = g.Clip; g.SetClip(clip);
                if (img != null)
                {
                    DrawCover(g, img, pr);
                    if (!_g.Detected) using (var v = new SolidBrush(Color.FromArgb(150, 9, 11, 10))) g.FillRectangle(v, pr);
                }
                else
                {
                    using (var b = new SolidBrush(FpsUi.Card)) g.FillRectangle(b, pr);
                    TextRenderer.DrawText(g, "🎮", FpsUi.GlyphL, pr, _g.Detected ? FpsUi.Neon : FpsUi.Dim2,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                g.Clip = save; save.Dispose();
            }
            using (var pen = new Pen(FpsUi.Border)) using (var bp = Round(pr, 12)) g.DrawPath(pen, bp);

            // Titre + statut + chemin
            const int rx = 200;
            TextRenderer.DrawText(g, _g.Name, FpsUi.H1, new Rectangle(rx, 24, W - rx - 24, 60), FpsUi.Ink,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            string status = _g.Detected ? ("● DÉTECTÉ" + (string.IsNullOrEmpty(_g.Store) ? "" : "   ·   " + _g.Store)) : "non installé sur ce PC";
            TextRenderer.DrawText(g, status, FpsUi.H3, new Rectangle(rx, 88, W - rx - 24, 22), _g.Detected ? FpsUi.Neon : FpsUi.Dim,
                TextFormatFlags.NoPrefix);
            if (!string.IsNullOrEmpty(_g.InstallPath))
                TextRenderer.DrawText(g, _g.InstallPath, FpsUi.Tiny, new Rectangle(rx, 110, W - rx - 24, 16), FpsUi.Dim,
                    TextFormatFlags.NoPrefix | TextFormatFlags.PathEllipsis);

            // Réglages FPS
            int fy = 268;
            Section(g, "RÉGLAGES POUR DÉBLOQUER LES FPS", fy, W);
            string uncap = _g.Uncap ?? "Règle la limite d'images sur Illimitée (ou 500) et coupe la V-Sync dans les options du jeu.";
            TextRenderer.DrawText(g, uncap, FpsUi.Body, new Rectangle(24, fy + 28, W - 48, 84), FpsUi.Ink,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);

            // Profil conseillé (néophyte)
            int py = fy + 116;
            Section(g, "PROFIL CONSEILLÉ (simple)", py, W);
            TextRenderer.DrawText(g,
                "Mode Jeu de Windows · plan Performances ultimes · Game DVR / Game Bar coupés · NVIDIA Reflex / AMD Anti-Lag ON · V-Sync OFF.  "
                + "Le bouton ⚡ ci-dessous applique tout ça d'un coup (sûr et réversible).",
                FpsUi.Small, new Rectangle(24, py + 28, W - 48, 46), FpsUi.Dim, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }

        private static void Section(Graphics g, string title, int y, int W)
        {
            TextRenderer.DrawText(g, title, FpsUi.Small, new Rectangle(24, y, W - 48, 18), FpsUi.NeonDim, TextFormatFlags.NoPrefix);
            using (var pen = new Pen(FpsUi.Border)) g.DrawLine(pen, 24, y + 20, W - 24, y + 20);
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
