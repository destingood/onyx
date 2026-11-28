using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    // ----------------------------------------------------------------------
    //  Overlay de stats in-game : petite fenêtre topmost "click-through" qui
    //  affiche CPU / GPU / températures / RAM en direct par-dessus le jeu
    //  (fenêtré / sans bordure), façon Afterburner. Réutilise le pattern
    //  overlay du viseur (Crosshair) + le monitoring matériel (HwMonitor).
    // ----------------------------------------------------------------------

    internal class StatsOverlaySettings
    {
        /// <summary>Tout ce qui est réglable : mesures affichées, position, taille, couleur.</summary>
        public CompteurJeu.Options O = new CompteurJeu.Options();

        public bool Enabled { get { return O.Actif; } set { O.Actif = value; } }
        public int Corner { get { return O.Position; } set { O.Position = value; } }

        public string ConfigPath { get { return AppPaths.File("bt-overlay.txt"); } }

        public void Save() { try { File.WriteAllText(ConfigPath, CompteurJeu.Ecrit(O)); } catch { } }

        public static StatsOverlaySettings Load()
        {
            var s = new StatsOverlaySettings();
            try { if (File.Exists(s.ConfigPath)) s.O = CompteurJeu.Lit(File.ReadAllText(s.ConfigPath)); } catch { }
            return s;
        }
    }

    /// <summary>Gestion globale de l'overlay de stats (singleton).</summary>
    internal static class StatsOverlayManager
    {
        private static StatsOverlayWindow _win;

        public static bool IsVisible { get { return _win != null && !_win.IsDisposed && _win.Visible; } }

        public static void Show(StatsOverlaySettings s)
        {
            if (_win == null || _win.IsDisposed) _win = new StatsOverlayWindow();
            _win.Applique(s.O);
            if (!_win.Visible) _win.Show();
            _win.BeginSampling();
        }

        public static void Hide() { if (_win != null && !_win.IsDisposed) _win.Hide(); }

        /// <summary>Aperçu de 5 secondes avec des valeurs de démonstration, sans mesure ni session
        /// ETW ; ensuite le compteur reprend son état (affiché et mesuré s'il est activé, sinon masqué).</summary>
        public static void Apercu(StatsOverlaySettings s)
        {
            if (_win == null || _win.IsDisposed) _win = new StatsOverlayWindow();
            _win.Applique(s.O);
            if (!_win.Visible) _win.Show();
            _win.SeedDemo();
            var t = new Timer { Interval = 5000 };
            t.Tick += (o, e) =>
            {
                t.Stop(); t.Dispose();
                try { if (s.Enabled) Show(s); else Hide(); } catch { }
            };
            t.Start();
        }

        public static void Toggle(StatsOverlaySettings s) { if (IsVisible) Hide(); else Show(s); }

        public static void ShowOnStartupIfEnabled(Action<string, int> log)
        {
            var s = StatsOverlaySettings.Load();
            if (!s.Enabled) return;
            try { Show(s); if (log != null) log("Overlay de stats affiché (activé dans les réglages).", 0); } catch { }
        }
    }

    /// <summary>Fenêtre overlay compacte, translucide, topmost et click-through.</summary>
    internal class StatsOverlayWindow : Form
    {
        private readonly HwMonitor _mon = new HwMonitor();
        private readonly Timer _timer = new Timer();
        private Timer _keeper;
        private HwSample _last;
        private bool _sampling;
        private CompteurJeu.Options _o = new CompteurJeu.Options();
        private double _k = 1.0;                       // échelle (petit, moyen, grand)
        private readonly CompteurJeu.Chrono _chrono = new CompteurJeu.Chrono();
        private TimeSpan? _duree;
        private long _octetsRecus = -1, _octetsEnvoyes = -1;
        private DateTime _releveReseau;
        private double _debitRecu = double.NaN, _debitEnvoye = double.NaN;
        private string _batterie = "—";
        private FpsEtw _fps;
        private double _lastFps = double.NaN;
        private double _lastLow = double.NaN;
        private string _fpsName;
        private readonly System.Collections.Generic.Queue<double> _fpsHist = new System.Collections.Generic.Queue<double>();
        private const int HistMax = 46;

        private const int WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80,
                          WS_EX_NOACTIVATE = 0x8000000, WS_EX_TOPMOST = 0x8;

        private static readonly Color Bg = Color.FromArgb(10, 12, 11);
        private static readonly Color Neon = FpsUi.Gold;
        private static readonly Color Ink = Color.FromArgb(235, 238, 236);
        private static readonly Color Dim = Color.FromArgb(150, 154, 150);

        public StatsOverlayWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Bg;
            Opacity = 0.82;
            DoubleBuffered = true;
            Applique(_o);

            _timer.Interval = 1000;
            _timer.Tick += (s, e) => Sample();
            // Ce minuteur était une variable LOCALE, démarrée et jamais arrêtée : il continuait de
            // battre toutes les 3 secondes bien après que l'utilisateur ait masqué l'overlay, et
            // sa capture de « this » empêchait la fenêtre d'être libérée. Il devient un champ,
            // arrêté en même temps que le reste.
            _keeper = new Timer { Interval = 3000 };
            _keeper.Tick += (s, e) => { try { if (Visible) TopMost = true; } catch { } };
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

        public void BeginSampling()
        {
            // Session ETW pour les FPS du jeu au premier plan (nécessite l'élévation ; échoue en
            // silence sinon → l'overlay affiche « FPS — » et garde les autres stats).
            if (_fps == null) { try { _fps = new FpsEtw(); _fps.Start(); } catch { } }
            Sample();
            if (!_timer.Enabled) _timer.Start();
            // Le maintien au premier plan repart avec l'overlay : il est arrêté quand on le masque.
            try { if (_keeper != null && !_keeper.Enabled) _keeper.Start(); } catch { }
        }

        /// <summary>Alimente l'overlay avec des données de démonstration (pour l'inspection
        /// visuelle hors jeu). Fige l'échantillonnage pour que le rendu reste stable.</summary>
        internal void SeedDemo()
        {
            try { _timer.Stop(); } catch { }
            _lastFps = 144; _lastLow = 118; _fpsName = "Counter-Strike 2";
            _last = new HwSample { CpuLoad = 38, RamLoad = 46, CpuTempC = 58, CpuMhz = 4720, DisqueActivite = 12 };
            _batterie = CompteurJeu.TexteBatterie(0.82, false, false);
            _last.Gpu = new GpuInfo { Ok = true, Util = 72, TempC = 64, VramUsedMB = 5320, VramTotalMB = 12288, PowerW = 186, FanRpm = 1450, FanPct = 42 };
            _debitRecu = 124.6e6; _debitEnvoye = 850e3; _duree = TimeSpan.FromMinutes(65);
            _fpsHist.Clear();
            int[] demo = { 120, 132, 140, 138, 144, 150, 146, 142, 139, 148, 152, 144, 141, 137, 145, 150, 148, 144, 142, 146, 151, 149, 143, 140, 147, 152, 146, 141 };
            foreach (int v in demo) _fpsHist.Enqueue(v);
            Invalidate();
        }

        /// <summary>Applique les réglages : taille de la fenêtre (selon les lignes choisies), puis place.</summary>
        public void Applique(CompteurJeu.Options o)
        {
            _o = o ?? new CompteurJeu.Options();
            _k = CompteurJeu.Echelle(_o.Taille);
            try
            {
                int l, h; CompteurJeu.Dimensions(_o, out l, out h);
                ClientSize = new Size(l, h);
                var ancienne = Region;
                Region = RoundedRegion(ClientRectangle, E(_o.Style == 1 ? 8 : 12));
                if (ancienne != null) ancienne.Dispose();
                var wa = Screen.PrimaryScreen.WorkingArea;
                int x, y; CompteurJeu.Place(_o.Position, wa.Left, wa.Top, wa.Width, wa.Height, Width, Height, out x, out y);
                Location = new Point(x, y);
            }
            catch { }
            Invalidate();
        }

        private int E(double v) { return (int)Math.Round(v * _k); }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) return;
            try { _timer.Stop(); } catch { }
            try { if (_keeper != null) _keeper.Stop(); } catch { }

            // ON FERME AUSSI LA SESSION ETW. Masquer l'overlay arrêtait bien l'échantillonnage,
            // mais la session de mesure d'images restait ouverte : Hide() ne détruit pas le
            // handle de la fenêtre, et FpsEtw n'est libérée que dans OnHandleDestroyed.
            //
            // Cette session reçoit un événement PAR IMAGE PRÉSENTÉE, par TOUS les processus de la
            // machine — puis range chaque frametime dans une liste qu'elle élague en continu.
            // Elle continuait donc de tourner indéfiniment après que l'utilisateur ait décoché
            // « Afficher l'overlay en jeu », c'est-à-dire précisément quand il croyait avoir
            // rendu la main au jeu. Dans une application dont c'est tout le propos, c'est le
            // dernier endroit où laisser tourner quelque chose d'invisible.
            //
            // BeginSampling la recrée à la réouverture : le champ est remis à null.
            try { if (_fps != null) { _fps.Dispose(); _fps = null; } } catch { }
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            try
            {
                _timer.Stop();
                if (_keeper != null) { _keeper.Stop(); _keeper.Dispose(); _keeper = null; }
                _mon.Dispose();
                if (_fps != null) { _fps.Dispose(); _fps = null; }
            }
            catch { }
            base.OnHandleDestroyed(e);
        }

        private void Sample()
        {
            if (_sampling) return;
            _sampling = true;
            Task.Run(() =>
            {
                HwSample s = null;
                try { s = _mon.Sample(); } catch { }
                double fps = double.NaN, low = double.NaN; string fname = null;
                try
                {
                    if (_fps != null && _fps.Running)
                    {
                        var ps = _fps.Snapshot(1000);
                        // Le jeu est la fenêtre au premier plan — PAS le processus le plus rapide.
                        // Un navigateur accéléré dépasse un jeu bridé à 60 Hz, et l'overlay
                        // affichait alors le débit d'images de Chrome pendant la partie.
                        var jeu = FpsEtw.ChoisirJeu(ps, FpsEtw.PidPremierPlan());
                        if (jeu != null) { fps = jeu.Fps; low = jeu.OnePctLowFps; fname = jeu.Name; }
                    }
                }
                catch { }
                bool reseau = _o.A(CompteurJeu.Reseau);
                double recu = double.NaN, envoye = double.NaN;
                if (reseau) { try { ReleveReseau(out recu, out envoye); } catch { } }
                else _octetsRecus = -1;
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        _last = s; _lastFps = fps; _lastLow = low; _fpsName = fname;
                        _debitRecu = recu; _debitEnvoye = envoye;
                        _duree = _chrono.Marque(fname, DateTime.UtcNow);
                        if (_o.A(CompteurJeu.Batterie)) _batterie = LitBatterie();
                        if (!double.IsNaN(fps)) { _fpsHist.Enqueue(fps); while (_fpsHist.Count > HistMax) _fpsHist.Dequeue(); }
                        _sampling = false; Invalidate();
                    }));
                }
                catch { _sampling = false; }
            });
        }

        /// <summary>
        /// Débit depuis le relevé précédent, sur les cartes Ethernet et Wi-Fi actives seulement : un
        /// VPN ou une carte virtuelle recompterait les mêmes octets. Appelé hors du fil d'interface.
        /// </summary>
        private void ReleveReseau(out double recu, out double envoye)
        {
            long r = 0, e = 0;
            foreach (var c in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (c.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                var t = c.NetworkInterfaceType;
                if (t != System.Net.NetworkInformation.NetworkInterfaceType.Ethernet
                    && t != System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211
                    && t != System.Net.NetworkInformation.NetworkInterfaceType.GigabitEthernet) continue;
                var st = c.GetIPStatistics();
                r += st.BytesReceived; e += st.BytesSent;
            }
            DateTime maintenant = DateTime.UtcNow;
            double sec = (maintenant - _releveReseau).TotalSeconds;
            recu = _octetsRecus < 0 ? double.NaN : CompteurJeu.Debit(_octetsRecus, r, sec);
            envoye = _octetsRecus < 0 ? double.NaN : CompteurJeu.Debit(_octetsEnvoyes, e, sec);
            _octetsRecus = r; _octetsEnvoyes = e; _releveReseau = maintenant;
        }

        /// <summary>Niveau de la batterie (GetSystemPowerStatus, instantané) ; « — » sur un PC fixe.</summary>
        private static string LitBatterie()
        {
            try
            {
                PowerStatus p = SystemInformation.PowerStatus;
                bool sans = (p.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) != 0 || (p.BatteryChargeStatus & BatteryChargeStatus.Unknown) != 0;
                return CompteurJeu.TexteBatterie(p.BatteryLifePercent, (p.BatteryChargeStatus & BatteryChargeStatus.Charging) != 0, sans);
            }
            catch { return "—"; }
        }

        /// <summary>Bandeau : la fenêtre prend la largeur réellement dessinée (marge contre les
        /// petits écarts d'une seconde à l'autre), puis se replace dans son coin.</summary>
        private void AjusteLargeur(int voulue)
        {
            try
            {
                ClientSize = new Size(voulue, ClientSize.Height);
                var ancienne = Region;
                Region = RoundedRegion(ClientRectangle, E(8));
                if (ancienne != null) ancienne.Dispose();
                var wa = Screen.PrimaryScreen.WorkingArea;
                int x, y; CompteurJeu.Place(_o.Position, wa.Left, wa.Top, wa.Width, wa.Height, Width, Height, out x, out y);
                Location = new Point(x, y);
                Invalidate();
            }
            catch { }
        }

        private struct Segment { public string Nom, Valeur; public Color Couleur; }

        /// <summary>Style en ligne : « 144 FPS · 1% 118 · CPU 38 % 58° · … » sur une seule ligne.</summary>
        private void PeintEnLigne(Graphics g, Color? fixe)
        {
            var r = ClientRectangle;
            using (var pen = new Pen(FpsUi.Accent(70))) g.DrawRectangle(pen, 0, 0, r.Width - 1, r.Height - 1);
            HwSample s = _last;
            bool gpuOk = s != null && s.Gpu != null && s.Gpu.Ok;
            var l = new System.Collections.Generic.List<Segment>();
            Color fc = double.IsNaN(_lastFps) ? Dim : fixe ?? (_lastFps >= 100 ? Neon : _lastFps >= 55 ? Color.FromArgb(230, 175, 45) : Color.FromArgb(232, 84, 74));
            l.Add(new Segment { Valeur = (double.IsNaN(_lastFps) ? "—" : _lastFps.ToString("0")) + " FPS", Couleur = fc });
            if (_o.A(CompteurJeu.Bas1) && !double.IsNaN(_lastLow) && _lastLow > 0)
                l.Add(new Segment { Nom = "1%", Valeur = _lastLow.ToString("0"), Couleur = fixe ?? (_lastLow >= 80 ? Neon : _lastLow >= 45 ? Color.FromArgb(230, 175, 45) : Color.FromArgb(232, 84, 74)) });
            if (_o.A(CompteurJeu.Cpu) || _o.A(CompteurJeu.CpuTemp))
            {
                string v = !_o.A(CompteurJeu.Cpu) ? "" : s == null || s.CpuLoad < 0 ? "—" : s.CpuLoad.ToString("0") + " %";
                if (_o.A(CompteurJeu.CpuTemp) && s != null && !double.IsNaN(s.CpuTempC)) v = (v + " " + s.CpuTempC.ToString("0") + "°").Trim();
                l.Add(new Segment { Nom = "CPU", Valeur = v.Length == 0 ? "—" : v, Couleur = s == null ? Dim : fixe ?? LoadColor(s.CpuLoad) });
            }
            if (_o.A(CompteurJeu.CpuMhz))
                l.Add(new Segment { Valeur = CompteurJeu.TexteFrequence(s != null ? s.CpuMhz : double.NaN), Couleur = fixe ?? Ink });
            if (_o.A(CompteurJeu.Gpu) || _o.A(CompteurJeu.GpuTemp))
            {
                double gu = gpuOk ? s.Gpu.Util : double.NaN, gt = gpuOk ? s.Gpu.TempC : double.NaN;
                string v = !_o.A(CompteurJeu.Gpu) ? "" : double.IsNaN(gu) ? "—" : gu.ToString("0") + " %";
                if (_o.A(CompteurJeu.GpuTemp) && !double.IsNaN(gt) && gt > 0) v = (v + " " + gt.ToString("0") + "°").Trim();
                l.Add(new Segment { Nom = "GPU", Valeur = v.Length == 0 ? "—" : v, Couleur = double.IsNaN(gu) ? Dim : fixe ?? LoadColor(gu) });
            }
            if (_o.A(CompteurJeu.Vram))
                l.Add(new Segment { Nom = "VRAM", Valeur = gpuOk ? CompteurJeu.TexteVram(s.Gpu.VramUsedMB, s.Gpu.VramTotalMB) : "—", Couleur = fixe ?? Ink });
            if (_o.A(CompteurJeu.Watts))
                l.Add(new Segment { Valeur = gpuOk && s.Gpu.PowerW > 0 ? s.Gpu.PowerW.ToString("0") + " W" : "—", Couleur = fixe ?? Ink });
            if (_o.A(CompteurJeu.Ventilo))
                l.Add(new Segment { Nom = "VENT", Valeur = gpuOk ? CompteurJeu.TexteVentilateur(s.Gpu.FanRpm, s.Gpu.FanPct) : "—", Couleur = fixe ?? Ink });
            if (_o.A(CompteurJeu.Ram))
                l.Add(new Segment { Nom = "RAM", Valeur = s == null ? "—" : s.RamLoad.ToString("0") + " %", Couleur = s == null ? Dim : fixe ?? LoadColor(s.RamLoad) });
            if (_o.A(CompteurJeu.Disque))
                l.Add(new Segment { Nom = "DISQUE", Valeur = s == null || double.IsNaN(s.DisqueActivite) ? "—" : s.DisqueActivite.ToString("0") + " %", Couleur = s == null ? Dim : fixe ?? LoadColor(s.DisqueActivite) });
            if (_o.A(CompteurJeu.Reseau))
                l.Add(new Segment { Valeur = "↓ " + CompteurJeu.TexteDebit(_debitRecu) + "  ↑ " + CompteurJeu.TexteDebit(_debitEnvoye), Couleur = fixe ?? Ink });
            if (_o.A(CompteurJeu.Batterie))
                l.Add(new Segment { Nom = "BATT", Valeur = _batterie, Couleur = fixe ?? Ink });
            if (_o.A(CompteurJeu.Heure))
                l.Add(new Segment { Valeur = DateTime.Now.ToString("HH:mm"), Couleur = fixe ?? Ink });
            if (_o.A(CompteurJeu.Duree))
                l.Add(new Segment { Nom = "PARTIE", Valeur = _duree.HasValue ? CompteurJeu.TexteDuree(_duree.Value) : "—", Couleur = fixe ?? Ink });

            const TextFormatFlags f = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
            int x = E(9);
            using (var lab = new Font("Segoe UI", (float)(7.5 * _k)))
            using (var val = new Font("Segoe UI Semibold", (float)(10 * _k)))
            using (var sep = new Pen(Color.FromArgb(60, 64, 62)))
            {
                for (int i = 0; i < l.Count; i++)
                {
                    if (i > 0) { g.DrawLine(sep, x - E(6), E(8), x - E(6), r.Height - E(8)); }
                    if (l[i].Nom != null)
                    {
                        int wn = TextRenderer.MeasureText(g, l[i].Nom, lab, Size.Empty, f).Width;
                        TextRenderer.DrawText(g, l[i].Nom, lab, new Rectangle(x, 0, wn + 2, r.Height), Dim, f);
                        x += wn + E(4);
                    }
                    int wv = TextRenderer.MeasureText(g, l[i].Valeur, val, Size.Empty, f).Width;
                    TextRenderer.DrawText(g, l[i].Valeur, val, new Rectangle(x, 0, wv + 2, r.Height), l[i].Couleur, f);
                    x += wv + E(13);
                }
            }
            int voulue = x - E(13) + E(10);
            if (voulue > r.Width || voulue < r.Width - E(30))
                try { BeginInvoke((Action)(() => AjusteLargeur(voulue + E(8)))); } catch { }
        }

        private static Region RoundedRegion(Rectangle r, int radius)
        {
            using (var p = new GraphicsPath())
            {
                int d = radius * 2;
                p.AddArc(r.X, r.Y, d, d, 180, 90);
                p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                p.CloseFigure();
                return new Region(p);
            }
        }

        /// <summary>Couleur fixe choisie, ou null = code couleur selon la valeur.</summary>
        private Color? CouleurFixe()
        {
            switch (_o.Couleur)
            {
                case 1: return Color.White;
                case 2: return FpsUi.Gold;
                case 3: return Color.FromArgb(70, 215, 120);
                case 4: return Color.FromArgb(110, 200, 255);
                default: return null;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            var r = ClientRectangle;
            Color? fixe = CouleurFixe();
            if (_o.Style == 1) { PeintEnLigne(g, fixe); return; }

            // Liseré néon fin.
            using (var pen = new Pen(FpsUi.Accent(70))) g.DrawRectangle(pen, 0, 0, r.Width - 1, r.Height - 1);

            // En-tête : marque + nom du jeu détecté (à droite).
            using (var hf = new Font("Segoe UI Semibold", (float)(8 * _k)))
            {
                TextRenderer.DrawText(g, "ONYX", hf, new Point(E(12), E(7)), Neon, TextFormatFlags.NoPadding);
                if (!string.IsNullOrEmpty(_fpsName))
                {
                    string nm = _fpsName; if (nm.Length > 18) nm = nm.Substring(0, 17) + "…";
                    TextRenderer.DrawText(g, nm, hf, new Rectangle(E(90), E(7), r.Width - E(98), E(14)), Dim,
                        TextFormatFlags.Right | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                }
            }

            // FPS (métrique héros).
            string fv = double.IsNaN(_lastFps) ? "—" : _lastFps.ToString("0");
            Color fc = double.IsNaN(_lastFps) ? Dim : fixe ?? (_lastFps >= 100 ? Neon : _lastFps >= 55 ? Color.FromArgb(230, 175, 45) : Color.FromArgb(232, 84, 74));
            using (var big = new Font("Segoe UI", (float)(26 * _k), FontStyle.Bold))
            using (var unit = new Font("Segoe UI Semibold", (float)(11 * _k)))
            using (var lowLab = new Font("Segoe UI", (float)(7.5 * _k)))
            using (var lowVal = new Font("Segoe UI Semibold", (float)(13 * _k)))
            {
                var sz = TextRenderer.MeasureText(g, fv, big, Size.Empty, TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, fv, big, new Point(E(10), E(20)), fc, TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, "FPS", unit, new Point(E(16) + sz.Width, E(40)), fc, TextFormatFlags.NoPadding);
                // 1% low : indicateur de micro-saccades (le plus parlant en compétitif).
                if (_o.A(CompteurJeu.Bas1) && !double.IsNaN(_lastLow) && _lastLow > 0)
                {
                    Color lc = fixe ?? (_lastLow >= 80 ? Neon : _lastLow >= 45 ? Color.FromArgb(230, 175, 45) : Color.FromArgb(232, 84, 74));
                    TextRenderer.DrawText(g, "1% LOW", lowLab, new Rectangle(r.Width - E(80), E(24), E(68), E(12)), Dim, TextFormatFlags.Right | TextFormatFlags.NoPadding);
                    TextRenderer.DrawText(g, _lastLow.ToString("0"), lowVal, new Rectangle(r.Width - E(80), E(36), E(68), E(22)), lc, TextFormatFlags.Right | TextFormatFlags.NoPadding);
                }
            }

            HwSample s = _last;
            int y = E(72);
            if (CompteurJeu.NbLignes(_o) > 0)
                using (var pen = new Pen(Color.FromArgb(40, 43, 41))) g.DrawLine(pen, E(12), E(64), r.Width - E(12), E(64));
            using (var lab = new Font("Segoe UI", (float)(8.5 * _k)))
            using (var val = new Font("Segoe UI Semibold", (float)(10.5 * _k)))
            {
                bool gpuOk = s != null && s.Gpu != null && s.Gpu.Ok;
                if (_o.A(CompteurJeu.Cpu) || _o.A(CompteurJeu.CpuTemp))
                    Row(g, lab, val, ref y, "CPU",
                        !_o.A(CompteurJeu.Cpu) ? null : s == null || s.CpuLoad < 0 ? "—" : s.CpuLoad.ToString("0") + " %",
                        _o.A(CompteurJeu.CpuTemp) && s != null && !double.IsNaN(s.CpuTempC) ? s.CpuTempC.ToString("0") + "°" : null,
                        s == null ? Dim : fixe ?? LoadColor(s.CpuLoad), fixe);
                if (_o.A(CompteurJeu.CpuMhz))
                    Large(g, lab, val, ref y, "FRÉQ", CompteurJeu.TexteFrequence(s != null ? s.CpuMhz : double.NaN), fixe ?? Ink);
                if (_o.A(CompteurJeu.Gpu) || _o.A(CompteurJeu.GpuTemp))
                {
                    double gu = gpuOk ? s.Gpu.Util : double.NaN;
                    double gt = gpuOk ? s.Gpu.TempC : double.NaN;
                    Row(g, lab, val, ref y, "GPU",
                        !_o.A(CompteurJeu.Gpu) ? null : double.IsNaN(gu) ? "—" : gu.ToString("0") + " %",
                        !_o.A(CompteurJeu.GpuTemp) || double.IsNaN(gt) || gt <= 0 ? null : gt.ToString("0") + "°",
                        double.IsNaN(gu) ? Dim : fixe ?? LoadColor(gu), fixe);
                }
                if (_o.A(CompteurJeu.Vram))
                    Large(g, lab, val, ref y, "VRAM", gpuOk ? CompteurJeu.TexteVram(s.Gpu.VramUsedMB, s.Gpu.VramTotalMB) : "—",
                        !gpuOk || s.Gpu.VramTotalMB <= 0 ? fixe ?? Ink : fixe ?? LoadColor(100.0 * s.Gpu.VramUsedMB / s.Gpu.VramTotalMB));
                if (_o.A(CompteurJeu.Watts))
                    Large(g, lab, val, ref y, "GPU W", gpuOk && s.Gpu.PowerW > 0 ? s.Gpu.PowerW.ToString("0") + " W" : "—", fixe ?? Ink);
                if (_o.A(CompteurJeu.Ventilo))
                    Large(g, lab, val, ref y, "VENT", gpuOk ? CompteurJeu.TexteVentilateur(s.Gpu.FanRpm, s.Gpu.FanPct) : "—", fixe ?? Ink, lab);
                if (_o.A(CompteurJeu.Ram))
                    Row(g, lab, val, ref y, "RAM", s == null ? "—" : s.RamLoad.ToString("0") + " %", null, s == null ? Dim : fixe ?? LoadColor(s.RamLoad), fixe);
                if (_o.A(CompteurJeu.Disque))
                    Large(g, lab, val, ref y, "DISQUE", s == null || double.IsNaN(s.DisqueActivite) ? "—" : s.DisqueActivite.ToString("0") + " %",
                        s == null || double.IsNaN(s.DisqueActivite) ? Dim : fixe ?? LoadColor(s.DisqueActivite));
                if (_o.A(CompteurJeu.Reseau))
                    Large(g, lab, val, ref y, "NET", "↓ " + CompteurJeu.TexteDebit(_debitRecu) + "  ↑ " + CompteurJeu.TexteDebit(_debitEnvoye), fixe ?? Ink, lab);
                if (_o.A(CompteurJeu.Batterie))
                    Large(g, lab, val, ref y, "BATT", _batterie, fixe ?? Ink);
                if (_o.A(CompteurJeu.Heure))
                    Large(g, lab, val, ref y, "HEURE", DateTime.Now.ToString("HH:mm"), fixe ?? Ink);
                if (_o.A(CompteurJeu.Duree))
                    Large(g, lab, val, ref y, "PARTIE", _duree.HasValue ? CompteurJeu.TexteDuree(_duree.Value) : "—", fixe ?? Ink);
            }

            // Mini-historique des FPS (sparkline).
            if (_o.A(CompteurJeu.Courbe) && _fpsHist.Count >= 2)
            {
                var plot = new Rectangle(E(12), r.Height - E(22), r.Width - E(24), E(14));
                double[] arr = _fpsHist.ToArray();
                double max = 1; foreach (double v in arr) if (v > max) max = v;
                var pts = new PointF[arr.Length];
                for (int i = 0; i < arr.Length; i++)
                {
                    float px = plot.Left + (float)i / (HistMax - 1) * plot.Width;
                    float py = plot.Bottom - (float)(arr[i] / max) * plot.Height;
                    pts[i] = new PointF(px, py);
                }
                using (var pen = new Pen(Color.FromArgb(190, fixe ?? FpsUi.Gold), (float)(1.4 * _k))) g.DrawLines(pen, pts);
            }
        }

        /// <summary>Ligne « nom · charge · température ». Charge absente : la température reste à droite.</summary>
        private void Row(Graphics g, Font lab, Font val, ref int y, string name, string value, string temp, Color vc, Color? fixe)
        {
            TextRenderer.DrawText(g, name, lab, new Point(E(12), y + E(2)), Dim, TextFormatFlags.NoPadding);
            if (value != null)
                TextRenderer.DrawText(g, value, val, new Rectangle(E(48), y, E(70), E(22)), vc, TextFormatFlags.Right | TextFormatFlags.NoPadding);
            if (temp != null)
                TextRenderer.DrawText(g, temp, val, new Rectangle(E(130), y, E(66), E(22)), fixe ?? TempColor(temp), TextFormatFlags.Right | TextFormatFlags.NoPadding);
            y += E(28);
        }

        /// <summary>Ligne « nom · valeur longue » (mémoire vidéo, débit, heure) : la valeur prend toute la largeur.</summary>
        private void Large(Graphics g, Font lab, Font val, ref int y, string name, string value, Color vc, Font petite = null)
        {
            // « petite » : deux débits côte à côte ne tiennent pas dans la police des valeurs.
            TextRenderer.DrawText(g, name, lab, new Point(E(12), y + E(2)), Dim, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, value, petite ?? val, new Rectangle(E(44), y + (petite != null ? E(2) : 0), Width - E(44) - E(26), E(22)), vc,
                TextFormatFlags.Right | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            y += E(28);
        }

        private static Color LoadColor(double v)
        {
            if (double.IsNaN(v)) return Dim;
            return v < 60 ? Neon : v < 85 ? Color.FromArgb(230, 175, 45) : Color.FromArgb(232, 84, 74);
        }

        private static Color TempColor(string t)
        {
            int v; if (!int.TryParse(t.Replace("°", ""), out v)) return Ink;
            return v < 70 ? Neon : v < 84 ? Color.FromArgb(230, 175, 45) : Color.FromArgb(232, 84, 74);
        }
    }
}
