using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;

namespace BTOptimizer
{
    /// <summary>Un événement de crash/instabilité relevé dans les journaux Windows.</summary>
    internal class CrashEvent
    {
        public DateTime Time;
        public string Kind;    // « Crash », « Blocage »…
        public string Detail;  // exe + module
        public bool IsGame;
    }

    /// <summary>
    /// Lecture (seule) des journaux Windows via wevtutil : crashs d'applications/jeux,
    /// erreurs du pilote GPU, BSOD, coupures de courant et erreurs matérielles WHEA.
    /// Aucun droit particulier requis (journaux Application/Système).
    /// </summary>
    internal static class CrashScan
    {
        private static string Query(string logName, string xpath, int max)
        {
            NativeResult r = Sys.Run(Sys.Sys32("wevtutil.exe"),
                "qe " + logName + " \"/q:" + xpath + "\" /c:" + max + " /rd:true /f:xml");
            return r.ExitCode == 0 ? r.Output : null;
        }

        private static string TimeFilter(int days)
        {
            long ms = (long)days * 24L * 3600L * 1000L;
            return "TimeCreated[timediff(@SystemTime) <= " + ms + "]";
        }

        private static int CountEvents(string xml)
        {
            if (xml == null) return -1;
            int n = 0, i = 0;
            while ((i = xml.IndexOf("<Event ", i, StringComparison.Ordinal)) >= 0) { n++; i += 7; }
            return n;
        }

        /// <summary>Nombre d'événements d'un fournisseur du journal Système ; -1 si illisible.</summary>
        public static int CountProvider(string provider, int days)
        {
            return CountEvents(Query("System",
                "*[System[Provider[@Name='" + provider + "'] and " + TimeFilter(days) + "]]", 200));
        }

        /// <summary>Redémarrages brutaux (Kernel-Power 41 : coupure/plantage dur).</summary>
        public static int HardResets(int days)
        {
            return CountEvents(Query("System",
                "*[System[Provider[@Name='Microsoft-Windows-Kernel-Power'] and (EventID=41) and " + TimeFilter(days) + "]]", 50));
        }

        /// <summary>Écrans bleus consignés (source BugCheck).</summary>
        public static int Bsod(int days) { return CountProvider("BugCheck", days); }

        /// <summary>Erreurs matérielles WHEA (CPU/RAM/PCIe).</summary>
        public static int Whea(int days) { return CountProvider("Microsoft-Windows-WHEA-Logger", days); }

        // ---- Erreurs du pilote GPU, multi-constructeur (NVIDIA / AMD / Intel) ----
        private static string[] _gpuProviders;

        /// <summary>Fournisseurs de journal correspondant au(x) pilote(s) GPU détecté(s) sur ce PC.</summary>
        public static string[] GpuDriverProviders()
        {
            if (_gpuProviders != null) return _gpuProviders;
            var provs = new List<string>();
            try
            {
                using (var s = new System.Management.ManagementObjectSearcher("SELECT Name FROM Win32_VideoController"))
                    foreach (System.Management.ManagementObject mo in s.Get())
                    {
                        string n = (Convert.ToString(mo["Name"]) ?? "").ToLowerInvariant();
                        if (n.Contains("nvidia") || n.Contains("geforce") || n.Contains("rtx") || n.Contains("gtx"))
                            Add(provs, "nvlddmkm");
                        else if (n.Contains("amd") || n.Contains("radeon"))
                            { Add(provs, "amdkmdag"); Add(provs, "amdwddmg"); Add(provs, "atikmdag"); }
                        else if (n.Contains("intel") || n.Contains("arc"))
                            { Add(provs, "igfxn"); Add(provs, "igdkmd64"); Add(provs, "igfx"); }
                    }
            }
            catch { }
            // Repli : si le nom est inconnu, on couvre les trois familles.
            if (provs.Count == 0) { Add(provs, "nvlddmkm"); Add(provs, "amdkmdag"); Add(provs, "amdwddmg"); }
            _gpuProviders = provs.ToArray();
            return _gpuProviders;
        }

        private static void Add(List<string> l, string s) { if (!l.Contains(s)) l.Add(s); }

        /// <summary>Total des erreurs du pilote GPU (toutes familles présentes) sur N jours.</summary>
        public static int GpuDriverErrors(int days)
        {
            int total = 0;
            foreach (string p in GpuDriverProviders())
            {
                int c = CountProvider(p, days);
                if (c > 0) total += c;
            }
            return total;
        }

