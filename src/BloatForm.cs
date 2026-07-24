using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Qui ralentit mon PC ? — top des processus par CPU (échantillonné) et RAM, avec
    /// repérage des logiciels de fond connus (RGB, lanceurs, overlays, navigateurs, cloud)
    /// qui grignotent tes perfs pendant le jeu. Lecture seule ; fermeture optionnelle,
    /// confirmée, jamais sur un process système.
    /// </summary>
    internal class BloatForm : Form
    {
        private readonly Action<string, int> _log;
        private ListView _list;
        private Label _summary;
        private Button _btnScan, _btnClose, _btnKill;
        private static readonly Color Accent = Color.FromArgb(79, 70, 229);

        // Processus système à ne JAMAIS proposer de fermer.
        private static readonly HashSet<string> Critical = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "explorer", "dwm", "csrss", "wininit", "winlogon", "services", "lsass", "smss",
            "svchost", "system", "registry", "fontdrvhost", "audiodg", "ctfmon", "sihost",
            "taskhostw", "runtimebroker", "searchhost", "startmenuexperiencehost", "textinputhost",
            "shellexperiencehost", "dllhost", "conhost", "btoptimizer", "dotnet", "idle", "memory compression"
        };

        // Logiciels de fond connus : nom de process (minuscule, sans .exe) -> (catégorie, conseil).
        private class Known { public string Cat; public string Tip; public Known(string c, string t) { Cat = c; Tip = t; } }
        private static readonly Dictionary<string, Known> Bloat = new Dictionary<string, Known>(StringComparer.OrdinalIgnoreCase)
        {
            // RGB / périphériques
            { "icue", new Known("RGB", "Corsair iCUE — fermable en jeu") },
            { "lghub", new Known("RGB", "Logitech G HUB — fermable en jeu") },
            { "logioptionsplus", new Known("RGB", "Logi Options+ — fermable en jeu") },
            { "armourycrate", new Known("RGB", "ASUS Armoury Crate — lourd, fermable en jeu") },
            { "armstrong", new Known("RGB", "ASUS/AsusService — fermable en jeu") },
            { "razer synapse", new Known("RGB", "Razer Synapse — fermable en jeu") },
            { "razer central", new Known("RGB", "Razer Central — fermable en jeu") },
            { "msi center", new Known("RGB", "MSI Center — fermable en jeu") },
            { "signalrgbcore", new Known("RGB", "SignalRGB — fermable en jeu") },
            { "signalrgb", new Known("RGB", "SignalRGB — fermable en jeu") },
            { "openrgb", new Known("RGB", "OpenRGB — fermable en jeu") },
            { "lightingservice", new Known("RGB", "service RGB — fermable en jeu") },
            { "aac ambient lighting", new Known("RGB", "ASUS Aura — fermable en jeu") },
            // Lanceurs
            { "epicgameslauncher", new Known("lanceur", "Epic — fermable en jeu (sauf jeux Epic)") },
            { "eadesktop", new Known("lanceur", "EA App — fermable en jeu (sauf jeux EA)") },
            { "eabackgroundservice", new Known("lanceur", "EA (service de fond)") },
            { "ubisoftconnect", new Known("lanceur", "Ubisoft Connect — fermable en jeu") },
            { "upc", new Known("lanceur", "Ubisoft Connect — fermable en jeu") },
            { "battle.net", new Known("lanceur", "Battle.net — fermable en jeu (sauf jeux Blizzard)") },
            { "galaxyclient", new Known("lanceur", "GOG Galaxy — fermable en jeu") },
            { "riotclientservices", new Known("lanceur", "Riot — nécessaire pour LoL/Valo, sinon fermable") },
            { "razer cortex", new Known("RGB", "Razer Cortex — « booster » de fond, souvent inutile") },
            { "nzxt cam", new Known("RGB", "NZXT CAM — monitoring/RGB, lourd, fermable en jeu") },
            // Fonds d'écran animés & overlays (gros consommateurs GPU)
            { "wallpaper32", new Known("fond animé", "Wallpaper Engine — fond d'écran animé, VRAI tueur de FPS en jeu") },
            { "wallpaper64", new Known("fond animé", "Wallpaper Engine — fond d'écran animé, VRAI tueur de FPS en jeu") },
            { "lively", new Known("fond animé", "Lively Wallpaper — fond animé, à couper en jeu") },
            // Overlays / comms / stream / capture
            { "overwolf", new Known("overlay", "Overwolf — overlay lourd, fermable en jeu") },
            { "discord", new Known("comms", "Discord — l'overlay peut coûter des FPS") },
            { "medal", new Known("capture", "Medal.tv — capture de clips en continu, coûte des FPS") },
            { "wemod", new Known("overlay", "WeMod — overlay de triche/trainer, à couper en compétitif") },
            { "streamlabs", new Known("stream", "Streamlabs — overlay/stream lourd, fermable si tu ne streames pas") },
            { "ms-teams", new Known("comms", "Microsoft Teams — tourne souvent en fond, fermable en jeu") },
            { "slack", new Known("comms", "Slack — fermable en jeu") },
            { "spotify", new Known("audio", "Spotify — fermable en jeu (musique en fond)") },
            // Navigateurs
            { "chrome", new Known("navigateur", "Chrome — gros consommateur RAM/CPU, ferme les onglets") },
            { "msedge", new Known("navigateur", "Edge — ferme-le en jeu") },
            { "firefox", new Known("navigateur", "Firefox — ferme-le en jeu") },
            { "opera", new Known("navigateur", "Opera — ferme-le en jeu") },
            { "brave", new Known("navigateur", "Brave — ferme-le en jeu") },
            // Cloud
            { "onedrive", new Known("☁️ cloud", "OneDrive — synchro en fond (voir optimisation dédiée)") },
            { "dropbox", new Known("☁️ cloud", "Dropbox — synchro en fond, fermable en jeu") },
            { "googledrivefs", new Known("☁️ cloud", "Google Drive — synchro en fond, fermable en jeu") },
            { "megasync", new Known("☁️ cloud", "MEGAsync — synchro en fond, fermable en jeu") },
        };

        private class Row
        {
            public int Pid; public string Name; public double CpuPct; public long RamMB; public Known Tag;
        }

        public BloatForm(Action<string, int> log)
        {
            _log = log;
            Build();
            Scan();
            Theme.Apply(this);
        }

        private void Build()
        {
            Text = "Fluide — Qui ralentit mon PC ?";
            ClientSize = new Size(700, 500);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = Color.FromArgb(245, 246, 248);
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  Qui ralentit mon PC ? — les gourmands de fond",
                Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 12.5f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            var intro = new Label
            {
                Text = "Top des processus par CPU (mesuré sur 1 s) et RAM. Les logiciels de fond connus (RGB, "
                     + "lanceurs, navigateurs, overlays) sont étiquetés : ce sont eux qu'on ferme avant une partie.",
                Location = new Point(18, 58), Size = new Size(664, 40), ForeColor = Color.FromArgb(60, 64, 72)
            };
            Controls.Add(intro);

            _list = new ListView
            {
                Location = new Point(18, 104), Size = new Size(664, 300),
                View = View.Details, FullRowSelect = true, GridLines = false, MultiSelect = false
            };
            _list.Columns.Add("Processus", 190);
            _list.Columns.Add("CPU", 70, HorizontalAlignment.Right);
            _list.Columns.Add("RAM", 90, HorizontalAlignment.Right);
            _list.Columns.Add("Type / conseil", 310);
            Controls.Add(_list);

            _summary = new Label
            {
                Location = new Point(18, 412), Size = new Size(664, 22),
                Font = new Font("Segoe UI Semibold", 9.5f), ForeColor = Color.FromArgb(60, 64, 72)
            };
            Controls.Add(_summary);

            _btnScan = MakeBtn("Re-mesurer", 18, 448, 130, 38, false);
            _btnScan.Click += (s, e) => Scan();
            _btnKill = MakeBtn("Fermer l'application sélectionnée", 158, 448, 280, 38, false);
            _btnKill.ForeColor = Color.FromArgb(180, 70, 20);
            _btnKill.Click += OnKill;
            // Lien fonction → outil : automatiser durablement les priorités/affinités des fâcheux.
            var btnLasso = MakeBtn("⚙️ Process Lasso", 436, 448, 148, 38, false);
            LibScan.WireToolButton(btnLasso, this, _log, "⚙️ Process Lasso", new[] { "BitSum.ProcessLasso" });
            _btnClose = MakeBtn("Fermer", 592, 448, 90, 38, true);
            _btnClose.Click += (s, e) => Close();
            Controls.Add(_btnScan); Controls.Add(_btnKill); Controls.Add(btnLasso); Controls.Add(_btnClose);
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

        private void SetBusy(bool busy)
        {
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            _btnScan.Enabled = !busy; _btnKill.Enabled = !busy; _list.Enabled = !busy;
        }

        private static Known Lookup(string name)
        {
            string n = name.ToLowerInvariant();
            Known k;
            if (Bloat.TryGetValue(n, out k)) return k;
            // Correspondances partielles pour les variantes (ex. « Razer Synapse Service »).
            foreach (var kv in Bloat)
                if (n.Contains(kv.Key) || kv.Key.Contains(n)) return kv.Value;
            return null;
        }

        /// <summary>Applis de fond CONNUES actuellement en cours : { processus, catégorie, conseil }.
        /// Pour le Copilote (« prépare ma partie »). Jamais un processus critique ; dédoublonné.</summary>
        public static List<string[]> RunningBloat()
        {
            var res = new List<string[]>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        string n = p.ProcessName;
                        if (seen.Contains(n) || Critical.Contains(n)) continue;
                        Known k = Lookup(n);
                        if (k == null) continue;
                        seen.Add(n);
                        res.Add(new[] { n, k.Cat, k.Tip });
                    }
                    catch { }
                    finally { try { p.Dispose(); } catch { } }
                }
            }
            catch { }
            return res;
        }

        private void Scan()
        {
            SetBusy(true);
            _summary.Text = "Mesure du CPU sur 1 seconde...";
            Task.Run(() =>
            {
                var rows = Sample();
                try { BeginInvoke((Action)(() => Populate(rows))); } catch { }
            });
        }

        private List<Row> Sample()
        {
            int cores = Math.Max(1, Environment.ProcessorCount);
            var first = new Dictionary<int, TimeSpan>();
            var procs = new Dictionary<int, Process>();
            foreach (Process p in Process.GetProcesses())
            {
                try { first[p.Id] = p.TotalProcessorTime; procs[p.Id] = p; } catch { }
            }
            var sw = Stopwatch.StartNew();
            System.Threading.Thread.Sleep(1000);
            sw.Stop();
            double elapsedMs = sw.Elapsed.TotalMilliseconds;

            var rows = new List<Row>();
            foreach (var kv in procs)
            {
                Process p = kv.Value;
                try
                {
                    p.Refresh();
                    if (p.HasExited) continue;
                    TimeSpan t0 = first[kv.Key];
                    TimeSpan t1 = p.TotalProcessorTime;
                    double cpuPct = (t1 - t0).TotalMilliseconds / (elapsedMs * cores) * 100.0;
                    long ramMB = p.WorkingSet64 / (1024 * 1024);
                    if (cpuPct < 0) cpuPct = 0;
                    rows.Add(new Row { Pid = kv.Key, Name = p.ProcessName, CpuPct = cpuPct, RamMB = ramMB, Tag = Lookup(p.ProcessName) });
                }
                catch { }
                finally { try { p.Dispose(); } catch { } }
            }
            // Tri par impact : CPU d'abord, puis RAM. On garde le top 20.
            return rows.OrderByDescending(r => r.CpuPct).ThenByDescending(r => r.RamMB).Take(20).ToList();
        }

        private void Populate(List<Row> rows)
        {
            _list.Items.Clear();
            int bloatCount = 0; long bloatRam = 0;
            foreach (Row r in rows)
            {
                var it = new ListViewItem(r.Name) { Tag = r };
                it.SubItems.Add(r.CpuPct.ToString("0.#") + " %");
                it.SubItems.Add(r.RamMB.ToString("N0") + " Mo");
                if (r.Tag != null)
                {
                    it.SubItems.Add(r.Tag.Cat + " — " + r.Tag.Tip);
                    it.ForeColor = Color.FromArgb(180, 70, 20);
                    bloatCount++; bloatRam += r.RamMB;
                }
                else it.SubItems.Add(Critical.Contains(r.Name) ? "système (ne pas fermer)" : "");
                _list.Items.Add(it);
            }
            _summary.Text = bloatCount == 0
                ? "Aucun logiciel de fond connu au sommet — ton PC est plutôt propre."
                : bloatCount + " logiciel(s) de fond repéré(s) (~" + bloatRam.ToString("N0") + " Mo) : à fermer avant une partie.";
            SetBusy(false);
        }

        private void OnKill(object sender, EventArgs e)
        {
            if (_list.SelectedItems.Count != 1)
            {
                MessageBox.Show(this, "Sélectionne d'abord une application dans la liste.", "Fluide",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var r = _list.SelectedItems[0].Tag as Row;
            if (r == null) return;
            if (Critical.Contains(r.Name))
            {
                MessageBox.Show(this, "« " + r.Name + " » est un processus système : fermeture refusée pour ta sécurité.",
                    "Fluide", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (MessageBox.Show(this,
                    "Fermer « " + r.Name + " » (PID " + r.Pid + ") ?\n\n"
                    + "Réversible : tu pourras le relancer. Enregistre ton travail dans cette appli avant.",
                    "Fermer l'application", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;
            try
            {
                Process p = Process.GetProcessById(r.Pid);
                p.CloseMainWindow();
                if (!p.WaitForExit(1500)) p.Kill();
                if (_log != null) _log("Application fermée : " + r.Name + " (PID " + r.Pid + ").", 1);
            }
            catch (Exception ex) { if (_log != null) _log("Fermeture impossible : " + ex.Message, 2); }
            Scan();
        }
    }
}
