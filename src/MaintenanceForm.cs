using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    // ----------------------------------------------------------------------
    //  Entretien du PC : les 6 routines d'entretien (façon « FPS doctor »)
    //  réunies dans UN panneau, chacune en 1 clic, avec la date du dernier
    //  passage mémorisée (bt-entretien.txt) et un rappel passé 15 jours.
    //  Aucun verrou commercial : le délai est un simple indicateur.
    //  Tout réutilise les moteurs existants (Sys.*) — rien de dupliqué.
    // ----------------------------------------------------------------------
    internal class MaintenanceForm : Form
    {
        private class Routine
        {
            public int Id;
            public string Name;
            public string Desc;
            public bool NeedsConfirm;   // touche des données personnelles → question avant
            public bool InAll;          // inclus dans « TOUT ENTRETENIR »
            public Action<Action<string, int>> Run;
            public Label When;
            public Button Btn;
        }

        private const int StaleDays = 15;   // même repère que le cooldown FPS doctor (indicatif)

        private readonly Action<string, int> _log;
        private readonly List<Routine> _routines = new List<Routine>();
        private Button _btnAll;
        private Label _status;
        private bool _busy;

        private static Color OkColor { get { return Theme.OkColor; } }   // suit le thème (néon en sombre)
        private static readonly Color WarnColor = Color.FromArgb(200, 120, 0);

        public MaintenanceForm(Action<string, int> log)
        {
            _log = log;
            BuildRoutines();
            BuildUi();
            Theme.Apply(this);
            RefreshWhenLabels();
        }

        private void BuildRoutines()
        {
            _routines.Add(new Routine
            {
                Id = 0, Name = "Nettoyage des fichiers temporaires", InAll = true,
                Desc = "Temp utilisateur/Windows, cache Windows Update, WER, crash dumps, journaux CBS, caches navigateurs.",
                Run = log => CleanKind("temp", log)
            });
            _routines.Add(new Routine
            {
                Id = 1, Name = "Optimisation des disques (TRIM SSD / défrag HDD)", InAll = true,
                Desc = "defrag /O sur chaque lecteur fixe : RE-TRIM pour les SSD, défragmentation pour les HDD.",
                Run = Sys.OptimizeDrives
            });
            _routines.Add(new Routine
            {
                Id = 2, Name = "Vidage des caches GPU (shaders)", InAll = true,
                Desc = "Shaders NVIDIA (DX/GL), DirectX Windows et AMD : à refaire après une MAJ de pilote ou en cas de stutters.",
                Run = log => CleanKind("gpu", log)
            });
            _routines.Add(new Routine
            {
                Id = 3, Name = "Suppression de l'historique Windows", NeedsConfirm = true, InAll = true,
                Desc = "Fichiers récents, Jump Lists (y compris épinglés) et cache des miniatures — reconstruits ensuite.",
                Run = log => CleanKind("history", log)
            });
            _routines.Add(new Routine
            {
                Id = 4, Name = "Réparation des fichiers système (DISM + SFC)", InAll = false,
                Desc = "DISM /RestoreHealth puis SFC /scannow — long (10-20 min), à lancer quand des crashs persistent.",
                Run = Sys.RepairWindows
            });
            _routines.Add(new Routine
            {
                Id = 5, Name = "Rafraîchissement réseau (DNS + ARP)", InAll = true,
                Desc = "Vide le cache DNS et le cache ARP — léger, sans coupure ni redémarrage.",
                Run = Sys.NetworkRefresh
            });
        }

        private void BuildUi()
        {
            Text = "Entretien du PC — Fluide";
            ClientSize = new Size(720, 478);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            Font = new Font("Segoe UI", 9f);

            var intro = new Label();
            intro.SetBounds(16, 10, 688, 34);
            intro.Text = "Les 6 routines d'entretien réunies : chaque ligne se lance en 1 clic, la date du dernier "
                       + "passage est mémorisée, et « à refaire » apparaît passé " + StaleDays + " jours (simple repère, rien n'est bloqué).";
            intro.ForeColor = Theme.InkDimColor;

            int y = 56;
            foreach (Routine r in _routines)
            {
                var name = new Label();
                name.SetBounds(16, y, 430, 18);
                name.Font = new Font("Segoe UI Semibold", 9f);
                name.Text = r.Name;

                var desc = new Label();
                desc.SetBounds(16, y + 18, 442, 16);
                desc.Font = new Font("Segoe UI", 8f);
                desc.ForeColor = Theme.InkDimColor;
                desc.Text = r.Desc;

                r.When = new Label();
                r.When.SetBounds(452, y + 8, 148, 20);
                r.When.TextAlign = ContentAlignment.MiddleRight;
                r.When.Font = new Font("Segoe UI", 8.5f);

                r.Btn = MakeBtn("Lancer", 608, y + 4, 94, 30, false);
                Routine captured = r;
                r.Btn.Click += (s, e) => RunRoutine(captured);

                Controls.AddRange(new Control[] { name, desc, r.When, r.Btn });
                y += 54;
            }

            _btnAll = MakeBtn("▶ TOUT ENTRETENIR (routines rapides, sans DISM/SFC)", 16, y + 6, 420, 38, true);
            _btnAll.Click += (s, e) => RunAll();

            _status = new Label();
            _status.SetBounds(448, y + 6, 256, 38);
            _status.TextAlign = ContentAlignment.MiddleLeft;
            _status.ForeColor = Theme.InkDimColor;
            _status.Text = "Prêt.";

            Controls.AddRange(new Control[] { intro, _btnAll, _status });
        }

        // --- Exécution ---------------------------------------------------------
        private void RunRoutine(Routine r)
        {
            if (_busy) return;
            if (r.NeedsConfirm &&
                MessageBox.Show(this,
                    "Cette routine supprime les fichiers récents, les Jump Lists (y compris tes éléments épinglés) "
                    + "et le cache des miniatures. Ils se reconstruisent ensuite.\n\nContinuer ?",
                    "Historique Windows", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            RunSequence(new List<Routine> { r }, r.Name + "...");
        }

        private void RunAll()
        {
            if (_busy) return;
            var seq = new List<Routine>();
            bool withHistory = MessageBox.Show(this,
                "Inclure la suppression de l'historique Windows ? (fichiers récents, Jump Lists — y compris "
                + "épinglés — et miniatures ; tout se reconstruit ensuite)\n\n"
                + "« Non » lance quand même les autres routines.",
                "TOUT ENTRETENIR", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
            foreach (Routine r in _routines)
                if (r.InAll && (withHistory || !r.NeedsConfirm)) seq.Add(r);
            RunSequence(seq, "Entretien complet : " + seq.Count + " routine(s) — quelques minutes...");
        }

        private void RunSequence(List<Routine> seq, string intro)
        {
            _busy = true;
            SetBusyUi(true);
            SetStatus(intro, 0);
            Task.Run(() =>
            {
                int ok = 0, ko = 0;
                foreach (Routine r in seq)
                {
                    try
                    {
                        RelayLog("Entretien : " + r.Name + "...", 0);
                        r.Run(RelayLog);
                        SaveStamp(r.Id);
                        ok++;
                    }
                    catch (Exception ex)
                    {
                        ko++;
                        RelayLog("Entretien ÉCHEC : " + r.Name + " -> " + ex.Message, 3);
                    }
                }
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        RefreshWhenLabels();
                        SetStatus("Terminé : " + ok + " routine(s) OK" + (ko > 0 ? ", " + ko + " échec(s)" : "") + ".",
                                  ko > 0 ? 2 : 1);
                        _busy = false;
                        SetBusyUi(false);
                    }));
                }
                catch { _busy = false; }
            });
        }

        /// <summary>Vide les cibles de nettoyage d'un groupe donné (temp / gpu / history).</summary>
        private static void CleanKind(string kind, Action<string, int> log)
        {
            int files = 0;
            long mb = 0;
            foreach (Sys.CleanTarget t in Sys.CleanTargets())
            {
                if (t.Kind != kind) continue;
                mb += t.SizeMB;
                files += Sys.CleanTargetNow(t, log);
            }
            log("Groupe « " + kind + " » : " + files + " fichier(s), ≈ " + mb.ToString("N0") + " Mo libérés.", 1);
        }

        // --- Mémoire du dernier passage (bt-entretien.txt) ---------------------
        private static string StampPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-entretien.txt"); }
        }

        private static Dictionary<int, DateTime> LoadStamps()
        {
            var map = new Dictionary<int, DateTime>();
            try
            {
                if (!File.Exists(StampPath)) return map;
                foreach (string line in File.ReadAllLines(StampPath))
                {
                    string[] p = line.Split('=');
                    int id; DateTime when;
                    if (p.Length == 2 && int.TryParse(p[0], out id)
                        && DateTime.TryParse(p[1], CultureInfo.InvariantCulture, DateTimeStyles.None, out when))
                        map[id] = when;
                }
            }
            catch { }
            return map;
        }

        private static void SaveStamp(int id)
        {
            try
            {
                Dictionary<int, DateTime> map = LoadStamps();
                map[id] = DateTime.Now;
                var lines = new List<string>();
                foreach (KeyValuePair<int, DateTime> kv in map)
                    lines.Add(kv.Key + "=" + kv.Value.ToString("s", CultureInfo.InvariantCulture));
                File.WriteAllLines(StampPath, lines);
            }
            catch { }
        }

        private void RefreshWhenLabels()
        {
            Dictionary<int, DateTime> map = LoadStamps();
            foreach (Routine r in _routines)
            {
                DateTime when;
                if (!map.TryGetValue(r.Id, out when))
                {
                    r.When.Text = "jamais fait · à faire";
                    r.When.ForeColor = WarnColor;
                    continue;
                }
                int days = (int)(DateTime.Now - when).TotalDays;
                if (days >= StaleDays)
                {
                    r.When.Text = "il y a " + days + " j · à refaire";
                    r.When.ForeColor = WarnColor;
                }
                else
                {
                    r.When.Text = days <= 0 ? "aujourd'hui ✔" : "il y a " + days + " j ✔";
                    r.When.ForeColor = OkColor;
                }
            }
        }

        // --- Plomberie ---------------------------------------------------------
        private void SetBusyUi(bool busy)
        {
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            _btnAll.Enabled = !busy;
            foreach (Routine r in _routines) r.Btn.Enabled = !busy;
        }

        private void SetStatus(string text, int level)
        {
            _status.Text = text;
            _status.ForeColor = level == 1 ? OkColor : level >= 2 ? WarnColor : Theme.InkDimColor;
        }

        private void RelayLog(string msg, int level)
        {
            if (_log == null) return;
            try { _log(msg, level); } catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_busy)
            {
                e.Cancel = true;
                SetStatus("Patiente : entretien en cours...", 2);
                return;
            }
            base.OnFormClosing(e);
        }

        private static Button MakeBtn(string text, int x, int y, int w, int h, bool primary)
        {
            var b = new Button();
            b.Text = text;
            b.SetBounds(x, y, w, h);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = primary ? 0 : 1;
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            b.BackColor = primary ? Color.FromArgb(0, 150, 90) : Color.White;
            b.ForeColor = primary ? Color.White : Color.FromArgb(40, 44, 52);
            b.UseVisualStyleBackColor = false;
            return b;
        }
    }
}
