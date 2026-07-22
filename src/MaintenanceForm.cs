using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// 🩺 Routines d'entretien — la maintenance périodique du PC en 1 clic, façon « check-up ».
    /// Chaque routine réutilise les outils déjà présents (nettoyage, TRIM/défrag, reset des
    /// caches GPU, réparation Windows, rafraîchissement réseau) et mémorise sa dernière
    /// exécution pour rappeler quand la refaire. Rien de bloquant : purement indicatif.
    /// </summary>
    internal class MaintenanceForm : Form
    {
        private readonly Action<string, int> _log;
        private readonly List<Routine> _routines;
        private readonly Dictionary<string, long> _last;
        private Button _btnAll, _btnRam, _btnClose;
        private bool _busy;

        private static readonly Color Accent = Color.FromArgb(0, 150, 90);
        private static readonly Color Bg = Color.FromArgb(245, 246, 248);
        private static readonly Color Ink = Color.FromArgb(40, 44, 52);
        private static readonly Color Sub = Color.FromArgb(96, 100, 108);

        private sealed class Routine
        {
            public string Id;
            public string Title;
            public string Desc;
            public int RecommendDays;                 // cadence conseillée
            public bool Slow;                         // exclu du « Tout lancer (rapide) »
            public Action<Action<string, int>> Run;
            public Label Status;                      // référence pour rafraîchir l'état
            public Button Button;
        }

        public MaintenanceForm(Action<string, int> log)
        {
            _log = log;
            _last = LoadState();
            _routines = BuildRoutines();
            Build();
            RefreshStatuses();
            Theme.Apply(this);
        }

        // -------------------------------------------------------------- routines
        private List<Routine> BuildRoutines()
        {
            return new List<Routine>
            {
                new Routine
                {
                    Id = "temp", RecommendDays = 7, Slow = false,
                    Title = "🧹 Nettoyage des fichiers temporaires",
                    Desc = "Vide les dossiers temporaires, caches des navigateurs, rapports d'erreurs et la corbeille.",
                    Run = delegate(Action<string, int> log)
                    {
                        long mb = 0;
                        foreach (Sys.CleanTarget t in Sys.CleanTargets()) mb += Sys.CleanTargetNow(t, log);
                        log("Nettoyage terminé : ~" + Math.Max(0, mb) + " Mo libérés. ✔", 1);
                    }
                },
                new Routine
                {
                    Id = "disk", RecommendDays = 30, Slow = true,
                    Title = "🖴 Optimisation des disques (TRIM SSD / défrag HDD)",
                    Desc = "Ré-applique le TRIM sur les SSD et défragmente les disques durs. Peut durer plusieurs minutes.",
                    Run = Sys.OptimizeDrives
                },
                new Routine
                {
                    Id = "gpu", RecommendDays = 15, Slow = false,
                    Title = "🎮 Réinitialisation des caches GPU",
                    Desc = "Vide les caches de shaders (DirectX / NVIDIA / AMD / Vulkan) et lève les verrous de fréquence. À faire après une mise à jour de pilote ou en cas de saccades.",
                    Run = delegate(Action<string, int> log)
                    {
                        Sys.ResetGpuLocks(log);
                        long mb = 0;
                        foreach (Sys.CleanTarget t in Sys.CleanTargets())
                            if (t.Name != null && t.Name.IndexOf("Shaders", StringComparison.OrdinalIgnoreCase) >= 0)
                                mb += Sys.CleanTargetNow(t, log);
                        log("Caches GPU réinitialisés (~" + Math.Max(0, mb) + " Mo). Les shaders se recompilent au 1er lancement (saccades passagères normales). ✔", 1);
                    }
                },
                new Routine
                {
                    Id = "history", RecommendDays = 15, Slow = false,
                    Title = "🗂 Suppression de l'historique Windows",
                    Desc = "Efface les documents récents, les listes de raccourcis (Jump Lists) et le cache des miniatures. Vie privée + Explorateur allégé.",
                    Run = Sys.CleanWindowsHistory
                },
                new Routine
                {
                    Id = "repair", RecommendDays = 90, Slow = true,
                    Title = "🔧 Réparation des fichiers système (DISM + SFC)",
                    Desc = "Vérifie et répare l'image de Windows puis les fichiers système corrompus. Opération longue (10–20 min).",
                    Run = Sys.RepairWindows
                },
                new Routine
                {
                    Id = "network", RecommendDays = 15, Slow = false,
                    Title = "🌐 Rafraîchissement du réseau (DNS + ARP)",
                    Desc = "Vide le cache DNS, la table ARP et le cache NetBIOS puis réenregistre le DNS. Sans réinitialisation dure ni redémarrage.",
                    Run = Sys.RefreshNetwork
                }
            };
        }

        // -------------------------------------------------------------- interface
        private void Build()
        {
            Text = "DesTinGOOD — Routines d'entretien";
            ClientSize = new Size(726, 588);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = Bg;
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 54, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  🩺 Routines d'entretien — garde ton PC comme neuf",
                Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 13f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            var intro = new Label
            {
                Text = "Un PC jamais entretenu se dégrade en silence. Lance ces routines de temps en temps : "
                     + "chacune retient sa dernière exécution et te dit quand la refaire.",
                Location = new Point(18, 62), Size = new Size(690, 38), ForeColor = Sub
            };
            Controls.Add(intro);

            _btnAll = MakeBtn("▶  Tout lancer (routines rapides)", 18, 104, 300, 40, true);
            _btnAll.Click += OnRunAll;
            Controls.Add(_btnAll);

            var note = new Label
            {
                Text = "Enchaîne les routines rapides (temp, GPU, historique, réseau). "
                     + "Les longues (disques, réparation) se lancent une par une.",
                Location = new Point(330, 104), Size = new Size(378, 40), ForeColor = Sub,
                TextAlign = ContentAlignment.MiddleLeft
            };
            Controls.Add(note);

            var flow = new FlowLayoutPanel
            {
                Location = new Point(18, 156), Size = new Size(690, 372),
                AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false,
                BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(flow);
            for (int i = 0; i < _routines.Count; i++) flow.Controls.Add(BuildRow(_routines[i], i));

            _btnRam = MakeBtn("🧠  Tester la RAM (redémarrage)", 18, 540, 260, 36, false);
            _btnRam.Click += OnRamTest;
            Controls.Add(_btnRam);

            _btnClose = MakeBtn("Fermer", 626, 540, 82, 36, false);
            _btnClose.Click += delegate { Close(); };
            Controls.Add(_btnClose);
        }

        private Panel BuildRow(Routine r, int index)
        {
            var row = new Panel
            {
                Size = new Size(666, 96), Margin = new Padding(0),
                BackColor = index % 2 == 0 ? Color.White : Color.FromArgb(248, 249, 251)
            };

            row.Controls.Add(new Label
            {
                Text = r.Title, Location = new Point(14, 10), Size = new Size(500, 22),
                Font = new Font("Segoe UI Semibold", 10.5f), ForeColor = Ink
            });
            row.Controls.Add(new Label
            {
                Text = r.Desc, Location = new Point(14, 33), Size = new Size(508, 34), ForeColor = Sub
            });
            r.Status = new Label
            {
                Location = new Point(14, 70), Size = new Size(508, 18),
                Font = new Font("Segoe UI", 8.5f), ForeColor = Sub
            };
            row.Controls.Add(r.Status);

            r.Button = MakeBtn("Lancer", 540, 30, 108, 40, true);
            Routine captured = r;
            r.Button.Click += delegate { RunRoutine(captured); };
            row.Controls.Add(r.Button);

            var sep = new Panel { Location = new Point(0, 95), Size = new Size(666, 1), BackColor = Color.FromArgb(232, 234, 238) };
            row.Controls.Add(sep);
            return row;
        }

        private static Button MakeBtn(string text, int x, int y, int w, int h, bool primary)
        {
            var b = new Button
            {
                Text = text, Location = new Point(x, y), Size = new Size(w, h), FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Accent : Color.White,
                ForeColor = primary ? Color.White : Ink,
                Font = primary ? new Font("Segoe UI Semibold", 9.5f) : new Font("Segoe UI", 9f)
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            return b;
        }

        // -------------------------------------------------------------- exécution
        private void SetBusy(bool busy)
        {
            _busy = busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            _btnAll.Enabled = !busy; _btnRam.Enabled = !busy;
            foreach (Routine r in _routines) if (r.Button != null) r.Button.Enabled = !busy;
        }

        private void RunRoutine(Routine r)
        {
            if (_busy) return;
            if (r.Slow)
            {
                string warn = r.Id == "repair"
                    ? "Cette réparation (DISM + SFC) peut prendre 10 à 20 minutes. Continuer ?"
                    : "Cette optimisation des disques peut prendre plusieurs minutes. Continuer ?";
                if (MessageBox.Show(this, warn, r.Title, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                    return;
            }
            SetBusy(true);
            _log("Routine « " + Strip(r.Title) + " » démarrée...", 0);
            Task.Run(delegate
            {
                try { r.Run(_log); }
                catch (Exception ex) { _log("Routine « " + Strip(r.Title) + " » : " + ex.Message, 3); }
                Stamp(r.Id);
                Done();
            });
        }

        private void OnRunAll(object sender, EventArgs e)
        {
            if (_busy) return;
            var fast = new List<Routine>();
            foreach (Routine r in _routines) if (!r.Slow) fast.Add(r);
            if (MessageBox.Show(this,
                    "Lancer les " + fast.Count + " routines rapides à la suite ?\n\n"
                    + "Nettoyage des temporaires · caches GPU · historique Windows · rafraîchissement réseau.",
                    "Tout lancer", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            SetBusy(true);
            _log("Entretien : enchaînement de " + fast.Count + " routines rapides...", 0);
            Task.Run(delegate
            {
                foreach (Routine r in fast)
                {
                    try { r.Run(_log); Stamp(r.Id); }
                    catch (Exception ex) { _log("Routine « " + Strip(r.Title) + " » : " + ex.Message, 3); }
                }
                _log("Entretien rapide terminé. ✔", 1);
                Done();
            });
        }

        private void OnRamTest(object sender, EventArgs e)
        {
            if (_busy) return;
            if (MessageBox.Show(this,
                    "Ouvrir le Diagnostic de mémoire Windows ?\n\n"
                    + "Windows proposera de redémarrer maintenant ou au prochain démarrage pour tester la RAM "
                    + "(une RAM défaillante provoque plantages et écrans bleus). Enregistre ton travail avant.",
                    "Tester la RAM", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                return;
            Sys.LaunchMemoryDiagnostic(_log);
        }

        private void Done()
        {
            try { BeginInvoke((Action)(delegate { SetBusy(false); RefreshStatuses(); })); }
            catch { }
        }

        // -------------------------------------------------------------- état / persistance
        private void RefreshStatuses()
        {
            long now = Now();
            foreach (Routine r in _routines)
            {
                if (r.Status == null) continue;
                long ts;
                if (!_last.TryGetValue(r.Id, out ts) || ts <= 0)
                {
                    r.Status.Text = "Jamais exécutée · conseillé tous les " + r.RecommendDays + " j";
                    r.Status.ForeColor = Sub;
                    continue;
                }
                int days = (int)Math.Max(0, (now - ts) / 86400);
                string ago = days == 0 ? "aujourd'hui" : "il y a " + days + " j";
                if (days >= r.RecommendDays)
                {
                    r.Status.Text = "⚠ à refaire — dernière exécution " + ago;
                    r.Status.ForeColor = Color.FromArgb(190, 120, 0);
                }
                else
                {
                    r.Status.Text = "✔ fait " + ago + " · prochaine conseillée dans " + (r.RecommendDays - days) + " j";
                    r.Status.ForeColor = Accent;
                }
            }
        }

        private void Stamp(string id)
        {
            lock (_last) { _last[id] = Now(); SaveState(_last); }
        }

        private static long Now()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        private static string StatePath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-maintenance.txt");
        }

        private static Dictionary<string, long> LoadState()
        {
            var d = new Dictionary<string, long>();
            try
            {
                string p = StatePath();
                if (!File.Exists(p)) return d;
                foreach (string line in File.ReadAllLines(p))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    long val;
                    if (long.TryParse(line.Substring(eq + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out val))
                        d[key] = val;
                }
            }
            catch { }
            return d;
        }

        private static void SaveState(Dictionary<string, long> d)
        {
            try
            {
                var lines = new List<string>();
                foreach (KeyValuePair<string, long> kv in d)
                    lines.Add(kv.Key + "=" + kv.Value.ToString(CultureInfo.InvariantCulture));
                File.WriteAllLines(StatePath(), lines.ToArray());
            }
            catch { }
        }

        // Retire l'émoji de tête pour les messages du journal.
        private static string Strip(string title)
        {
            if (string.IsNullOrEmpty(title)) return title;
            int i = 0;
            while (i < title.Length && (char.IsWhiteSpace(title[i]) || char.IsSurrogate(title[i]) || title[i] > 0x2000)) i++;
            return i > 0 && i < title.Length ? title.Substring(i) : title;
        }
    }
}