        // Bruit de développement à ignorer dans la liste des crashs applicatifs.
        private static readonly string[] NoiseExe =
        {
            "dotnet.exe", "btoptimizer.exe", "btoptimizertest.exe", "probeharness.exe",
            "msbuild.exe", "servicehub", "vctip.exe", "conhost.exe", "werfault.exe"
        };

        private static bool IsNoise(string exe)
        {
            string low = (exe ?? "").ToLowerInvariant();
            foreach (string n in NoiseExe) if (low.StartsWith(n.Replace(".exe", ""))) return true;
            return false;
        }

        private static bool LooksLikeGame(string exe)
        {
            string low = (exe ?? "").ToLowerInvariant();
            foreach (string g in GameScan.PriorityExes())
                if (low == g.ToLowerInvariant()) return true;
            return low.Contains("-win64-shipping") || low.Contains("launcher");
        }

        /// <summary>Crashs (Application Error) et blocages (Application Hang) récents, bruit dev filtré.</summary>
        public static List<CrashEvent> Recent(int days)
        {
            var events = new List<CrashEvent>();
            string xml = Query("Application",
                "*[System[(Provider[@Name='Application Error'] or Provider[@Name='Application Hang']) and " + TimeFilter(days) + "]]", 60);
            if (xml == null) return events;
            try
            {
                var doc = new XmlDocument();
                doc.LoadXml("<r>" + xml + "</r>");
                foreach (XmlNode ev in doc.SelectNodes("//*[local-name()='Event']"))
                {
                    string provider = "", systemTime = "";
                    var data = new List<string>();
                    foreach (XmlNode node in ev.SelectNodes(".//*"))
                    {
                        if (node.LocalName == "Provider" && node.Attributes["Name"] != null)
                            provider = node.Attributes["Name"].Value;
                        else if (node.LocalName == "TimeCreated" && node.Attributes["SystemTime"] != null)
                            systemTime = node.Attributes["SystemTime"].Value;
                        else if (node.LocalName == "Data")
                            data.Add(node.InnerText);
                    }
                    string exe = data.Count > 0 ? data[0] : "?";
                    if (IsNoise(exe)) continue;

                    DateTime t;
                    if (!DateTime.TryParse(systemTime, CultureInfo.InvariantCulture,
                            DateTimeStyles.AdjustToUniversal, out t)) t = DateTime.UtcNow;

                    bool hang = provider == "Application Hang";
                    string module = (!hang && data.Count > 3 && data[3].Length > 0 && data[3] != exe) ? "  (module : " + data[3] + ")" : "";
                    events.Add(new CrashEvent
                    {
                        Time = t.ToLocalTime(),
                        Kind = hang ? "Blocage (figé)" : "Crash",
                        Detail = exe + module,
                        IsGame = LooksLikeGame(exe)
                    });
                }
            }
            catch { }
            return events;
        }
    }

    /// <summary>
    /// Moniteur de stabilité : ce qui a réellement planté sur CE PC (14 jours) — jeux,
    /// pilote GPU, écrans bleus, coupures — avec un verdict et le lien vers la réparation.
    /// </summary>
    internal class StabilityForm : Form
    {
        private readonly Action<string, int> _log;
        private Label _summary, _verdict;
        private ListView _list;
        private Button _btnScan, _btnRepair, _btnClose;

        private static readonly Color Accent = Color.FromArgb(0, 150, 90);
        private const int Days = 14;

        public StabilityForm(Action<string, int> log)
        {
            _log = log;
            Build();
            Scan();
            Theme.Apply(this);
        }

