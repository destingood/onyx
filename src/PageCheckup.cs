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
    // Page Check Up+ : routines de maintenance selectionnables + ligne cardiaque
    // animee + execution reelle (nettoyage, caches, reseau, SFC/DISM, defrag).
    internal class PageCheckup : FpsPage
    {
        private class Routine { public string Name, Desc; public int Id; public ToggleSwitch Sel; }

        private readonly List<Routine> _routines = new List<Routine>();
        private Panel _heart;
        private Button _run;
        private readonly Timer _beat = new Timer();
        private float _phase;

        public PageCheckup(DashboardForm host) : base(host)
        {
            Build();
            _beat.Interval = 40;
            _beat.Tick += (s, e) => { _phase += 0.06f; if (_heart != null) _heart.Invalidate(); };
        }

        public override void OnShown() { DoLayout(); _beat.Start(); }
        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (!Visible) { try { _beat.Stop(); } catch { } } }
        protected override void OnHandleDestroyed(EventArgs e) { try { _beat.Stop(); } catch { } base.OnHandleDestroyed(e); }

        private void Build()
        {
            Add(0, "Nettoyage des fichiers temporaires", "Supprime les fichiers temporaires Windows et applications. Libère de l'espace disque.");
            Add(1, "Optimisation des disques", "TRIM sur les SSD, défragmentation consolidée sur les HDD.");
            Add(2, "Réinitialisation des caches GPU", "Supprime les caches de shaders DirectX/NVIDIA. Résout stutters et artefacts.");
            Add(3, "Suppression de l'historique Windows", "Efface les fichiers récents, le cache de miniatures et les Jump Lists.");
            Add(4, "Réparation des fichiers système", "Exécute SFC + DISM pour réparer les fichiers système corrompus.");
            Add(5, "Rafraîchissement réseau", "Vide le cache DNS et le cache ARP de Windows.");

            _heart = new Panel();
            _heart.BackColor = Color.Transparent;
            _heart.Paint += PaintHeart;
            Controls.Add(_heart);

            _run = FpsUi.GhostButton("💉   LANCER LE CHECK UP+");
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
            card.Paint += (s, e) => FpsUi.PaintCard(e.Graphics, ((Panel)s).ClientRectangle, FpsUi.Card, FpsUi.Border, 12f);

            var nm = new Label(); nm.Text = name; nm.Font = FpsUi.H3; nm.ForeColor = FpsUi.Ink; nm.BackColor = Color.Transparent;
            nm.SetBounds(18, 16, 250, 44);
            var ds = new Label(); ds.Text = desc; ds.Font = FpsUi.Small; ds.ForeColor = FpsUi.Dim; ds.BackColor = Color.Transparent;
            ds.SetBounds(18, 64, 300, 66);

            var sel = new ToggleSwitch(); sel.Location = new Point(292, 18); sel.On = (id == 0 || id == 2 || id == 5);
            card.Controls.Add(nm); card.Controls.Add(ds); card.Controls.Add(sel);
            Controls.Add(card);
            _routines.Add(new Routine { Id = id, Name = name, Desc = desc, Sel = sel });
        }

        private void DoLayout()
        {
            int L = 34, top = 100, gap = 18, cols = 3;
            int w = (ClientSize.Width - L * 2 - gap * (cols - 1)) / cols, hgt = 150;
            int i = 0;
            foreach (var r in _routines)
            {
                Panel card = (Panel)r.Sel.Parent;
                int col = i % cols, row = i / cols;
                card.SetBounds(L + col * (w + gap), top + row * (hgt + gap), w, hgt);
                i++;
            }
            int by = top + 2 * (hgt + gap) + 6;
            if (_heart != null) _heart.SetBounds(L, by, w * 2 + gap, 92);
            if (_run != null) _run.SetBounds(L + w * 2 + gap + 24, by + 18, w - 24, 56);
        }

        private void PaintHeart(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle r = _heart.ClientRectangle;
            FpsUi.PaintCard(g, r, FpsUi.Card, FpsUi.Border, 12f);
            TextRenderer.DrawText(g, "RYTHME CARDIAQUE DU SYSTÈME", FpsUi.Small, new Point(16, 12), FpsUi.Dim, TextFormatFlags.NoPadding);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int midY = r.Height / 2 + 12, left = 16, right = r.Width - 16;
            var pts = new List<PointF>();
            for (int x = left; x <= right; x += 3)
            {
                float t = (x - left) * 0.06f + _phase;
                float baseY = midY;
                float m = t % 6.283f;
                float spike = 0;
                float local = m % 2.0f;
                if (local < 0.5f) spike = -(0.5f - Math.Abs(local - 0.25f) * 4) * 34;
                pts.Add(new PointF(x, baseY + spike));
            }
            using (var pen = new Pen(FpsUi.Neon, 2f)) g.DrawLines(pen, pts.ToArray());
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
                try { BeginInvoke((Action)(() => { _run.Enabled = true; _run.Text = "💉   LANCER LE CHECK UP+";
                    MessageBox.Show(FindForm(), report.ToString(), "Check Up+ terminé", MessageBoxButtons.OK, MessageBoxIcon.Information); })); }
                catch { }
            });
        }

        private string RunOne(int id)
        {
            switch (id)
            {
                case 0: return "Fichiers temporaires : " + CleanDirs(new[] { Path.GetTempPath(), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp") }) + " libéré(s).";
                case 1: Console("defrag", "/C /O", false); return "Optimisation des disques lancée (console).";
                case 2:
                    string la = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    return "Caches GPU : " + CleanDirs(new[] { Path.Combine(la, "D3DSCache"), Path.Combine(la, @"NVIDIA\DXCache"), Path.Combine(la, @"NVIDIA\GLCache") }) + " libéré(s).";
                case 3:
                    string rec = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Recent");
                    return "Historique Windows : " + CleanDirs(new[] { rec }) + " nettoyé(s).";
                case 4: Console("cmd.exe", "/k sfc /scannow ^& DISM /Online /Cleanup-Image /RestoreHealth", true); return "Réparation système lancée (console SFC + DISM).";
                case 5: Console("cmd.exe", "/c ipconfig /flushdns ^& netsh interface ip delete arpcache", false); return "Cache DNS et ARP vidés.";
                default: return "Routine inconnue.";
            }
        }

        private static string CleanDirs(string[] dirs)
        {
            long freed = 0;
            foreach (string d in dirs)
            {
                try
                {
                    if (!Directory.Exists(d)) continue;
                    foreach (string f in Directory.GetFiles(d, "*", SearchOption.AllDirectories))
                    {
                        try { var fi = new FileInfo(f); long sz = fi.Length; fi.Delete(); freed += sz; } catch { }
                    }
                }
                catch { }
            }
            double mb = freed / (1024.0 * 1024.0);
            return mb >= 1 ? mb.ToString("0.0") + " Mo" : (freed / 1024.0).ToString("0") + " Ko";
        }

        private static void Console(string exe, string args, bool keepOpen)
        {
            try { Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true, WindowStyle = keepOpen ? ProcessWindowStyle.Normal : ProcessWindowStyle.Minimized }); }
            catch { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            PaintTitle(e.Graphics, "ROUTINE CHECK UP+", "À réaliser régulièrement pour maintenir ton système dans un état clinique optimal.");
        }
    }
}
