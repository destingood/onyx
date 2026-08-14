using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Benchmark rapide de PERFORMANCE (différent de l'analyse de latence) : mesure la
    /// puissance CPU (1 cœur et tous cœurs), la bande passante mémoire, et la vitesse du disque
    /// qui héberge le dossier TEMPORAIRE — pas forcément le disque système, ce que l'affichage
    /// prétendait sans jamais le vérifier. Le lecteur mesuré est donc nommé.
    ///
    /// Indicatif — utile pour comparer avant/après optimisation ou entre PC.
    /// </summary>
    internal static class PerfBench
    {
        public class Result
        {
            public double CpuSingleMops, CpuMultiMops;   // millions d'opérations / s
            public double RamGBs;                        // Go/s
            public double DiskWriteMBs, DiskReadMBs;     // Mo/s (négatif = mesure échouée)
            /// <summary>Lettre du lecteur RÉELLEMENT mesuré. Le test écrit dans le dossier
            /// temporaire, qui n'est pas forcément sur le disque système — l'affichage annonçait
            /// « Disque système » sans jamais le vérifier.</summary>
            public string DiskLettre = "";
            /// <summary>Type de média tel que Windows le déclare (3 = mécanique, 4 = SSD,
            /// 0 = inconnu). Lu, pas déduit du débit.</summary>
            public int DiskMedia;
        }

        // Charge de calcul mixte entier/flottant ; retourne un accumulateur pour éviter que le JIT ne l'élimine.
        private static double Workload(int inner)
        {
            double acc = 1.0; long x = 12345;
            for (int i = 0; i < inner; i++)
            {
                x = x * 1103515245 + 12345;
                acc += Math.Sqrt((double)((x >> 16) & 0xFFFF) + 1.0);
            }
            return acc;
        }

        private static double CpuBench(int threads, int durationMs)
        {
            const int inner = 2048;   // opérations par itération
            long totalIters = 0;
            // L'accumulateur ne sert qu'à empêcher le compilateur d'éliminer le calcul. Il était
            // partagé et écrit par tous les threads sans synchronisation : une course de données,
            // sans effet sur le résultat (le comptage passe par Interlocked) mais qui fait écrire
            // les cœurs sur la même ligne de cache — exactement ce qu'un banc d'essai CPU doit
            // éviter. Chaque thread garde donc le sien.
            long sinkGarde = 0;
            var work = new Action(() =>
            {
                var sw = Stopwatch.StartNew();
                long iters = 0;
                double local = 0;
                while (sw.ElapsedMilliseconds < durationMs) { local += Workload(inner); iters++; }
                Interlocked.Add(ref totalIters, iters);
                Interlocked.Add(ref sinkGarde, (long)local & 1);   // consomme « local » sans le partager
            });

            var sw2 = Stopwatch.StartNew();
            if (threads <= 1) work();
            else
            {
                var ts = new Thread[threads];
                for (int i = 0; i < threads; i++) { ts[i] = new Thread(new ThreadStart(work)); ts[i].Start(); }
                foreach (Thread t in ts) t.Join();
            }
            sw2.Stop();
            GC.KeepAlive(sinkGarde);
            double ops = (double)totalIters * inner;
            return ops / sw2.Elapsed.TotalSeconds / 1e6;   // MOPS
        }

        private static double RamBench()
        {
            int n = 32 * 1024 * 1024 / 8;   // 32 Mo de doubles
            double[] a = new double[n];
            for (int i = 0; i < n; i++) a[i] = i;            // amorçage (fault-in des pages)
            double best = 0;
            for (int pass = 0; pass < 3; pass++)
            {
                var sw = Stopwatch.StartNew();
                double s = 0;
                for (int i = 0; i < n; i++) s += a[i];
                sw.Stop();
                GC.KeepAlive(s);
                double gbs = (n * 8.0) / sw.Elapsed.TotalSeconds / 1e9;
                if (gbs > best) best = gbs;
            }
            return best;
        }

        private static void DiskBench(out double writeMBs, out double readMBs)
        {
            writeMBs = readMBs = -1;
            string tmp = Path.Combine(Path.GetTempPath(), "bt-perfbench.tmp");
            const long total = 128L * 1024 * 1024;   // 128 Mo
            byte[] buf = new byte[4 * 1024 * 1024];
            for (int i = 0; i < buf.Length; i++) buf[i] = (byte)i;
            try
            {
                var sw = Stopwatch.StartNew();
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, buf.Length, FileOptions.WriteThrough))
                {
                    long w = 0;
                    while (w < total) { fs.Write(buf, 0, buf.Length); w += buf.Length; }
                    fs.Flush(true);
                }
                sw.Stop();
                writeMBs = (total / 1048576.0) / sw.Elapsed.TotalSeconds;

                sw.Restart();
                using (var fs = new FileStream(tmp, FileMode.Open, FileAccess.Read, FileShare.None, buf.Length, FileOptions.SequentialScan))
                {
                    long r; long read = 0;
                    while ((r = fs.Read(buf, 0, buf.Length)) > 0) read += r;
                    GC.KeepAlive(read);
                }
                sw.Stop();
                readMBs = (total / 1048576.0) / sw.Elapsed.TotalSeconds;
            }
            catch { }
            finally { try { File.Delete(tmp); } catch { } }
        }

        public static Result Run(Action<string> progress)
        {
            var r = new Result();
            if (progress != null) progress("CPU (1 cœur)...");
            r.CpuSingleMops = CpuBench(1, 1500);
            if (progress != null) progress("CPU (tous les cœurs)...");
            r.CpuMultiMops = CpuBench(Environment.ProcessorCount, 1500);
            if (progress != null) progress("Mémoire...");
            r.RamGBs = RamBench();
            if (progress != null) progress("Disque...");
            DiskBench(out r.DiskWriteMBs, out r.DiskReadMBs);

            // Quel disque vient-on RÉELLEMENT de mesurer, et qu'en dit Windows ? Deviner le média
            // à partir du débit était la seule façon de se tromper — la réponse est disponible.
            try
            {
                string racine = Path.GetPathRoot(Path.GetTempPath());
                if (!string.IsNullOrEmpty(racine))
                {
                    r.DiskLettre = racine.TrimEnd('\\');
                    var types = Diagnostics.DriveTypes();
                    Diagnostics.DriveKind k;
                    if (types != null && types.TryGetValue(char.ToUpperInvariant(racine[0]), out k) && k != null)
                        r.DiskMedia = k.MediaType;
                }
            }
            catch { }
            return r;
        }
    }

    /// <summary>Panneau Benchmark rapide : lance les mesures et affiche les résultats + un verdict.</summary>
    internal class BenchForm : Form
    {
        private readonly Action<string, int> _log;
        private Label _cpu1, _cpuN, _ram, _disk, _verdict, _status;
        private Button _btnRun, _btnClose;

        private static readonly Color Accent = Theme.AccentColor;

        public BenchForm(Action<string, int> log)
        {
            _log = log;
            Build();
            Theme.Apply(this);
        }

        private void Build()
        {
            Text = "ONYX — Benchmark rapide";
            ClientSize = new Size(560, 420);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = Color.FromArgb(245, 246, 248);
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  Benchmark rapide — la puissance de ton PC",
                Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 12.5f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            var hint = new Label
            {
                Text = "Mesure indicative en ~7 s : CPU (1 cœur et tous cœurs), mémoire, disque système. "
                     + "Ferme les autres applis pour un résultat fiable. Compare avant/après optimisation.",
                Location = new Point(18, 60), Size = new Size(524, 40), ForeColor = Color.FromArgb(60, 64, 72)
            };
            Controls.Add(hint);

            _cpu1 = Card(18, 104, "CPU — 1 cœur");
            _cpuN = Card(18, 156, "CPU — tous les cœurs");
            _ram = Card(18, 208, "Mémoire (bande passante)");
            _disk = Card(18, 260, "Disque système");
            Controls.Add(_cpu1); Controls.Add(_cpuN); Controls.Add(_ram); Controls.Add(_disk);

            _verdict = new Label
            {
                Location = new Point(18, 314), Size = new Size(524, 40), ForeColor = Color.FromArgb(60, 64, 72),
                Font = new Font("Segoe UI", 9.5f)
            };
            Controls.Add(_verdict);

            _status = new Label { Location = new Point(18, 360), Size = new Size(300, 22), ForeColor = Color.Gray };
            Controls.Add(_status);

            _btnRun = MakeBtn("▶ Lancer le benchmark", 18, 378, 220, 34, true);
            _btnRun.Click += OnRun;
            _btnClose = MakeBtn("Fermer", 452, 378, 90, 34, false);
            _btnClose.Click += (s, e) => Close();
            Controls.Add(_btnRun); Controls.Add(_btnClose);
        }

        private static Label Card(int x, int y, string title)
        {
            return new Label
            {
                Location = new Point(x, y), Size = new Size(524, 46),
                Font = new Font("Consolas", 10.5f), ForeColor = Color.FromArgb(40, 44, 52),
                BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(10, 4, 6, 4),
                TextAlign = ContentAlignment.MiddleLeft, Text = title + " : —"
            };
        }

        private static Button MakeBtn(string text, int x, int y, int w, int h, bool primary)
        {
            var b = new Button
            {
                Text = text, Location = new Point(x, y), Size = new Size(w, h), FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Color.FromArgb(40, 44, 52),
                Font = primary ? new Font("Segoe UI Semibold", 9.5f) : new Font("Segoe UI", 9f)
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            return b;
        }

        private void OnRun(object sender, EventArgs e)
        {
            _btnRun.Enabled = false;
            Cursor = Cursors.WaitCursor;
            _cpu1.Text = "CPU — 1 cœur : mesure...";
            Task.Run(() =>
            {
                PerfBench.Result r = PerfBench.Run(msg =>
                {
                    try { BeginInvoke((Action)(() => _status.Text = msg)); } catch { }
                });
                try { BeginInvoke((Action)(() => Show(r))); } catch { }
            });
        }

        private void Show(PerfBench.Result r)
        {
            _cpu1.Text = "CPU — 1 cœur : " + r.CpuSingleMops.ToString("N0") + " Mops/s";
            _cpuN.Text = "CPU — tous les cœurs (" + Environment.ProcessorCount + ") : " + r.CpuMultiMops.ToString("N0") + " Mops/s";
            _ram.Text = "Mémoire : " + r.RamGBs.ToString("0.0") + " Go/s (lecture séquentielle)";
            // On nomme le lecteur mesuré : le test écrit dans le dossier temporaire, qui n'est pas
            // toujours sur le disque système. Et un échec (-1) ne s'affiche plus comme un chiffre.
            _disk.Text = "Disque " + (string.IsNullOrEmpty(r.DiskLettre) ? "(temporaire)" : r.DiskLettre)
                + " : écriture " + VerdictDisque.Debit(r.DiskWriteMBs)
                + " · lecture " + VerdictDisque.Debit(r.DiskReadMBs);

            // Verdict indicatif (seuils larges pour un PC de jeu actuel).
            bool cpuGood = r.CpuMultiMops > 8000;
            bool ramGood = r.RamGBs > 8;
            bool diskGood = r.DiskReadMBs > 400;
            int good = (cpuGood ? 1 : 0) + (ramGood ? 1 : 0) + (diskGood ? 1 : 0);

            string surDisque = VerdictDisque.Media(r.DiskMedia, r.DiskReadMBs, r.DiskLettre);
            if (surDisque != null)
            {
                _verdict.ForeColor = Color.FromArgb(200, 110, 0);
                _verdict.Text = "→ " + surDisque;
            }
            else if (good == 3)
            {
                _verdict.ForeColor = Accent;
                _verdict.Text = "✔ Machine performante et équilibrée. Refais le test après optimisation pour comparer.";
            }
            else
            {
                _verdict.ForeColor = Color.FromArgb(60, 64, 72);
                _verdict.Text = "Résultats enregistrés au journal. Ferme les applis de fond (voir « Qui ralentit mon PC ») "
                    + "et relance pour un score plus représentatif.";
            }

            _status.Text = "";
            if (_log != null)
                _log("Benchmark : CPU 1c " + r.CpuSingleMops.ToString("N0") + " / multi " + r.CpuMultiMops.ToString("N0")
                    + " Mops/s, RAM " + r.RamGBs.ToString("0.0") + " Go/s, disque " + r.DiskLettre
                    + " L " + VerdictDisque.Debit(r.DiskReadMBs)
                    + " / E " + VerdictDisque.Debit(r.DiskWriteMBs) + ".", 0);
            _btnRun.Enabled = true;
            Cursor = Cursors.Default;
        }
    }
}
