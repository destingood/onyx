using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Panneau DNS v2 : benchmark parallèle « spécial jeux » (3 domaines × 2 serveurs par
    /// fournisseur), IPv4 + IPv6 appliqués ensemble, et FILET DE SÉCURITÉ — si le nouveau
    /// résolveur ne répond pas, retour automatique aux réglages précédents.
    /// </summary>
    internal class DnsForm : Form
    {
        private readonly Action<string, int> _log;
        private ComboBox _combo;
        private Label _current;
        private ListView _results;
        private Button _btnTest, _btnApply, _btnFlush, _btnClose;

        private static readonly Color Bg     = Color.FromArgb(245, 246, 248);
        private static readonly Color Header = Color.FromArgb(28, 30, 38);
        private static readonly Color Accent = Color.FromArgb(0, 150, 90);

        // Domaines du benchmark : un généraliste + deux « jeux » (résolus en continu par les lanceurs).
        private static readonly string[] BenchDomains = { "www.google.com", "steampowered.com", "riotgames.com" };

        private class Provider
        {
            public string Name; public string[] Servers; public string[] V6; public bool Filtering;
            public double Median = -1; public double Worst = -1;   // rempli par le test
            public Provider(string n, string[] v4, string[] v6, bool filtering)
            { Name = n; Servers = v4; V6 = v6; Filtering = filtering; }
            public override string ToString() { return Name; }
        }

        private readonly Provider[] _providers = new[]
        {
            new Provider("Automatique (DHCP / box) — par défaut", null, null, false),
            new Provider("Cloudflare — 1.1.1.1", new[] { "1.1.1.1", "1.0.0.1" },
                new[] { "2606:4700:4700::1111", "2606:4700:4700::1001" }, false),
            new Provider("Google — 8.8.8.8", new[] { "8.8.8.8", "8.8.4.4" },
                new[] { "2001:4860:4860::8888", "2001:4860:4860::8844" }, false),
            new Provider("Quad9 SANS filtre — 9.9.9.10", new[] { "9.9.9.10", "149.112.112.10" },
                new[] { "2620:fe::10", "2620:fe::fe:10" }, false),
            new Provider("OpenDNS — 208.67.222.222", new[] { "208.67.222.222", "208.67.220.220" },
                new[] { "2620:119:35::35", "2620:119:53::53" }, false),
            new Provider("AdGuard SANS filtre — 94.140.14.140", new[] { "94.140.14.140", "94.140.14.141" },
                new[] { "2a10:50c0::1:ff", "2a10:50c0::2:ff" }, false),
            new Provider("Cloudflare anti-malware — 1.1.1.2 (filtre)", new[] { "1.1.1.2", "1.0.0.2" },
                new[] { "2606:4700:4700::1112", "2606:4700:4700::1002" }, true),
            new Provider("Quad9 sécurisé — 9.9.9.9 (filtre)", new[] { "9.9.9.9", "149.112.112.112" },
                new[] { "2620:fe::fe", "2620:fe::9" }, true),
            new Provider("AdGuard anti-pub — 94.140.14.14 (filtre : peut bloquer boutiques)", new[] { "94.140.14.14", "94.140.15.15" },
                new[] { "2a10:50c0::ad1:ff", "2a10:50c0::ad2:ff" }, true),
        };

        public DnsForm(Action<string, int> log)
        {
            _log = log;
            Build();
            RefreshCurrent();
            Theme.Apply(this);
        }

        private void Build()
        {
            Text = "Fluide — DNS";
            ClientSize = new Size(640, 532);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = Bg;
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = Header };
            banner.Controls.Add(new Label
            {
                Text = "  DNS — résolution plus rapide, sans se faire piéger", Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 13f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            var lblCur = new Label { Text = "DNS actuel :", Location = new Point(18, 62), AutoSize = true, Font = new Font("Segoe UI Semibold", 9f) };
            _current = new Label { Location = new Point(96, 62), Size = new Size(526, 46), ForeColor = Color.FromArgb(60, 64, 72) };
            Controls.Add(lblCur); Controls.Add(_current);

            _btnTest = MakeBtn("TESTER les résolveurs (spécial jeux, ~5 s)", 18, 112, 290, 32, false);
            _btnTest.ForeColor = Accent;
            _btnTest.Click += OnTest;
            Controls.Add(_btnTest);

            var lblDomains = new Label
            {
                Text = "Domaines testés : " + string.Join(", ", BenchDomains) + " — médiane sur 2 serveurs par fournisseur.",
                Location = new Point(316, 118), Size = new Size(306, 30), ForeColor = Color.Gray, Font = new Font("Segoe UI", 8.5f)
            };
            Controls.Add(lblDomains);

            _results = new ListView
            {
                Location = new Point(18, 152), Size = new Size(604, 190),
                View = View.Details, FullRowSelect = true, GridLines = false, MultiSelect = false
            };
            _results.Columns.Add("Fournisseur", 278);
            _results.Columns.Add("Médiane", 84, HorizontalAlignment.Right);
            _results.Columns.Add("Pire", 84, HorizontalAlignment.Right);
            _results.Columns.Add("Filtre", 140);
            _results.SelectedIndexChanged += (s, e) =>
            {
                if (_results.SelectedItems.Count == 1)
                {
                    var p = _results.SelectedItems[0].Tag as Provider;
                    if (p != null) _combo.SelectedItem = p;
                }
            };
            Controls.Add(_results);

            var lblSel = new Label { Text = "Résolveur à appliquer :", Location = new Point(18, 352), AutoSize = true, Font = new Font("Segoe UI Semibold", 9f) };
            _combo = new ComboBox { Location = new Point(18, 372), Size = new Size(604, 26), DropDownStyle = ComboBoxStyle.DropDownList };
            _combo.Items.AddRange(_providers);
            _combo.SelectedIndex = 1; // Cloudflare par défaut
            Controls.Add(lblSel); Controls.Add(_combo);

            var info = new Label
            {
                Text = "Appliqué en IPv4 ET IPv6 sur toutes les cartes actives, cache vidé. FILET DE SÉCURITÉ : si le "
                     + "nouveau résolveur ne répond pas, retour AUTOMATIQUE aux réglages précédents. Les résolveurs "
                     + "« (filtre) » peuvent bloquer des boutiques en jeu.",
                Location = new Point(18, 404), Size = new Size(604, 46), ForeColor = Color.Gray, Font = new Font("Segoe UI", 8.5f)
            };
            Controls.Add(info);

            _btnApply = MakeBtn("APPLIQUER", 18, 458, 200, 40, true);
            _btnApply.Click += OnApply;
            _btnFlush = MakeBtn("Vider le cache DNS", 228, 458, 170, 40, false);
            _btnFlush.Click += (s, e) => { Sys.FlushDns(); if (_log != null) _log("Cache DNS vidé.", 1); };
            _btnClose = MakeBtn("Fermer", 532, 458, 90, 40, false);
            _btnClose.Click += (s, e) => Close();
            Controls.Add(_btnApply); Controls.Add(_btnFlush); Controls.Add(_btnClose);
        }

        private static Button MakeBtn(string text, int x, int y, int w, int h, bool primary)
        {
            var b = new Button
            {
                Text = text, Location = new Point(x, y), Size = new Size(w, h),
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Accent : Color.White,
                ForeColor = primary ? Color.White : Color.FromArgb(40, 44, 52),
                Font = primary ? new Font("Segoe UI Semibold", 10f) : new Font("Segoe UI", 9f)
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            return b;
        }

        private void SetBusy(bool busy)
        {
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            _btnTest.Enabled = !busy; _btnApply.Enabled = !busy; _combo.Enabled = !busy; _results.Enabled = !busy;
        }

        // ------------------------------------------------------------------
        //  Benchmark parallèle : 3 domaines × 2 serveurs par fournisseur.
        // ------------------------------------------------------------------
        private void OnTest(object sender, EventArgs e)
        {
            SetBusy(true);
            _results.Items.Clear();
            _results.Items.Add(new ListViewItem("Test en cours (tous les fournisseurs en parallèle)..."));
            Task.Run(() =>
            {
                var tasks = new List<Task>();
                foreach (Provider p in _providers)
                {
                    if (p.Servers == null) continue;
                    Provider captured = p;
                    tasks.Add(Task.Run(() => Bench(captured)));
                }
                try { Task.WaitAll(tasks.ToArray()); } catch { }
                try { BeginInvoke((Action)ShowResults); } catch { }
            });
        }

        private static void Bench(Provider p)
        {
            var times = new List<double>();
            foreach (string server in p.Servers)
                foreach (string domain in BenchDomains)
                {
                    double ms = DnsBench.QueryMs(server, domain, 700, 2);
                    if (ms >= 0) times.Add(ms);
                }
            if (times.Count == 0) { p.Median = -1; p.Worst = -1; return; }
            times.Sort();
            p.Median = times[times.Count / 2];
            p.Worst = times[times.Count - 1];
        }

        private void ShowResults()
        {
            _results.Items.Clear();
            Provider best = null;
            foreach (Provider p in _providers.Where(x => x.Servers != null).OrderBy(x => x.Median < 0 ? double.MaxValue : x.Median))
            {
                var it = new ListViewItem(p.Name) { Tag = p };
                it.SubItems.Add(p.Median < 0 ? "—" : p.Median.ToString("0") + " ms");
                it.SubItems.Add(p.Worst < 0 ? "injoignable" : p.Worst.ToString("0") + " ms");
                it.SubItems.Add(p.Filtering ? "anti-pub / malware" : "aucun");
                _results.Items.Add(it);
                if (!p.Filtering && p.Median >= 0 && (best == null || p.Median < best.Median)) best = p;
            }
            if (best != null)
            {
                // Conseillé : le plus rapide SANS filtre (un filtre peut bloquer boutiques/CDN de jeux).
                _combo.SelectedItem = best;
                foreach (ListViewItem it in _results.Items)
                    if (it.Tag == best) { it.Text = "→ " + it.Text; it.Font = new Font(_results.Font, FontStyle.Bold); break; }
                if (_log != null) _log("DNS : test terminé — conseillé (sans filtre) : " + best.Name
                    + " (médiane " + best.Median.ToString("0") + " ms).", 1);
            }
            SetBusy(false);
        }

        private void RefreshCurrent()
        {
            _current.Text = Sys.CurrentDnsSummary();
        }

        // ------------------------------------------------------------------
        //  Application avec filet de sécurité (retour arrière automatique).
        // ------------------------------------------------------------------
        private void OnApply(object sender, EventArgs e)
        {
            var p = _combo.SelectedItem as Provider;
            if (p == null) return;
            string msg = (p.Servers == null)
                ? "Remettre le DNS en automatique (DHCP), IPv4 et IPv6, sur toutes les cartes ?"
                : "Appliquer le DNS " + string.Join(" / ", p.Servers) + " (+ IPv6) sur toutes les cartes réseau actives ?\n\n"
                  + "Si le résolveur ne répond pas, tes réglages précédents seront restaurés automatiquement.";
            if (p.Filtering)
                msg += "\n\n⚠ Ce résolveur FILTRE des domaines : si une boutique en jeu (Steam, Game Pass, boutique "
                     + "intégrée) charge à l'infini ensuite, reviens ici et choisis un résolveur SANS filtre.";
            if (MessageBox.Show(this, msg, "DNS", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            SetBusy(true);
            Task.Run(() =>
            {
                bool reverted = false;
                try
                {
                    Dictionary<string, string[]> snap = Sys.SnapshotDns();
                    Sys.SetDns(p.Servers, _log);          // IPv4 (null = automatique) + cache vidé
                    Sys.SetDnsV6(p.V6, _log);             // IPv6 (null = automatique)

                    if (p.Servers != null && !ResolversAnswer(p.Servers))
                    {
                        reverted = true;
                        if (_log != null) _log("Le résolveur choisi ne répond PAS depuis ce réseau — retour arrière : IPv4 restauré à l'exact précédent, IPv6 remis en automatique.", 3);
                        Sys.RestoreDnsSnapshot(snap, _log);
                        Sys.SetDnsV6(null, _log);   // IPv6 : automatique = état sûr et joignable (le snapshot ne couvre que l'IPv4)
                    }
                }
                catch (Exception ex) { if (_log != null) _log("DNS : " + ex.Message, 3); }
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        RefreshCurrent();
                        SetBusy(false);
                        MessageBox.Show(this,
                            reverted
                                ? "Ce résolveur est injoignable depuis ton réseau.\n\n• IPv4 : tes réglages précédents ont été restaurés à l'identique.\n• IPv6 : remis en automatique (état sûr et joignable).\n\nRien n'est cassé."
                                : "DNS mis à jour (IPv4 + IPv6).",
                            "Fluide", MessageBoxButtons.OK,
                            reverted ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                    }));
                }
                catch { }
            });
        }

        /// <summary>Vrai si au moins un des serveurs répond réellement à une requête DNS.</summary>
        private static bool ResolversAnswer(string[] servers)
        {
            foreach (string s in servers)
                if (DnsBench.QueryMs(s, "www.google.com", 900, 2) >= 0) return true;
            return false;
        }
    }
}
