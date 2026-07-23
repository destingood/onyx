using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// « Prêt pour le match ? » : checklist tournoi en 30 secondes — réseau (ping/gigue/
    /// perte), timer 1 ms, MODE JEU, pilote GPU 24 h, téléchargements en fond, disque,
    /// optimisations clés. Aucun réglage modifié : uniquement des contrôles.
    /// </summary>
    internal class TournamentForm : Form
    {
        private readonly Action<string, int> _log;
        private Label _verdict;
        private ListView _list;
        private Button _btnScan, _btnBoost, _btnClose;

        private static readonly Color Accent = Color.FromArgb(0, 150, 90);

        private class CheckResult
        {
            public string Name; public string Detail; public bool Ok;
            public CheckResult(string name, bool ok, string detail) { Name = name; Ok = ok; Detail = detail; }
        }

        public TournamentForm(Action<string, int> log)
        {
            _log = log;
            Build();
            Scan();
            Theme.Apply(this);
        }

        private void Build()
        {
            Text = "Fluide — Prêt pour le match ?";
            ClientSize = new Size(640, 500);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = Color.FromArgb(245, 246, 248);
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  Prêt pour le match ? — checklist avant de jouer",
                Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 12.5f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            _verdict = new Label
            {
                Location = new Point(18, 60), Size = new Size(604, 30),
                Font = new Font("Segoe UI Semibold", 12f), ForeColor = Color.FromArgb(60, 64, 72)
            };
            Controls.Add(_verdict);

            _list = new ListView
            {
                Location = new Point(18, 96), Size = new Size(604, 316),
                View = View.Details, FullRowSelect = true, GridLines = false, MultiSelect = false
            };
            _list.Columns.Add("État", 52);
            _list.Columns.Add("Contrôle", 208);
            _list.Columns.Add("Détail", 330);
            Controls.Add(_list);

            _btnScan = MakeBtn("Re-tester", 18, 428, 120, 38, false);
            _btnScan.Click += (s, e) => Scan();
            _btnBoost = MakeBtn("▶ Activer MODE JEU maintenant", 148, 428, 250, 38, true);
            _btnBoost.Click += OnBoost;
            _btnClose = MakeBtn("Fermer", 532, 428, 90, 38, false);
            _btnClose.Click += (s, e) => Close();
            Controls.Add(_btnScan); Controls.Add(_btnBoost); Controls.Add(_btnClose);
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

        private void OnBoost(object sender, EventArgs e)
        {
            if (GameBoost.IsActive) GameBoost.Deactivate(_log);
            else GameBoost.Activate(_log);
            Scan();
        }

        private void Scan()
        {
            Cursor = Cursors.WaitCursor;
            _btnScan.Enabled = false; _btnBoost.Enabled = false;
            _verdict.Text = "Contrôles en cours (ping réseau ~5 s)...";
            Task.Run(() =>
            {
                List<CheckResult> results = RunChecks();
                try { BeginInvoke((Action)(() => Populate(results))); } catch { }
            });
        }

        private static CheckResult PingCheck()
        {
            var times = new List<long>();
            int loss = 0;
            try
            {
                using (var ping = new Ping())
                    for (int i = 0; i < 10; i++)
                    {
                        try
                        {
                            PingReply r = ping.Send("1.1.1.1", 800);
                            if (r != null && r.Status == IPStatus.Success) times.Add(r.RoundtripTime);
                            else loss++;
                        }
                        catch { loss++; }
                    }
            }
            catch { return new CheckResult("Réseau (ping 1.1.1.1)", false, "test impossible (hors ligne ?)"); }

            if (times.Count == 0)
                return new CheckResult("Réseau (ping 1.1.1.1)", false, "100 % de perte — connexion coupée ?");
            double avg = times.Average();
            double jitter = 0;
            for (int i = 1; i < times.Count; i++) jitter += Math.Abs(times[i] - times[i - 1]);
            jitter = times.Count > 1 ? jitter / (times.Count - 1) : 0;
            int lossPct = loss * 100 / 10;
            bool ok = lossPct == 0 && avg <= 50 && jitter <= 8;
            return new CheckResult("Réseau (ping 1.1.1.1)", ok,
                avg.ToString("0") + " ms, gigue " + jitter.ToString("0.#") + " ms, perte " + lossPct + " %"
                + (ok ? "" : " — évite le Wi-Fi/les téléchargements pendant le match"));
        }

        private List<CheckResult> RunChecks()
        {
            var res = new List<CheckResult>();

            res.Add(PingCheck());

            res.Add(new CheckResult("Timer Windows 1 ms", Native.TimerActive,
                Native.TimerActive ? "actif" : "inactif — coche « Timer 1 ms » (ou il s'activera seul en jeu si Timer AUTO est coché)"));

            res.Add(new CheckResult("MODE JEU (services suspendus, RAM libérée)", GameBoost.IsActive,
                GameBoost.IsActive ? "actif" : "inactif — bouton ci-dessous ou Ctrl+Alt+G"));

            int nvl = CrashScan.GpuDriverErrors(1);
            res.Add(new CheckResult("Pilote GPU (dernières 24 h)", nvl == 0,
                nvl <= 0 ? "aucune erreur" : nvl + " erreur(s) — vois Stabilité / réparation avant de jouer"));

            bool dl = Sys.IsServiceRunning("DoSvc") || Sys.IsServiceRunning("BITS") || Sys.IsServiceRunning("wuauserv");
            res.Add(new CheckResult("Téléchargements Windows en fond", !dl,
                dl ? "Windows Update/BITS actifs — une MAJ peut manger la connexion en plein match"
                   : "aucun transfert Windows en cours"));

            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
                long freeGB = drive.AvailableFreeSpace / (1024L * 1024L * 1024L);
                res.Add(new CheckResult("Espace disque système", freeGB >= 15,
                    freeGB + " Go libres" + (freeGB >= 15 ? "" : " — < 15 Go : Windows ralentit (Nettoyage disque)")));
            }
            catch { }

            string[] keyIds = { "power_ultimate", "sysresp", "netthrottle", "gamedvr_off", "game_mode" };
            int applied = 0;
            try
            {
                foreach (Tweak t in Catalog.All())
                    if (keyIds.Contains(t.Id))
                        try { if (t.Check != null && t.Check() == true) applied++; } catch { }
            }
            catch { }
            res.Add(new CheckResult("Optimisations clés (alim, réactivité, réseau, DVR)", applied == keyIds.Length,
                applied + "/" + keyIds.Length + " appliquées" + (applied == keyIds.Length ? "" : " — bouton ⚡ TOUT OPTIMISER")));

            return res;
        }

        private void Populate(List<CheckResult> results)
        {
            _list.Items.Clear();
            int ok = 0;
            foreach (CheckResult r in results)
            {
                if (r.Ok) ok++;
                var it = new ListViewItem(r.Ok ? "✔" : "⚠");
                it.SubItems.Add(r.Name);
                it.SubItems.Add(r.Detail);
                _list.Items.Add(it);
            }
            bool ready = ok == results.Count;
            _verdict.ForeColor = ready ? Accent : Color.FromArgb(200, 110, 0);
            _verdict.Text = ready
                ? "✔ PRÊT POUR LE MATCH — " + ok + "/" + results.Count + " contrôles au vert. GLHF !"
                : (results.Count - ok) + " point(s) à régler avant de jouer (" + ok + "/" + results.Count + " au vert).";
            _btnBoost.Text = GameBoost.IsActive ? "■ Couper MODE JEU (après le match)" : "▶ Activer MODE JEU maintenant";
            if (_log != null) _log("Checklist match : " + ok + "/" + results.Count + " au vert.", ready ? 1 : 2);
            _btnScan.Enabled = true; _btnBoost.Enabled = true;
            Cursor = Cursors.Default;
        }
    }
}
