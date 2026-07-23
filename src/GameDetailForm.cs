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

            var opt = FpsUi.NeonButton("⚡  Optimiser mon PC pour le jeu");
            opt.SetBounds(24, ClientSize.Height - 58, 330, 40);
            opt.Click += (s, e) => OptimizeForGame();
            Controls.Add(opt);

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

        private void OptimizeForGame()
        {
            if (MessageBox.Show(this,
                "Appliquer les optimisations RECOMMANDÉES (sûres) pour améliorer les FPS ?\n\n"
                + "Elles conviennent à la plupart des jeux et restent entièrement réversibles depuis la page Optimisations.",
                "Optimiser pour " + _g.Name, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            Cursor = Cursors.WaitCursor;
            Task.Run(() =>
            {
                int n = 0;
                try
                {
                    var list = Catalog.All().Where(t => t.Recommended).ToList(); n = list.Count;
                    Engine.Run(list, true, true, false, _log);
                    try { AppStats.Invalidate(); } catch { }
                }
                catch (Exception ex) { if (_log != null) _log("Optimiser pour le jeu : " + ex.Message, 2); }
                try { BeginInvoke((Action)(() => { Cursor = Cursors.Default;
                    MessageBox.Show(this, n + " optimisation(s) recommandée(s) appliquée(s). Bon jeu ! 🎮", "Fluide", MessageBoxButtons.OK, MessageBoxIcon.Information); })); }
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
