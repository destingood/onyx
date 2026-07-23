using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Analyse du trajet réseau (traceroute) : montre chaque saut entre ton PC et une
    /// destination, avec la latence à chaque étape, pour voir OÙ le lag apparaît (ta box, ton
    /// FAI, ou au-delà). Implémenté en ICMP (TTL croissant) — indépendant de la langue Windows.
    /// </summary>
    internal class NetRouteForm : Form
    {
        private readonly Action<string, int> _log;
        private TextBox _target;
        private ListView _list;
        private Label _verdict;
        private Button _btnTrace, _btnClose;

        private static readonly Color Accent = Color.FromArgb(79, 70, 229);
        private static readonly Color Warn = Color.FromArgb(200, 110, 0);
        private const int MaxHops = 30;

        private class Hop { public int N; public string Addr; public double Ms; }

        public NetRouteForm(Action<string, int> log)
        {
            _log = log;
            Build();
            Theme.Apply(this);
        }

        private void Build()
        {
            Text = "Fluide — Trajet réseau";
            ClientSize = new Size(660, 500);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = Color.FromArgb(245, 246, 248);
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  Trajet réseau — où le lag apparaît-il ?",
                Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 12.5f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            var lbl = new Label { Text = "Destination :", Location = new Point(18, 66), AutoSize = true, Font = new Font("Segoe UI Semibold", 9f) };
            _target = new TextBox { Location = new Point(110, 63), Size = new Size(300, 26), Text = "1.1.1.1" };
            var hint = new Label
            {
                Text = "IP ou nom d'hôte (ex. 1.1.1.1, ou l'IP d'un serveur de jeu).",
                Location = new Point(420, 66), Size = new Size(230, 20), ForeColor = Color.Gray, Font = new Font("Segoe UI", 8.5f)
            };
            Controls.Add(lbl); Controls.Add(_target); Controls.Add(hint);

            _btnTrace = MakeBtn("Analyser le trajet", 18, 96, 200, 32, true);
            _btnTrace.Click += OnTrace;
            Controls.Add(_btnTrace);

            _list = new ListView
            {
                Location = new Point(18, 138), Size = new Size(624, 250),
                View = View.Details, FullRowSelect = true, GridLines = false, MultiSelect = false
            };
            _list.Columns.Add("Saut", 50);
            _list.Columns.Add("Adresse", 340);
            _list.Columns.Add("Latence", 110, HorizontalAlignment.Right);
            _list.Columns.Add("", 110);
            Controls.Add(_list);

            _verdict = new Label
            {
                Location = new Point(18, 398), Size = new Size(624, 56), ForeColor = Color.FromArgb(60, 64, 72),
                Font = new Font("Segoe UI", 9.5f)
            };
            Controls.Add(_verdict);

            _btnClose = MakeBtn("Fermer", 552, 456, 90, 34, false);
            _btnClose.Click += (s, e) => Close();
            Controls.Add(_btnClose);
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

        private void OnTrace(object sender, EventArgs e)
        {
            string target = _target.Text.Trim();
            if (target.Length == 0) return;
            _btnTrace.Enabled = false; _target.Enabled = false;
            Cursor = Cursors.WaitCursor;
            _list.Items.Clear();
            _verdict.Text = "Analyse du trajet vers " + target + " (jusqu'à " + MaxHops + " sauts)...";
            Task.Run(() =>
            {
                List<Hop> hops = Trace(target);
                try { BeginInvoke((Action)(() => Show(hops))); } catch { }
            });
        }

        private static List<Hop> Trace(string target)
        {
            var hops = new List<Hop>();
            byte[] buf = new byte[32];
            try
            {
                using (var ping = new Ping())
                    for (int ttl = 1; ttl <= MaxHops; ttl++)
                    {
                        var opts = new PingOptions(ttl, true);
                        double best = -1; string addr = null; bool reached = false;
                        for (int probe = 0; probe < 3; probe++)
                        {
                            try
                            {
                                PingReply r = ping.Send(target, 1000, buf, opts);
                                if (r == null) continue;
                                if (r.Address != null) addr = r.Address.ToString();
                                if (r.Status == IPStatus.Success || r.Status == IPStatus.TtlExpired)
                                    if (best < 0 || r.RoundtripTime < best) best = r.RoundtripTime;
                                if (r.Status == IPStatus.Success) reached = true;
                            }
                            catch { }
                        }
                        hops.Add(new Hop { N = ttl, Addr = addr ?? "* (pas de réponse)", Ms = best });
                        if (reached) break;
                    }
            }
            catch { }
            return hops;
        }

        private void Show(List<Hop> hops)
        {
            _list.Items.Clear();
            double prev = 0; int jumpHop = -1; double jumpDelta = 0;
            foreach (Hop h in hops)
            {
                var it = new ListViewItem(h.N.ToString());
                it.SubItems.Add(h.Addr);
                it.SubItems.Add(h.Ms < 0 ? "—" : h.Ms.ToString("0") + " ms");
                double delta = (h.Ms >= 0 && prev >= 0) ? h.Ms - prev : 0;
                // Repère le premier saut où la latence bondit nettement (> 30 ms d'un coup).
                if (h.Ms >= 0 && prev > 0 && delta > 30 && jumpHop < 0) { jumpHop = h.N; jumpDelta = delta; }
                it.SubItems.Add(h.Ms >= 0 && prev > 0 && delta > 30 ? "+ " + delta.ToString("0") + " ms" : "");
                if (h.Ms < 0) it.ForeColor = Color.Gray;
                else if (h.Ms >= 0 && prev > 0 && delta > 30) it.ForeColor = Warn;
                _list.Items.Add(it);
                if (h.Ms >= 0) prev = h.Ms;
            }

            if (hops.Count == 0)
            {
                _verdict.ForeColor = Color.FromArgb(200, 60, 40);
                _verdict.Text = "Aucun saut mesuré (destination injoignable ou ICMP bloqué).";
            }
            else if (jumpHop == 1)
            {
                _verdict.ForeColor = Warn;
                _verdict.Text = "→ La latence part haut DÈS le 1er saut (ta box / ton Wi-Fi) : passe en Ethernet, "
                    + "vérifie ton Wi-Fi. C'est chez toi que ça se joue.";
            }
            else if (jumpHop == 2)
            {
                _verdict.ForeColor = Warn;
                _verdict.Text = "→ Bond de latence au saut 2 (~+" + jumpDelta.ToString("0") + " ms) : sortie de ta box vers "
                    + "le FAI. Souvent le lien d'accès (fibre/câble) ou la congestion FAI aux heures de pointe.";
            }
            else if (jumpHop > 2)
            {
                _verdict.ForeColor = Warn;
                _verdict.Text = "→ Bond de latence au saut " + jumpHop + " (~+" + jumpDelta.ToString("0") + " ms) : loin dans le réseau "
                    + "(FAI/peering/hébergeur). Hors de ton contrôle — teste un autre serveur/région dans le jeu.";
            }
            else
            {
                _verdict.ForeColor = Accent;
                _verdict.Text = "✔ Trajet régulier, sans bond de latence anormal. Le chemin réseau est sain "
                    + "jusqu'à la destination.";
            }

            if (_log != null && hops.Count > 0)
            {
                Hop last = hops[hops.Count - 1];
                _log("Traceroute : " + hops.Count + " saut(s) vers " + _target.Text.Trim()
                    + ", dernier " + (last.Ms < 0 ? "—" : last.Ms.ToString("0") + " ms")
                    + (jumpHop > 0 ? " · bond au saut " + jumpHop : "") + ".", 0);
            }
            _btnTrace.Enabled = true; _target.Enabled = true;
            Cursor = Cursors.Default;
        }
    }
}
