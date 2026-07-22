using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace BTOptimizer
{
    // ----------------------------------------------------------------------
    //  Viseur (crosshair) personnalisé : superpose un réticule au centre de
    //  l'écran par-dessus les jeux (fenêtré / sans bordure). Réglages persistés
    //  dans bt-crosshair.txt. Overlay transparent et "click-through".
    // ----------------------------------------------------------------------

    /// <summary>Réglages du viseur.</summary>
    internal class CrosshairSettings
    {
        public int Shape = 1;          // 0 croix · 1 croix+point · 2 point · 3 cercle · 4 cercle+point
        public Color Color = Color.FromArgb(0, 230, 118);
        public int Size = 12;          // longueur des branches (px)
        public int Thickness = 2;      // épaisseur (px)
        public int Gap = 4;            // écart central (px)
        public int Dot = 3;            // diamètre du point central (px)
        public int Opacity = 90;       // 10..100 %
        public bool Outline = true;    // liseré noir pour lisibilité
        public bool Enabled = false;   // affiché au démarrage
        public bool AutoGame = false;  // affiché automatiquement quand un jeu tourne

        public string ConfigPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-crosshair.txt"); }
        }

        public void Save()
        {
            try
            {
                string s = string.Join(";", new string[]
                {
                    Shape.ToString(CultureInfo.InvariantCulture),
                    Color.ToArgb().ToString(CultureInfo.InvariantCulture),
                    Size.ToString(CultureInfo.InvariantCulture),
                    Thickness.ToString(CultureInfo.InvariantCulture),
                    Gap.ToString(CultureInfo.InvariantCulture),
                    Dot.ToString(CultureInfo.InvariantCulture),
                    Opacity.ToString(CultureInfo.InvariantCulture),
                    Outline ? "1" : "0",
                    Enabled ? "1" : "0",
                    AutoGame ? "1" : "0"
                });
                File.WriteAllText(ConfigPath, s);
            }
            catch { }
        }

        public static CrosshairSettings Load()
        {
            var c = new CrosshairSettings();
            try
            {
                string path = c.ConfigPath;
                if (!File.Exists(path)) return c;
                string[] p = File.ReadAllText(path).Trim().Split(';');
                int v;
                if (p.Length > 0 && int.TryParse(p[0], out v)) c.Shape = v;
                if (p.Length > 1 && int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) c.Color = Color.FromArgb(v);
                if (p.Length > 2 && int.TryParse(p[2], out v)) c.Size = v;
                if (p.Length > 3 && int.TryParse(p[3], out v)) c.Thickness = v;
                if (p.Length > 4 && int.TryParse(p[4], out v)) c.Gap = v;
                if (p.Length > 5 && int.TryParse(p[5], out v)) c.Dot = v;
                if (p.Length > 6 && int.TryParse(p[6], out v)) c.Opacity = v;
                if (p.Length > 7) c.Outline = p[7] == "1";
                if (p.Length > 8) c.Enabled = p[8] == "1";
                if (p.Length > 9) c.AutoGame = p[9] == "1";
            }
            catch { }
            return c;
        }
    }

    /// <summary>Gestion globale de l'overlay (singleton).</summary>
    internal static class Crosshair
    {
        private static CrosshairOverlay _overlay;

        public static bool IsVisible { get { return _overlay != null && !_overlay.IsDisposed && _overlay.Visible; } }

        public static void Show(CrosshairSettings s)
        {
            if (_overlay == null || _overlay.IsDisposed)
                _overlay = new CrosshairOverlay();
            _overlay.ApplySettings(s);
            if (!_overlay.Visible) _overlay.Show();
            _overlay.Refresh();
        }

        public static void Update(CrosshairSettings s)
        {
            if (IsVisible) _overlay.ApplySettings(s);
        }

        public static void Hide()
        {
            if (_overlay != null && !_overlay.IsDisposed) _overlay.Hide();
        }

        public static void Toggle(CrosshairSettings s)
        {
            if (IsVisible) Hide(); else Show(s);
        }

        /// <summary>Affiche le viseur au démarrage si l'utilisateur l'avait activé.</summary>
        public static void ShowOnStartupIfEnabled(Action<string, int> log)
        {
            var s = CrosshairSettings.Load();
            _autoSettings = s;
            if (!s.Enabled) return;
            try { Show(s); if (log != null) log("Viseur (crosshair) affiché (activé dans les réglages).", 0); }
            catch { }
        }

        // --- Viseur AUTO en jeu (même détection que le MODE JEU AUTO) ---------
        private static CrosshairSettings _autoSettings;  // cache lu par le tick (aucune I/O par tick)
        private static bool _autoShown;                  // c'est l'AUTO qui a affiché le viseur
        private static bool _wasInGame;                  // pour n'afficher qu'à l'ENTRÉE en jeu

        /// <summary>true si « afficher automatiquement en jeu » est coché dans les réglages.</summary>
        public static bool AutoGameEnabled
        {
            get
            {
                if (_autoSettings == null) _autoSettings = CrosshairSettings.Load();
                return _autoSettings.AutoGame;
            }
        }

        /// <summary>À appeler après un enregistrement des réglages : le tick relira le fichier.</summary>
        public static void ReloadAutoSettings() { _autoSettings = null; }

        /// <summary>
        /// Tick « viseur AUTO en jeu » : affiche le réticule à l'ENTRÉE en jeu (transition
        /// bureau → jeu), le retire au retour au bureau. Ne ré-affiche pas un viseur masqué
        /// à la main en pleine partie et ne retire jamais un affichage manuel.
        /// </summary>
        public static void AutoTick(bool engage, bool stillInGame)
        {
            CrosshairSettings s = _autoSettings;
            if (s == null || !s.AutoGame) { _wasInGame = stillInGame; _autoShown = false; return; }
            if (engage && !_wasInGame && !IsVisible) { Show(s); _autoShown = true; }
            else if (!stillInGame && _autoShown) { Hide(); _autoShown = false; }
            if (engage) _wasInGame = true;
            else if (!stillInGame) _wasInGame = false;
        }
    }

    /// <summary>Fenêtre overlay transparente, topmost et click-through.</summary>
    internal class CrosshairOverlay : Form
    {
        private CrosshairSettings _s = new CrosshairSettings();
        private Timer _topmostKeeper;

        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_TOOLWINDOW = 0x80;
        private const int WS_EX_NOACTIVATE = 0x8000000;
        private const int WS_EX_TOPMOST = 0x8;

        public CrosshairOverlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Magenta;        // couleur "magique" rendue transparente
            TransparencyKey = Color.Magenta;
            DoubleBuffered = true;
            Bounds = Screen.PrimaryScreen.Bounds;

            _topmostKeeper = new Timer();
            _topmostKeeper.Interval = 3000;
            _topmostKeeper.Tick += (s, e) => { try { if (Visible) TopMost = true; } catch { } };
            _topmostKeeper.Start();
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
                return cp;
            }
        }

        public void ApplySettings(CrosshairSettings s)
        {
            _s = s;
            double o = s.Opacity; if (o < 10) o = 10; if (o > 100) o = 100;
            Opacity = o / 100.0;
            try { Bounds = Screen.PrimaryScreen.Bounds; } catch { }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int cx = ClientSize.Width / 2;
            int cy = ClientSize.Height / 2;

            int th = Math.Max(1, _s.Thickness);
            int size = Math.Max(0, _s.Size);
            int gap = Math.Max(0, _s.Gap);
            int dot = Math.Max(0, _s.Dot);

            // Liseré noir (dessiné d'abord, un peu plus épais) pour la lisibilité.
            if (_s.Outline)
                DrawShape(g, cx, cy, th + 2, size, gap, dot, Color.FromArgb(180, 0, 0, 0));
            DrawShape(g, cx, cy, th, size, gap, dot, _s.Color);
        }

        private void DrawShape(Graphics g, int cx, int cy, int th, int size, int gap, int dot, Color col)
        {
            using (var pen = new Pen(col, th))
            using (var br = new SolidBrush(col))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;

                bool cross = _s.Shape == 0 || _s.Shape == 1;
                bool circle = _s.Shape == 3 || _s.Shape == 4;
                bool center = _s.Shape == 1 || _s.Shape == 2 || _s.Shape == 4;

                if (cross && size > 0)
                {
                    g.DrawLine(pen, cx, cy - gap - size, cx, cy - gap);           // haut
                    g.DrawLine(pen, cx, cy + gap, cx, cy + gap + size);           // bas
                    g.DrawLine(pen, cx - gap - size, cy, cx - gap, cy);           // gauche
                    g.DrawLine(pen, cx + gap, cy, cx + gap + size, cy);           // droite
                }
                if (circle)
                {
                    int r = Math.Max(2, gap + size);
                    g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
                }
                if (center && dot > 0)
                {
                    float d = dot;
                    g.FillEllipse(br, cx - d / 2f, cy - d / 2f, d, d);
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_topmostKeeper != null) { _topmostKeeper.Stop(); _topmostKeeper.Dispose(); }
            base.OnFormClosing(e);
        }
    }
}
