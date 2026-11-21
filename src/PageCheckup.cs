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
    // Page Bilan & entretien : routines de maintenance sélectionnables, récapitulatif
    // et exécution réelle (nettoyage, caches, réseau, SFC/DISM, defrag).
    internal class PageCheckup : FpsPage
    {
        private class Routine { public string Name, Desc; public int Id; public ToggleSwitch Sel; }

        private readonly List<Routine> _routines = new List<Routine>();
        private Panel _summary;
        private Button _run;

        public PageCheckup(DashboardForm host) : base(host)
        {
            Build();
        }

        public override void OnShown() { DoLayout(); }

        private void Build()
        {
            Add(0, "Nettoyage des fichiers temporaires", "Supprime les fichiers temporaires Windows et applications. Libère de l'espace disque.");
            Add(1, "Optimisation des disques", "TRIM sur les SSD, défragmentation consolidée sur les HDD.");
            Add(2, "Réinitialisation des caches GPU", "Supprime les caches de shaders DirectX/NVIDIA. Seulement en cas d'artefacts après une MAJ de pilote : sinon, saccades le temps de les recompiler.");
            Add(3, "Suppression de l'historique Windows", "Efface les fichiers récents, le cache de miniatures et les Jump Lists.");
            Add(4, "Réparation des fichiers système", "Exécute SFC + DISM pour réparer les fichiers système corrompus.");
            Add(5, "Rafraîchissement réseau", "Vide le cache DNS et le cache ARP de Windows.");

            _summary = new Panel();
            _summary.BackColor = Color.Transparent;
            _summary.Paint += PaintSummary;
            Controls.Add(_summary);

            _run = OnyxDesign.AccentButton("Lancer la maintenance  →");
            _run.Font = FpsUi.H3; _run.Height = 56;
            _run.Click += (s, e) => RunSelected();
            Controls.Add(_run);

            Resize += (s, e) => DoLayout();
        }

        private void Add(int id, string name, string desc)
        {
            var card = new Panel();
            card.Tag = "routine";
            card.BackColor = Color.Transparent;
            card.Paint += (s, e) =>
            {
                var selected = card.Controls["toggle"] as ToggleSwitch;
                bool active = selected != null && selected.On;
                OnyxDesign.PaintCard(e.Graphics, card.ClientRectangle, FpsUi.Card, active ? OnyxDesign.Edge : FpsUi.Border, 12);
                int icon = id == 0 || id == 1 || id == 3 ? NavIcons.Stockage : id == 2 ? NavIcons.Jeux : NavIcons.Systeme;
                OnyxDesign.IconWell(e.Graphics, icon, new Rectangle(18, 16, 32, 32), active);
            };

            var nm = new Label(); nm.Text = name; nm.Font = FpsUi.H3; nm.ForeColor = FpsUi.Ink; nm.BackColor = Color.Transparent;
            nm.Name = "title";
            nm.SetBounds(18, 16, 250, 44);
            var ds = new Label(); ds.Text = desc; ds.Font = FpsUi.Small; ds.ForeColor = FpsUi.Dim; ds.BackColor = Color.Transparent;
            ds.Name = "description";
            ds.SetBounds(18, 64, 300, 66);

            var sel = new ToggleSwitch(); sel.Location = new Point(292, 18); sel.On = (id == 0 || id == 5);   // shaders (2) décochés : les vider fait saccader
            sel.Name = "toggle";
            sel.AccessibleName = name;
            sel.Toggled += (s, e) => { card.Invalidate(); if (_summary != null) _summary.Invalidate(); };
            card.Controls.Add(nm); card.Controls.Add(ds); card.Controls.Add(sel);
            Controls.Add(card);
            _routines.Add(new Routine { Id = id, Name = name, Desc = desc, Sel = sel });
            OnyxDesign.HoverCard(card);
        }

        private void DoLayout()
        {
            int L = 34, top = Host != null ? Host.ContentTop(100) : 100, gap = 18, cols = 3;
            int w = (ClientSize.Width - L * 2 - gap * (cols - 1)) / cols, hgt = 170;
            int i = 0;
            foreach (var r in _routines)
            {
                Panel card = (Panel)r.Sel.Parent;
                int col = i % cols, row = i / cols;
                card.SetBounds(L + col * (w + gap), top + row * (hgt + gap), w, hgt);
                card.Controls["title"].SetBounds(62, 16, Math.Max(80, w - 136), 58);
                card.Controls["description"].SetBounds(18, 80, w - 36, 76);
                r.Sel.Location = new Point(w - r.Sel.Width - 18, 18);
                i++;
            }
            int by = top + 2 * (hgt + gap) + 6;
            if (_summary != null) _summary.SetBounds(L, by, w * 2 + gap, 92);
            if (_run != null) _run.SetBounds(L + w * 2 + gap + 24, by + 18, w - 24, 56);
        }

        private void PaintSummary(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle r = _summary.ClientRectangle;
            OnyxDesign.PaintCard(g, r, FpsUi.Card, FpsUi.Border, 12f);
            int selected = 0;
            foreach (var routine in _routines) if (routine.Sel.On) selected++;
            TextRenderer.DrawText(g, selected + (selected == 1 ? " opération sélectionnée" : " opérations sélectionnées"), FpsUi.H3, new Point(18, 18), FpsUi.Ink, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, "Choisis les opérations à lancer pour entretenir ton PC.", FpsUi.Body,
                new Rectangle(18, 46, r.Width - 36, 36), FpsUi.Dim, TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        }

        private void RunSelected()
        {
            var ids = new List<int>();
            foreach (var r in _routines) if (r.Sel.On) ids.Add(r.Id);
            if (ids.Count == 0) { MessageBox.Show(FindForm(), "Sélectionne au moins une routine.", "Check Up+", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (MessageBox.Show(FindForm(), "Lancer " + ids.Count + " routine(s) de maintenance ?\n\nCertaines (SFC/DISM, défragmentation) ouvriront une console et peuvent durer plusieurs minutes.",
                "Check Up+", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            _run.Enabled = false; _run.Text = "Check Up+ en cours...";
            Task.Run(() =>
            {
                var report = new System.Text.StringBuilder();
                foreach (int id in ids)
                {
                    try { report.AppendLine("• " + RunOne(id)); } catch (Exception ex) { report.AppendLine("• Erreur : " + ex.Message); }
                }
                try { BadgeStore.IncCheckups(1); BadgeCatalog.EvaluateEvents(); } catch { }   // débloque Infirmier / Routine (+ toast)
                try { BeginInvoke((Action)(() => { _run.Enabled = true; _run.Text = "Lancer la maintenance  →";
                    MessageBox.Show(FindForm(), report.ToString(), "Check Up+ terminé", MessageBoxButtons.OK, MessageBoxIcon.Information); })); }
                catch { }
            });
        }

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

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            PaintPremiumTitle(e.Graphics, "BILAN & ENTRETIEN", "Vérifie la santé de ton PC et choisis les opérations de maintenance utiles.");
        }
    }
}
