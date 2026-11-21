using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    internal class PageCheckup : FpsPage
    {
        private sealed class Routine { public string Name, Desc, Hint; public int Id; public ToggleSwitch Sel; public BufferedPanel Card; }
        private readonly List<Routine> _routines = new List<Routine>();
        private readonly List<Button> _tools = new List<Button>();
        private readonly OnyxScrollPanel _scroll = new OnyxScrollPanel();
        private readonly BufferedPanel _content;
        private readonly BufferedPanel _hero = new BufferedPanel(), _historyCard = new BufferedPanel(), _summary = new BufferedPanel();
        private readonly Button[] _tabs = new Button[2];
        private readonly Button _scan, _report, _run, _clear;
        private readonly Label _section = FpsUi.Text("", FpsUi.H3, FpsUi.Dim);
        private List<int> _history = new List<int>();
        private bool _maintenance, _running;
        private string _progress = "";
        private static readonly string[] ToolNames = { "Stabilité & crashs", "Températures", "Ce qui ralentit le PC", "Réglages à vérifier", "Santé des disques", "Entretien avancé" };
        private static readonly string[] ToolDescriptions = { "Écrans bleus, erreurs GPU et arrêts inattendus.", "Surveille la chauffe et les limites thermiques.", "Repère les programmes qui consomment des ressources.", "Examine les réglages laissés par d’autres optimiseurs.", "Contrôle les informations SMART et l’espace disponible.", "Retrouve les outils de maintenance de Windows." };
        private static readonly int[] ToolIcons = { NavIcons.CheckUp, NavIcons.Systeme, NavIcons.Optimisations, NavIcons.Outils, NavIcons.Stockage, NavIcons.Outils };

        public PageCheckup(DashboardForm host) : base(host)
        {
            _report = OnyxDesign.Button("Exporter le rapport");
            _report.Click += (s, e) => Host.GenerateHealthReport(); Controls.Add(_report);
            for (int i = 0; i < 2; i++)
            {
                int tab = i;
                _tabs[i] = OnyxDesign.Button(i == 0 ? "Bilan du PC" : "Entretien");
                _tabs[i].Name = "checkup-tab-" + i; _tabs[i].Font = FpsUi.H3;
                _tabs[i].Click += (s, e) => ChangeView(tab == 1); Controls.Add(_tabs[i]);
            }
            _content = _scroll.Content; Controls.Add(_scroll);
            _hero.Paint += PaintHero; _content.Controls.Add(_hero);
            _historyCard.Paint += PaintHistory; _content.Controls.Add(_historyCard);
            _scan = OnyxDesign.PrimaryButton("Analyser la santé du PC  →");
            _scan.Click += (s, e) => { Host.OpenDialog(new HealthForm(Host.Log)); RefreshHistory(); };
            _hero.Controls.Add(_scan);
            _section.AutoSize = false; _content.Controls.Add(_section);
            Action[] actions = {
                () => Host.OpenDialog(new StabilityForm(Host.Log)), () => Host.OpenDialog(new ThermalForm(Host.Log)),
                () => Host.OpenDialog(new BloatForm(Host.Log)), () => Host.OpenDialog(new CheckupForm(Host.Log)),
                () => Host.OpenDialog(new DiskForm(Host.Log)), () => Host.OpenDialog(new MaintenanceForm(Host.Log))
            };
            for (int i = 0; i < actions.Length; i++)
            {
                var card = new DiagnosticCard(i); int index = i;
                card.Click += (s, e) => actions[index](); _tools.Add(card); _content.Controls.Add(card);
            }
            Add(0, "Fichiers temporaires", "Supprime les fichiers temporaires de Windows et des applications.", "LIBÉRER DE L’ESPACE");
            Add(1, "Optimisation des disques", "Lance TRIM sur les SSD et la défragmentation sur les HDD.", "OUTIL WINDOWS · PLUSIEURS MINUTES");
            Add(2, "Caches de shaders GPU", "À utiliser après un problème de pilote. Les shaders seront recompilés : des saccades peuvent apparaître temporairement.", "EN CAS DE PROBLÈME");
            Add(3, "Historique Windows", "Efface les fichiers récents, les miniatures et les Jump Lists.", "EFFACER LES TRACES RÉCENTES");
            Add(4, "Réparation du système", "Lance SFC et DISM pour vérifier et réparer les fichiers de Windows.", "OUTIL WINDOWS · PLUSIEURS MINUTES");
            Add(5, "Caches réseau", "Vide les caches DNS et ARP de Windows pour rafraîchir les résolutions réseau.", "RAFRAÎCHIR LE RÉSEAU");
            _summary.Paint += PaintSummary; Controls.Add(_summary);
            _run = OnyxDesign.PrimaryButton("Vérifier et lancer  →"); _run.Click += (s, e) => RunSelected(); _summary.Controls.Add(_run);
            _clear = OnyxDesign.Button("Tout désélectionner");
            _clear.Click += (s, e) => { foreach (var r in _routines) r.Sel.On = false; UpdateSelection(); }; _summary.Controls.Add(_clear);
            Resize += (s, e) => DoLayout(); ChangeView(false);
        }
        public override void OnShown() { RefreshHistory(); DoLayout(); }
        private void RefreshHistory() { _history = HealthForm.LoadHistory(); _historyCard.Invalidate(); }
        private void Add(int id, string title, string description, string hint)
        {
            var r = new Routine { Id = id, Name = title, Desc = description, Hint = hint, Card = new BufferedPanel(), Sel = new ToggleSwitch() };
            r.Card.Name = "checkup-routine-" + id; r.Card.BackColor = FpsUi.BgMain;
            r.Sel.On = id == 0 || id == 5; r.Sel.AccessibleName = title;
            r.Sel.Toggled += (s, e) => UpdateSelection();
            r.Card.Paint += (s, e) => PaintRoutine(e.Graphics, r);
            r.Card.Controls.Add(r.Sel); _content.Controls.Add(r.Card); _routines.Add(r);
            OnyxDesign.HoverCard(r.Card);
        }
        private int SelectedCount { get { int n = 0; foreach (var r in _routines) if (r.Sel.On) n++; return n; } }
        private void UpdateSelection()
        {
            foreach (var r in _routines) { r.Sel.Enabled = !_running; r.Card.Invalidate(); }
            _run.Enabled = !_running && SelectedCount > 0;
            _clear.Enabled = !_running && SelectedCount > 0;
            _run.Text = _running ? "En cours…" : "Vérifier et lancer  →"; _summary.Invalidate();
        }
        private void ChangeView(bool maintenance)
        {
            _maintenance = maintenance; _scroll.ScrollTo(0);
            _hero.Visible = _historyCard.Visible = !maintenance;
            foreach (var tool in _tools) tool.Visible = !maintenance;
            foreach (var r in _routines) r.Card.Visible = maintenance;
            _summary.Visible = maintenance;
            _section.Text = maintenance ? "CHOISIS LES OPÉRATIONS À EFFECTUER" : "EXAMINER UN POINT PRÉCIS";
            for (int i = 0; i < 2; i++)
            {
                bool active = maintenance == (i == 1);
                _tabs[i].BackColor = active ? FpsUi.BlendGold(FpsUi.Card, .12f) : FpsUi.Card;
                _tabs[i].ForeColor = active ? FpsUi.Gold : FpsUi.Dim;
                _tabs[i].FlatAppearance.BorderSize = active ? 1 : 0;
                _tabs[i].FlatAppearance.BorderColor = FpsUi.GoldDim;
            }
            UpdateSelection(); DoLayout();
        }
        private void DoLayout()
        {
            if (_run == null) return;
            _report.SetBounds(Width - 214, 20, 180, 36);
            _tabs[0].SetBounds(34, 98, 154, 40); _tabs[1].SetBounds(198, 98, 154, 40);
            _scroll.SetBounds(34, 156, Math.Max(400, Width - 50), Math.Max(150, Height - (_maintenance ? 272 : 174)));
            int w = _scroll.ContentWidth;
            int height;
            if (!_maintenance)
            {
                int main = (w - 16) * 62 / 100;
                _hero.SetBounds(0, 0, main, 214); _historyCard.SetBounds(main + 16, 0, w - main - 16, 214);
                _scan.SetBounds(24, 156, 240, 38); _section.SetBounds(0, 238, w, 24);
                int cols = w >= 1050 ? 3 : 2, cw = (w - (cols - 1) * 16) / cols;
                for (int i = 0; i < _tools.Count; i++) _tools[i].SetBounds((i % cols) * (cw + 16), 278 + i / cols * 156, cw, 140);
                height = 278 + ((_tools.Count + cols - 1) / cols) * 156;
            }
            else
            {
                _section.SetBounds(0, 0, w, 24); int cw = (w - 16) / 2;
                for (int i = 0; i < _routines.Count; i++)
                {
                    var r = _routines[i]; r.Card.SetBounds(i % 2 * (cw + 16), 42 + i / 2 * 168, cw, 152);
                    r.Sel.Location = new Point(cw - r.Sel.Width - 18, 20);
                }
                height = 42 + 3 * 168;
            }
            _scroll.ContentHeight = height;
            _summary.SetBounds(34, Height - 100, Width - 68, 82);
            _run.SetBounds(_summary.Width - 230, 21, 210, 40); _clear.SetBounds(_run.Left - 188, 23, 174, 36);
            Invalidate();
        }
        private void PaintHero(object sender, PaintEventArgs e)
        {
            var g = e.Graphics; int w = _hero.Width;
            OnyxDesign.PaintCard(g, _hero.ClientRectangle, FpsUi.BlendGold(FpsUi.Card, .035f), OnyxDesign.Edge, 12);
            OnyxDesign.Text(g, "DIAGNOSTIC WINDOWS & GAMING", FpsUi.Small, FpsUi.Gold, new Rectangle(24, 22, w - 48, 20));
            OnyxDesign.Text(g, "Un bilan avant d’agir.", OnyxDesign.Title, FpsUi.Ink, new Rectangle(24, 55, w - 48, 34));
            OnyxDesign.Text(g, "Stabilité, disques, réseau et réglages : retrouve les points à examiner et les outils adaptés.", FpsUi.Body, FpsUi.Dim, new Rectangle(24, 100, w - 48, 44), true);
        }
        private void PaintHistory(object sender, PaintEventArgs e)
        {
            var g = e.Graphics; int w = _historyCard.Width;
            OnyxDesign.PaintCard(g, _historyCard.ClientRectangle, FpsUi.Card, FpsUi.Border, 12);
            OnyxDesign.Text(g, "DERNIER SCORE ENREGISTRÉ", FpsUi.Small, FpsUi.Dim, new Rectangle(20, 22, w - 40, 22));
            string value = _history.Count == 0 ? "—" : _history[_history.Count - 1].ToString();
            OnyxDesign.Text(g, value, OnyxDesign.Display, FpsUi.Gold, new Rectangle(18, 58, 100, 54));
            OnyxDesign.Text(g, "/ 100", FpsUi.H3, FpsUi.Dim, new Rectangle(124, 82, 70, 24));
            OnyxDesign.Text(g, _history.Count == 0 ? "Ton premier bilan t’attend." : "Score indicatif du dernier bilan.", FpsUi.H3, FpsUi.Ink, new Rectangle(20, 125, w - 40, 24));
            OnyxDesign.Text(g, "Lance une analyse pour connaître l’état actuel.", FpsUi.Small, FpsUi.Dim, new Rectangle(20, 158, w - 40, 42), true);
        }
        private void PaintRoutine(Graphics g, Routine r)
        {
            int w = r.Card.Width;
            OnyxDesign.PaintCard(g, r.Card.ClientRectangle, FpsUi.Card, r.Sel.On ? FpsUi.GoldDim : FpsUi.Border, 12);
            OnyxDesign.Text(g, r.Name, FpsUi.H3, FpsUi.Ink, new Rectangle(18, 20, w - 100, 24));
            OnyxDesign.Text(g, r.Desc, FpsUi.Small, FpsUi.Dim, new Rectangle(18, 57, w - 36, 56), true);
            OnyxDesign.Text(g, r.Hint, FpsUi.Tiny, r.Id == 2 ? FpsUi.Warn : FpsUi.Gold, new Rectangle(18, 124, w - 36, 20));
        }
        private void PaintSummary(object sender, PaintEventArgs e)
        {
            OnyxDesign.PaintCard(e.Graphics, _summary.ClientRectangle, FpsUi.Card, OnyxDesign.Edge, 12);
            OnyxDesign.Text(e.Graphics, _running ? "Maintenance en cours" : SelectedCount + " opération(s) sélectionnée(s)", FpsUi.H3, FpsUi.Ink, new Rectangle(20, 17, _clear.Left - 36, 24));
            OnyxDesign.Text(e.Graphics, _running ? _progress : "Vérifie ta sélection avant de lancer les opérations.", FpsUi.Small, FpsUi.Dim, new Rectangle(20, 46, _clear.Left - 36, 22));
        }
        private void RunSelected()
        {
            if (_running) return;
            var selected = _routines.FindAll(r => r.Sel.On);
            if (selected.Count == 0) return;
            string names = string.Join("\n", selected.ConvertAll(r => "• " + r.Name));
            if (MessageBox.Show(FindForm(), "Lancer ces opérations ?\n\n" + names + "\n\nSFC/DISM et l’optimisation des disques peuvent ouvrir une console et durer plusieurs minutes.", "Confirmer la maintenance", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            _running = true; _progress = "Préparation des opérations…"; UpdateSelection();
            Task.Run(() =>
            {
                var report = new System.Text.StringBuilder();
                foreach (var routine in selected)
                {
                    string name = routine.Name;
                    try { BeginInvoke((Action)(() => { if (IsDisposed) return; _progress = name; _summary.Invalidate(); })); } catch { }
                    try { report.AppendLine("• " + RunOne(routine.Id)); } catch (Exception ex) { report.AppendLine("• Erreur : " + ex.Message); }
                }
                try { BadgeStore.IncCheckups(1); BadgeCatalog.EvaluateEvents(); } catch { }
                try { BeginInvoke((Action)(() => {
                    if (IsDisposed) return;
                    _running = false; UpdateSelection();
                    MessageBox.Show(FindForm(), report.ToString(), "Compte rendu de maintenance", MessageBoxButtons.OK, MessageBoxIcon.Information);
                })); } catch { }
            });
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); PaintPremiumTitle(e.Graphics, "BILAN & SANTÉ", "Comprends l’état de ton PC et prépare son entretien.");
        }
        private sealed class DiagnosticCard : Button
        {
            private readonly int _index; private bool _hot;
            public DiagnosticCard(int index)
            {
                _index = index; Text = ToolNames[index]; AccessibleName = Text; BackColor = FpsUi.BgMain; Cursor = Cursors.Hand;
                FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                MouseEnter += (s, e) => { _hot = true; Invalidate(); }; MouseLeave += (s, e) => { _hot = false; Invalidate(); };
                GotFocus += (s, e) => Invalidate(); LostFocus += (s, e) => Invalidate();
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; g.Clear(BackColor);
                OnyxDesign.PaintCard(g, ClientRectangle, FpsUi.Card, _hot || (Focused && ShowFocusCues) ? FpsUi.GoldDim : FpsUi.Border, 12);
                OnyxDesign.IconWell(g, ToolIcons[_index], new Rectangle(18, 18, 34, 34), _hot);
                OnyxDesign.Text(g, Text, FpsUi.H3, FpsUi.Ink, new Rectangle(64, 24, Width - 108, 26));
                OnyxDesign.Text(g, "↗", FpsUi.H3, FpsUi.Gold, new Rectangle(Width - 34, 23, 24, 24));
                OnyxDesign.Text(g, ToolDescriptions[_index], FpsUi.Small, FpsUi.Dim, new Rectangle(18, 72, Width - 36, 48), true);
            }
        }
#if BTTEST
        internal void PreviewView(bool maintenance) { _tabs[maintenance ? 1 : 0].PerformClick(); }
        internal void PreviewBottom() { _scroll.ScrollTo(_content.Height); }
        internal void ValidateCheckupUi()
        {
            bool[] saved = _routines.ConvertAll(r => r.Sel.On).ToArray();
            PreviewView(true); _clear.PerformClick();
            if (SelectedCount != 0 || _run.Enabled) throw new InvalidOperationException("La maintenance vide doit être désactivée.");
            var click = typeof(ToggleSwitch).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            click.Invoke(_routines[2].Sel, new object[] { EventArgs.Empty });
            if (SelectedCount != 1 || !_run.Enabled) throw new InvalidOperationException("Sélection d’une opération incorrecte.");
            PreviewView(false); PreviewView(true);
            if (!_routines[2].Sel.On) throw new InvalidOperationException("La navigation perd la sélection.");
            for (int i = 0; i < saved.Length; i++) _routines[i].Sel.On = saved[i];
            UpdateSelection();
            if (_scroll.Bottom > _summary.Top || !ClientRectangle.Contains(_summary.Bounds)) throw new InvalidOperationException("Le pied de page masque les opérations.");
            _scroll.ValidateScrolling();
            _routines[5].Sel.Focus();
            int focusBottom = _scroll.PointToClient(_routines[5].Sel.PointToScreen(new Point(0, _routines[5].Sel.Height))).Y;
            if (focusBottom > _scroll.Height) throw new InvalidOperationException("La sélection au clavier doit défiler vers l’opération.");
        }
#endif
        private string RunOne(int id)
        {
            switch (id)
            {
                case 0: return "Fichiers temporaires : " + CleanDirs(new[] { Path.GetTempPath(), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp") }) + " libéré(s).";
                case 1: return Maintenance.ResumeLancement(Console("defrag", "/C /O", false), "Optimisation des disques");
                case 2:
                    string la = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    return "Caches GPU : " + CleanDirs(new[] { Path.Combine(la, "D3DSCache"), Path.Combine(la, @"NVIDIA\DXCache"), Path.Combine(la, @"NVIDIA\GLCache") }) + " libéré(s).";
                case 3:
                {
                    // La description promet trois choses : fichiers récents, Jump Lists ET cache de
                    // miniatures. Les deux premières étaient faites (les Jump Lists vivent dans des
                    // sous-dossiers de Recent) ; la troisième, jamais — la promesse était vide.
                    string rec = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Recent");
                    string expl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\Explorer");
                    long freed = CleanOne(rec) + CleanMotif(expl, Maintenance.EstCacheMiniature);
                    return "Historique Windows et cache de miniatures : " + Taille(freed) + " nettoyé(s).";
                }
                case 4: return Maintenance.ResumeLancement(
                    Console("cmd.exe", "/k sfc /scannow & DISM /Online /Cleanup-Image /RestoreHealth", true),
                    "Réparation système (SFC + DISM)");
                case 5:
                {
                    // Commandes COURTES : on les attend et on lit leur code de retour. Annoncer
                    // « vidés » sans avoir regardé était une confirmation qui ne reposait sur rien.
                    bool dns = false, arp = false;
                    try { dns = Sys.Run(Sys.Sys32("ipconfig.exe"), "/flushdns").ExitCode == 0; } catch { }
                    try { arp = Sys.Run(Sys.Sys32("netsh.exe"), "interface ip delete arpcache").ExitCode == 0; } catch { }
                    return Maintenance.ResumeReseau(dns, arp);
                }
                default: return "Routine inconnue.";
            }
        }

        private static string CleanDirs(string[] dirs)
        {
            long freed = 0;
            foreach (string d in dirs) freed += CleanOne(d);
            return Taille(freed);
        }

        private static string Taille(long octets)
        {
            double mb = octets / (1024.0 * 1024.0);
            return mb >= 1 ? mb.ToString("0.0") + " Mo" : (octets / 1024.0).ToString("0") + " Ko";
        }

        /// <summary>Supprime, dans un dossier et lui seul, les fichiers retenus par le filtre.
        /// Sert là où le dossier contient aussi des fichiers qui ne sont PAS des caches.</summary>
        private static long CleanMotif(string dir, Func<string, bool> garde)
        {
            long freed = 0;
            if (!Directory.Exists(dir)) return 0;
            try
            {
                foreach (string f in Directory.EnumerateFiles(dir))
                {
                    if (!garde(Path.GetFileName(f))) continue;
                    try { var fi = new FileInfo(f); long sz = fi.Length; fi.Delete(); freed += sz; } catch { }
                }
            }
            catch { }
            return freed;
        }

        // Parcours récursif résilient : un sous-dossier protégé (accès refusé) n'interrompt
        // pas tout le nettoyage (contrairement à GetFiles(AllDirectories) qui lève et abandonne).
        private static long CleanOne(string dir)
        {
            long freed = 0;
            if (!Directory.Exists(dir)) return 0;
            try
            {
                foreach (string f in Directory.EnumerateFiles(dir))
                {
                    try { var fi = new FileInfo(f); long sz = fi.Length; fi.Delete(); freed += sz; } catch { }
                }
            }
            catch { }
            try
            {
                foreach (string sub in Directory.EnumerateDirectories(dir)) freed += CleanOne(sub);
            }
            catch { }
            return freed;
        }

        /// <summary>Lance une console. Rend VRAI seulement si le processus a réellement démarré :
        /// l'échec était avalé, et le compte-rendu annonçait quand même une réussite.</summary>
        private static bool Console(string exe, string args, bool keepOpen)
        {
            try
            {
                Process p = Process.Start(new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = true,
                    WindowStyle = keepOpen ? ProcessWindowStyle.Normal : ProcessWindowStyle.Minimized
                });
                return p != null;
            }
            catch { return false; }
        }

    }
}
