using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Qualité réseau en jeu : mesure la latence, la GIGUE et la PERTE vers la box
    /// (réseau local / Wi-Fi) ET vers internet, pour dire en 10 s si le lag vient de
    /// TON installation (Wi-Fi, câble) ou du FAI / de l'hébergeur du jeu.
    /// </summary>
    internal class NetworkForm : Form
    {
        private readonly Action<string, int> _log;
        private Label _verdict;
        private ListView _list;
        private Button _btnScan, _btnRepair, _btnClose;

        private static readonly Color Accent = Color.FromArgb(0, 150, 90);
        private const int Pings = 20;

        private class Hop
        {
            public string Label; public string Host; public bool IsLocal;
            public Hop(string label, string host, bool local) { Label = label; Host = host; IsLocal = local; }
        }

        private class HopResult
        {
            public string Label, Host; public bool IsLocal;
            public double Avg = -1, Jitter = -1, Worst = -1; public int LossPct = 100;
        }

        public NetworkForm(Action<string, int> log)
        {
            _log = log;
            Build();
            Scan();
            Theme.Apply(this);
        }

        private void Build()
        {
            Text = "DesTinGOOD — Qualité réseau en jeu";
            ClientSize = new Size(680, 460);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = Color.FromArgb(245, 246, 248);
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  Qualité réseau — le lag vient de chez toi ou du FAI ?",
                Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 12.5f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            var intro = new Label
            {
                Text = "Ce qui compte en jeu n'est pas le débit mais la STABILITÉ : gigue (variation du ping) et perte de "
                     + "paquets. On mesure vers ta box (ton Wi-Fi/câble) et vers internet — l'écart désigne le coupable.",
                Location = new Point(18, 58), Size = new Size(644, 40), ForeColor = Color.FromArgb(60, 64, 72)
            };
            Controls.Add(intro);

            _list = new ListView
            {
                Location = new Point(18, 104), Size = new Size(644, 176),
                View = View.Details, FullRowSelect = true, GridLines = false, MultiSelect = false
            };
            _list.Columns.Add("Segment", 250);
            _list.Columns.Add("Ping moyen", 96, HorizontalAlignment.Right);
            _list.Columns.Add("Gigue", 90, HorizontalAlignment.Right);
            _list.Columns.Add("Pire", 90, HorizontalAlignment.Right);
            _list.Columns.Add("Perte", 100, HorizontalAlignment.Right);
            Controls.Add(_list);

            _verdict = new Label
            {
                Location = new Point(18, 292), Size = new Size(644, 78),
                Font = new Font("Segoe UI", 9.5f), ForeColor = Color.FromArgb(60, 64, 72)
            };
            Controls.Add(_verdict);

            _btnScan = MakeBtn("Re-tester", 18, 388, 130, 38, false);
            _btnScan.Click += (s, e) => Scan();
            _btnRepair = MakeBtn("Réparer le réseau (Winsock / TCP-IP)", 158, 388, 290, 38, false);
            _btnRepair.ForeColor = Accent;
            _btnRepair.Click += OnRepair;
            _btnClose = MakeBtn("Fermer", 572, 388, 90, 38, true);
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

        private void OnRepair(object sender, EventArgs e)
        {
            if (MessageBox.Show(this,
                    "Réinitialiser Winsock et la pile TCP/IP (vide aussi le cache DNS) ?\n"
                    + "Règle beaucoup de soucis réseau. Un REDÉMARRAGE sera nécessaire.",
                    "Réparer le réseau", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                return;
            Task.Run(() => Sys.NetworkRepair(_log));
        }

        private static string DefaultGateway()
        {
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (GatewayIPAddressInformation g in ni.GetIPProperties().GatewayAddresses)
                        if (g.Address != null && g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                            && !g.Address.Equals(IPAddress.Any))
                            return g.Address.ToString();
                }
            }
            catch { }
            return null;
        }

        private static HopResult Measure(Hop hop)
        {
            var r = new HopResult { Label = hop.Label, Host = hop.Host, IsLocal = hop.IsLocal };
            var times = new List<long>();
            int loss = 0;
            try
            {
                using (var ping = new Ping())
                    for (int i = 0; i < Pings; i++)
                    {
                        try
                        {
                            PingReply reply = ping.Send(hop.Host, 900);
                            if (reply != null && reply.Status == IPStatus.Success) times.Add(reply.RoundtripTime);
                            else loss++;
                        }
                        catch { loss++; }
                        System.Threading.Thread.Sleep(30);
                    }
            }
            catch { }
            r.LossPct = loss * 100 / Pings;
            if (times.Count > 0)
            {
                r.Avg = times.Average();
                r.Worst = times.Max();
                double j = 0;
                for (int i = 1; i < times.Count; i++) j += Math.Abs(times[i] - times[i - 1]);
                r.Jitter = times.Count > 1 ? j / (times.Count - 1) : 0;
            }
            return r;
        }

        private void Scan()
        {
            Cursor = Cursors.WaitCursor;
            _btnScan.Enabled = false;
            _list.Items.Clear();
            _verdict.Text = "Mesure en cours (20 pings par segment)...";
            Task.Run(() =>
            {
                string gw = DefaultGateway();
                var hops = new List<Hop>();
                if (gw != null) hops.Add(new Hop("Box / routeur (ton réseau local)", gw, true));
                hops.Add(new Hop("Internet — Cloudflare (1.1.1.1)", "1.1.1.1", false));
                hops.Add(new Hop("Internet — Google (8.8.8.8)", "8.8.8.8", false));

                var results = new List<HopResult>();
                foreach (Hop h in hops) results.Add(Measure(h));
                try { BeginInvoke((Action)(() => Populate(results, gw != null))); } catch { }
            });
        }

        private void Populate(List<HopResult> results, bool haveGateway)
        {
            _list.Items.Clear();
            foreach (HopResult r in results)
            {
                var it = new ListViewItem(r.Label);
                it.SubItems.Add(r.Avg < 0 ? "—" : r.Avg.ToString("0") + " ms");
                it.SubItems.Add(r.Jitter < 0 ? "—" : r.Jitter.ToString("0.#") + " ms");
                it.SubItems.Add(r.Worst < 0 ? "—" : r.Worst.ToString("0") + " ms");
                it.SubItems.Add(r.LossPct + " %");
                if (r.LossPct >= 5 || (r.Jitter >= 0 && r.Jitter > 12) || r.Avg < 0)
                    it.ForeColor = Color.FromArgb(200, 60, 40);
                else if (r.Jitter >= 8 || r.LossPct >= 1)
                    it.ForeColor = Color.FromArgb(200, 110, 0);
                _list.Items.Add(it);
            }

            HopResult local = results.FirstOrDefault(x => x.IsLocal);
            HopResult net = results.Where(x => !x.IsLocal && x.Avg >= 0).OrderBy(x => x.Avg).FirstOrDefault();

            bool localBad = local != null && (local.LossPct >= 2 || local.Jitter > 8);
            bool netBad = net != null && (net.LossPct >= 2 || net.Jitter > 15);

            if (local != null && localBad)
            {
                _verdict.ForeColor = Color.FromArgb(200, 60, 40);
                _verdict.Text = "→ Le problème est CHEZ TOI : gigue/perte élevée dès la box (gigue " + local.Jitter.ToString("0.#")
                    + " ms, perte " + local.LossPct + " %). En Wi-Fi ? Passe en CÂBLE Ethernet — c'est LE gain n°1. "
                    + "Sinon : évite les téléchargements en fond, éloigne-toi des micro-ondes/Bluetooth, change de canal Wi-Fi.";
            }
            else if (netBad && (local == null || !localBad))
            {
                _verdict.ForeColor = Color.FromArgb(200, 110, 0);
                _verdict.Text = "→ Ta box est SAINE, l'instabilité apparaît sur internet : c'est le FAI ou l'hébergeur du jeu. "
                    + "Teste un autre serveur/région dans le jeu, essaie un DNS plus rapide (bouton DNS), et si ça persiste "
                    + "aux heures de pointe, contacte ton FAI avec ces chiffres.";
            }
            else if (local == null && net != null && !netBad)
            {
                _verdict.ForeColor = Accent;
                _verdict.Text = "✔ Latence internet stable. (Passerelle non détectée — probablement un VPN actif : coupe-le pour "
                    + "tester ton vrai réseau.)";
            }
            else
            {
                _verdict.ForeColor = Accent;
                _verdict.Text = "✔ Réseau stable de bout en bout : box saine, internet stable. Le réseau n'est pas ton problème "
                    + "en jeu — regarde plutôt FPS/frametime () et pilote GPU ().";
            }

            if (_log != null && net != null)
                _log("Réseau : internet " + net.Avg.ToString("0") + " ms, gigue " + net.Jitter.ToString("0.#")
                     + " ms, perte " + net.LossPct + " %"
                     + (local != null ? " · box gigue " + local.Jitter.ToString("0.#") + " ms" : "") + ".", 0);
            _btnScan.Enabled = true;
            Cursor = Cursors.Default;
        }
    }
}