        private void Build()
        {
            Text = "DesTinGOOD — Stabilité du PC";
            ClientSize = new Size(680, 520);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = Color.FromArgb(245, 246, 248);
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  🩺 Stabilité — qu'est-ce qui a planté sur ce PC ? (14 jours)",
                Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 12.5f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            _summary = new Label
            {
                Location = new Point(18, 60), Size = new Size(644, 40),
                Font = new Font("Segoe UI Semibold", 9.5f), ForeColor = Color.FromArgb(60, 64, 72)
            };
            Controls.Add(_summary);

            _list = new ListView
            {
                Location = new Point(18, 104), Size = new Size(644, 268),
                View = View.Details, FullRowSelect = true, GridLines = false, MultiSelect = false
            };
            _list.Columns.Add("Quand", 118);
            _list.Columns.Add("Type", 110);
            _list.Columns.Add("Programme (🎮 = jeu détecté)", 392);
            Controls.Add(_list);

            _verdict = new Label
            {
                Location = new Point(18, 380), Size = new Size(644, 56),
                Font = new Font("Segoe UI", 9.5f), ForeColor = Color.FromArgb(60, 64, 72)
            };
            Controls.Add(_verdict);

            _btnScan = MakeBtn("Ré-analyser", 18, 448, 130, 38, false);
            _btnScan.Click += (s, e) => Scan();
            _btnRepair = MakeBtn("🛒 Ouvrir la réparation (boutiques / crashs)", 158, 448, 310, 38, true);
            _btnRepair.Click += (s, e) => { using (var f = new ShopFixForm(_log)) f.ShowDialog(this); Scan(); };
            _btnClose = MakeBtn("Fermer", 572, 448, 90, 38, false);
            _btnClose.Click += (s, e) => Close();
            Controls.Add(_btnScan); Controls.Add(_btnRepair); Controls.Add(_btnClose);
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

        private void Scan()
        {
            Cursor = Cursors.WaitCursor;
            _btnScan.Enabled = false;
            _summary.Text = "Lecture des journaux Windows...";
            Task.Run(() =>
            {
                List<CrashEvent> events = CrashScan.Recent(Days);
                int nvl = CrashScan.GpuDriverErrors(Days);
                int bsod = CrashScan.Bsod(Days);
                int hard = CrashScan.HardResets(Days);
                int whea = CrashScan.Whea(Days);
                try { BeginInvoke((Action)(() => Populate(events, nvl, bsod, hard, whea))); } catch { }
            });
        }

        private void Populate(List<CrashEvent> events, int nvl, int bsod, int hard, int whea)
        {
            int crashes = 0, hangs = 0, games = 0;
            _list.Items.Clear();
            foreach (CrashEvent ev in events)
            {
                if (ev.Kind.StartsWith("Crash")) crashes++; else hangs++;
                if (ev.IsGame) games++;
                var it = new ListViewItem(ev.Time.ToString("dd/MM HH:mm"));
                it.SubItems.Add(ev.Kind);
                it.SubItems.Add((ev.IsGame ? "🎮 " : "") + ev.Detail);
                _list.Items.Add(it);
            }

            // Un seul crash pilote écrit une RAFALE d'événements nvlddmkm : au-delà du plafond
            // de lecture, on affiche « 200+ » plutôt qu'un faux compte précis.
            string nvlText = nvl >= 200 ? "200+" : Math.Max(0, nvl).ToString();
            _summary.Text = "Applis/jeux : " + crashes + " crash(s) + " + hangs + " blocage(s) (dont " + games + " jeu(x))"
                          + "   ·   Pilote GPU : " + nvlText
                          + "   ·   Écrans bleus : " + Math.Max(0, bsod)
                          + "   ·   Coupures : " + Math.Max(0, hard)
                          + "   ·   WHEA : " + Math.Max(0, whea);

            if (nvl > 0)
            {
                _verdict.ForeColor = Color.FromArgb(200, 110, 0);
                _verdict.Text = "→ Le pilote GPU a signalé des erreurs (" + (nvl >= 200 ? "200+" : nvl.ToString())
                              + " événements, rafales comprises) : c'est la piste n°1 des crashs de jeux "
                              + "(« dispositif de rendu perdu ») et des boutiques infinies. Clique sur le bouton réparation "
                              + "(HAGS / overclock / power limit y sont vérifiés avec preuve).";
            }
            else if (bsod > 0 || whea > 0 || hard > 0)
            {
                _verdict.ForeColor = Color.FromArgb(200, 60, 40);
                _verdict.Text = "→ Signes MATÉRIELS (écran bleu / WHEA / coupure brute) : contrôle les températures "
                              + "(Moniteur matériel), teste sans XMP/EXPO, vérifie l'alimentation. Les optimisations "
                              + "logicielles ne créent pas ces événements-là.";
            }
            else if (crashes + hangs > 0)
            {
                _verdict.ForeColor = Color.FromArgb(60, 64, 72);
                _verdict.Text = "→ Crashs applicatifs isolés, sans erreur pilote ni signe matériel : vérifie l'intégrité "
                              + "des fichiers du jeu (Steam → Propriétés → Fichiers installés), et son anticheat. "
                              + "Le système, lui, est stable.";
            }
            else
            {
                _verdict.ForeColor = Accent;
                _verdict.Text = "✔ Aucun crash enregistré sur " + Days + " jours. Ton PC est stable — joue tranquille.";
            }

            if (_log != null)
                _log("Stabilité (14 j) : " + (crashes + hangs) + " crash/blocage applis, " + Math.Max(0, nvl)
                     + " erreur(s) pilote GPU, " + Math.Max(0, bsod) + " BSOD.", 0);
            _btnScan.Enabled = true;
            Cursor = Cursors.Default;
        }
    }
}
